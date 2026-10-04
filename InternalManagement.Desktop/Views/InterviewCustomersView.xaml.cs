using System.Windows;
using System.Windows.Controls;
using InternalManagement.Desktop.ViewModels;

namespace InternalManagement.Desktop.Views;

public partial class InterviewCustomersView : UserControl
{
    public InterviewCustomersView()
    {
        InitializeComponent();
    }

    public InterviewCustomersViewModel? ViewModel { get; private set; }
    public string SearchText => InterviewSearchBox.Text;

    public event RoutedEventHandler? SearchChanged;
    public event RoutedEventHandler? RefreshRequested;
    public event RoutedEventHandler? SyncRequested;
    public event RoutedEventHandler? CreateRequested;

    public void SetViewModel(InterviewCustomersViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    private void InterviewSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (InterviewSearchPlaceholder is null) return;
        InterviewSearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchText)
            ? Visibility.Visible
            : Visibility.Collapsed;
        SearchChanged?.Invoke(sender, e);
    }

    private void RefreshInterview_Click(object sender, RoutedEventArgs e) =>
        RefreshRequested?.Invoke(sender, e);

    private void SyncInterviewFromWebsite_Click(object sender, RoutedEventArgs e) =>
        SyncRequested?.Invoke(sender, e);

    private void CreateInterviewCustomerDialog_Click(object sender, RoutedEventArgs e) =>
        CreateRequested?.Invoke(sender, e);
}
