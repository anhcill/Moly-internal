using System.Windows;
using System.Windows.Controls;

namespace InternalManagement.Desktop.Views;

public partial class DashboardView : UserControl
{
    public event RoutedEventHandler? OpenCoursesRequested;
    public event RoutedEventHandler? OpenQuestionsRequested;
    public event EventHandler<DashboardScopeChangedEventArgs>? ScopeChanged;
    public event RoutedEventHandler? RefreshRequested;

    private bool _suppressScopeChanged;

    public string SelectedScope { get; private set; } = "company";

    public DashboardView()
    {
        InitializeComponent();
    }

    public void SelectScope(string scope, bool notify = false)
    {
        var normalized = NormalizeScope(scope);
        _suppressScopeChanged = true;
        try
        {
            CompanyScopeButton.IsChecked = normalized == "company";
            EducationScopeButton.IsChecked = normalized == "education";
            FashionScopeButton.IsChecked = normalized == "fashion";
            SelectedScope = normalized;
        }
        finally
        {
            _suppressScopeChanged = false;
        }

        if (notify)
            ScopeChanged?.Invoke(this, new DashboardScopeChangedEventArgs(normalized));
    }

    private void Scope_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton radio || radio.Tag is not string scope) return;
        SelectedScope = NormalizeScope(scope);
        if (!_suppressScopeChanged)
            ScopeChanged?.Invoke(this, new DashboardScopeChangedEventArgs(SelectedScope));
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) =>
        RefreshRequested?.Invoke(sender, e);

    private void GoToCourses_Click(object sender, RoutedEventArgs e) =>
        OpenCoursesRequested?.Invoke(sender, e);

    private void GoToQuestions_Click(object sender, RoutedEventArgs e) =>
        OpenQuestionsRequested?.Invoke(sender, e);

    private static string NormalizeScope(string? scope) => scope?.Trim().ToLowerInvariant() switch
    {
        "education" => "education",
        "fashion" => "fashion",
        _ => "company"
    };
}

public sealed class DashboardScopeChangedEventArgs(string scope) : EventArgs
{
    public string Scope { get; } = scope;
}
