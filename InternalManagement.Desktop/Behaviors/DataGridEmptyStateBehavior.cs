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
/// Automatically displays a sleek EmptyState overlay inside any DataGrid when it has no items,
/// without replacing DataGrid.ControlTemplate (preserving virtualization, column sorting, and scrolling).
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

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    public static string GetEmptyTitle(DependencyObject obj) => (string)obj.GetValue(EmptyTitleProperty);
    public static void SetEmptyTitle(DependencyObject obj, string value) => obj.SetValue(EmptyTitleProperty, value);

    public static string GetEmptySubtitle(DependencyObject obj) => (string)obj.GetValue(EmptySubtitleProperty);
    public static void SetEmptySubtitle(DependencyObject obj, string value) => obj.SetValue(EmptySubtitleProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid) return;

        if ((bool)e.NewValue)
        {
            grid.Loaded += Grid_Loaded;
            grid.Unloaded += Grid_Unloaded;
            grid.SizeChanged += Grid_SizeChanged;
            ((INotifyCollectionChanged)grid.Items).CollectionChanged += (_, _) => UpdateAdorner(grid);
            DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(DataGrid))
                ?.AddValueChanged(grid, OnItemsSourceChanged);

            if (grid.IsLoaded)
                UpdateAdorner(grid);
        }
        else
        {
            grid.Loaded -= Grid_Loaded;
            grid.Unloaded -= Grid_Unloaded;
            grid.SizeChanged -= Grid_SizeChanged;
            DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(DataGrid))
                ?.RemoveValueChanged(grid, OnItemsSourceChanged);
            RemoveAdorner(grid);
        }
    }

    private static void Grid_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is DataGrid grid) UpdateAdorner(grid);
    }

    private static void Grid_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is DataGrid grid) RemoveAdorner(grid);
    }

    private static void Grid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is DataGrid grid) UpdateAdorner(grid);
    }

    private static void OnItemsSourceChanged(object? sender, EventArgs e)
    {
        if (sender is DataGrid grid) UpdateAdorner(grid);
    }

    private static void UpdateAdorner(DataGrid grid)
    {
        if (!grid.IsLoaded) return;

        var layer = AdornerLayer.GetAdornerLayer(grid);
        if (layer is null) return;

        var isEmpty = grid.Items.Count == 0 && grid.ActualHeight >= 110 && grid.ActualWidth >= 180;
        var existing = (EmptyStateAdorner?)grid.GetValue(AdornerInstanceProperty);

        if (isEmpty)
        {
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
            }
            else
            {
                existing.InvalidateMeasure();
                existing.InvalidateArrange();
            }
        }
        else if (existing is not null)
        {
            layer.Remove(existing);
            grid.ClearValue(AdornerInstanceProperty);
        }
    }

    private static void RemoveAdorner(DataGrid grid)
    {
        var existing = (EmptyStateAdorner?)grid.GetValue(AdornerInstanceProperty);
        if (existing is null) return;

        AdornerLayer.GetAdornerLayer(grid)?.Remove(existing);
        grid.ClearValue(AdornerInstanceProperty);
    }

    private sealed class EmptyStateAdorner : Adorner
    {
        private readonly FrameworkElement _child;

        public EmptyStateAdorner(UIElement adornedElement, FrameworkElement child)
            : base(adornedElement)
        {
            _child = child;
            IsHitTestVisible = false;
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
            // Leave room for the 40px DataGrid header row
            var topOffset = Math.Min(40, finalSize.Height * 0.25);
            var rect = new Rect(0, topOffset, finalSize.Width, Math.Max(0, finalSize.Height - topOffset));
            _child.Arrange(rect);
            return finalSize;
        }
    }
}
