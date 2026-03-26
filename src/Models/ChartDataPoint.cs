using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace WorkoutMixer.Models;

internal sealed class ChartDataPoint : INotifyPropertyChanged
{
    private double _duration;
    private ChartZone _zone;

    public ChartDataPoint(double duration, ChartZone zone)
    {
        _duration = Math.Max(0.1, duration);
        _zone = zone;
    }

    public double Duration
    {
        get => _duration;
        set
        {
            var normalizedValue = Math.Max(0.01, value);

            if (Math.Abs(_duration - normalizedValue) < 0.001)
                return;

            _duration = normalizedValue;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DurationSeconds));
            OnPropertyChanged(nameof(Intensity));
            OnPropertyChanged(nameof(Brush));
        }
    }

    public double DurationSeconds
    {
        get => Duration * 60;
        set => Duration = value / 60;
    }

    public ChartZone Zone
    {
        get => _zone;
        set
        {
            if (_zone == value)
                return;

            _zone = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Intensity));
            OnPropertyChanged(nameof(Brush));
        }
    }

    public double Intensity => Zone.MaxValue;
    public Brush Brush => Zone.Brush;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
