using System.Windows;

namespace InternalManagement.Desktop.Views;

public sealed record FeatureViewActionEventArgs(string Action, object Sender, RoutedEventArgs Args);
