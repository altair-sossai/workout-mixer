using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using NAudio.Wave;

namespace WorkoutMixer.Models;

public sealed class Mp3File : INotifyPropertyChanged
{
    private static readonly string[] Suffixes = ["B", "KB", "MB", "GB"];
    private static int _nextAccentIndex = -1;

    private readonly Lazy<IReadOnlyList<double>> _waveform;

    private Mp3File(string path, TimeSpan duration, long sizeBytes)
    {
        Path = path;
        FileName = System.IO.Path.GetFileName(path);
        Duration = duration;
        SizeBytes = sizeBytes;
        AccentColor = PickAccentColor();
        AccentBrush = CreateFrozenBrush(AccentColor);
        _waveform = new Lazy<IReadOnlyList<double>>(() => CreateWaveform(Path));
    }

    public string Path { get; }
    public string FileName { get; }
    public TimeSpan Duration { get; }
    public string DurationFormatted => FormatDuration(Duration);
    public long SizeBytes { get; }
    public string SizeFormatted => FormatSize(SizeBytes);
    public Color AccentColor { get; }
    public Brush AccentBrush { get; }
    public IReadOnlyList<double> Waveform => _waveform.Value;

    public int Position
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? duration.ToString(@"hh\:mm\:ss")
            : duration.ToString(@"mm\:ss");
    }

    public static Mp3File Load(string path, bool preloadWaveform = false)
    {
        var file = Create(path);

        if (preloadWaveform) _ = file.Waveform;

        return file;
    }

    public static implicit operator Mp3File(string path)
    {
        return Load(path);
    }

    private static Mp3File Create(string path)
    {
        using var reader = new Mp3FileReader(path);
        var fileInfo = new FileInfo(path);

        return new Mp3File(path, reader.TotalTime, fileInfo.Length);
    }

    private static IReadOnlyList<double> CreateWaveform(string path)
    {
        using var reader = new AudioFileReader(path);

        var sampleRate = reader.WaveFormat.SampleRate;
        var channels = reader.WaveFormat.Channels;
        var totalSeconds = Math.Max(1, (int)Math.Ceiling(reader.TotalTime.TotalSeconds));
        var buffer = new float[sampleRate * channels];
        var sumOfSquares = new double[totalSeconds];
        var sampleCounts = new int[totalSeconds];
        long frameIndex = 0;

        int read;

        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            for (var i = 0; i < read; i += channels)
            {
                double frameSquareSum = 0;
                var frameSamples = Math.Min(channels, read - i);

                for (var channel = 0; channel < frameSamples; channel++)
                {
                    var sample = buffer[i + channel];
                    frameSquareSum += sample * sample;
                }

                var frameMeanSquare = frameSquareSum / frameSamples;
                var currentSecond = Math.Min(totalSeconds - 1, (int)(frameIndex / (double)sampleRate));

                sumOfSquares[currentSecond] += frameMeanSquare;
                sampleCounts[currentSecond]++;
                frameIndex++;
            }

        var amplitudes = new double[totalSeconds];

        for (var i = 0; i < totalSeconds; i++)
        {
            if (sampleCounts[i] == 0)
                continue;

            var meanSquare = sumOfSquares[i] / sampleCounts[i];
            amplitudes[i] = Math.Sqrt(meanSquare);
        }

        var maxAmplitude = amplitudes.Max();

        if (maxAmplitude <= 0)
            return amplitudes;

        for (var i = 0; i < amplitudes.Length; i++) amplitudes[i] /= maxAmplitude;

        return amplitudes;
    }

    private static string FormatSize(long bytes)
    {
        double value = bytes;
        var suffixIndex = 0;

        while (value >= 1024 && suffixIndex < Suffixes.Length - 1)
        {
            value /= 1024;
            suffixIndex++;
        }

        return $"{value:0.##} {Suffixes[suffixIndex]}";
    }

    private static Color PickAccentColor()
    {
        var index = Interlocked.Increment(ref _nextAccentIndex);
        return GenerateSequentialAccentColor(index);
    }

    private static Color GenerateSequentialAccentColor(int index)
    {
        const double goldenRatioConjugate = 0.618033988749895;
        var hue = index * goldenRatioConjugate % 1d;

        // Alternate saturation and value bands to keep adjacent generated colors distinct.
        var saturation = 0.62 + index % 3 * 0.1;
        var value = 0.72 + index % 2 * 0.12;

        return ColorFromHsv(hue * 360d, Math.Min(saturation, 0.92), Math.Min(value, 0.92));
    }

    private static Color ColorFromHsv(double hue, double saturation, double value)
    {
        var chroma = value * saturation;
        var hueSection = hue / 60d;
        var x = chroma * (1 - Math.Abs(hueSection % 2 - 1));
        double red = 0;
        double green = 0;
        double blue = 0;

        switch (hueSection)
        {
            case >= 0 and < 1:
                red = chroma;
                green = x;
                break;
            case >= 1 and < 2:
                red = x;
                green = chroma;
                break;
            case >= 2 and < 3:
                green = chroma;
                blue = x;
                break;
            case >= 3 and < 4:
                green = x;
                blue = chroma;
                break;
            case >= 4 and < 5:
                red = x;
                blue = chroma;
                break;
            default:
                red = chroma;
                blue = x;
                break;
        }

        var match = value - chroma;

        return Color.FromRgb(
            (byte)Math.Round((red + match) * 255),
            (byte)Math.Round((green + match) * 255),
            (byte)Math.Round((blue + match) * 255));
    }

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}