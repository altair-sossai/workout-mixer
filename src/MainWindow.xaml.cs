using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MahApps.Metro.Controls.Dialogs;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using NAudio.Lame;
using WorkoutMixer.Commands;
using WorkoutMixer.Components;
using WorkoutMixer.Configuration;
using WorkoutMixer.Configuration.Extensions;
using WorkoutMixer.Models;
using WorkoutMixer.Models.Extensions;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Path = System.IO.Path;
using ShapePath = System.Windows.Shapes.Path;

namespace WorkoutMixer;

public partial class MainWindow
{
    private const double BottomMargin = 32;
    private const double RightMargin = 18;
    private const double LeftMargin = 34;
    private const double TopMargin = 12;
    private const double BaseChartWidth = 1200;
    private const int TrackOverlapSeconds = 5;
    private const int FinalChartSampleSeconds = 2;

    private readonly ObservableCollection<ChartDataPoint> _chartData = [];
    private readonly ObservableCollection<Mp3File> _files = [];
    private readonly Random _random = new();
    private readonly IReadOnlyList<ChartZone> _zones;
    private List<double>? _combinedWaveformCache;
    private Mp3File? _playingFile;
    private TimeSpan _playingPosition;
    private bool _isPlaybackActive;
    private double _chartZoom = 1;

    public MainWindow(IOptions<ChartOptions> chartOptions)
    {
        _zones = chartOptions.Value.ToChartZones();

        if (_zones.Count == 0)
            throw new InvalidOperationException("At least one chart zone must be configured.");

        MoveUpCommand = new RelayCommand<Mp3File>(MoveUpFile);
        MoveDownCommand = new RelayCommand<Mp3File>(MoveDownFile);
        RemoveCommand = new RelayCommand<Mp3File>(RemoveFile);
        AddChartDataPointCommand = new RelayCommand<object>(_ => AddChartDataPoint());
        MoveChartDataPointUpCommand = new RelayCommand<ChartDataPoint>(MoveChartDataPointUp);
        MoveChartDataPointDownCommand = new RelayCommand<ChartDataPoint>(MoveChartDataPointDown);
        RemoveChartDataPointCommand = new RelayCommand<ChartDataPoint>(RemoveChartDataPoint);

        DataContext = this;

        InitializeComponent();

        foreach (var point in GenerateChartData())
            _chartData.Add(point);

        FilesListBox.ItemsSource = _files;
        ChartDataPointsGrid.ItemsSource = _chartData;
        UpdateChartWidth();

        _files.CollectionChanged += Files_CollectionChanged;
        _chartData.CollectionChanged += ChartData_CollectionChanged;
        Mp3FileListItem.PlaybackProgressChanged += Mp3FileListItem_PlaybackProgressChanged;

        foreach (var point in _chartData)
            point.PropertyChanged += ChartDataPoint_PropertyChanged;

        Loaded += MainWindow_Loaded;
        Unloaded += MainWindow_Unloaded;
        ChartCanvas.SizeChanged += ChartCanvas_SizeChanged;

        UpdateFileSummary();
    }

    public IReadOnlyList<ChartZone> AvailableZones => _zones;
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand AddChartDataPointCommand { get; }
    public ICommand MoveChartDataPointUpCommand { get; }
    public ICommand MoveChartDataPointDownCommand { get; }
    public ICommand RemoveChartDataPointCommand { get; }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        DrawChart();
    }

    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize is { Width: > 0, Height: > 0 })
            DrawChart();
    }

    private void MainWindow_Unloaded(object sender, RoutedEventArgs e)
    {
        Mp3FileListItem.PlaybackProgressChanged -= Mp3FileListItem_PlaybackProgressChanged;
    }

    private void ZoomOutChart_Click(object sender, RoutedEventArgs e)
    {
        SetChartZoom(_chartZoom - 1);
    }

    private void ResetChartZoom_Click(object sender, RoutedEventArgs e)
    {
        SetChartZoom(1);
    }

    private void ZoomInChart_Click(object sender, RoutedEventArgs e)
    {
        SetChartZoom(_chartZoom + 1);
    }

    private void Files_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _combinedWaveformCache = null;

        if (_playingFile is not null && !_files.Contains(_playingFile))
        {
            _playingFile = null;
            _playingPosition = TimeSpan.Zero;
            _isPlaybackActive = false;
        }

        DrawChart();
    }

    private void ChartData_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (ChartDataPoint point in e.OldItems)
                point.PropertyChanged -= ChartDataPoint_PropertyChanged;

        if (e.NewItems is not null)
            foreach (ChartDataPoint point in e.NewItems)
                point.PropertyChanged += ChartDataPoint_PropertyChanged;

        DrawChart();
    }

    private void ChartDataPoint_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChartDataPoint.Duration) or nameof(ChartDataPoint.Zone) or nameof(ChartDataPoint.Intensity))
            DrawChart();
    }

    private void Mp3FileListItem_PlaybackProgressChanged(object? sender, PlaybackProgressChangedEventArgs e)
    {
        _playingFile = e.File;
        _playingPosition = e.Position;
        _isPlaybackActive = e.IsPlaying;
        DrawChart();
    }

    private async void AddFiles_Click(object sender, RoutedEventArgs e)
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
                .Where(path => !_files.ContainsPath(path))
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

                    var file = await Task.Run(() => Mp3File.Load(path, preloadWaveform: true));
                    _files.Add(file);
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

    private async void SaveFinalMix_Click(object sender, RoutedEventArgs e)
    {
        if (_files.Count == 0)
        {
            await this.ShowMessageAsync("Save MP3", "Add at least one MP3 file before exporting the final mix.");
            return;
        }

        var saveFileDialog = new SaveFileDialog
        {
            Filter = "MP3 file (*.mp3)|*.mp3",
            DefaultExt = ".mp3",
            AddExtension = true,
            FileName = "workout-mix.mp3",
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

    private async void SaveIntensityReport_Click(object sender, RoutedEventArgs e)
    {
        if (_chartData.Count == 0)
        {
            await this.ShowMessageAsync("Save intensities", "Add at least one chart segment before exporting the intensity report.");
            return;
        }

        var saveFileDialog = new SaveFileDialog
        {
            Filter = "Text file (*.txt)|*.txt",
            DefaultExt = ".txt",
            AddExtension = true,
            FileName = "workout-intensities.txt",
            Title = "Choose where to save the intensity report"
        };

        if (saveFileDialog.ShowDialog(this) != true)
            return;

        try
        {
            await System.IO.File.WriteAllTextAsync(saveFileDialog.FileName, BuildIntensityReport(), Encoding.UTF8);
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

        var index = _files.IndexOf(item);

        if (index <= 0)
            return;

        _files.Move(index, index - 1);
        UpdatePositions();
        UpdateFileSummary();
        SelectFile(item);
    }

    private void MoveDownFile(Mp3File? item)
    {
        if (item is null)
            return;

        var index = _files.IndexOf(item);

        if (index < 0 || index >= _files.Count - 1)
            return;

        _files.Move(index, index + 1);
        UpdatePositions();
        UpdateFileSummary();
        SelectFile(item);
    }

    private void RemoveFile(Mp3File? item)
    {
        if (item is null)
            return;

        var index = _files.IndexOf(item);

        if (index < 0)
            return;

        _files.RemoveAt(index);
        UpdatePositions();
        UpdateFileSummary();

        if (_files.Count == 0)
            return;

        SelectFile(_files[Math.Min(index, _files.Count - 1)]);
    }

    private void DrawChart()
    {
        if (ChartCanvas.ActualWidth <= 0 || ChartCanvas.ActualHeight <= 0)
            return;

        ChartCanvas.Children.Clear();

        var width = ChartCanvas.ActualWidth;
        var height = ChartCanvas.ActualHeight;
        var usableWidth = width - LeftMargin - RightMargin;
        var usableHeight = height - TopMargin - BottomMargin;
        var chartDurationMinutes = Math.Max(1, _chartData.Sum(point => point.Duration));
        var combinedWaveform = BuildCombinedWaveform();
        var waveformDurationMinutes = Math.Max(0, combinedWaveform.Count / 60d);
        var totalMinutes = Math.Max(chartDurationMinutes, Math.Max(1, waveformDurationMinutes));

        if (usableWidth <= 0 || usableHeight <= 0)
            return;

        AddLine(LeftMargin, height - BottomMargin, width - RightMargin, height - BottomMargin);
        AddLine(LeftMargin, TopMargin, LeftMargin, height - BottomMargin);

        foreach (var value in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
        {
            var y = TopMargin + usableHeight - value * usableHeight;
            AddText($"{value:0.##}", LeftMargin - 6, y - 8, HorizontalAlignment.Right);
        }

        if (_chartData.Count == 0)
        {
            AddText("No chart data configured", LeftMargin + 8, TopMargin + 8, HorizontalAlignment.Left);
            return;
        }

        double currentTime = 0;

        foreach (var dataPoint in _chartData)
        {
            var x = LeftMargin + currentTime / totalMinutes * usableWidth;
            var zoneWidth = dataPoint.Duration / totalMinutes * usableWidth;
            var y = TopMargin + usableHeight - dataPoint.Intensity * usableHeight;
            var zoneHeight = dataPoint.Intensity * usableHeight;

            var rectangle = new Rectangle
            {
                Width = zoneWidth,
                Height = zoneHeight,
                Fill = dataPoint.Brush,
                Opacity = 0.72,
                ToolTip = $"{dataPoint.Zone.Name}\nDuration: {dataPoint.Duration:0.##} min\nIntensity: {dataPoint.Intensity:0.00}"
            };

            Canvas.SetLeft(rectangle, x);
            Canvas.SetTop(rectangle, y);
            ChartCanvas.Children.Add(rectangle);

            currentTime += dataPoint.Duration;
        }

        var points = BuildWaveformPoints(combinedWaveform, totalMinutes, usableWidth, usableHeight);

        if (points.Count > 1)
        {
            var path = new ShapePath
            {
                Stroke = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
                Opacity = 0.55,
                StrokeThickness = 1.8
            };

            var figure = new PathFigure { StartPoint = points[0], IsClosed = false, IsFilled = false };

            for (var i = 1; i < points.Count; i++)
                figure.Segments.Add(new LineSegment(points[i], true));

            path.Data = new PathGeometry([figure]);
            ChartCanvas.Children.Add(path);
        }

        if (points.Count <= 40)
        {
            foreach (var point in points)
            {
                var circle = new Ellipse
                {
                    Width = 5,
                    Height = 5,
                    Fill = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
                    Stroke = Brushes.White,
                    StrokeThickness = 0.5
                };

                Canvas.SetLeft(circle, point.X - 2.5);
                Canvas.SetTop(circle, point.Y - 2.5);
                ChartCanvas.Children.Add(circle);
            }
        }

        foreach (var minute in GenerateTimeScale(totalMinutes))
        {
            var x = LeftMargin + minute / totalMinutes * usableWidth;
            AddText(FormatTimeScaleLabel(minute), x, height - BottomMargin + 5, HorizontalAlignment.Center);
        }

        DrawPlaybackMarker(totalMinutes, height, usableWidth);
    }

    private void AddLine(double x1, double y1, double x2, double y2)
    {
        ChartCanvas.Children.Add(new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = Brushes.Black
        });
    }

    private void AddText(string text, double x, double y, HorizontalAlignment alignment)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(76, 86, 99))
        };

        ChartCanvas.Children.Add(textBlock);
        Canvas.SetLeft(textBlock, alignment == HorizontalAlignment.Center ? x - 10 : x - 26);
        Canvas.SetTop(textBlock, y);
    }

    private List<ChartDataPoint> GenerateChartData()
    {
        var data = new List<ChartDataPoint>();

        for (var i = 0; i < 10; i++)
            data.Add(new ChartDataPoint(_random.Next(2, 9), _zones[_random.Next(_zones.Count)]));

        return data;
    }

    private IEnumerable<double> GenerateTimeScale(double totalMinutes)
    {
        var step = GetTimeScaleStep(totalMinutes);
        var values = new List<double> { 0 };

        for (var minute = step; minute < totalMinutes; minute += step)
            values.Add(minute);

        var roundedTotal = Math.Ceiling(totalMinutes * 2) / 2;

        if (Math.Abs(values[^1] - roundedTotal) > 0.001)
            values.Add(roundedTotal);

        return values;
    }

    private double GetTimeScaleStep(double totalMinutes)
    {
        if (_chartZoom >= 4.5)
            return 0.5;

        if (_chartZoom >= 3)
            return 2;

        if (_chartZoom >= 1.75)
            return 5;

        if (totalMinutes <= 20)
            return 2;

        if (totalMinutes <= 45)
            return 5;

        return 10;
    }

    private static string FormatTimeScaleLabel(double minute)
    {
        if (minute < 1)
            return $"{minute * 60:0}s";

        if (Math.Abs(minute % 1) < 0.001)
            return $"{minute:0}m";

        var wholeMinutes = (int)Math.Floor(minute);
        var seconds = (minute - wholeMinutes) * 60;
        return $"{wholeMinutes}m{seconds:00}s";
    }

    private List<double> BuildCombinedWaveform()
    {
        if (_combinedWaveformCache is not null)
            return _combinedWaveformCache;

        if (_files.Count == 0)
            return _combinedWaveformCache = [];

        var combined = new List<double>(_files[0].Waveform);

        for (var fileIndex = 1; fileIndex < _files.Count; fileIndex++)
        {
            var waveform = _files[fileIndex].Waveform;

            if (waveform.Count == 0)
                continue;

            if (combined.Count == 0)
            {
                combined.AddRange(waveform);
                continue;
            }

            var overlap = Math.Min(TrackOverlapSeconds, Math.Min(combined.Count, waveform.Count));
            var overlapStart = combined.Count - overlap;

            for (var i = 0; i < overlap; i++)
            {
                var fadeIn = overlap == 1 ? 1 : i / (double)(overlap - 1);
                var fadeOut = 1 - fadeIn;
                combined[overlapStart + i] = combined[overlapStart + i] * fadeOut + waveform[i] * fadeIn;
            }

            for (var i = overlap; i < waveform.Count; i++)
                combined.Add(waveform[i]);
        }

        _combinedWaveformCache = combined;
        return combined;
    }

    private List<Point> BuildWaveformPoints(
        IReadOnlyList<double> combinedWaveform,
        double totalMinutes,
        double usableWidth,
        double usableHeight)
    {
        if (combinedWaveform.Count > 0)
        {
            var reducedWaveform = ReduceWaveformResolution(combinedWaveform, FinalChartSampleSeconds);
            var targetPointCount = Math.Max(2, (int)Math.Ceiling(usableWidth));
            var bucketSize = Math.Max(1, reducedWaveform.Count / (double)targetPointCount);
            var points = new List<Point>(targetPointCount);

            for (var bucketIndex = 0; bucketIndex < targetPointCount; bucketIndex++)
            {
                var start = (int)Math.Floor(bucketIndex * bucketSize);
                var end = Math.Min(reducedWaveform.Count, (int)Math.Floor((bucketIndex + 1) * bucketSize));

                if (start >= reducedWaveform.Count)
                    break;

                if (end <= start)
                    end = Math.Min(reducedWaveform.Count, start + 1);

                double amplitudeSum = 0;

                for (var sampleIndex = start; sampleIndex < end; sampleIndex++)
                    amplitudeSum += reducedWaveform[sampleIndex];

                var amplitude = amplitudeSum / (end - start);
                var sampleCenter = start + (end - start) / 2d;
                var timeInMinutes = sampleCenter * FinalChartSampleSeconds / 60d;
                var x = LeftMargin + timeInMinutes / totalMinutes * usableWidth;
                var y = TopMargin + usableHeight - amplitude * usableHeight;
                points.Add(new Point(x, y));
            }

            return points;
        }

        var fallbackPoints = new List<Point>(_chartData.Count);
        double currentTime = 0;

        foreach (var dataPoint in _chartData)
        {
            var midpointTime = currentTime + dataPoint.Duration / 2;
            var x = LeftMargin + midpointTime / totalMinutes * usableWidth;
            var y = TopMargin + usableHeight - dataPoint.Intensity * usableHeight;
            fallbackPoints.Add(new Point(x, y));
            currentTime += dataPoint.Duration;
        }

        return fallbackPoints;
    }

    private static List<double> ReduceWaveformResolution(IReadOnlyList<double> waveform, int secondsPerPoint)
    {
        if (waveform.Count == 0 || secondsPerPoint <= 1)
            return [.. waveform];

        var reduced = new List<double>((int)Math.Ceiling(waveform.Count / (double)secondsPerPoint));

        for (var start = 0; start < waveform.Count; start += secondsPerPoint)
        {
            var end = Math.Min(waveform.Count, start + secondsPerPoint);
            double sum = 0;

            for (var index = start; index < end; index++)
                sum += waveform[index];

            reduced.Add(sum / (end - start));
        }

        return reduced;
    }

    private void UpdatePositions()
    {
        for (var i = 0; i < _files.Count; i++)
            _files[i].Position = i + 1;
    }

    private void SetChartZoom(double zoom)
    {
        _chartZoom = Math.Clamp(Math.Round(zoom), 1, 10);
        UpdateChartWidth();
        DrawChart();
    }

    private void UpdateChartWidth()
    {
        if (ChartCanvas is null)
            return;

        ChartCanvas.Width = BaseChartWidth * _chartZoom;
        ChartZoomTextBlock.Text = $"{_chartZoom:0.##}x";
    }

    private void DrawPlaybackMarker(double totalMinutes, double chartHeight, double usableWidth)
    {
        if (!_isPlaybackActive || _playingFile is null)
            return;

        var playbackMinutes = GetPlaybackPositionMinutes(_playingFile, _playingPosition);

        if (playbackMinutes < 0 || playbackMinutes > totalMinutes)
            return;

        var x = LeftMargin + playbackMinutes / totalMinutes * usableWidth;

        ChartCanvas.Children.Add(new Line
        {
            X1 = x,
            Y1 = TopMargin,
            X2 = x,
            Y2 = chartHeight - BottomMargin,
            Stroke = Brushes.Black,
            StrokeThickness = 3,
            Opacity = 0.98
        });
    }

    private double GetPlaybackPositionMinutes(Mp3File file, TimeSpan position)
    {
        double elapsedSeconds = 0;

        for (var index = 0; index < _files.Count; index++)
        {
            var currentFile = _files[index];

            if (ReferenceEquals(currentFile, file))
                return (elapsedSeconds + position.TotalSeconds) / 60d;

            elapsedSeconds += currentFile.Waveform.Count;

            if (index < _files.Count - 1)
                elapsedSeconds -= GetOverlapSeconds(currentFile, _files[index + 1]);
        }

        return -1;
    }

    private static int GetOverlapSeconds(Mp3File first, Mp3File second)
    {
        return Math.Min(TrackOverlapSeconds, Math.Min(first.Waveform.Count, second.Waveform.Count));
    }

    private void ExportFinalMix(string outputPath, IProgress<(double Progress, string Message)>? progress = null)
    {
        var readers = new List<AudioFileReader>();
        var providers = new List<ISampleProvider>();
        var targetFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        double startOffsetSeconds = 0;
        double totalOutputSeconds = 0;

        try
        {
            for (var index = 0; index < _files.Count; index++)
            {
                var file = _files[index];
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

                var fadeIn = index > 0 ? TimeSpan.FromSeconds(GetOverlapSeconds(_files[index - 1], file)) : TimeSpan.Zero;
                var fadeOut = index < _files.Count - 1 ? TimeSpan.FromSeconds(GetOverlapSeconds(file, _files[index + 1])) : TimeSpan.Zero;
                sampleProvider = new CrossfadeSampleProvider(sampleProvider, fadeIn, fadeOut, reader.TotalTime);

                providers.Add(new OffsetSampleProvider(sampleProvider)
                {
                    DelayBy = TimeSpan.FromSeconds(startOffsetSeconds)
                });

                startOffsetSeconds += reader.TotalTime.TotalSeconds;
                totalOutputSeconds = Math.Max(totalOutputSeconds, startOffsetSeconds);

                if (index < _files.Count - 1)
                    startOffsetSeconds -= GetOverlapSeconds(file, _files[index + 1]);
            }

            var mixer = new MixingSampleProvider(providers) { ReadFully = false };
            var waveProvider = new SampleToWaveProvider16(mixer);
            var buffer = new byte[waveProvider.WaveFormat.AverageBytesPerSecond * 4];
            var totalBytesEstimate = Math.Max(buffer.Length, (long)Math.Ceiling(totalOutputSeconds * waveProvider.WaveFormat.AverageBytesPerSecond));
            long writtenBytes = 0;

            progress?.Report((0, "Preparing final mix..."));

            using var writer = new LameMP3FileWriter(outputPath, waveProvider.WaveFormat, LAMEPreset.VBR_90);

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

        foreach (var point in _chartData)
        {
            var duration = TimeSpan.FromMinutes(point.Duration);
            var endTime = currentTime + duration;
            var durationSeconds = (int)Math.Round(duration.TotalSeconds);

            builder.AppendLine(
                $"{point.Zone.Name} - {durationSeconds}segs - {FormatReportTime(currentTime)} até {FormatReportTime(endTime)}");

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

    private sealed class CrossfadeSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly long _fadeInSamples;
        private readonly long _fadeOutStartSample;
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

    private void AddChartDataPoint()
    {
        var point = new ChartDataPoint(5, _zones[0]);
        _chartData.Add(point);
        ChartDataPointsGrid.SelectedItem = point;
        ChartDataPointsGrid.ScrollIntoView(point);
    }

    private void MoveChartDataPointUp(ChartDataPoint? item)
    {
        if (item is null)
            return;

        var index = _chartData.IndexOf(item);

        if (index <= 0)
            return;

        _chartData.Move(index, index - 1);
        ChartDataPointsGrid.SelectedItem = item;
    }

    private void MoveChartDataPointDown(ChartDataPoint? item)
    {
        if (item is null)
            return;

        var index = _chartData.IndexOf(item);

        if (index < 0 || index >= _chartData.Count - 1)
            return;

        _chartData.Move(index, index + 1);
        ChartDataPointsGrid.SelectedItem = item;
    }

    private void RemoveChartDataPoint(ChartDataPoint? item)
    {
        if (item is null)
            return;

        _chartData.Remove(item);
    }

    private void UpdateFileSummary()
    {
        if (_files.Count == 0)
        {
            FileSummaryTextBlock.Text = "No files selected.";
            return;
        }

        var totalDuration = TimeSpan.FromTicks(_files.Sum(item => item.Duration.Ticks));
        FileSummaryTextBlock.Text = $"{_files.Count} file(s) loaded | Total duration: {Mp3File.FormatDuration(totalDuration)}";
    }

    private void SelectFile(Mp3File item)
    {
        FilesListBox.SelectedItem = item;
        FilesListBox.ScrollIntoView(item);
    }
}
