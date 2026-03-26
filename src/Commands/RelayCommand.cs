using System.Windows.Input;

namespace WorkoutMixer.Commands;

public sealed class RelayCommand<T>(Action<T?> execute, Predicate<T?>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return canExecute?.Invoke((T?)parameter) ?? true;
    }

    public void Execute(object? parameter)
    {
        execute((T?)parameter);
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}