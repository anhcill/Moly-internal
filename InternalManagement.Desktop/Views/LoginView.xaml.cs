using System.Windows;
using System.Windows.Controls;

namespace InternalManagement.Desktop.Views;

public partial class LoginView : UserControl
{
    public LoginView() => InitializeComponent();

    public event EventHandler<FeatureViewActionEventArgs>? ActionRequested;

    private void CancelForgotPassword_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(CancelForgotPassword_Click), sender, e));

    private void ForgotPasswordButton_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(ForgotPasswordButton_Click), sender, e));

    private void LoginButton_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(LoginButton_Click), sender, e));

    private void LoginServerConfigButton_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(LoginServerConfigButton_Click), sender, e));

    private void PasswordVisibilityButton_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(PasswordVisibilityButton_Click), sender, e));

    private void RememberMeCheckBox_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(RememberMeCheckBox_Click), sender, e));

    private void SubmitForgotPassword_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(SubmitForgotPassword_Click), sender, e));

    private void InputBox_KeyDown(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(InputBox_KeyDown), sender, e));

    private void PasswordBox_LostFocus(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(PasswordBox_LostFocus), sender, e));

    private void UsernameTextBox_LostFocus(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(UsernameTextBox_LostFocus), sender, e));

    private void VisiblePasswordTextBox_LostFocus(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(VisiblePasswordTextBox_LostFocus), sender, e));

    private void UsernameTextBox_TextChanged(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(UsernameTextBox_TextChanged), sender, e));

    private void VisiblePasswordTextBox_TextChanged(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(VisiblePasswordTextBox_TextChanged), sender, e));

    private void UsernameTextBox_GotFocus(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(UsernameTextBox_GotFocus), sender, e));

    private void PasswordBox_GotFocus(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(PasswordBox_GotFocus), sender, e));

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(PasswordBox_PasswordChanged), sender, e));

    private void VisiblePasswordTextBox_GotFocus(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(VisiblePasswordTextBox_GotFocus), sender, e));
}
