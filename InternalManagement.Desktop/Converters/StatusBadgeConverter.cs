using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace InternalManagement.Desktop.Converters;

public enum StatusTone
{
    Neutral,
    Success,
    Warning,
    Danger,
    Info,
    Accent
}

public static class StatusBadgeClassifier
{
    public static StatusTone Classify(object? value)
    {
        var raw = value?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return StatusTone.Neutral;

        var key = raw.ToUpperInvariant();

        // Success / Positive
        if (key is "ACTIVE" or "PUBLISHED" or "PAID" or "COMPLETED" or "APPROVED" or "SETTLED"
            or "PRESENT" or "HEALTHY" or "SYNCED" or "SUCCEEDED" or "SUCCESS" or "OPEN"
            or "ĐANG LÀM VIỆC" or "ĐÃ THANH TOÁN" or "ĐÃ CHI" or "ĐÃ DUYỆT" or "ĐÃ PHÁT HÀNH"
            or "ĐANG MỞ" or "HOÀN THÀNH" or "ĐÚNG GIỜ" or "CHÍNH THỨC" or "OFFICIAL" or "ĐÃ ĐÓNG ĐỦ")
            return StatusTone.Success;

        // Warning / In Progress
        if (key is "PARTIAL" or "PENDING" or "INREVIEW" or "IN_REVIEW" or "CALCULATED" or "PROCESSING"
            or "INSPECTING" or "LATE" or "TRIAL" or "HOLD" or "PENDINGREVIEW"
            or "ĐÓNG MỘT PHẦN" or "CHỜ DUYỆT" or "ĐANG XỬ LÝ" or "ĐI MUỘN" or "HỌC THỬ"
            or "CÒN NỢ" or "CHỜ THANH TOÁN" or "ĐÃ TÍNH")
            return StatusTone.Warning;

        // Danger / Error / Negative
        if (key is "CANCELLED" or "CANCELED" or "VOIDED" or "REFUNDED" or "FAILED" or "DEADLETTER"
            or "DEAD_LETTER" or "REJECTED" or "ABSENT" or "BLACKLISTED" or "REVOKED" or "UNPAID"
            or "OVERDUE" or "ERROR"
            or "ĐÃ HỦY" or "HOÀN TIỀN" or "TỪ CHỐI" or "VẮNG MẶT" or "BLACKLIST" or "QUÁ HẠN" or "LỖI")
            return StatusTone.Danger;

        // Info / Special
        if (key is "1-1" or "1V1" or "ONE_ON_ONE" or "VIP" or "LEAVE" or "ONLINE" or "FULL_TIME" or "FULLTIME"
            or "NGHỈ PHÉP" or "LỚP 1-1" or "CHÍNH THỨC (FULL-TIME)")
            return StatusTone.Info;

        // Accent / Purple
        if (key is "PART_TIME" or "PARTTIME" or "MENTOR" or "TEACHINGASSISTANT" or "SCHEDULED" or "UPCOMING"
            or "SẮP MỞ" or "BÁN THỜI GIAN")
            return StatusTone.Accent;

        // Substring fallbacks for Vietnamese composite status labels
        if (key.Contains("HOÀN THÀNH") || key.Contains("ĐÃ CHI") || key.Contains("ĐÃ DUYỆT") || key.Contains("ĐANG MỞ") || key.Contains("ĐÃ ĐÓNG"))
            return StatusTone.Success;
        if (key.Contains("CHỜ") || key.Contains("NỢ") || key.Contains("THỬ") || key.Contains("MỘT PHẦN") || key.Contains("MUỘN"))
            return StatusTone.Warning;
        if (key.Contains("HỦY") || key.Contains("LỖI") || key.Contains("NGHỈ") || key.Contains("TỪ CHỐI") || key.Contains("BLACKLIST"))
            return StatusTone.Danger;

        return StatusTone.Neutral;
    }

    public static Brush ResolveResourceBrush(string key, string fallbackHex)
    {
        if (Application.Current?.TryFindResource(key) is Brush brush)
            return brush;
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallbackHex));
    }
}

public sealed class StatusBadgeBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        StatusBadgeClassifier.Classify(value) switch
        {
            StatusTone.Success => StatusBadgeClassifier.ResolveResourceBrush("PillGreenBgBrush", "#DCFCE7"),
            StatusTone.Warning => StatusBadgeClassifier.ResolveResourceBrush("PillOrangeBgBrush", "#FEF3C7"),
            StatusTone.Danger => StatusBadgeClassifier.ResolveResourceBrush("PillRedBgBrush", "#FEE2E2"),
            StatusTone.Info => StatusBadgeClassifier.ResolveResourceBrush("PillBlueBgBrush", "#DBEAFE"),
            StatusTone.Accent => StatusBadgeClassifier.ResolveResourceBrush("PillPurpleBgBrush", "#F3E8FF"),
            _ => StatusBadgeClassifier.ResolveResourceBrush("PillSlateBgBrush", "#F1F5F9")
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class StatusBadgeForegroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        StatusBadgeClassifier.Classify(value) switch
        {
            StatusTone.Success => StatusBadgeClassifier.ResolveResourceBrush("PillGreenTextBrush", "#15803D"),
            StatusTone.Warning => StatusBadgeClassifier.ResolveResourceBrush("PillOrangeTextBrush", "#B45309"),
            StatusTone.Danger => StatusBadgeClassifier.ResolveResourceBrush("PillRedTextBrush", "#B91C1C"),
            StatusTone.Info => StatusBadgeClassifier.ResolveResourceBrush("PillBlueTextBrush", "#1D4ED8"),
            StatusTone.Accent => StatusBadgeClassifier.ResolveResourceBrush("PillPurpleTextBrush", "#7E22CE"),
            _ => StatusBadgeClassifier.ResolveResourceBrush("PillSlateTextBrush", "#475569")
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class StatusBadgeDotConverter : IValueConverter
{
    private static readonly Brush GreenDot = Freeze("#10B981");
    private static readonly Brush OrangeDot = Freeze("#F59E0B");
    private static readonly Brush RedDot = Freeze("#EF4444");
    private static readonly Brush BlueDot = Freeze("#3B82F6");
    private static readonly Brush PurpleDot = Freeze("#8B5CF6");
    private static readonly Brush SlateDot = Freeze("#64748B");

    private static SolidColorBrush Freeze(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        StatusBadgeClassifier.Classify(value) switch
        {
            StatusTone.Success => GreenDot,
            StatusTone.Warning => OrangeDot,
            StatusTone.Danger => RedDot,
            StatusTone.Info => BlueDot,
            StatusTone.Accent => PurpleDot,
            _ => SlateDot
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
