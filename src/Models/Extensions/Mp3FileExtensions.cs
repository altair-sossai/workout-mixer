namespace WorkoutMixer.Models.Extensions;

public static class Mp3FileExtensions
{
    public static bool ContainsPath(this IEnumerable<Mp3File> files, string path)
    {
        return files.Any(file => string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase));
    }
}