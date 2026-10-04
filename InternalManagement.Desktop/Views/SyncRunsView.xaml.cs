using System.Windows;
using System.Windows.Controls;
using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop.Views;

public partial class SyncRunsView : UserControl
{
    public SyncRunsView()
    {
        InitializeComponent();
    }

    public event RoutedEventHandler? SyncCoursesRequested;
    public event RoutedEventHandler? SyncCustomersRequested;
    public event RoutedEventHandler? RefreshRunsRequested;
    public event RoutedEventHandler? ExportRunsRequested;
    public event RoutedEventHandler? RefreshDeadLettersRequested;
    public event RoutedEventHandler? RetryDeadLetterRequested;

    public ApiClient.DeadLetterItem? SelectedDeadLetter =>
        DeadLettersDataGrid.SelectedItem as ApiClient.DeadLetterItem;

    public IEnumerable<ApiClient.SyncRunItem> Runs =>
        SyncRunsDataGrid.Items.OfType<ApiClient.SyncRunItem>();

    public void SetRuns(IEnumerable<ApiClient.SyncRunItem>? runs) =>
        SyncRunsDataGrid.ItemsSource = runs?.ToArray() ?? Array.Empty<ApiClient.SyncRunItem>();

    public void SetDeadLetters(IEnumerable<ApiClient.DeadLetterItem>? deadLetters) =>
        DeadLettersDataGrid.ItemsSource = deadLetters?.ToArray() ?? Array.Empty<ApiClient.DeadLetterItem>();

    private void TriggerSyncCourses_Click(object sender, RoutedEventArgs e) => SyncCoursesRequested?.Invoke(sender, e);
    private void TriggerSyncCustomers_Click(object sender, RoutedEventArgs e) => SyncCustomersRequested?.Invoke(sender, e);
    private void RefreshSyncRuns_Click(object sender, RoutedEventArgs e) => RefreshRunsRequested?.Invoke(sender, e);
    private void ExportSyncRuns_Click(object sender, RoutedEventArgs e) => ExportRunsRequested?.Invoke(sender, e);
    private void RefreshDeadLetters_Click(object sender, RoutedEventArgs e) => RefreshDeadLettersRequested?.Invoke(sender, e);
    private void RetryDeadLetter_Click(object sender, RoutedEventArgs e) => RetryDeadLetterRequested?.Invoke(sender, e);
}
