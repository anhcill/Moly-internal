using System.Windows;
using System.Windows.Controls;
using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop.Views;

public partial class AttendanceView : UserControl
{
    public AttendanceView()
    {
        InitializeComponent();
    }

    public event RoutedEventHandler? FilterRequested;
    public event RoutedEventHandler? RefreshRequested;
    public event RoutedEventHandler? DownloadTemplateRequested;
    public event RoutedEventHandler? ImportRequested;

    public DateOnly? FromDate => AttendanceFromDatePicker.SelectedDate is { } date
        ? DateOnly.FromDateTime(date) : null;
    public DateOnly? ToDate => AttendanceToDatePicker.SelectedDate is { } date
        ? DateOnly.FromDateTime(date) : null;

    public void SetRecords(IEnumerable<ApiClient.AttendanceItem>? records)
    {
        var rows = records?.ToArray() ?? Array.Empty<ApiClient.AttendanceItem>();
        AttendanceDataGrid.ItemsSource = rows;
        AttendanceEmptyText.Visibility = records is not null && rows.Length == 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetSummary(ApiClient.AttendanceSummaryItem summary)
    {
        MetricAttendanceTotal.Text = summary.TotalRecords.ToString();
        MetricAttendancePresent.Text = summary.PresentCount.ToString();
        MetricAttendanceLate.Text = summary.LateCount.ToString();
        MetricAttendanceWorkHours.Text = $"{summary.TotalWorkHours:0.##} h";
    }

    private void FilterAttendance_Click(object sender, RoutedEventArgs e) => FilterRequested?.Invoke(sender, e);
    private void RefreshAttendance_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke(sender, e);
    private void DownloadAttendanceTemplate_Click(object sender, RoutedEventArgs e) => DownloadTemplateRequested?.Invoke(sender, e);
    private void ImportAttendance_Click(object sender, RoutedEventArgs e) => ImportRequested?.Invoke(sender, e);
}
