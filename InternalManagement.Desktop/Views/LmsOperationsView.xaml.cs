using System.Windows;
using System.Windows.Controls;

namespace InternalManagement.Desktop.Views;

public partial class LmsOperationsView : UserControl
{
    public LmsOperationsView() => InitializeComponent();

    public event EventHandler<FeatureViewActionEventArgs>? ActionRequested;

    private void AutoGenerateSlug_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(AutoGenerateSlug_Click), sender, e));

    private void DispatchLmsOutbox_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(DispatchLmsOutbox_Click), sender, e));

    private void RefreshLmsIntegration_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(RefreshLmsIntegration_Click), sender, e));

    private void RetryLmsOutbox_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(RetryLmsOutbox_Click), sender, e));

    private void SaveLmsMapping_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(SaveLmsMapping_Click), sender, e));

    private void LmsCourseMappingsDataGrid_SelectionChanged(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(LmsCourseMappingsDataGrid_SelectionChanged), sender, e));
}
