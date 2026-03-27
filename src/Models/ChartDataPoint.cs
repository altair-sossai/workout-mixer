using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace WorkoutMixer.Models;

public sealed class ChartDataPoint(double duration, ChartZone zone, int rpm)
    : INotifyPropertyChanged
{
    public double Duration
    {
        get;
        set
        {
            var normalizedValue = Math.Max(0.01, value);

            if (Math.Abs(field - normalizedValue) < 0.001)
                return;

            field = normalizedValue;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DurationSeconds));
            OnPropertyChanged(nameof(DurationMinutes));
            OnPropertyChanged(nameof(Intensity));
            OnPropertyChanged(nameof(Brush));
        }
    } = Math.Max(0.1, duration);

    public double DurationSeconds
    {
        get => Duration * 60;
        set => Duration = value / 60;
    }

    public double DurationMinutes
    {
        get => Duration;
        set => Duration = value;
    }

    public ChartZone Zone
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Intensity));
            OnPropertyChanged(nameof(Brush));
        }
    } = zone;

    public int Rpm
    {
        get;
        set
        {
            var normalizedValue = Math.Max(1, value);

            if (field == normalizedValue)
                return;

            field = normalizedValue;
            OnPropertyChanged();
        }
    } = Math.Max(1, rpm);

    public double Intensity => Zone.MaxValue;
    public Brush Brush => Zone.Brush;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}