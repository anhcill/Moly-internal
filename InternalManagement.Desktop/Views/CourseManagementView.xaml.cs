using System.Windows;
using System.Windows.Controls;

namespace InternalManagement.Desktop.Views;

public partial class CourseManagementView : UserControl
{
    public CourseManagementView() => InitializeComponent();

    public event EventHandler<FeatureViewActionEventArgs>? ActionRequested;

    private void ClearCscaCourseFilter_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(ClearCscaCourseFilter_Click), sender, e));

    private void CreateCourseDialog_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(CreateCourseDialog_Click), sender, e));

    private void CreateCscaClassDialog_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(CreateCscaClassDialog_Click), sender, e));

    private void DeleteCourse_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(DeleteCourse_Click), sender, e));

    private void DeleteCscaClass_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(DeleteCscaClass_Click), sender, e));

    private void EditCourseDialog_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(EditCourseDialog_Click), sender, e));

    private void EditCscaClass_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(EditCscaClass_Click), sender, e));

    private void RefreshCourses_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(RefreshCourses_Click), sender, e));

    private void RefreshCsca_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(RefreshCsca_Click), sender, e));

    private void ViewCourseClasses_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(ViewCourseClasses_Click), sender, e));

    private void ViewCscaClassDetails_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(ViewCscaClassDetails_Click), sender, e));

    private void CoursesDataGrid_MouseDoubleClick(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(CoursesDataGrid_MouseDoubleClick), sender, e));

    private void CscaClassesDataGrid_MouseDoubleClick(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(CscaClassesDataGrid_MouseDoubleClick), sender, e));

    private void CourseModuleTabControl_SelectionChanged(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(CourseModuleTabControl_SelectionChanged), sender, e));

    private void CscaCourseFilterComboBox_SelectionChanged(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(CscaCourseFilterComboBox_SelectionChanged), sender, e));

    private void CscaFinanceCourseFilterComboBox_SelectionChanged(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(CscaFinanceCourseFilterComboBox_SelectionChanged), sender, e));

    private void CourseSearch_TextChanged(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(CourseSearch_TextChanged), sender, e));

    private void CscaSearch_TextChanged(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(CscaSearch_TextChanged), sender, e));
}
