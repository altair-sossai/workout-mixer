using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WorkoutMixer.Configuration;
using WorkoutMixer.Components.EventArgs;
using WorkoutMixer.Models;
using ShapePath = System.Windows.Shapes.Path;

namespace WorkoutMixer.Components;

public partial class WorkoutChartView
{
    private const double BottomMargin = 32;
    private const double RightMargin = 18;
    private const double LeftMargin = 34;
    private const double TopMargin = 12;
    private const double BaseChartWidth = 1200;

    public static readonly DependencyProperty ChartDataPointsProperty = DependencyProperty.Register(
        nameof(ChartDataPoints),
        typeof(IEnumerable<ChartDataPoint>),
        typeof(WorkoutChartView),
        new PropertyMetadata(null, OnChartDataPointsChanged));

    public static readonly DependencyProperty FilesProperty = DependencyProperty.Register(
        nameof(Files),
        typeof(IEnumerable<Mp3File>),
        typeof(WorkoutChartView),
        new PropertyMetadata(null, OnFilesChanged));

    private readonly Dictionary<Mp3File, TimeSpan> _activePlaybackPositions = [];

    private INotifyCollectionChanged? _chartDataCollection;

    private double _chartZoom = 1;
    private List<double>? _combinedWaveformCache;
    private INotifyCollectionChanged? _filesCollection;
    private Mp3File? _playingFile;
    private TimeSpan _playingPosition;
    private readonly AudioOptions _audioOptions;
    private readonly WaveformOptions _waveformOptions;

    public WorkoutChartView()
    {
        _audioOptions = App.Services.GetRequiredService<IOptions<AudioOptions>>().Value;
        _waveformOptions = App.Services.GetRequiredService<IOptions<WaveformOptions>>().Value;
        InitializeComponent();

        Loaded += WorkoutChartView_Loaded;
        Unloaded += WorkoutChartView_Unloaded;
        ChartCanvas.SizeChanged += ChartCanvas_SizeChanged;
    }

    public IEnumerable<ChartDataPoint>? ChartDataPoints
    {
        get => (IEnumerable<ChartDataPoint>?)GetValue(ChartDataPointsProperty);
        set => SetValue(ChartDataPointsProperty, value);
    }

    public IEnumerable<Mp3File>? Files
    {
        get => (IEnumerable<Mp3File>?)GetValue(FilesProperty);
        set => SetValue(FilesProperty, value);
    }

    private static void OnChartDataPointsChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var control = (WorkoutChartView)dependencyObject;
        control.UnsubscribeFromChartData(e.OldValue as IEnumerable<ChartDataPoint>);
        control.SubscribeToChartData(e.NewValue as IEnumerable<ChartDataPoint>);
        control.DrawChart();
    }

    private static void OnFilesChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var control = (WorkoutChartView)dependencyObject;
        control.UnsubscribeFromFiles();
        control.SubscribeToFiles(e.NewValue as IEnumerable<Mp3File>);
        control._combinedWaveformCache = null;
        control.DrawChart();
    }

    private void WorkoutChartView_Loaded(object sender, RoutedEventArgs e)
    {
        Mp3FileListItem.PlaybackProgressChanged += Mp3FileListItem_PlaybackProgressChanged;
        UpdateChartWidth();
        DrawChart();
    }

    private void WorkoutChartView_Unloaded(object sender, RoutedEventArgs e)
    {
        Mp3FileListItem.PlaybackProgressChanged -= Mp3FileListItem_PlaybackProgressChanged;
        UnsubscribeFromChartData(ChartDataPoints);
        UnsubscribeFromFiles();
    }

    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize is { Width: > 0, Height: > 0 })
            DrawChart();
    }

    private void ChartCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ChartCanvas.ActualWidth <= 0 || ChartCanvas.ActualHeight <= 0)
            return;

        var usableWidth = ChartCanvas.ActualWidth - LeftMargin - RightMargin;

        if (usableWidth <= 0)
            return;

        var x = Math.Clamp(e.GetPosition(ChartCanvas).X, LeftMargin, ChartCanvas.ActualWidth - RightMargin);
        var chartData = GetChartDataPoints();
        var chartDurationMinutes = Math.Max(1, chartData.Sum(point => point.Duration));
        var waveformDurationMinutes = Math.Max(0, BuildCombinedWaveform().Count / 60d);
        var totalMinutes = Math.Max(chartDurationMinutes, Math.Max(1, waveformDurationMinutes));
        var timelineSeconds = (x - LeftMargin) / usableWidth * totalMinutes * 60d;

        if (Mp3FileListItem.TryPlayTimelinePosition(GetFiles(), timelineSeconds))
            e.Handled = true;
    }

    private void ZoomOutChart_Click(object sender, RoutedEventArgs e)
    {
        SetChartZoom(_chartZoom - 0.5);
    }

    private void ResetChartZoom_Click(object sender, RoutedEventArgs e)
    {
        SetChartZoom(1);
    }

    private void ZoomInChart_Click(object sender, RoutedEventArgs e)
    {
        SetChartZoom(_chartZoom + 0.5);
    }

    private void SubscribeToChartData(IEnumerable<ChartDataPoint>? points)
    {
        if (points is null)
            return;

        _chartDataCollection = points as INotifyCollectionChanged;

        if (_chartDataCollection is not null)
            _chartDataCollection.CollectionChanged += ChartData_CollectionChanged;

        foreach (var point in points)
            point.PropertyChanged += ChartDataPoint_PropertyChanged;
    }

    private void UnsubscribeFromChartData(IEnumerable<ChartDataPoint>? points)
    {
        if (_chartDataCollection is not null)
        {
            _chartDataCollection.CollectionChanged -= ChartData_CollectionChanged;
            _chartDataCollection = null;
        }

        if (points is null)
            return;

        foreach (var point in points)
            point.PropertyChanged -= ChartDataPoint_PropertyChanged;
    }

    private void SubscribeToFiles(IEnumerable<Mp3File>? files)
    {
        if (files is null)
            return;

        _filesCollection = files as INotifyCollectionChanged;

        if (_filesCollection is not null)
            _filesCollection.CollectionChanged += Files_CollectionChanged;
    }

    private void UnsubscribeFromFiles()
    {
        if (_filesCollection is not null)
        {
            _filesCollection.CollectionChanged -= Files_CollectionChanged;
            _filesCollection = null;
        }
    }

    private void Files_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _combinedWaveformCache = null;

        var files = GetFiles();
        var removedFiles = _activePlaybackPositions.Keys.Where(file => !files.Contains(file)).ToList();

        foreach (var removedFile in removedFiles)
            _activePlaybackPositions.Remove(removedFile);

        if (_playingFile is not null && !files.Contains(_playingFile))
        {
            _playingFile = null;
            _playingPosition = TimeSpan.Zero;
        }

        DrawChart();
    }

    private void ChartData_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (ChartDataPoint point in e.OldItems)
                point.PropertyChanged -= ChartDataPoint_PropertyChanged;
        }

        if (e.NewItems is not null)
        {
            foreach (ChartDataPoint point in e.NewItems)
                point.PropertyChanged += ChartDataPoint_PropertyChanged;
        }

        DrawChart();
    }

    private void ChartDataPoint_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChartDataPoint.Duration) or nameof(ChartDataPoint.Zone) or nameof(ChartDataPoint.Intensity))
            DrawChart();
    }

    private void Mp3FileListItem_PlaybackProgressChanged(object? sender, PlaybackProgressChangedEventArgs e)
    {
        if (e.IsPlaying)
            _activePlaybackPositions[e.File] = e.Position;
        else
            _activePlaybackPositions.Remove(e.File);

        var activePlayback = _activePlaybackPositions
            .Select(item => new
            {
                File = item.Key,
                Position = item.Value,
                AbsoluteSeconds = GetPlaybackPositionMinutes(item.Key, item.Value) * 60d
            })
            .Where(item => item.AbsoluteSeconds >= 0)
            .OrderByDescending(item => item.AbsoluteSeconds)
            .FirstOrDefault();

        if (activePlayback is null)
        {
            _playingFile = null;
            _playingPosition = TimeSpan.Zero;
        }
        else
        {
            _playingFile = activePlayback.File;
            _playingPosition = activePlayback.Position;
        }

        DrawChart();

        if (activePlayback is not null)
            EnsurePlaybackMarkerVisible();
    }

    private void DrawChart()
    {
        if (ChartCanvas.ActualWidth <= 0 || ChartCanvas.ActualHeight <= 0)
            return;

        ChartCanvas.Children.Clear();

        var chartData = GetChartDataPoints();
        var files = GetFiles();
        var width = ChartCanvas.ActualWidth;
        var height = ChartCanvas.ActualHeight;
        var usableWidth = width - LeftMargin - RightMargin;
        var usableHeight = height - TopMargin - BottomMargin;
        var chartDurationMinutes = Math.Max(1, chartData.Sum(point => point.Duration));
        var combinedWaveform = BuildCombinedWaveform();
        var fileTimeline = BuildFileTimeline(files);
        var waveformDurationMinutes = Math.Max(0, combinedWaveform.Count / 60d);
        var totalMinutes = Math.Max(chartDurationMinutes, Math.Max(1, waveformDurationMinutes));

        if (usableWidth <= 0 || usableHeight <= 0)
            return;

        AddLine(LeftMargin, height - BottomMargin, width - RightMargin, height - BottomMargin);
        AddLine(LeftMargin, TopMargin, LeftMargin, height - BottomMargin);

        foreach (var value in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
        {
            var y = TopMargin + usableHeight - value * usableHeight;
            AddText($"{value:P0}", LeftMargin - 6, y - 8, HorizontalAlignment.Right);
        }

        if (chartData.Count == 0)
        {
            AddText("No chart data configured", LeftMargin + 8, TopMargin + 8, HorizontalAlignment.Left);
            return;
        }

        double currentTime = 0;

        foreach (var dataPoint in chartData)
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
                Opacity = 0.58,
                ToolTip = $"{dataPoint.Zone.Name}\nDuration: {dataPoint.Duration:0.##} min\nIntensity: {dataPoint.Intensity:0.00}"
            };

            Canvas.SetLeft(rectangle, x);
            Canvas.SetTop(rectangle, y);
            ChartCanvas.Children.Add(rectangle);

            currentTime += dataPoint.Duration;
        }

        DrawTrackRanges(fileTimeline, totalMinutes, usableWidth, usableHeight);
        DrawWaveformSegments(fileTimeline, totalMinutes, usableWidth, usableHeight, chartData);

        foreach (var minute in GenerateTimeScale(totalMinutes))
        {
            var x = LeftMargin + minute / totalMinutes * usableWidth;
            AddText(FormatTimeScaleLabel(minute), x, height - BottomMargin + 5, HorizontalAlignment.Center);
        }

        DrawPlaybackMarker(totalMinutes, height, usableWidth);
    }

    private void DrawTrackRanges(
        IReadOnlyList<FileTimelineSegment> fileTimeline,
        double totalMinutes,
        double usableWidth,
        double usableHeight)
    {
        foreach (var segment in fileTimeline)
        {
            var startMinutes = segment.StartSeconds / 60d;
            var durationMinutes = (segment.EndSeconds - segment.StartSeconds) / 60d;
            var x = LeftMargin + startMinutes / totalMinutes * usableWidth;
            var width = Math.Max(2, durationMinutes / totalMinutes * usableWidth);

            var background = new Rectangle
            {
                Width = width,
                Height = usableHeight,
                Fill = segment.File.AccentBrush,
                Opacity = 0.06,
                IsHitTestVisible = false
            };

            Canvas.SetLeft(background, x);
            Canvas.SetTop(background, TopMargin);
            ChartCanvas.Children.Add(background);

            var marker = new Rectangle
            {
                Width = width,
                Height = 8,
                Fill = segment.File.AccentBrush,
                Opacity = 0.72,
                ToolTip = $"#{segment.File.Position} {segment.File.FileName}"
            };

            Canvas.SetLeft(marker, x);
            Canvas.SetTop(marker, TopMargin + 2);
            ChartCanvas.Children.Add(marker);

            if (width >= 52)
                AddTrackLabel($"#{segment.File.Position}", x + 4, TopMargin + 1, segment.File.AccentBrush);
        }
    }

    private void DrawWaveformSegments(
        IReadOnlyList<FileTimelineSegment> fileTimeline,
        double totalMinutes,
        double usableWidth,
        double usableHeight,
        IReadOnlyList<ChartDataPoint> chartData)
    {
        if (fileTimeline.Count == 0)
        {
            var fallbackPoints = BuildWaveformPoints([], totalMinutes, usableWidth, usableHeight, chartData);

            if (fallbackPoints.Count > 1)
                AddWaveformPath(fallbackPoints, new SolidColorBrush(Color.FromRgb(88, 88, 88)), false);

            return;
        }

        foreach (var segment in fileTimeline)
        {
            var points = BuildWaveformPointsForFile(segment, totalMinutes, usableWidth, usableHeight);

            if (points.Count <= 1)
                continue;

            AddWaveformPath(points, segment.File.AccentBrush, ReferenceEquals(segment.File, _playingFile));
        }
    }

    private void AddWaveformPath(IReadOnlyList<Point> points, Brush stroke, bool isPlaying)
    {
        var path = new ShapePath
        {
            Stroke = stroke,
            Opacity = isPlaying ? 1 : 0.84,
            StrokeThickness = isPlaying ? 3.1 : 2.25,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round
        };

        var figure = new PathFigure { StartPoint = points[0], IsClosed = false, IsFilled = false };

        for (var i = 1; i < points.Count; i++)
            figure.Segments.Add(new LineSegment(points[i], true));

        path.Data = new PathGeometry([figure]);
        ChartCanvas.Children.Add(path);
    }

    private void AddTrackLabel(string text, double x, double y, Brush foreground)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = foreground,
            IsHitTestVisible = false
        };

        ChartCanvas.Children.Add(textBlock);
        Canvas.SetLeft(textBlock, x);
        Canvas.SetTop(textBlock, y);
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

        var files = GetFiles();

        if (files.Count == 0)
            return _combinedWaveformCache = [];

        var combined = new List<double>(files[0].Waveform);

        for (var fileIndex = 1; fileIndex < files.Count; fileIndex++)
        {
            var waveform = files[fileIndex].Waveform;

            if (waveform.Count == 0)
                continue;

            if (combined.Count == 0)
            {
                combined.AddRange(waveform);
                continue;
            }

            var overlap = Math.Min(_audioOptions.TrackOverlapSeconds, Math.Min(combined.Count, waveform.Count));
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

    private IReadOnlyList<FileTimelineSegment> BuildFileTimeline(IReadOnlyList<Mp3File> files)
    {
        if (files.Count == 0)
            return [];

        var segments = new List<FileTimelineSegment>(files.Count);
        double startSeconds = 0;

        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            var durationSeconds = file.Waveform.Count;
            segments.Add(new FileTimelineSegment(file, startSeconds, startSeconds + durationSeconds));

            startSeconds += durationSeconds;

            if (index < files.Count - 1)
                startSeconds -= GetOverlapSeconds(file, files[index + 1]);
        }

        return segments;
    }

    private List<Point> BuildWaveformPoints(
        IReadOnlyList<double> combinedWaveform,
        double totalMinutes,
        double usableWidth,
        double usableHeight,
        IReadOnlyList<ChartDataPoint> chartData)
    {
        if (combinedWaveform.Count > 0)
        {
            var reducedWaveform = ReduceWaveformResolution(combinedWaveform, _waveformOptions.FinalChartSampleSeconds);
            var smoothedWaveform = SmoothWaveform(reducedWaveform, _waveformOptions.SmoothingRadius);
            var targetPointCount = Math.Max(2, (int)Math.Ceiling(usableWidth / _waveformOptions.PointReductionFactor));
            var bucketSize = Math.Max(1, smoothedWaveform.Count / (double)targetPointCount);
            var points = new List<Point>(targetPointCount);

            for (var bucketIndex = 0; bucketIndex < targetPointCount; bucketIndex++)
            {
                var start = (int)Math.Floor(bucketIndex * bucketSize);
                var end = Math.Min(smoothedWaveform.Count, (int)Math.Floor((bucketIndex + 1) * bucketSize));

                if (start >= smoothedWaveform.Count)
                    break;

                if (end <= start)
                    end = Math.Min(smoothedWaveform.Count, start + 1);

                double amplitudeSum = 0;

                for (var sampleIndex = start; sampleIndex < end; sampleIndex++)
                    amplitudeSum += smoothedWaveform[sampleIndex];

                var amplitude = amplitudeSum / (end - start);
                var sampleCenter = start + (end - start) / 2d;
                var timeInMinutes = sampleCenter * _waveformOptions.FinalChartSampleSeconds / 60d;
                var x = LeftMargin + timeInMinutes / totalMinutes * usableWidth;
                var y = TopMargin + usableHeight - amplitude * usableHeight;
                points.Add(new Point(x, y));
            }

            return points;
        }

        var fallbackPoints = new List<Point>(chartData.Count);
        double currentTime = 0;

        foreach (var dataPoint in chartData)
        {
            var midpointTime = currentTime + dataPoint.Duration / 2;
            var x = LeftMargin + midpointTime / totalMinutes * usableWidth;
            var y = TopMargin + usableHeight - dataPoint.Intensity * usableHeight;
            fallbackPoints.Add(new Point(x, y));
            currentTime += dataPoint.Duration;
        }

        return fallbackPoints;
    }

    private List<Point> BuildWaveformPointsForFile(
        FileTimelineSegment segment,
        double totalMinutes,
        double usableWidth,
        double usableHeight)
    {
        var waveform = segment.File.Waveform;

        if (waveform.Count == 0)
            return [];

        var reducedWaveform = ReduceWaveformResolution(waveform, _waveformOptions.FinalChartSampleSeconds);
        var smoothedWaveform = SmoothWaveform(reducedWaveform, _waveformOptions.SmoothingRadius);
        var widthInPixels = Math.Max(16, (segment.EndSeconds - segment.StartSeconds) / 60d / totalMinutes * usableWidth);
        var targetPointCount = Math.Max(2, (int)Math.Ceiling(widthInPixels / _waveformOptions.PointReductionFactor));
        var bucketSize = Math.Max(1, smoothedWaveform.Count / (double)targetPointCount);
        var points = new List<Point>(targetPointCount);

        for (var bucketIndex = 0; bucketIndex < targetPointCount; bucketIndex++)
        {
            var start = (int)Math.Floor(bucketIndex * bucketSize);
            var end = Math.Min(smoothedWaveform.Count, (int)Math.Floor((bucketIndex + 1) * bucketSize));

            if (start >= smoothedWaveform.Count)
                break;

            if (end <= start)
                end = Math.Min(smoothedWaveform.Count, start + 1);

            double amplitudeSum = 0;

            for (var sampleIndex = start; sampleIndex < end; sampleIndex++)
                amplitudeSum += smoothedWaveform[sampleIndex];

            var amplitude = amplitudeSum / (end - start);
            var sampleCenter = start + (end - start) / 2d;
            var timeInMinutes = (segment.StartSeconds + sampleCenter * _waveformOptions.FinalChartSampleSeconds) / 60d;
            var x = LeftMargin + timeInMinutes / totalMinutes * usableWidth;
            var y = TopMargin + usableHeight - amplitude * usableHeight;
            points.Add(new Point(x, y));
        }

        return points;
    }

    private static List<double> SmoothWaveform(IReadOnlyList<double> waveform, int radius)
    {
        if (waveform.Count == 0 || radius <= 0)
            return [.. waveform];

        var smoothed = new List<double>(waveform.Count);

        for (var index = 0; index < waveform.Count; index++)
        {
            var start = Math.Max(0, index - radius);
            var end = Math.Min(waveform.Count - 1, index + radius);
            double sum = 0;

            for (var sampleIndex = start; sampleIndex <= end; sampleIndex++)
                sum += waveform[sampleIndex];

            smoothed.Add(sum / (end - start + 1));
        }

        return smoothed;
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

    private void SetChartZoom(double zoom)
    {
        _chartZoom = Math.Clamp(Math.Round(zoom * 2, MidpointRounding.AwayFromZero) / 2, 1, 10);
        UpdateChartWidth();
        DrawChart();
    }

    private void UpdateChartWidth()
    {
        ChartCanvas.Width = BaseChartWidth * _chartZoom;
        ChartZoomTextBlock.Text = $"{_chartZoom:0.##}x";
    }

    private void DrawPlaybackMarker(double totalMinutes, double chartHeight, double usableWidth)
    {
        if (_playingFile is null)
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
            Stroke = _playingFile.AccentBrush,
            StrokeThickness = 3,
            Opacity = 0.98
        });
    }

    private void EnsurePlaybackMarkerVisible()
    {
        if (_playingFile is null)
            return;

        var markerX = GetPlaybackMarkerCanvasX();

        if (markerX is null)
            return;

        var left = ChartScrollViewer.HorizontalOffset;
        var right = left + ChartScrollViewer.ViewportWidth;

        if (markerX >= left && markerX <= right)
            return;

        var targetOffset = Math.Clamp(
            markerX.Value - ChartScrollViewer.ViewportWidth / 2,
            0,
            Math.Max(0, ChartScrollViewer.ExtentWidth - ChartScrollViewer.ViewportWidth));

        ChartScrollViewer.ScrollToHorizontalOffset(targetOffset);
    }

    private double? GetPlaybackMarkerCanvasX()
    {
        if (_playingFile is null || ChartCanvas.ActualWidth <= 0 || ChartCanvas.ActualHeight <= 0)
            return null;

        var width = ChartCanvas.ActualWidth;
        var usableWidth = width - LeftMargin - RightMargin;
        var chartData = GetChartDataPoints();
        var chartDurationMinutes = Math.Max(1, chartData.Sum(point => point.Duration));
        var combinedWaveform = BuildCombinedWaveform();
        var waveformDurationMinutes = Math.Max(0, combinedWaveform.Count / 60d);
        var totalMinutes = Math.Max(chartDurationMinutes, Math.Max(1, waveformDurationMinutes));
        var playbackMinutes = GetPlaybackPositionMinutes(_playingFile, _playingPosition);

        if (usableWidth <= 0 || playbackMinutes < 0 || playbackMinutes > totalMinutes)
            return null;

        return LeftMargin + playbackMinutes / totalMinutes * usableWidth;
    }

    private double GetPlaybackPositionMinutes(Mp3File file, TimeSpan position)
    {
        var files = GetFiles();
        double elapsedSeconds = 0;

        for (var index = 0; index < files.Count; index++)
        {
            var currentFile = files[index];

            if (ReferenceEquals(currentFile, file))
                return (elapsedSeconds + position.TotalSeconds) / 60d;

            elapsedSeconds += currentFile.Waveform.Count;

            if (index < files.Count - 1)
                elapsedSeconds -= GetOverlapSeconds(currentFile, files[index + 1]);
        }

        return -1;
    }

    private int GetOverlapSeconds(Mp3File first, Mp3File second)
    {
        return Math.Min(_audioOptions.TrackOverlapSeconds, Math.Min(first.Waveform.Count, second.Waveform.Count));
    }

    private IReadOnlyList<ChartDataPoint> GetChartDataPoints()
    {
        return ChartDataPoints?.ToList() ?? [];
    }

    private IReadOnlyList<Mp3File> GetFiles()
    {
        return Files?.ToList() ?? [];
    }

    private sealed record FileTimelineSegment(Mp3File File, double StartSeconds, double EndSeconds);
}
