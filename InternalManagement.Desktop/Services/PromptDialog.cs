using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace InternalManagement.Desktop.Services;

public sealed record PromptOption(string Value, string Label);

public sealed record PromptField(
    string Key,
    string Label,
    string? InitialValue = null,
    bool IsRequired = true,
    IReadOnlyList<PromptOption>? Options = null,
    bool IsMultiline = false);

public static class PromptDialog
{
    public static bool TryShow(
        Window owner,
        string title,
        IReadOnlyList<PromptField> fields,
        out IReadOnlyDictionary<string, string> values)
    {
        var controls = new Dictionary<string, Control>();
        IReadOnlyDictionary<string, string> submittedValues = new Dictionary<string, string>();
        var dialog = new Window
        {
            Owner = owner,
            Title = title,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = fields.Count >= 6 ? 760 : 520,
            MinWidth = fields.Count >= 6 ? 680 : 460,
            MaxHeight = 680,
            ResizeMode = ResizeMode.NoResize,
            Background = Brushes.White,
            ShowInTaskbar = false
        };

        var root = new Grid { Margin = new Thickness(24, 20, 24, 18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 25, 54)),
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(titleBlock, 0);
        root.Children.Add(titleBlock);

        var isTwoColumn = fields.Count >= 6;
        var fieldsGrid = new Grid();
        fieldsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (isTwoColumn)
        {
            fieldsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            fieldsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        var fieldColumns = isTwoColumn ? 2 : 1;
        var fieldRows = (int)Math.Ceiling(fields.Count / (double)fieldColumns);
        for (var row = 0; row < fieldRows; row++)
            fieldsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(fieldsGrid, 1);
        root.Children.Add(fieldsGrid);

        for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
        {
            var field = fields[fieldIndex];
            var fieldPanel = new StackPanel();
            fieldPanel.Children.Add(new TextBlock
            {
                Text = field.IsRequired ? $"{field.Label} *" : field.Label,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(52, 64, 92)),
                Margin = new Thickness(0, 0, 0, 4)
            });

            Control control;
            if (field.Options is { Count: > 0 })
            {
                var combo = new ComboBox
                {
                    Height = 34,
                    Padding = new Thickness(8, 5, 8, 5),
                    ItemsSource = field.Options,
                    DisplayMemberPath = nameof(PromptOption.Label),
                    SelectedValuePath = nameof(PromptOption.Value),
                    SelectedIndex = 0,
                    Margin = new Thickness(0, 0, 0, 9)
                };
                if (!string.IsNullOrWhiteSpace(field.InitialValue))
                {
                    combo.SelectedValue = field.InitialValue;
                }
                control = combo;
            }
            else
            {
                var textBox = new TextBox
                {
                    Text = field.InitialValue ?? string.Empty,
                    Height = field.IsMultiline ? 78 : 34,
                    Padding = new Thickness(8, 5, 8, 5),
                    Margin = new Thickness(0, 0, 0, 9),
                    Style = Application.Current.TryFindResource("ModernTextBox") as Style,
                    AcceptsReturn = field.IsMultiline,
                    TextWrapping = field.IsMultiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                    VerticalScrollBarVisibility = field.IsMultiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden
                };
                control = textBox;
            }

            controls[field.Key] = control;
            fieldPanel.Children.Add(control);
            Grid.SetColumn(fieldPanel, fieldColumns == 1 ? 0 : fieldIndex % 2 == 0 ? 0 : 2);
            Grid.SetRow(fieldPanel, fieldColumns == 1 ? fieldIndex : fieldIndex / 2);
            fieldsGrid.Children.Add(fieldPanel);
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var cancelButton = new Button
        {
            Content = "Hủy",
            Width = 90,
            Height = 34,
            Margin = new Thickness(0, 0, 8, 0),
            Style = Application.Current.TryFindResource("OutlineButton") as Style
        };
        cancelButton.Click += (_, _) => dialog.DialogResult = false;

        var saveButton = new Button
        {
            Content = "Lưu dữ liệu",
            Width = 120,
            Height = 34,
            Style = Application.Current.TryFindResource("PrimaryGradientButton") as Style
        };
        saveButton.Click += (_, _) =>
        {
            var result = new Dictionary<string, string>();
            foreach (var field in fields)
            {
                var value = controls[field.Key] switch
                {
                    TextBox textBox => textBox.Text.Trim(),
                    ComboBox combo => (combo.SelectedValue as string) ?? string.Empty,
                    _ => string.Empty
                };

                if (field.IsRequired && string.IsNullOrWhiteSpace(value))
                {
                    MessageBox.Show($"Vui lòng nhập {field.Label.ToLowerInvariant()}.", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                result[field.Key] = value;
            }

            submittedValues = result;
            dialog.DialogResult = true;
        };

        buttons.Children.Add(cancelButton);
        buttons.Children.Add(saveButton);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        dialog.Content = new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var accepted = dialog.ShowDialog() == true;
        values = submittedValues;
        return accepted;
    }
}
