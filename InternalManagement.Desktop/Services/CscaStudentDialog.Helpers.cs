using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace InternalManagement.Desktop.Services;

public static partial class CscaStudentDialog
{
    public static int ResolveAutomaticPaymentStatus(decimal payableAmount, decimal paidAmount, int currentStatus)
    {
        // A free class can still be explicitly marked Paid. Do not immediately
        // overwrite that selection just because both payable and paid are zero.
        if (payableAmount <= 0)
            return currentStatus;
        if (paidAmount >= payableAmount)
            return 2;
        return paidAmount <= 0 ? 0 : 1;
    }

    private static string FormatMoneyInput(decimal value) =>
        string.Format(new CultureInfo("vi-VN"), "{0:N0}", value);

    private static decimal ParseMoney(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var raw = new string(text.Where(char.IsDigit).ToArray());
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var val) ? val : 0;
    }

    private static void AttachCurrencyFormatting(TextBox box, Action? onValueChanged = null)
    {
        var isFormatting = false;
        box.TextChanged += (_, _) =>
        {
            if (isFormatting) return;
            isFormatting = true;
            try
            {
                var rawText = box.Text;
                var caretIndex = box.CaretIndex;
                var digitsBeforeCaret = 0;
                for (int i = 0; i < Math.Min(caretIndex, rawText.Length); i++)
                {
                    if (char.IsDigit(rawText[i])) digitsBeforeCaret++;
                }

                var onlyDigits = new string(rawText.Where(char.IsDigit).ToArray());
                if (string.IsNullOrEmpty(onlyDigits))
                {
                    box.Text = "0";
                    box.CaretIndex = 1;
                }
                else
                {
                    if (decimal.TryParse(onlyDigits, NumberStyles.Number, CultureInfo.InvariantCulture, out var num))
                    {
                        var formatted = string.Format(new CultureInfo("vi-VN"), "{0:N0}", num);
                        box.Text = formatted;

                        var newCaret = 0;
                        var countedDigits = 0;
                        for (int i = 0; i < formatted.Length; i++)
                        {
                            if (char.IsDigit(formatted[i])) countedDigits++;
                            if (countedDigits <= digitsBeforeCaret) newCaret = i + 1;
                        }
                        box.CaretIndex = Math.Min(newCaret, formatted.Length);
                    }
                }
            }
            finally
            {
                isFormatting = false;
                onValueChanged?.Invoke();
            }
        };
    }

    private static (Grid Grid, StackPanel Left, StackPanel Right) CreateTwoColumnGrid()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel();
        var right = new StackPanel();
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return (grid, left, right);
    }

    private static void AddFormField(StackPanel parent, string labelText, UIElement control)
    {
        parent.Children.Add(new TextBlock
        {
            Text = labelText,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            Margin = new Thickness(0, 0, 0, 4)
        });
        parent.Children.Add(control);
    }

    private static TextBox CreateTextBox(string? text) => new()
    {
        Text = text ?? string.Empty,
        Height = 36,
        Padding = new Thickness(10, 7, 10, 7),
        Background = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
        BorderThickness = new Thickness(1),
        FontSize = 13,
        VerticalContentAlignment = VerticalAlignment.Center
    };

    private static Button CreatePresetButton(string text, Action onClick)
    {
        var btn = new Button
        {
            Content = text,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 8, 0),
            Height = 28,
            FontSize = 11,
            Cursor = Cursors.Hand,
            Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            BorderThickness = new Thickness(1),
            Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85))
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private static string? NullIfEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
