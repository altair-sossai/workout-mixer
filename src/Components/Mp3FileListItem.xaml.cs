using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using NAudio.Wave;
using WorkoutMixer.Models;
using ShapePath = System.Windows.Shapes.Path;

namespace WorkoutMixer.Components;

public partial class Mp3FileListItem
{
    private const double BottomMargin = 10;
    private const double LeftMargin = 8;
    private const double RightMargin = 8;
    private const double TopMargin = 8;

    private static Mp3FileListItem? _activePlayer;

    public static readonly DependencyProperty FileProperty = DependencyProperty.Register(nameof(File), typeof(Mp3File), typeof(Mp3FileListItem), new PropertyMetadata(null, OnFileChanged));
    public static readonly DependencyProperty MoveUpCommandProperty = DependencyProperty.Register(nameof(MoveUpCommand), typeof(ICommand), typeof(Mp3FileListItem));
    public static readonly DependencyProperty MoveDownCommandProperty = DependencyProperty.Register(nameof(MoveDownCommand), typeof(ICommand), typeof(Mp3FileListItem));
    public static readonly DependencyProperty RemoveCommandProperty = DependencyProperty.Register(nameof(RemoveCommand), typeof(ICommand), typeof(Mp3FileListItem));

    private readonly DispatcherTimer _playbackTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(200)
    };

    private readonly List<double> _waveformValues = [];

    private AudioFileReader? _audioReader;
    private bool _isSeeking;
    private bool _isUpdatingSlider;
    private WaveOutEvent? _waveOut;

    public static event EventHandler<PlaybackProgressChangedEventArgs>? PlaybackProgressChanged;

    public Mp3FileListItem()
    {
        InitializeComponent();

        Loaded += Mp3FileListItem_Loaded;
        Unloaded += Mp3FileListItem_Unloaded;

        _playbackTimer.Tick += PlaybackTimer_Tick;
    }

    public Mp3File? File
    {
        get => (Mp3File?)GetValue(FileProperty);
        set => SetValue(FileProperty, value);
    }

    public ICommand? MoveUpCommand
    {
        get => (ICommand?)GetValue(MoveUpCommandProperty);
        set => SetValue(MoveUpCommandProperty, value);
    }

    public ICommand? MoveDownCommand
    {
        get => (ICommand?)GetValue(MoveDownCommandProperty);
        set => SetValue(MoveDownCommandProperty, value);
    }

    public ICommand? RemoveCommand
    {
        get => (ICommand?)GetValue(RemoveCommandProperty);
        set => SetValue(RemoveCommandProperty, value);
    }

    private static void OnFileChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs _)
    {
        var control = (Mp3FileListItem)dependencyObject;

        control.ResetPlayback();
        control.LoadWaveform();
        control.UpdatePlaybackUi();
    }

    private void Mp3FileListItem_Loaded(object sender, RoutedEventArgs e)
    {
        LoadWaveform();
        UpdatePlaybackUi();
    }

    private void Mp3FileListItem_Unloaded(object sender, RoutedEventArgs e)
    {
        DisposePlayer();
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (File is null)
            return;

        EnsurePlayer();

        if (_activePlayer is not null && _activePlayer != this)
            _activePlayer.PausePlayback();

        _activePlayer = this;

        _waveOut?.Play();
        _playbackTimer.Start();

        UpdatePlaybackUi();
        PublishPlaybackProgress(true);
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        PausePlayback();
    }

    private void PausePlayback()
    {
        _waveOut?.Pause();
        _playbackTimer.Stop();

        UpdatePlaybackUi();
        PublishPlaybackProgress(false);
    }

    private void SeekSlider_DragStarted(object sender, DragStartedEventArgs e)
    {
        _isSeeking = true;
    }

    private void SeekSlider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _isSeeking = false;
        SeekToSliderValue();
        UpdatePlaybackUi();
        PublishPlaybackProgress(_waveOut?.PlaybackState == PlaybackState.Playing);
    }

    private void SeekSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isSeeking) 
            return;

        SeekToSliderValue();
        UpdatePlaybackUi();
        PublishPlaybackProgress(_waveOut?.PlaybackState == PlaybackState.Playing);
    }

    private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingSlider) 
            return;

        if (_isSeeking)
        {
            CurrentTimeTextBlock.Text = Mp3File.FormatDuration(TimeSpan.FromSeconds(SeekSlider.Value));
            return;
        }

        if (_audioReader is not null && SeekSlider.IsMouseOver && Mouse.LeftButton == MouseButtonState.Pressed)
        {
            SeekToSliderValue();
            UpdatePlaybackUi();
            PublishPlaybackProgress(_waveOut?.PlaybackState == PlaybackState.Playing);
        }
    }

    private void PlaybackTimer_Tick(object? sender, EventArgs e)
    {
        if (_audioReader is null || _isSeeking)
            return;

        _isUpdatingSlider = true;
        SeekSlider.Value = Math.Min(SeekSlider.Maximum, _audioReader.CurrentTime.TotalSeconds);
        _isUpdatingSlider = false;
        CurrentTimeTextBlock.Text = Mp3File.FormatDuration(_audioReader.CurrentTime);
        PublishPlaybackProgress(true);
    }

    private void WaveformCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize is { Width: > 0, Height: > 0 })
            DrawWaveform();
    }

    private void EnsurePlayer()
    {
        if (_audioReader is not null && _waveOut is not null)
            return;

        if (File is null)
            return;

        _audioReader = new AudioFileReader(File.Path);
        _waveOut = new WaveOutEvent();
        _waveOut.Init(_audioReader);
        _waveOut.PlaybackStopped += WaveOut_PlaybackStopped;
    }

    private void WaveOut_PlaybackStopped(object? sender, StoppedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (_audioReader is not null && _audioReader.Position >= _audioReader.Length)
                _audioReader.CurrentTime = TimeSpan.Zero;

            _playbackTimer.Stop();
            UpdatePlaybackUi();
            PublishPlaybackProgress(false);
        });
    }

    private void ResetPlayback()
    {
        DisposePlayer();
        _isUpdatingSlider = true;
        SeekSlider.Value = 0;
        _isUpdatingSlider = false;
        CurrentTimeTextBlock.Text = Mp3File.FormatDuration(TimeSpan.Zero);
        PublishPlaybackProgress(false);
    }

    private void DisposePlayer()
    {
        _playbackTimer.Stop();

        if (_activePlayer == this)
            _activePlayer = null;

        if (_waveOut is not null)
        {
            _waveOut.PlaybackStopped -= WaveOut_PlaybackStopped;
            _waveOut.Stop();
            _waveOut.Dispose();
            _waveOut = null;
        }

        _audioReader?.Dispose();
        _audioReader = null;
        PublishPlaybackProgress(false);
    }

    private void UpdatePlaybackUi()
    {
        if (File is null)
        {
            PlayButton.IsEnabled = false;
            PauseButton.IsEnabled = false;
            SeekSlider.Maximum = 1;
            DurationTextBlock.Text = "00:00";
            CurrentTimeTextBlock.Text = "00:00";
            return;
        }

        DurationTextBlock.Text = File.DurationFormatted;
        SeekSlider.Maximum = Math.Max(1, File.Duration.TotalSeconds);

        if (!_isSeeking)
        {
            var currentTime = _audioReader?.CurrentTime ?? TimeSpan.Zero;
            _isUpdatingSlider = true;
            SeekSlider.Value = Math.Min(SeekSlider.Maximum, currentTime.TotalSeconds);
            _isUpdatingSlider = false;
            CurrentTimeTextBlock.Text = Mp3File.FormatDuration(currentTime);
        }

        var playbackState = _waveOut?.PlaybackState ?? PlaybackState.Stopped;

        PlayButton.IsEnabled = playbackState != PlaybackState.Playing;
        PauseButton.IsEnabled = playbackState == PlaybackState.Playing;
    }

    private void SeekToSliderValue()
    {
        if (_audioReader is null) 
            return;

        _audioReader.CurrentTime = TimeSpan.FromSeconds(SeekSlider.Value);
        CurrentTimeTextBlock.Text = Mp3File.FormatDuration(_audioReader.CurrentTime);
    }

    private void PublishPlaybackProgress(bool isPlaying)
    {
        if (File is null)
            return;

        PlaybackProgressChanged?.Invoke(
            this,
            new PlaybackProgressChangedEventArgs(
                File,
                _audioReader?.CurrentTime ?? TimeSpan.Zero,
                isPlaying));
    }

    private void LoadWaveform()
    {
        _waveformValues.Clear();

        if (File is null)
        {
            DrawWaveform();
            return;
        }

        _waveformValues.AddRange(File.Waveform);

        DrawWaveform();
    }

    private void DrawWaveform()
    {
        if (WaveformCanvas.ActualWidth <= 0 || WaveformCanvas.ActualHeight <= 0)
            return;

        WaveformCanvas.Children.Clear();

        var width = WaveformCanvas.ActualWidth;
        var height = WaveformCanvas.ActualHeight;
        var usableWidth = width - LeftMargin - RightMargin;
        var usableHeight = height - TopMargin - BottomMargin;

        if (usableWidth <= 0 || usableHeight <= 0)
            return;

        foreach (var guide in new[] { 0.0, 0.5, 1.0 })
        {
            var y = TopMargin + usableHeight - guide * usableHeight;

            WaveformCanvas.Children.Add(new Line
            {
                X1 = LeftMargin,
                Y1 = y,
                X2 = width - RightMargin,
                Y2 = y,
                Stroke = new SolidColorBrush(Color.FromRgb(224, 232, 241)),
                StrokeThickness = guide == 0 ? 1.4 : 1
            });
        }

        if (_waveformValues.Count == 0)
            return;

        var points = new List<Point>(_waveformValues.Count);

        for (var i = 0; i < _waveformValues.Count; i++)
        {
            var value = _waveformValues[i];
            var x = _waveformValues.Count == 1
                ? LeftMargin + usableWidth / 2
                : LeftMargin + i / (double)(_waveformValues.Count - 1) * usableWidth;
            var y = TopMargin + usableHeight - value * usableHeight;
            points.Add(new Point(x, y));
        }

        var areaFigure = new PathFigure
        {
            StartPoint = new Point(LeftMargin, height - BottomMargin)
        };

        foreach (var point in points)
            areaFigure.Segments.Add(new LineSegment(point, true));

        areaFigure.Segments.Add(new LineSegment(new Point(width - RightMargin, height - BottomMargin), true));

        var areaPath = new ShapePath
        {
            Fill = new SolidColorBrush(Color.FromArgb(55, 31, 111, 235)),
            Data = new PathGeometry([areaFigure])
        };

        WaveformCanvas.Children.Add(areaPath);

        var lineGeometry = new PathGeometry();
        var lineFigure = new PathFigure { StartPoint = points[0] };
        const double smoothing = 0.18;

        for (var i = 0; i < points.Count - 1; i++)
        {
            var p0 = i > 0 ? points[i - 1] : points[i];
            var p1 = points[i];
            var p2 = points[i + 1];
            var p3 = i < points.Count - 2 ? points[i + 2] : p2;

            var controlPoint1 = new Point(
                p1.X + (p2.X - p0.X) * smoothing,
                p1.Y + (p2.Y - p0.Y) * smoothing);

            var controlPoint2 = new Point(
                p2.X - (p3.X - p1.X) * smoothing,
                p2.Y - (p3.Y - p1.Y) * smoothing);

            lineFigure.Segments.Add(new BezierSegment(controlPoint1, controlPoint2, p2, true));
        }

        lineGeometry.Figures.Add(lineFigure);

        WaveformCanvas.Children.Add(new ShapePath
        {
            Data = lineGeometry,
            Stroke = new SolidColorBrush(Color.FromRgb(31, 111, 235)),
            StrokeThickness = 2,
            Opacity = 0.95
        });
    }
}

public sealed class PlaybackProgressChangedEventArgs(Mp3File file, TimeSpan position, bool isPlaying) : EventArgs
{
    public Mp3File File { get; } = file;
    public TimeSpan Position { get; } = position;
    public bool IsPlaying { get; } = isPlaying;
}
