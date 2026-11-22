using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace InternalManagement.Desktop.Behaviors;

/// <summary>
/// Allows mouse-wheel events on DataGrid / ListView controls inside a page-level ScrollViewer
/// to scroll the inner table when it has scrollable overflow, and smoothly bubble up to the
/// outer page ScrollViewer when the inner table reaches top/bottom or has no vertical overflow.
/// Keeps WPF UI virtualization 100% intact.
/// </summary>
public static class ScrollBubbleBehavior
{
    public static readonly DependencyProperty EnableBubbleProperty =
        DependencyProperty.RegisterAttached(
            "EnableBubble",
            typeof(bool),
            typeof(ScrollBubbleBehavior),
            new PropertyMetadata(false, OnEnableBubbleChanged));

    public static bool GetEnableBubble(DependencyObject obj) => (bool)obj.GetValue(EnableBubbleProperty);
    public static void SetEnableBubble(DependencyObject obj, bool value) => obj.SetValue(EnableBubbleProperty, value);

    private static void OnEnableBubbleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        element.PreviewMouseWheel -= Element_PreviewMouseWheel;
        if (e.NewValue is true)
        {
            element.PreviewMouseWheel += Element_PreviewMouseWheel;
        }
    }

    private static void Element_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not DependencyObject source) return;

        var innerScrollViewer = FindDescendantScrollViewer(source);
        if (innerScrollViewer is not null && innerScrollViewer.ScrollableHeight > 0.5)
        {
            var scrollingUp = e.Delta > 0;
            var scrollingDown = e.Delta < 0;
            var canScrollUp = innerScrollViewer.VerticalOffset > 0.5;
            var canScrollDown = innerScrollViewer.VerticalOffset < innerScrollViewer.ScrollableHeight - 0.5;

            if ((scrollingUp && canScrollUp) || (scrollingDown && canScrollDown))
            {
                // Let the inner table handle its own vertical scrolling.
                return;
            }
        }

        // Re-raise the wheel event on the visual parent so the outer page ScrollViewer scrolls.
        var parent = VisualTreeHelper.GetParent(source) as UIElement;
        if (parent is null) return;

        e.Handled = true;
        var bubbledArgs = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = sender
        };
        parent.RaiseEvent(bubbledArgs);
    }

    private static ScrollViewer? FindDescendantScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv) return sv;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var found = FindDescendantScrollViewer(child);
            if (found is not null) return found;
        }
        return null;
    }
}

/// <summary>
/// Attached placeholder support for TextBox / PasswordBox styles.
/// </summary>
public static class InputBehaviors
{
    public static readonly DependencyProperty PlaceholderTextProperty =
        DependencyProperty.RegisterAttached(
            "PlaceholderText",
            typeof(string),
            typeof(InputBehaviors),
            new PropertyMetadata(string.Empty));

    public static string GetPlaceholderText(DependencyObject obj) => (string)obj.GetValue(PlaceholderTextProperty);
    public static void SetPlaceholderText(DependencyObject obj, string value) => obj.SetValue(PlaceholderTextProperty, value);
}
