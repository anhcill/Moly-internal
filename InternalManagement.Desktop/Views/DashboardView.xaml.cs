using System.Windows;
using System.Windows.Controls;

namespace InternalManagement.Desktop.Views;

public partial class DashboardView : UserControl
{
    public event RoutedEventHandler? OpenCoursesRequested;
    public event RoutedEventHandler? OpenQuestionsRequested;

    public DashboardView()
    {
        InitializeComponent();
    }

    private void GoToCourses_Click(object sender, RoutedEventArgs e) =>
        OpenCoursesRequested?.Invoke(sender, e);

    private void GoToQuestions_Click(object sender, RoutedEventArgs e) =>
        OpenQuestionsRequested?.Invoke(sender, e);
}
