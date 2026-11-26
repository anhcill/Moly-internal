using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using InternalManagement.Desktop.Controls;

namespace InternalManagement.Desktop.Behaviors;

/// <summary>
/// Displays a clean EmptyState overlay inside a DataGrid ONLY when it is visible,
/// has an assigned ItemsSource, and truly has 0 items. Immediately removes the overlay
/// whenever items arrive or the grid becomes hidden/unloaded.
/// </summary>
public static class DataGridEmptyStateBehavior
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(DataGridEmptyStateBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty EmptyTitleProperty = DependencyProperty.RegisterAttached(
        "EmptyTitle",
        typeof(string),
        typeof(DataGridEmptyStateBehavior),
        new PropertyMetadata("Chưa có dữ liệu"));

    public static readonly DependencyProperty EmptySubtitleProperty = DependencyProperty.RegisterAttached(
        "EmptySubtitle",
        typeof(string),
        typeof(DataGridEmptyStateBehavior),
        new PropertyMetadata("Thử thay đổi bộ lọc tìm kiếm hoặc thêm bản ghi mới."));

    private static readonly DependencyProperty AdornerInstanceProperty = DependencyProperty.RegisterAttached(
        "AdornerInstance",
        typeof(EmptyStateAdorner),
        typeof(DataGridEmptyStateBehavior),
        new PropertyMetadata(null));

    private static readonly DependencyProperty AdornerLayerInstanceProperty = DependencyProperty.RegisterAttached(
        "AdornerLayerInstance",
        typeof(AdornerLayer),
        typeof(DataGridEmptyStateBehavior),
        new PropertyMetadata(null));

    private static readonly DependencyProperty HookedCollectionSubscriptionProperty = DependencyProperty.RegisterAttached(
        "HookedCollectionSubscription",
        typeof(CollectionSubscription),
        typeof(DataGridEmptyStateBehavior),
        new PropertyMetadata(null));

    private sealed record CollectionSubscription(
        INotifyCollectionChanged Collection,
        NotifyCollectionChangedEventHandler Handler);

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    public static string GetEmptyTitle(DependencyObject obj) => (string)obj.GetValue(EmptyTitleProperty);
    public static void SetEmptyTitle(DependencyObject obj, string value) => obj.SetValue(EmptyTitleProperty, value);

    public static string GetEmptySubtitle(DependencyObject obj) => (string)obj.GetValue(EmptySubtitleProperty);
    public static void SetEmptySubtitle(DependencyObject obj, string value) => obj.SetValue(EmptySubtitleProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid) return;

        var hasItemsDesc = DependencyPropertyDescriptor.FromProperty(ItemsControl.HasItemsProperty, typeof(DataGrid));
        var itemsSourceDesc = DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(DataGrid));

        if ((bool)e.NewValue)
        {
            grid.Loaded += Grid_StateChanged;
            grid.Unloaded += Grid_Unloaded;
            grid.SizeChanged += Grid_SizeChanged;
            grid.IsVisibleChanged += Grid_IsVisibleChanged;
            hasItemsDesc?.AddValueChanged(grid, Grid_PropertyChanged);
            itemsSourceDesc?.AddValueChanged(grid, Grid_ItemsSourceChanged);
            HookItemsCollection(grid);

            if (grid.IsLoaded)
                UpdateAdorner(grid);
        }
        else
        {
            grid.Loaded -= Grid_StateChanged;
            grid.Unloaded -= Grid_Unloaded;
            grid.SizeChanged -= Grid_SizeChanged;
            grid.IsVisibleChanged -= Grid_IsVisibleChanged;
            hasItemsDesc?.RemoveValueChanged(grid, Grid_PropertyChanged);
            itemsSourceDesc?.RemoveValueChanged(grid, Grid_ItemsSourceChanged);
            UnhookItemsCollection(grid);
            RemoveAdorner(grid);
        }
    }

    private static void Grid_StateChanged(object sender, RoutedEventArgs e)
    {
        if (sender is DataGrid grid)
        {
            HookItemsCollection(grid);
            UpdateAdorner(grid);
        }
    }

    private static void Grid_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is DataGrid grid)
        {
            UnhookItemsCollection(grid);
            RemoveAdorner(grid);
        }
    }

    private static void Grid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is DataGrid grid)
            UpdateAdorner(grid);
    }

    private static void Grid_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is DataGrid grid)
            UpdateAdorner(grid);
    }

    private static void Grid_PropertyChanged(object? sender, EventArgs e)
    {
        if (sender is DataGrid grid)
            UpdateAdorner(grid);
    }

    private static void Grid_ItemsSourceChanged(object? sender, EventArgs e)
    {
        if (sender is DataGrid grid)
        {
            HookItemsCollection(grid);
            UpdateAdorner(grid);
        }
    }

    private static void HookItemsCollection(DataGrid grid)
    {
        UnhookItemsCollection(grid);
        if (grid.ItemsSource is INotifyCollectionChanged notify)
        {
            NotifyCollectionChangedEventHandler handler = (_, _) => UpdateAdorner(grid);
            notify.CollectionChanged += handler;
            grid.SetValue(HookedCollectionSubscriptionProperty, new CollectionSubscription(notify, handler));
        }
    }

    private static void UnhookItemsCollection(DataGrid grid)
    {
        if (grid.GetValue(HookedCollectionSubscriptionProperty) is CollectionSubscription sub)
        {
            sub.Collection.CollectionChanged -= sub.Handler;
            grid.ClearValue(HookedCollectionSubscriptionProperty);
        }
    }

    private static void UpdateAdorner(DataGrid grid)
    {
        if (!grid.IsLoaded || !grid.IsVisible)
        {
            RemoveAdorner(grid);
            return;
        }

        var isEmpty = grid.ItemsSource is not null
            && !grid.HasItems
            && grid.Items.Count == 0
            && grid.ActualHeight >= 110
            && grid.ActualWidth >= 180;

        var existing = (EmptyStateAdorner?)grid.GetValue(AdornerInstanceProperty);
        if (!isEmpty)
        {
            if (existing is not null)
                RemoveAdorner(grid);
            return;
        }

        var layer = AdornerLayer.GetAdornerLayer(grid);
        if (layer is null) return;

        if (existing is null)
        {
            var content = new EmptyState
            {
                Title = GetEmptyTitle(grid),
                Subtitle = GetEmptySubtitle(grid),
                IsHitTestVisible = false
            };
            var adorner = new EmptyStateAdorner(grid, content);
            layer.Add(adorner);
            grid.SetValue(AdornerInstanceProperty, adorner);
            grid.SetValue(AdornerLayerInstanceProperty, layer);
        }
        else
        {
            existing.InvalidateMeasure();
            existing.InvalidateArrange();
        }
    }

    private static void RemoveAdorner(DataGrid grid)
    {
        var existing = (EmptyStateAdorner?)grid.GetValue(AdornerInstanceProperty);
        if (existing is null) return;

        var layer = (AdornerLayer?)grid.GetValue(AdornerLayerInstanceProperty) ?? AdornerLayer.GetAdornerLayer(grid);
        layer?.Remove(existing);
        grid.ClearValue(AdornerInstanceProperty);
        grid.ClearValue(AdornerLayerInstanceProperty);
    }

    private sealed class EmptyStateAdorner : Adorner
    {
        private readonly FrameworkElement _child;

        public EmptyStateAdorner(UIElement adornedElement, FrameworkElement child)
            : base(adornedElement)
        {
            _child = child;
            IsHitTestVisible = false;
            IsClipEnabled = true;
            AddVisualChild(_child);
        }

        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => _child;

        protected override Size MeasureOverride(Size constraint)
        {
            var availableHeight = Math.Max(0, constraint.Height - 40);
            _child.Measure(new Size(constraint.Width, availableHeight));
            return constraint;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var topOffset = Math.Min(40, finalSize.Height * 0.25);
            var rect = new Rect(0, topOffset, finalSize.Width, Math.Max(0, finalSize.Height - topOffset));
            _child.Arrange(rect);
            return finalSize;
        }
    }
}
