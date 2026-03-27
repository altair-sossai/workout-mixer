using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NAudio.Wave;
using WorkoutMixer.Configuration;
using WorkoutMixer.Components.EventArgs;
using WorkoutMixer.Models;

namespace WorkoutMixer.Components;

public partial class Mp3FileListItem
{
    private static readonly HashSet<Mp3FileListItem> LoadedItems = [];

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
    private Mp3FileListItem? _nextCrossfadeItem;
    private WaveOutEvent? _waveOut;
    private readonly AudioOptions _audioOptions;

    public Mp3FileListItem()
    {
        _audioOptions = App.Services.GetRequiredService<IOptions<AudioOptions>>().Value;
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

    public static bool TryPlayTimelinePosition(IReadOnlyList<Mp3File> files, double timelineSeconds)
    {
        if (files.Count == 0)
            return false;

        var segments = BuildTimeline(files);
        var clampedSeconds = Math.Clamp(timelineSeconds, 0, Math.Max(0, segments[^1].EndSeconds));
        var targetIndex = segments.FindLastIndex(segment => clampedSeconds >= segment.StartSeconds);

        if (targetIndex < 0)
            return false;

        var targetSegment = segments[targetIndex];
        var targetItem = GetLoadedItem(targetSegment.File);

        if (targetItem is null)
            return false;

        foreach (var item in LoadedItems.ToList())
            item.StopPlayback(true);

        if (targetIndex > 0)
        {
            var previousSegment = segments[targetIndex - 1];
            var overlapStart = targetSegment.StartSeconds;
            var overlapEnd = Math.Min(previousSegment.EndSeconds, targetSegment.EndSeconds);

            if (clampedSeconds >= overlapStart && clampedSeconds < overlapEnd)
            {
                var previousItem = GetLoadedItem(previousSegment.File);

                if (previousItem is not null)
                {
                    var overlapDuration = Math.Max(0.001, overlapEnd - overlapStart);
                    var progress = Math.Clamp((clampedSeconds - overlapStart) / overlapDuration, 0, 1);

                    previousItem.StartPlaybackAt(
                        TimeSpan.FromSeconds(clampedSeconds - previousSegment.StartSeconds),
                        1 - progress,
                        false);

                    targetItem.StartPlaybackAt(
                        TimeSpan.FromSeconds(clampedSeconds - targetSegment.StartSeconds),
                        progress,
                        false);

                    previousItem._nextCrossfadeItem = targetItem;
                    return true;
                }
            }
        }

        targetItem.StartPlaybackAt(
            TimeSpan.FromSeconds(clampedSeconds - targetSegment.StartSeconds),
            1,
            false);

        return true;
    }

    private static void OnFileChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs _)
    {
        var control = (Mp3FileListItem)dependencyObject;

        control.ResetPlayback();
        control.UpdatePlaybackUi();
    }

    private void Mp3FileListItem_Loaded(object sender, RoutedEventArgs e)
    {
        LoadedItems.Add(this);
        UpdatePlaybackUi();
    }

    private void Mp3FileListItem_Unloaded(object sender, RoutedEventArgs e)
    {
        LoadedItems.Remove(this);
        DisposePlayer();
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (File is null)
            return;

        StopOtherPlayers();
        StartPlayback(false);
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        PausePlayback();
    }

    private void PausePlayback()
    {
        _nextCrossfadeItem?.PausePlayback();
        _waveOut?.Pause();
        _playbackTimer.Stop();

        _nextCrossfadeItem = null;
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

        ProcessCrossfadePreview();

        _isUpdatingSlider = true;
        SeekSlider.Value = Math.Min(SeekSlider.Maximum, _audioReader.CurrentTime.TotalSeconds);
        _isUpdatingSlider = false;
        UpdateCurrentTimeText(_audioReader.CurrentTime);
        PublishPlaybackProgress(true);
    }

    private void StartPlayback(bool stopOthers, double volume = 1)
    {
        if (File is null)
            return;

        if (stopOthers)
            StopOtherPlayers();

        EnsurePlayer();

        _audioReader!.Volume = (float)Math.Clamp(volume, 0, 1);
        _waveOut?.Play();
        _playbackTimer.Start();

        UpdatePlaybackUi();
        PublishPlaybackProgress(true);
    }

    private void StartPlaybackAt(TimeSpan position, double volume, bool stopOthers)
    {
        if (File is null)
            return;

        if (stopOthers)
            StopOtherPlayers();

        EnsurePlayer();

        var safePosition = TimeSpan.FromSeconds(Math.Clamp(position.TotalSeconds, 0, File.Duration.TotalSeconds));
        _audioReader!.CurrentTime = safePosition;
        _audioReader.Volume = (float)Math.Clamp(volume, 0, 1);
        _nextCrossfadeItem = null;
        _waveOut?.Play();
        _playbackTimer.Start();

        UpdatePlaybackUi();
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
            {
                _audioReader.CurrentTime = TimeSpan.Zero;
                _audioReader.Volume = 1;
            }

            _playbackTimer.Stop();
            _nextCrossfadeItem = null;
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
        _nextCrossfadeItem = null;

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
        _audioReader.Volume = 1;
        _nextCrossfadeItem = null;
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

    private void ProcessCrossfadePreview()
    {
        if (_audioReader is null || File is null || _waveOut?.PlaybackState != PlaybackState.Playing)
            return;

        var remaining = _audioReader.TotalTime - _audioReader.CurrentTime;

        if (remaining <= TimeSpan.Zero)
            return;

        var overlap = TimeSpan.FromSeconds(Math.Min(_audioOptions.TrackOverlapSeconds, File.Duration.TotalSeconds));

        if (remaining > overlap)
        {
            _audioReader.Volume = 1;
            return;
        }

        var nextItem = _nextCrossfadeItem ?? GetNextItem();

        if (nextItem is null)
        {
            _audioReader.Volume = 1;
            return;
        }

        if (_nextCrossfadeItem is null)
        {
            _nextCrossfadeItem = nextItem;
            nextItem.StartPlayback(false, 0);
        }

        var progress = 1 - remaining.TotalSeconds / overlap.TotalSeconds;
        _audioReader.Volume = (float)Math.Clamp(1 - progress, 0, 1);

        if (nextItem._audioReader is not null)
            nextItem._audioReader.Volume = (float)Math.Clamp(progress, 0, 1);
    }

    private Mp3FileListItem? GetNextItem()
    {
        if (File is null)
            return null;

        return LoadedItems
            .Where(item => item != this && item.File is not null)
            .OrderBy(item => item.File!.Position)
            .FirstOrDefault(item => item.File!.Position == File.Position + 1);
    }

    private static List<(Mp3File File, double StartSeconds, double EndSeconds)> BuildTimeline(IReadOnlyList<Mp3File> files)
    {
        var segments = new List<(Mp3File File, double StartSeconds, double EndSeconds)>(files.Count);
        double startSeconds = 0;
        var trackOverlapSeconds = GetTrackOverlapSeconds();

        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            var durationSeconds = file.Waveform.Count;
            segments.Add((file, startSeconds, startSeconds + durationSeconds));

            startSeconds += durationSeconds;

            if (index < files.Count - 1)
                startSeconds -= Math.Min(trackOverlapSeconds, Math.Min(file.Waveform.Count, files[index + 1].Waveform.Count));
        }

        return segments;
    }

    private static int GetTrackOverlapSeconds()
    {
        return App.Services.GetRequiredService<IOptions<AudioOptions>>().Value.TrackOverlapSeconds;
    }

    private static Mp3FileListItem? GetLoadedItem(Mp3File file)
    {
        return LoadedItems.FirstOrDefault(item => ReferenceEquals(item.File, file));
    }

    private void StopOtherPlayers()
    {
        foreach (var item in LoadedItems.Where(item => item != this).ToList())
            item.StopPlayback(true);
    }

    private void StopPlayback(bool resetPosition)
    {
        _playbackTimer.Stop();
        _nextCrossfadeItem = null;

        if (_waveOut is not null)
            _waveOut.Pause();

        if (_audioReader is not null)
        {
            _audioReader.Volume = 1;

            if (resetPosition)
                _audioReader.CurrentTime = TimeSpan.Zero;
        }

        UpdatePlaybackUi();
        PublishPlaybackProgress(false);
    }
}
