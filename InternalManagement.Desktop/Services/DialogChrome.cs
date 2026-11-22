using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace InternalManagement.Desktop.Services;

/// <summary>
/// Shared Fluent SaaS modal chrome (sticky header, scrollable body, sticky footer, section cards)
/// used across C#-constructed desktop dialogs so every popup matches Light & Dark themes identically.
/// </summary>
internal static class DialogChrome
{
    public static Grid BuildModalShell(
        Window dialog,
        string title,
        string? subtitle,
        string iconGlyph,
        UIElement bodyContent,
        Panel footerButtons)
    {
        dialog.SetResourceReference(Window.BackgroundProperty, "CardBgBrush");
        ThemeService.SetWindowDarkMode(dialog, ThemeService.Instance.IsDark);

        var shell = new Grid();
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Sticky Header
        var header = new Border
        {
            Padding = new Thickness(24, 16, 24, 14),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        header.SetResourceReference(Border.BackgroundProperty, "CardAltBgBrush");
        header.SetResourceReference(Border.BorderBrushProperty, "BorderLightBrush");

        var headerStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var iconBadge = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(9),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        iconBadge.SetResourceReference(Border.BackgroundProperty, "SurfaceAccentBgBrush");
        iconBadge.SetResourceReference(Border.BorderBrushProperty, "SurfaceAccentBorderBrush");
        iconBadge.BorderThickness = new Thickness(1);
        var iconText = new TextBlock
        {
            Text = iconGlyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        iconText.SetResourceReference(TextBlock.ForegroundProperty, "SurfaceAccentTextBrush");
        iconBadge.Child = iconText;
        headerStack.Children.Add(iconBadge);

        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 17,
            FontWeight = FontWeights.Bold
        };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextDarkBrush");
        titleStack.Children.Add(titleBlock);

        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            var subtitleBlock = new TextBlock
            {
                Text = subtitle,
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            subtitleBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            titleStack.Children.Add(subtitleBlock);
        }

        headerStack.Children.Add(titleStack);
        header.Child = headerStack;
        Grid.SetRow(header, 0);
        shell.Children.Add(header);

        // Scrollable Body
        var bodyHost = new Border { Padding = new Thickness(24, 18, 24, 18) };
        bodyHost.Child = bodyContent;
        var scrollViewer = new ScrollViewer
        {
            Content = bodyHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scrollViewer, 1);
        shell.Children.Add(scrollViewer);

        // Sticky Footer
        var footer = new Border
        {
            Padding = new Thickness(24, 12, 24, 12),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = footerButtons
        };
        footer.SetResourceReference(Border.BackgroundProperty, "CardAltBgBrush");
        footer.SetResourceReference(Border.BorderBrushProperty, "BorderLightBrush");
        Grid.SetRow(footer, 2);
        shell.Children.Add(footer);

        dialog.Content = shell;
        return shell;
    }

    public static StackPanel CreateSectionCard(Panel parent, string heading, string? hint = null)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 13, 16, 10),
            Margin = new Thickness(0, 0, 0, 12)
        };
        border.SetResourceReference(Border.BackgroundProperty, "CardAltBgBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderLightBrush");

        var stack = new StackPanel();
        var headingBlock = new TextBlock
        {
            Text = heading,
            FontSize = 12.5,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, string.IsNullOrWhiteSpace(hint) ? 10 : 2)
        };
        headingBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextDarkBrush");
        stack.Children.Add(headingBlock);

        if (!string.IsNullOrWhiteSpace(hint))
        {
            var hintBlock = new TextBlock
            {
                Text = hint,
                FontSize = 11.5,
                Margin = new Thickness(0, 0, 0, 10),
                TextWrapping = TextWrapping.Wrap
            };
            hintBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            stack.Children.Add(hintBlock);
        }

        border.Child = stack;
        parent.Children.Add(border);
        return stack;
    }
}
