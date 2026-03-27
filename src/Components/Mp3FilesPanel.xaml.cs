using System.Windows;
using System.Windows.Input;
using WorkoutMixer.Models;

namespace WorkoutMixer.Components;

public partial class Mp3FilesPanel
{
    public static readonly DependencyProperty FilesSourceProperty = DependencyProperty.Register(
        nameof(FilesSource),
        typeof(object),
        typeof(Mp3FilesPanel));

    public static readonly DependencyProperty SummaryTextProperty = DependencyProperty.Register(
        nameof(SummaryText),
        typeof(string),
        typeof(Mp3FilesPanel),
        new PropertyMetadata("No files selected."));

    public static readonly DependencyProperty AddFilesCommandProperty = DependencyProperty.Register(
        nameof(AddFilesCommand),
        typeof(ICommand),
        typeof(Mp3FilesPanel));

    public static readonly DependencyProperty SaveFinalMixCommandProperty = DependencyProperty.Register(
        nameof(SaveFinalMixCommand),
        typeof(ICommand),
        typeof(Mp3FilesPanel));

    public static readonly DependencyProperty SaveIntensityReportCommandProperty = DependencyProperty.Register(
        nameof(SaveIntensityReportCommand),
        typeof(ICommand),
        typeof(Mp3FilesPanel));

    public static readonly DependencyProperty MoveUpCommandProperty = DependencyProperty.Register(
        nameof(MoveUpCommand),
        typeof(ICommand),
        typeof(Mp3FilesPanel));

    public static readonly DependencyProperty MoveDownCommandProperty = DependencyProperty.Register(
        nameof(MoveDownCommand),
        typeof(ICommand),
        typeof(Mp3FilesPanel));

    public static readonly DependencyProperty RemoveCommandProperty = DependencyProperty.Register(
        nameof(RemoveCommand),
        typeof(ICommand),
        typeof(Mp3FilesPanel));

    public Mp3FilesPanel()
    {
        InitializeComponent();
    }

    public object? FilesSource
    {
        get => GetValue(FilesSourceProperty);
        set => SetValue(FilesSourceProperty, value);
    }

    public string SummaryText
    {
        get => (string)GetValue(SummaryTextProperty);
        set => SetValue(SummaryTextProperty, value);
    }

    public ICommand? AddFilesCommand
    {
        get => (ICommand?)GetValue(AddFilesCommandProperty);
        set => SetValue(AddFilesCommandProperty, value);
    }

    public ICommand? SaveFinalMixCommand
    {
        get => (ICommand?)GetValue(SaveFinalMixCommandProperty);
        set => SetValue(SaveFinalMixCommandProperty, value);
    }

    public ICommand? SaveIntensityReportCommand
    {
        get => (ICommand?)GetValue(SaveIntensityReportCommandProperty);
        set => SetValue(SaveIntensityReportCommandProperty, value);
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

    public void SelectFile(Mp3File item)
    {
        FilesListBox.SelectedItem = item;
        FilesListBox.ScrollIntoView(item);
    }
}