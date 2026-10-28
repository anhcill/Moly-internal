using System.Windows.Controls;
using System.Windows;
using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop.Views;

public partial class CostBreakEvenView : UserControl
{
    private static readonly TimeSpan CscaBusinessUtcOffset = TimeSpan.FromHours(7);
    private bool _suppressFilterEvents;

    public CostBreakEvenView()
    {
        InitializeComponent();
        FromDatePicker.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        ToDatePicker.SelectedDate = DateTime.Today;
    }

    public event RoutedEventHandler? RefreshRequested;
    public event RoutedEventHandler? FiltersChanged;

    public DateTime? FromDate => ToBusinessUtcBoundary(FromDatePicker.SelectedDate?.Date);
    public DateTime? ToDateExclusive => ToBusinessUtcBoundary(ToDatePicker.SelectedDate?.Date.AddDays(1));
    public Guid? SelectedClassId => ClassSelector.SelectedValue is Guid id ? id : null;

    public void SetClassOptions(IEnumerable<ApiClient.CscaClassItem> classes)
    {
        _suppressFilterEvents = true;
        try
        {
            var selectedId = SelectedClassId;
            ClassSelector.ItemsSource = classes.OrderBy(x => x.Name).ToList();
            ClassSelector.SelectedValue = selectedId;
            FilterScopeText.Text = selectedId.HasValue
                ? (ClassSelector.SelectedItem as ApiClient.CscaClassItem)?.Name ?? "Lớp đã chọn"
                : "Tất cả lớp học";
        }
        finally
        {
            _suppressFilterEvents = false;
        }
    }

    public void SetFilterScope(string text) => FilterScopeText.Text = text;

    private static DateTime? ToBusinessUtcBoundary(DateTime? value) => value.HasValue
        ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified), CscaBusinessUtcOffset).UtcDateTime
        : null;

    public void SetAnalysisNote(string text) => AnalysisNoteText.Text = text;

    private void CostBreakEvenFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (!_suppressFilterEvents) FiltersChanged?.Invoke(sender, e);
    }

    private void RefreshCostBreakEven_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke(sender, e);

    private void ClearCostBreakEvenClass_Click(object sender, RoutedEventArgs e)
    {
        if (ClassSelector.SelectedIndex < 0) return;
        ClassSelector.SelectedIndex = -1;
    }
}
