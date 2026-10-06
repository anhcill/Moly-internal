using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop.Behaviors;

/// <summary>
/// Ghi nhớ các cột mà người dùng đã kéo đổi độ rộng và khôi phục ở lần mở sau.
/// </summary>
public static class DataGridColumnWidthPersistence
{
    private static readonly DataGridLayoutStore LayoutStore = new(DataGridLayoutStore.DefaultFilePath);

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(DataGridColumnWidthPersistence),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not DataGrid dataGrid)
        {
            return;
        }

        if ((bool)args.NewValue)
        {
            dataGrid.Loaded += OnDataGridLoaded;
            dataGrid.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnDragCompleted), true);
        }
        else
        {
            dataGrid.Loaded -= OnDataGridLoaded;
            dataGrid.RemoveHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnDragCompleted));
        }
    }

    private static void OnDataGridLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not DataGrid dataGrid)
        {
            return;
        }

        dataGrid.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => RestoreWidths(dataGrid)));
    }

    private static void OnDragCompleted(object sender, DragCompletedEventArgs args)
    {
        if (sender is not DataGrid dataGrid ||
            FindAncestor<DataGridColumnHeader>(args.OriginalSource as DependencyObject) is not { Column: { } column })
        {
            return;
        }

        var tableKey = GetTableKey(dataGrid);
        var columnKey = GetColumnKey(dataGrid, column);
        if (tableKey is null || columnKey is null)
        {
            return;
        }

        LayoutStore.SaveColumnWidth(tableKey, columnKey, column.ActualWidth);
    }

    private static void RestoreWidths(DataGrid dataGrid)
    {
        var tableKey = GetTableKey(dataGrid);
        if (tableKey is null)
        {
            return;
        }

        foreach (var column in dataGrid.Columns)
        {
            var columnKey = GetColumnKey(dataGrid, column);
            if (columnKey is null || LayoutStore.GetColumnWidth(tableKey, columnKey) is not { } savedWidth)
            {
                continue;
            }

            var width = Math.Max(column.MinWidth, savedWidth);
            if (double.IsFinite(column.MaxWidth))
            {
                width = Math.Min(column.MaxWidth, width);
            }

            column.Width = new DataGridLength(width, DataGridLengthUnitType.Pixel);
        }
    }

    private static string? GetTableKey(DataGrid dataGrid)
    {
        if (string.IsNullOrWhiteSpace(dataGrid.Name))
        {
            return null;
        }

        DependencyObject? current = dataGrid;
        while (current is not null)
        {
            if (current is UserControl or Window)
            {
                return $"{current.GetType().FullName}:{dataGrid.Name}";
            }

            current = GetParent(current);
        }

        return $"{dataGrid.GetType().FullName}:{dataGrid.Name}";
    }

    private static string? GetColumnKey(DataGrid dataGrid, DataGridColumn column)
    {
        var baseKey = GetBaseColumnKey(column);
        if (baseKey is null)
        {
            return null;
        }

        var duplicateIndex = 0;
        foreach (var candidate in dataGrid.Columns)
        {
            if (ReferenceEquals(candidate, column))
            {
                break;
            }

            if (string.Equals(GetBaseColumnKey(candidate), baseKey, StringComparison.Ordinal))
            {
                duplicateIndex++;
            }
        }

        return duplicateIndex == 0 ? baseKey : $"{baseKey}#{duplicateIndex}";
    }

    private static string? GetBaseColumnKey(DataGridColumn column)
    {
        if (!string.IsNullOrWhiteSpace(column.SortMemberPath))
        {
            return $"sort:{column.SortMemberPath}";
        }

        if (column is DataGridBoundColumn { Binding: Binding binding } &&
            !string.IsNullOrWhiteSpace(binding.Path?.Path))
        {
            return $"binding:{binding.Path.Path}";
        }

        var header = column.Header?.ToString();
        return string.IsNullOrWhiteSpace(header) ? null : $"header:{header}";
    }

    private static T? FindAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T match)
            {
                return match;
            }

            element = GetParent(element);
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject element)
    {
        if (element is Visual or System.Windows.Media.Media3D.Visual3D)
        {
            return VisualTreeHelper.GetParent(element);
        }

        return element is FrameworkContentElement contentElement
            ? contentElement.Parent
            : LogicalTreeHelper.GetParent(element);
    }
}
