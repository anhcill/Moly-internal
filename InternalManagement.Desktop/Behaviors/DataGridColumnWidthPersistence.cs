using System.ComponentModel;
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
    private static readonly DependencyPropertyDescriptor ColumnWidthDescriptor =
        DependencyPropertyDescriptor.FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn));

    private static readonly object StoresLock = new();
    private static readonly Dictionary<string, DataGridLayoutStore> Stores = new(StringComparer.OrdinalIgnoreCase);

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State",
        typeof(PersistenceState),
        typeof(DataGridColumnWidthPersistence));

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(DataGridColumnWidthPersistence),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty StoragePathProperty = DependencyProperty.RegisterAttached(
        "StoragePath",
        typeof(string),
        typeof(DataGridColumnWidthPersistence),
        new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static string? GetStoragePath(DependencyObject element) =>
        (string?)element.GetValue(StoragePathProperty);

    public static void SetStoragePath(DependencyObject element, string? value) =>
        element.SetValue(StoragePathProperty, value);

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not DataGrid dataGrid)
        {
            return;
        }

        if ((bool)args.NewValue)
        {
            dataGrid.Loaded += OnDataGridLoaded;
            dataGrid.Unloaded += OnDataGridUnloaded;
            dataGrid.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnDragCompleted), true);
        }
        else
        {
            dataGrid.Loaded -= OnDataGridLoaded;
            dataGrid.Unloaded -= OnDataGridUnloaded;
            dataGrid.RemoveHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnDragCompleted));
            DetachState(dataGrid, flushPendingWidths: true);
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
            new Action(() => InitializeState(dataGrid)));
    }

    private static void OnDataGridUnloaded(object sender, RoutedEventArgs args)
    {
        if (sender is DataGrid dataGrid)
        {
            DetachState(dataGrid, flushPendingWidths: true);
        }
    }

    private static void OnDragCompleted(object sender, DragCompletedEventArgs args)
    {
        if (sender is not DataGrid dataGrid ||
            FindAncestor<DataGridColumnHeader>(args.OriginalSource as DependencyObject) is not { Column: { } column })
        {
            return;
        }

        if (dataGrid.GetValue(StateProperty) is PersistenceState state)
        {
            QueueWidthSave(state, column, saveImmediately: true);
            return;
        }

        SaveColumnWidth(dataGrid, column);
    }

    private static void InitializeState(DataGrid dataGrid)
    {
        if (!dataGrid.IsLoaded || !GetIsEnabled(dataGrid))
        {
            return;
        }

        DetachState(dataGrid, flushPendingWidths: true);

        var state = new PersistenceState(dataGrid);
        dataGrid.SetValue(StateProperty, state);
        state.IsRestoring = true;

        try
        {
            RestoreWidths(dataGrid);
        }
        finally
        {
            state.IsRestoring = false;
        }

        foreach (var column in dataGrid.Columns)
        {
            EventHandler handler = (_, _) => OnColumnWidthChanged(state, column);
            ColumnWidthDescriptor.AddValueChanged(column, handler);
            state.ColumnHandlers[column] = handler;
        }
    }

    private static void OnColumnWidthChanged(PersistenceState state, DataGridColumn column)
    {
        if (!state.IsRestoring && state.DataGrid.IsLoaded)
        {
            QueueWidthSave(state, column, saveImmediately: false);
        }
    }

    private static void QueueWidthSave(PersistenceState state, DataGridColumn column, bool saveImmediately)
    {
        state.PendingColumns.Add(column);
        state.SaveTimer.Stop();

        if (saveImmediately)
        {
            FlushPendingWidths(state);
        }
        else
        {
            state.SaveTimer.Start();
        }
    }

    private static void FlushPendingWidths(PersistenceState state)
    {
        state.SaveTimer.Stop();
        foreach (var column in state.PendingColumns)
        {
            SaveColumnWidth(state.DataGrid, column);
        }

        state.PendingColumns.Clear();
    }

    private static void DetachState(DataGrid dataGrid, bool flushPendingWidths)
    {
        if (dataGrid.GetValue(StateProperty) is not PersistenceState state)
        {
            return;
        }

        if (flushPendingWidths)
        {
            FlushPendingWidths(state);
        }

        foreach (var (column, handler) in state.ColumnHandlers)
        {
            ColumnWidthDescriptor.RemoveValueChanged(column, handler);
        }

        state.ColumnHandlers.Clear();
        state.SaveTimer.Stop();
        dataGrid.ClearValue(StateProperty);
    }

    private static void SaveColumnWidth(DataGrid dataGrid, DataGridColumn column)
    {
        var tableKey = GetTableKey(dataGrid);
        var columnKey = GetColumnKey(dataGrid, column);
        if (tableKey is null || columnKey is null)
        {
            return;
        }

        var width = column.ActualWidth;
        if ((!double.IsFinite(width) || width < 24) && column.Width.IsAbsolute)
        {
            width = column.Width.Value;
        }

        GetLayoutStore(dataGrid).SaveColumnWidth(tableKey, columnKey, width);
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
            if (columnKey is null || GetLayoutStore(dataGrid).GetColumnWidth(tableKey, columnKey) is not { } savedWidth)
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

    private static DataGridLayoutStore GetLayoutStore(DataGrid dataGrid)
    {
        var path = GetStoragePath(dataGrid);
        if (string.IsNullOrWhiteSpace(path))
        {
            path = DataGridLayoutStore.DefaultFilePath;
        }

        lock (StoresLock)
        {
            if (!Stores.TryGetValue(path, out var store))
            {
                store = new DataGridLayoutStore(path);
                Stores[path] = store;
            }

            return store;
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

    private sealed class PersistenceState
    {
        public PersistenceState(DataGrid dataGrid)
        {
            DataGrid = dataGrid;
            SaveTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(250),
                DispatcherPriority.Background,
                (_, _) => FlushPendingWidths(this),
                dataGrid.Dispatcher);
            SaveTimer.Stop();
        }

        public DataGrid DataGrid { get; }

        public DispatcherTimer SaveTimer { get; }

        public Dictionary<DataGridColumn, EventHandler> ColumnHandlers { get; } = [];

        public HashSet<DataGridColumn> PendingColumns { get; } = [];

        public bool IsRestoring { get; set; }
    }
}
