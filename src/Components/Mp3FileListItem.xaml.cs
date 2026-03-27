using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using NAudio.Wave;
using WorkoutMixer.Components.EventArgs;
using WorkoutMixer.Models;

namespace WorkoutMixer.Components;

public partial class Mp3FileListItem
{
    private static Mp3FileListItem? _activePlayer;

    public static readonly DependencyProperty FileProperty = DependencyProperty.Register(nameof(File), typeof(Mp3File), typeof(Mp3FileListItem), new PropertyMetadata(null, OnFileChanged));
    public static readonly DependencyProperty MoveUpCommandProperty = DependencyProperty.Register(nameof(MoveUpCommand), typeof(ICommand), typeof(Mp3FileListItem));
    public static readonly DependencyProperty MoveDownCommandProperty = DependencyProperty.Register(nameof(MoveDownCommand), typeof(ICommand), typeof(Mp3FileListItem));
    public static readonly DependencyProperty RemoveCommandProperty = DependencyProperty.Register(nameof(RemoveCommand), typeof(ICommand), typeof(Mp3FileListItem));

    private readonly DispatcherTimer _playbackTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(200)
    };

    private AudioFileReader? _audioReader;
    private bool _isSeeking;
    private bool _isUpdatingSlider;
    private WaveOutEvent? _waveOut;

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

    public static event EventHandler<PlaybackProgressChangedEventArgs>? PlaybackProgressChanged;

    private static void OnFileChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs _)
    {
        var control = (Mp3FileListItem)dependencyObject;

        control.ResetPlayback();
        control.UpdatePlaybackUi();
    }

    private void Mp3FileListItem_Loaded(object sender, RoutedEventArgs e)
    {
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
            UpdateCurrentTimeText(TimeSpan.FromSeconds(SeekSlider.Value));
            return;
        }

        if (_audioReader is not null && SeekSlider.IsMouseOver && Mouse.LeftButton == MouseButtonState.Pressed)
        {
            SeekToSliderValue();
            UpdatePlaybackUi();
            PublishPlaybackProgress(_waveOut?.PlaybackState == PlaybackState.Playing);
        }
    }

    private void PlaybackTimer_Tick(object? sender, System.EventArgs e)
    {
        if (_audioReader is null || _isSeeking)
            return;

        _isUpdatingSlider = true;
        SeekSlider.Value = Math.Min(SeekSlider.Maximum, _audioReader.CurrentTime.TotalSeconds);
        _isUpdatingSlider = false;
        UpdateCurrentTimeText(_audioReader.CurrentTime);
        PublishPlaybackProgress(true);
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
            CurrentTimeTextBlock.Text = "00:00 / 00:00";
            return;
        }

        SeekSlider.Maximum = Math.Max(1, File.Duration.TotalSeconds);

        if (!_isSeeking)
        {
            var currentTime = _audioReader?.CurrentTime ?? TimeSpan.Zero;
            _isUpdatingSlider = true;
            SeekSlider.Value = Math.Min(SeekSlider.Maximum, currentTime.TotalSeconds);
            _isUpdatingSlider = false;
            UpdateCurrentTimeText(currentTime);
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
        UpdateCurrentTimeText(_audioReader.CurrentTime);
    }

    private void UpdateCurrentTimeText(TimeSpan currentTime)
    {
        var durationText = File?.DurationFormatted ?? "00:00";
        CurrentTimeTextBlock.Text = $"{Mp3File.FormatDuration(currentTime)} / {durationText}";
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
}