using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows.Input;
using MahApps.Metro.Controls.Dialogs;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using NAudio.Lame;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using WorkoutMixer.Commands;
using WorkoutMixer.Configuration;
using WorkoutMixer.Configuration.Extensions;
using WorkoutMixer.Models;
using WorkoutMixer.Models.Extensions;
using Path = System.IO.Path;

namespace WorkoutMixer;

public partial class MainWindow
{
    private readonly AudioOptions _audioOptions;
    private readonly ExportOptions _exportOptions;
    private readonly LAMEPreset _mp3Preset;
    private readonly WorkoutDefaultsOptions _workoutDefaultsOptions;

    public MainWindow(
        IOptions<ChartOptions> chartOptions,
        IOptions<AudioOptions> audioOptions,
        IOptions<ExportOptions> exportOptions,
        IOptions<WorkoutDefaultsOptions> workoutDefaultsOptions)
    {
        _audioOptions = audioOptions.Value;
        _exportOptions = exportOptions.Value;
        _workoutDefaultsOptions = workoutDefaultsOptions.Value;
        _mp3Preset = Enum.Parse<LAMEPreset>(_audioOptions.Mp3Preset, true);
        AvailableZones = chartOptions.Value.ToChartZones();

        if (AvailableZones.Count == 0)
            throw new InvalidOperationException("At least one chart zone must be configured.");

        MoveUpCommand = new RelayCommand<Mp3File>(MoveUpFile);
        MoveDownCommand = new RelayCommand<Mp3File>(MoveDownFile);
        RemoveCommand = new AsyncRelayCommand<Mp3File>(RemoveFileAsync);
        AddFilesCommand = new AsyncRelayCommand<object>(_ => AddFilesAsync());
        SaveFinalMixCommand = new AsyncRelayCommand<object>(_ => SaveFinalMixAsync());
        SaveIntensityReportCommand = new AsyncRelayCommand<object>(_ => SaveIntensityReportAsync());
        AddChartDataPointCommand = new RelayCommand<object>(_ => AddChartDataPoint());
        MoveChartDataPointUpCommand = new RelayCommand<ChartDataPoint>(MoveChartDataPointUp);
        MoveChartDataPointDownCommand = new RelayCommand<ChartDataPoint>(MoveChartDataPointDown);
        RemoveChartDataPointCommand = new AsyncRelayCommand<ChartDataPoint>(RemoveChartDataPointAsync);

        DataContext = this;

        InitializeComponent();

        UpdateFileSummary();
    }

    public IReadOnlyList<ChartZone> AvailableZones { get; }
    public ObservableCollection<ChartDataPoint> ChartData { get; } = [];
    public ObservableCollection<Mp3File> Files { get; } = [];

    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand AddFilesCommand { get; }
    public ICommand SaveFinalMixCommand { get; }
    public ICommand SaveIntensityReportCommand { get; }
    public ICommand AddChartDataPointCommand { get; }
    public ICommand MoveChartDataPointUpCommand { get; }
    public ICommand MoveChartDataPointDownCommand { get; }
    public ICommand RemoveChartDataPointCommand { get; }

    private async Task AddFilesAsync()
    {
        ProgressDialogController? progressController = null;

        try
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "MP3 files (*.mp3)|*.mp3",
                Multiselect = true,
                Title = "Select one or more MP3 files"
            };

            if (openFileDialog.ShowDialog(this) != true)
                return;

            var pathsToLoad = openFileDialog.FileNames
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(path => !Files.ContainsPath(path))
                .ToList();

            if (pathsToLoad.Count == 0)
                return;

            progressController = await this.ShowProgressAsync("Loading files", "Preparing selected tracks...");
            progressController.SetProgress(0);

            var failedImports = new List<string>();

            for (var i = 0; i < pathsToLoad.Count; i++)
            {
                var path = pathsToLoad[i];

                try
                {
                    progressController.SetMessage($"Loading {Path.GetFileName(path)}...");
                    progressController.SetProgress((i + 1d) / pathsToLoad.Count);

                    var file = await Task.Run(() => Mp3File.Load(path, true));
                    Files.Add(file);
                }
                catch (Exception exception)
                {
                    failedImports.Add($"{path}\n{exception.Message}");
                }
            }

            UpdatePositions();
            UpdateFileSummary();

            await progressController.CloseAsync();
            progressController = null;

            if (failedImports.Count > 0)
            {
                await this.ShowMessageAsync(
                    "File import error",
                    $"Some files could not be loaded:\n\n{string.Join("\n\n", failedImports)}");
            }
        }
        catch (Exception exception)
        {
            if (progressController is not null)
                await progressController.CloseAsync();

            await this.ShowMessageAsync("Error", $"An error occurred while adding files:\n{exception.Message}");
        }
    }

    private async Task SaveFinalMixAsync()
    {
        if (Files.Count == 0)
        {
            await this.ShowMessageAsync("Save MP3", "Add at least one MP3 file before exporting the final mix.");
            return;
        }

        var saveFileDialog = new SaveFileDialog
        {
            Filter = "MP3 file (*.mp3)|*.mp3",
            DefaultExt = ".mp3",
            AddExtension = true,
            FileName = _exportOptions.FinalMixDefaultFileName,
            Title = "Choose where to save the final MP3"
        };

        if (saveFileDialog.ShowDialog(this) != true)
            return;

        ProgressDialogController? progressController = null;

        try
        {
            progressController = await this.ShowProgressAsync("Saving MP3", "Rendering final mix...");
            progressController.SetProgress(0);

            var progress = new Progress<(double Progress, string Message)>(value =>
            {
                progressController.SetProgress(value.Progress);
                progressController.SetMessage(value.Message);
            });

            await Task.Run(() => ExportFinalMix(saveFileDialog.FileName, progress));

            await progressController.CloseAsync();
            await this.ShowMessageAsync("Save MP3", $"Final mix saved to:\n{saveFileDialog.FileName}");
        }
        catch (Exception exception)
        {
            if (progressController is not null)
                await progressController.CloseAsync();

            await this.ShowMessageAsync("Save MP3", $"Could not save the final MP3:\n{exception.Message}");
        }
    }

    private async Task SaveIntensityReportAsync()
    {
        if (ChartData.Count == 0)
        {
            await this.ShowMessageAsync("Save intensities", "Add at least one chart segment before exporting the intensity report.");
            return;
        }

        var saveFileDialog = new SaveFileDialog
        {
            Filter = "Text file (*.txt)|*.txt",
            DefaultExt = ".txt",
            AddExtension = true,
            FileName = _exportOptions.IntensityReportDefaultFileName,
            Title = "Choose where to save the intensity report"
        };

        if (saveFileDialog.ShowDialog(this) != true)
            return;

        try
        {
            await File.WriteAllTextAsync(saveFileDialog.FileName, BuildIntensityReport(), Encoding.UTF8);
            await this.ShowMessageAsync("Save intensities", $"Intensity report saved to:\n{saveFileDialog.FileName}");
        }
        catch (Exception exception)
        {
            await this.ShowMessageAsync("Save intensities", $"Could not save the intensity report:\n{exception.Message}");
        }
    }

    private void MoveUpFile(Mp3File? item)
    {
        if (item is null)
            return;

        var index = Files.IndexOf(item);

        if (index <= 0)
            return;

        Files.Move(index, index - 1);
        UpdatePositions();
        UpdateFileSummary();
        SelectFile(item);
    }

    private void MoveDownFile(Mp3File? item)
    {
        if (item is null)
            return;

        var index = Files.IndexOf(item);

        if (index < 0 || index >= Files.Count - 1)
            return;

        Files.Move(index, index + 1);
        UpdatePositions();
        UpdateFileSummary();
        SelectFile(item);
    }

    private async Task RemoveFileAsync(Mp3File? item)
    {
        if (item is null)
            return;

        var confirmation = await this.ShowMessageAsync(
            "Remove file",
            $"Are you sure you want to remove \"{item.FileName}\"?",
            MessageDialogStyle.AffirmativeAndNegative,
            new MetroDialogSettings
            {
                AffirmativeButtonText = "Remove",
                NegativeButtonText = "Cancel"
            });

        if (confirmation != MessageDialogResult.Affirmative)
            return;

        var index = Files.IndexOf(item);

        if (index < 0)
            return;

        Files.RemoveAt(index);
        UpdatePositions();
        UpdateFileSummary();

        if (Files.Count == 0)
            return;

        SelectFile(Files[Math.Min(index, Files.Count - 1)]);
    }

    private void UpdatePositions()
    {
        for (var i = 0; i < Files.Count; i++)
            Files[i].Position = i + 1;
    }

    private void ExportFinalMix(string outputPath, IProgress<(double Progress, string Message)>? progress = null)
    {
        var readers = new List<AudioFileReader>();
        var providers = new List<ISampleProvider>();
        var targetFormat = WaveFormat.CreateIeeeFloatWaveFormat(_audioOptions.TargetSampleRate, _audioOptions.TargetChannels);
        double startOffsetSeconds = 0;
        double totalOutputSeconds = 0;

        try
        {
            for (var index = 0; index < Files.Count; index++)
            {
                var file = Files[index];
                var reader = new AudioFileReader(file.Path);
                readers.Add(reader);

                ISampleProvider sampleProvider = reader;

                if (sampleProvider.WaveFormat.SampleRate != targetFormat.SampleRate)
                    sampleProvider = new WdlResamplingSampleProvider(sampleProvider, targetFormat.SampleRate);

                sampleProvider = sampleProvider.WaveFormat.Channels switch
                {
                    1 => new MonoToStereoSampleProvider(sampleProvider),
                    2 => sampleProvider,
                    _ => throw new InvalidOperationException($"Unsupported channel count for file '{file.FileName}'.")
                };

                var fadeIn = index > 0 ? TimeSpan.FromSeconds(GetOverlapSeconds(Files[index - 1], file)) : TimeSpan.Zero;
                var fadeOut = index < Files.Count - 1 ? TimeSpan.FromSeconds(GetOverlapSeconds(file, Files[index + 1])) : TimeSpan.Zero;
                sampleProvider = new CrossfadeSampleProvider(sampleProvider, fadeIn, fadeOut, reader.TotalTime);

                providers.Add(new OffsetSampleProvider(sampleProvider)
                {
                    DelayBy = TimeSpan.FromSeconds(startOffsetSeconds)
                });

                startOffsetSeconds += reader.TotalTime.TotalSeconds;
                totalOutputSeconds = Math.Max(totalOutputSeconds, startOffsetSeconds);

                if (index < Files.Count - 1)
                    startOffsetSeconds -= GetOverlapSeconds(file, Files[index + 1]);
            }

            var mixer = new MixingSampleProvider(providers) { ReadFully = false };
            var waveProvider = new SampleToWaveProvider16(mixer);
            var buffer = new byte[waveProvider.WaveFormat.AverageBytesPerSecond * 4];
            var totalBytesEstimate = Math.Max(buffer.Length, (long)Math.Ceiling(totalOutputSeconds * waveProvider.WaveFormat.AverageBytesPerSecond));
            long writtenBytes = 0;

            progress?.Report((0, "Preparing final mix..."));

            using var writer = new LameMP3FileWriter(outputPath, waveProvider.WaveFormat, _mp3Preset);

            int bytesRead;

            while ((bytesRead = waveProvider.Read(buffer, 0, buffer.Length)) > 0)
            {
                writer.Write(buffer, 0, bytesRead);
                writtenBytes += bytesRead;

                var progressValue = Math.Clamp(writtenBytes / (double)totalBytesEstimate, 0, 1);
                var elapsed = TimeSpan.FromSeconds(writtenBytes / (double)waveProvider.WaveFormat.AverageBytesPerSecond);
                progress?.Report((progressValue, $"Rendering final mix... {progressValue:P0} ({FormatReportTime(elapsed)}/{FormatReportTime(TimeSpan.FromSeconds(totalOutputSeconds))})"));
            }

            progress?.Report((1, "Final mix saved."));
        }
        finally
        {
            foreach (var reader in readers)
                reader.Dispose();
        }
    }

    private string BuildIntensityReport()
    {
        var builder = new StringBuilder();
        var currentTime = TimeSpan.Zero;

        foreach (var point in ChartData)
        {
            var duration = TimeSpan.FromMinutes(point.Duration);
            var endTime = currentTime + duration;
            var durationSeconds = (int)Math.Round(duration.TotalSeconds);

            builder.AppendLine(
                $"{point.Zone.Name} - {durationSeconds}segs - {point.Rpm} RPM - {FormatReportTime(currentTime)} até {FormatReportTime(endTime)}");

            currentTime = endTime;
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatReportTime(TimeSpan time)
    {
        return time.TotalHours >= 1
            ? time.ToString(@"hh\:mm\:ss")
            : time.ToString(@"mm\:ss");
    }

    private void AddChartDataPoint()
    {
        var point = new ChartDataPoint(_workoutDefaultsOptions.SegmentDurationMinutes, AvailableZones[0], _workoutDefaultsOptions.SegmentRpm);
        ChartData.Add(point);
        SegmentsPanel.SelectChartDataPoint(point);
    }

    private void MoveChartDataPointUp(ChartDataPoint? item)
    {
        if (item is null)
            return;

        var index = ChartData.IndexOf(item);

        if (index <= 0)
            return;

        ChartData.Move(index, index - 1);
        SegmentsPanel.SelectChartDataPoint(item);
    }

    private void MoveChartDataPointDown(ChartDataPoint? item)
    {
        if (item is null)
            return;

        var index = ChartData.IndexOf(item);

        if (index < 0 || index >= ChartData.Count - 1)
            return;

        ChartData.Move(index, index + 1);
        SegmentsPanel.SelectChartDataPoint(item);
    }

    private async Task RemoveChartDataPointAsync(ChartDataPoint? item)
    {
        if (item is null)
            return;

        var confirmation = await this.ShowMessageAsync(
            "Remove segment",
            $"Are you sure you want to remove the segment \"{item.Zone.Name}\" ({item.DurationSeconds:0}s, {item.Rpm} RPM)?",
            MessageDialogStyle.AffirmativeAndNegative,
            new MetroDialogSettings
            {
                AffirmativeButtonText = "Remove",
                NegativeButtonText = "Cancel"
            });

        if (confirmation != MessageDialogResult.Affirmative)
            return;

        ChartData.Remove(item);
    }

    private void UpdateFileSummary()
    {
        if (Files.Count == 0)
        {
            FilesPanel.SummaryText = "No files selected.";
            return;
        }

        var totalDuration = TimeSpan.FromTicks(Files.Sum(item => item.Duration.Ticks));
        FilesPanel.SummaryText = $"{Files.Count} file(s) loaded | Total duration: {Mp3File.FormatDuration(totalDuration)}";
    }

    private void SelectFile(Mp3File item)
    {
        FilesPanel.SelectFile(item);
    }

    private int GetOverlapSeconds(Mp3File first, Mp3File second)
    {
        return Math.Min(_audioOptions.TrackOverlapSeconds, Math.Min(first.Waveform.Count, second.Waveform.Count));
    }

    private sealed class CrossfadeSampleProvider : ISampleProvider
    {
        private readonly long _fadeInSamples;
        private readonly long _fadeOutStartSample;
        private readonly ISampleProvider _source;
        private readonly long _totalSamples;
        private long _position;

        public CrossfadeSampleProvider(ISampleProvider source, TimeSpan fadeIn, TimeSpan fadeOut, TimeSpan totalDuration)
        {
            _source = source;
            WaveFormat = source.WaveFormat;

            var sampleRate = WaveFormat.SampleRate * WaveFormat.Channels;
            _fadeInSamples = Math.Max(0, (long)(fadeIn.TotalSeconds * sampleRate));
            var fadeOutSamples = Math.Max(0, (long)(fadeOut.TotalSeconds * sampleRate));
            _totalSamples = Math.Max(0, (long)(totalDuration.TotalSeconds * sampleRate));
            _fadeOutStartSample = Math.Max(0, _totalSamples - fadeOutSamples);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            var samplesRead = _source.Read(buffer, offset, count);

            for (var i = 0; i < samplesRead; i++)
            {
                var gain = 1d;
                var absoluteSample = _position + i;

                if (_fadeInSamples > 0 && absoluteSample < _fadeInSamples)
                    gain = Math.Min(gain, absoluteSample / (double)_fadeInSamples);

                if (_totalSamples > 0 && absoluteSample >= _fadeOutStartSample)
                {
                    var fadeOutLength = Math.Max(1, _totalSamples - _fadeOutStartSample);
                    gain = Math.Min(gain, (_totalSamples - absoluteSample) / (double)fadeOutLength);
                }

                buffer[offset + i] *= (float)Math.Clamp(gain, 0, 1);
            }

            _position += samplesRead;
            return samplesRead;
        }
    }
}
