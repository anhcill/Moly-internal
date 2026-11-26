using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using InternalManagement.Desktop.Converters;

namespace InternalManagement.Desktop.Controls;

public partial class StatusPill : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(StatusPill),
        new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty DisplayTextProperty = DependencyProperty.Register(
        nameof(DisplayText),
        typeof(string),
        typeof(StatusPill),
        new PropertyMetadata(null, OnTextChanged));

    private static readonly SolidColorBrush GreenDot = Freeze("#10B981");
    private static readonly SolidColorBrush OrangeDot = Freeze("#F59E0B");
    private static readonly SolidColorBrush RedDot = Freeze("#EF4444");
    private static readonly SolidColorBrush BlueDot = Freeze("#3B82F6");
    private static readonly SolidColorBrush PurpleDot = Freeze("#8B5CF6");
    private static readonly SolidColorBrush SlateDot = Freeze("#64748B");

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string? DisplayText
    {
        get => (string?)GetValue(DisplayTextProperty);
        set => SetValue(DisplayTextProperty, value);
    }

    public StatusPill()
    {
        InitializeComponent();
        ApplyVisual();
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StatusPill pill)
            pill.ApplyVisual();
    }

    private void ApplyVisual()
    {
        var raw = Text?.Trim() ?? string.Empty;
        var label = !string.IsNullOrWhiteSpace(DisplayText) ? DisplayText! : FormatFriendlyLabel(raw);
        LabelBlock.Text = string.IsNullOrWhiteSpace(label) ? "—" : label;

        var tone = StatusBadgeClassifier.Classify(raw);
        switch (tone)
        {
            case StatusTone.Success:
                PillBorder.SetResourceReference(Border.BackgroundProperty, "SurfaceSuccessBgBrush");
                PillBorder.SetResourceReference(Border.BorderBrushProperty, "SurfaceSuccessBorderBrush");
                LabelBlock.SetResourceReference(TextBlock.ForegroundProperty, "SurfaceSuccessTextBrush");
                DotIndicator.Fill = GreenDot;
                break;
            case StatusTone.Warning:
                PillBorder.SetResourceReference(Border.BackgroundProperty, "SurfaceWarningBgBrush");
                PillBorder.SetResourceReference(Border.BorderBrushProperty, "SurfaceWarningBorderBrush");
                LabelBlock.SetResourceReference(TextBlock.ForegroundProperty, "SurfaceWarningTextBrush");
                DotIndicator.Fill = OrangeDot;
                break;
            case StatusTone.Danger:
                PillBorder.SetResourceReference(Border.BackgroundProperty, "SurfaceDangerBgBrush");
                PillBorder.SetResourceReference(Border.BorderBrushProperty, "SurfaceDangerBorderBrush");
                LabelBlock.SetResourceReference(TextBlock.ForegroundProperty, "SurfaceDangerTextBrush");
                DotIndicator.Fill = RedDot;
                break;
            case StatusTone.Info:
                PillBorder.SetResourceReference(Border.BackgroundProperty, "SurfaceInfoBgBrush");
                PillBorder.SetResourceReference(Border.BorderBrushProperty, "SurfaceInfoBorderBrush");
                LabelBlock.SetResourceReference(TextBlock.ForegroundProperty, "SurfaceInfoTextBrush");
                DotIndicator.Fill = BlueDot;
                break;
            case StatusTone.Accent:
                PillBorder.SetResourceReference(Border.BackgroundProperty, "SurfaceAccentBgBrush");
                PillBorder.SetResourceReference(Border.BorderBrushProperty, "SurfaceAccentBorderBrush");
                LabelBlock.SetResourceReference(TextBlock.ForegroundProperty, "SurfaceAccentTextBrush");
                DotIndicator.Fill = PurpleDot;
                break;
            default:
                PillBorder.SetResourceReference(Border.BackgroundProperty, "PillSlateBgBrush");
                PillBorder.SetResourceReference(Border.BorderBrushProperty, "BorderLightBrush");
                LabelBlock.SetResourceReference(TextBlock.ForegroundProperty, "PillSlateTextBrush");
                DotIndicator.Fill = SlateDot;
                break;
        }
    }

    private static string FormatFriendlyLabel(string raw) => raw.ToUpperInvariant() switch
    {
        "ACTIVE" => "Hoạt động",
        "OPEN" => "Đang mở",
        "DRAFT" => "Nháp",
        "PUBLISHED" => "Đã xuất bản",
        "ARCHIVED" => "Lưu trữ",
        "CLOSED" => "Đã đóng",
        "COMPLETED" => "Hoàn thành",
        "CANCELLED" or "CANCELED" => "Đã hủy",
        "PENDING" => "Chờ xử lý",
        "INREVIEW" or "IN_REVIEW" => "Chờ duyệt",
        "CALCULATED" => "Đã tính",
        "APPROVED" => "Đã duyệt",
        "PAID" => "Đã thanh toán",
        "PARTIAL" => "Đóng 1 phần",
        "UNPAID" => "Chưa đóng",
        "REFUNDED" => "Đã hoàn tiền",
        "PRESENT" => "Đúng giờ",
        "LATE" => "Đi muộn",
        "ABSENT" => "Vắng mặt",
        "LEAVE" => "Nghỉ phép",
        "RESIGNED" => "Đã nghỉ việc",
        "BLACKLISTED" => "Blacklist",
        "TRIAL" => "Học thử",
        "OFFICIAL" => "Chính thức",
        _ => raw
    };

    private static SolidColorBrush Freeze(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
