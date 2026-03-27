using System.Windows;
using System.Windows.Input;
using WorkoutMixer.Models;

namespace WorkoutMixer.Components;

public partial class ChartSegmentsPanel
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(object),
        typeof(ChartSegmentsPanel));

    public static readonly DependencyProperty AvailableZonesProperty = DependencyProperty.Register(
        nameof(AvailableZones),
        typeof(IEnumerable<ChartZone>),
        typeof(ChartSegmentsPanel));

    public static readonly DependencyProperty AddChartDataPointCommandProperty = DependencyProperty.Register(
        nameof(AddChartDataPointCommand),
        typeof(ICommand),
        typeof(ChartSegmentsPanel));

    public static readonly DependencyProperty MoveChartDataPointUpCommandProperty = DependencyProperty.Register(
        nameof(MoveChartDataPointUpCommand),
        typeof(ICommand),
        typeof(ChartSegmentsPanel));

    public static readonly DependencyProperty MoveChartDataPointDownCommandProperty = DependencyProperty.Register(
        nameof(MoveChartDataPointDownCommand),
        typeof(ICommand),
        typeof(ChartSegmentsPanel));

    public static readonly DependencyProperty RemoveChartDataPointCommandProperty = DependencyProperty.Register(
        nameof(RemoveChartDataPointCommand),
        typeof(ICommand),
        typeof(ChartSegmentsPanel));

    public ChartSegmentsPanel()
    {
        InitializeComponent();
    }

    public object? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public IEnumerable<ChartZone>? AvailableZones
    {
        get => (IEnumerable<ChartZone>?)GetValue(AvailableZonesProperty);
        set => SetValue(AvailableZonesProperty, value);
    }

    public ICommand? AddChartDataPointCommand
    {
        get => (ICommand?)GetValue(AddChartDataPointCommandProperty);
        set => SetValue(AddChartDataPointCommandProperty, value);
    }

    public ICommand? MoveChartDataPointUpCommand
    {
        get => (ICommand?)GetValue(MoveChartDataPointUpCommandProperty);
        set => SetValue(MoveChartDataPointUpCommandProperty, value);
    }

    public ICommand? MoveChartDataPointDownCommand
    {
        get => (ICommand?)GetValue(MoveChartDataPointDownCommandProperty);
        set => SetValue(MoveChartDataPointDownCommandProperty, value);
    }

    public ICommand? RemoveChartDataPointCommand
    {
        get => (ICommand?)GetValue(RemoveChartDataPointCommandProperty);
        set => SetValue(RemoveChartDataPointCommandProperty, value);
    }

    public void SelectChartDataPoint(ChartDataPoint item)
    {
        ChartDataPointsGrid.SelectedItem = item;
        ChartDataPointsGrid.ScrollIntoView(item);
    }
}