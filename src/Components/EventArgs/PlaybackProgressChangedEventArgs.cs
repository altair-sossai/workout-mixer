using WorkoutMixer.Models;

namespace WorkoutMixer.Components.EventArgs;

public sealed class PlaybackProgressChangedEventArgs(Mp3File file, TimeSpan position, bool isPlaying) : System.EventArgs
{
    public Mp3File File { get; } = file;
    public TimeSpan Position { get; } = position;
    public bool IsPlaying { get; } = isPlaying;
}