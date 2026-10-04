using System.Windows;
using System.Windows.Controls;
using InternalManagement.Desktop.ViewModels;

namespace InternalManagement.Desktop.Views;

public partial class QuestionBankView : UserControl
{
    public QuestionBankView()
    {
        InitializeComponent();
    }

    public QuestionBankViewModel? ViewModel { get; private set; }
    public string SearchText => QuestionSearchBox.Text;

    public event RoutedEventHandler? SearchChanged;
    public event RoutedEventHandler? RefreshRequested;
    public event RoutedEventHandler? CreateRequested;
    public event RoutedEventHandler? PublishRequested;

    public void SetViewModel(QuestionBankViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    private void QuestionSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (QuestionSearchPlaceholder is null) return;
        QuestionSearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchText)
            ? Visibility.Visible
            : Visibility.Collapsed;
        SearchChanged?.Invoke(sender, e);
    }

    private void RefreshQuestions_Click(object sender, RoutedEventArgs e) =>
        RefreshRequested?.Invoke(sender, e);

    private void CreateQuestionDialog_Click(object sender, RoutedEventArgs e) =>
        CreateRequested?.Invoke(sender, e);

    private void PublishQuestion_Click(object sender, RoutedEventArgs e) =>
        PublishRequested?.Invoke(sender, e);
}
