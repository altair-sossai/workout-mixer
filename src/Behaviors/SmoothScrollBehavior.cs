using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WorkoutMixer.Behaviors;

public static class SmoothScrollBehavior
{
    private const double WheelScrollDistance = 140;

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(SmoothScrollBehavior), new PropertyMetadata(false, OnIsEnabledChanged));
    private static readonly DependencyProperty ScrollStateProperty = DependencyProperty.RegisterAttached("ScrollState", typeof(ScrollState), typeof(SmoothScrollBehavior));

    public static bool GetIsEnabled(DependencyObject dependencyObject)
    {
        return (bool)dependencyObject.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(DependencyObject dependencyObject, bool value)
    {
        dependencyObject.SetValue(IsEnabledProperty, value);
    }

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not FrameworkElement element)
            return;

        if ((bool)e.NewValue)
        {
            element.Loaded += Element_Loaded;
            element.Unloaded += Element_Unloaded;

            if (element.IsLoaded) Attach(element);
        }
        else
        {
            element.Loaded -= Element_Loaded;
            element.Unloaded -= Element_Unloaded;
            Detach(element);
        }
    }

    private static void Element_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
            Attach(element);
    }

    private static void Element_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
            Detach(element);
    }

    private static void Attach(FrameworkElement element)
    {
        if (element.GetValue(ScrollStateProperty) is ScrollState)
            return;

        var scrollViewer = FindDescendant<ScrollViewer>(element);
        if (scrollViewer is null)
            return;

        var state = new ScrollState(scrollViewer);
        element.SetValue(ScrollStateProperty, state);
        element.PreviewMouseWheel += Element_PreviewMouseWheel;
    }

    private static void Detach(FrameworkElement element)
    {
        if (element.GetValue(ScrollStateProperty) is not ScrollState state)
            return;

        element.PreviewMouseWheel -= Element_PreviewMouseWheel;
        state.Dispose();
        element.ClearValue(ScrollStateProperty);
    }

    private static void Element_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not FrameworkElement element || element.GetValue(ScrollStateProperty) is not ScrollState state)
            return;

        e.Handled = true;

        var delta = -e.Delta / 120d * WheelScrollDistance;

        state.TargetOffset = Clamp(state.TargetOffset + delta, 0, Math.Max(0, state.ScrollViewer.ExtentHeight - state.ScrollViewer.ViewportHeight));

        var animation = new DoubleAnimation
        {
            From = state.ScrollViewer.VerticalOffset,
            To = state.TargetOffset,
            Duration = TimeSpan.FromMilliseconds(260),
            EasingFunction = new CubicEase
            {
                EasingMode = EasingMode.EaseOut
            }
        };

        state.Animator.BeginAnimation(ScrollAnimator.VerticalOffsetProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static T? FindDescendant<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is T result) return result;

            var nested = FindDescendant<T>(child);
            if (nested is not null) return nested;
        }

        return null;
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    private sealed class ScrollState(ScrollViewer scrollViewer) : IDisposable
    {
        public ScrollViewer ScrollViewer { get; } = scrollViewer;

        public ScrollAnimator Animator { get; } = new(scrollViewer);

        public double TargetOffset { get; set; } = scrollViewer.VerticalOffset;

        public void Dispose()
        {
            Animator.BeginAnimation(ScrollAnimator.VerticalOffsetProperty, null);
        }
    }

    private sealed class ScrollAnimator : Animatable
    {
        public static readonly DependencyProperty VerticalOffsetProperty =
            DependencyProperty.Register(
                nameof(VerticalOffset),
                typeof(double),
                typeof(ScrollAnimator),
                new PropertyMetadata(0d, OnVerticalOffsetChanged));

        private readonly ScrollViewer _scrollViewer;

        public ScrollAnimator(ScrollViewer scrollViewer)
        {
            _scrollViewer = scrollViewer;
            VerticalOffset = scrollViewer.VerticalOffset;
        }

        public double VerticalOffset
        {
            get => (double)GetValue(VerticalOffsetProperty);
            set => SetValue(VerticalOffsetProperty, value);
        }

        protected override Freezable CreateInstanceCore()
        {
            throw new NotSupportedException();
        }

        private static void OnVerticalOffsetChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        {
            var animator = (ScrollAnimator)dependencyObject;
            animator._scrollViewer.ScrollToVerticalOffset((double)e.NewValue);
        }
    }
}