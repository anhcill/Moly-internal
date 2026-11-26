using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace InternalManagement.Desktop.Services;

/// <summary>
/// Pure WPF vector chart renderer for the Finance module.
/// Fully theme-aware (Dark &amp; Light Mode) with hover tooltips and zero external dependencies.
/// </summary>
internal static class FinanceChartRenderer
{
    private static readonly SolidColorBrush EmeraldBrush = Freeze(Color.FromRgb(16, 185, 129));
    private static readonly SolidColorBrush RedBrush = Freeze(Color.FromRgb(239, 68, 68));
    private static readonly SolidColorBrush AmberBrush = Freeze(Color.FromRgb(245, 158, 11));
    private static readonly SolidColorBrush SkyBrush = Freeze(Color.FromRgb(14, 165, 233));
    private static readonly SolidColorBrush IndigoBrush = Freeze(Color.FromRgb(99, 102, 241));

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static void DrawMonthlyBarAndBalanceChart(Canvas canvas, ApiClient.FinanceWorkspaceReportItem? report)
    {
        canvas.Children.Clear();
        var rows = report?.Months?.ToList();
        if (rows is null || rows.Count == 0)
        {
            DrawEmptyMessage(canvas, "Chưa có dữ liệu dòng tiền theo tháng");
            return;
        }

        var width = canvas.ActualWidth;
        var height = canvas.ActualHeight;
        if (width < 180 || height < 120) return;

        const double left = 62, top = 18, right = 18, bottom = 42;
        var plotWidth = width - left - right;
        var plotHeight = height - top - bottom;

        var values = rows
            .SelectMany(x => new[] { x.TotalInflow, x.TotalOutflow, x.ClosingCash, x.NetProfit })
            .Append(0m)
            .ToArray();
        var minVal = Math.Min(0m, values.Min());
        var maxVal = Math.Max(1m, values.Max());
        var range = maxVal - minVal;

        double Y(decimal amount) => top + (double)((maxVal - amount) / range) * plotHeight;
        var zeroY = Y(0m);

        var gridLine = ThemeService.GetBrush("BorderLightBrush", "#E2E8F0");
        var zeroLine = ThemeService.GetBrush("BorderMediumBrush", "#94A3B8");
        var axisText = ThemeService.GetBrush("TextMutedBrush", "#64748B");
        var balanceLine = ThemeService.GetBrush("PrimaryBrush", "#6366F1");

        // Horizontal gridlines & Y ticks
        for (var i = 0; i <= 4; i++)
        {
            var tickVal = minVal + range * i / 4m;
            var y = Y(tickVal);
            canvas.Children.Add(new Line
            {
                X1 = left,
                X2 = width - right,
                Y1 = y,
                Y2 = y,
                Stroke = tickVal == 0 ? zeroLine : gridLine,
                StrokeThickness = tickVal == 0 ? 1.2 : 0.7,
                StrokeDashArray = tickVal == 0 ? null : new DoubleCollection { 3, 3 }
            });
            AddLabel(canvas, FormatShortMoney(tickVal), 4, y - 7, axisText, 9.5, 52, TextAlignment.Right);
        }

        var step = plotWidth / rows.Count;
        var barWidth = Math.Clamp(step * 0.28, 8, 26);
        var linePoints = new PointCollection();

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var centerX = left + step * (i + 0.5);
            var isForecast = row.Kind.Equals("Forecast", StringComparison.OrdinalIgnoreCase);
            var isMixed = row.Kind.Equals("Mixed", StringComparison.OrdinalIgnoreCase);

            // Subtle shading for forecast months
            if (isForecast || isMixed)
            {
                var shade = new Rectangle
                {
                    Width = Math.Max(4, step - 4),
                    Height = plotHeight,
                    RadiusX = 4,
                    RadiusY = 4,
                    Fill = isForecast
                        ? new SolidColorBrush(Color.FromArgb(20, 99, 102, 241))
                        : new SolidColorBrush(Color.FromArgb(18, 245, 158, 11))
                };
                Canvas.SetLeft(shade, left + i * step + 2);
                Canvas.SetTop(shade, top);
                canvas.Children.Insert(0, shade);
            }

            // Inflow bar (left)
            var inTop = Y(row.TotalInflow);
            var inBar = new Rectangle
            {
                Width = barWidth,
                Height = Math.Max(2, Math.Abs(zeroY - inTop)),
                RadiusX = 3,
                RadiusY = 3,
                Fill = EmeraldBrush,
                Opacity = isForecast ? 0.65 : 0.92,
                ToolTip = BuildMonthTooltip(row)
            };
            Canvas.SetLeft(inBar, centerX - barWidth - 1.5);
            Canvas.SetTop(inBar, Math.Min(inTop, zeroY));
            canvas.Children.Add(inBar);

            // Outflow bar (right)
            var outTop = Y(row.TotalOutflow);
            var outBar = new Rectangle
            {
                Width = barWidth,
                Height = Math.Max(2, Math.Abs(zeroY - outTop)),
                RadiusX = 3,
                RadiusY = 3,
                Fill = RedBrush,
                Opacity = isForecast ? 0.65 : 0.92,
                ToolTip = BuildMonthTooltip(row)
            };
            Canvas.SetLeft(outBar, centerX + 1.5);
            Canvas.SetTop(outBar, Math.Min(outTop, zeroY));
            canvas.Children.Add(outBar);

            // Closing balance line point
            var balY = Y(row.ClosingCash);
            linePoints.Add(new Point(centerX, balY));

            // X-axis month & kind badge
            AddLabel(canvas, row.MonthLabel, centerX - 24, height - 35, axisText, rows.Count > 12 ? 8.5 : 9.5, 48, TextAlignment.Center);
            var badgeText = isForecast ? "DB" : isMixed ? "TT+DB" : "TT";
            var badgeBrush = isForecast ? balanceLine : isMixed ? AmberBrush : axisText;
            AddLabel(canvas, badgeText, centerX - 20, height - 20, badgeBrush, 8.5, 40, TextAlignment.Center, FontWeights.SemiBold);
        }

        // Draw closing balance polyline + dots on top of bars
        if (linePoints.Count > 1)
        {
            canvas.Children.Add(new Polyline
            {
                Points = linePoints,
                Stroke = balanceLine,
                StrokeThickness = 2.2,
                StrokeLineJoin = PenLineJoin.Round
            });
        }

        for (var i = 0; i < linePoints.Count; i++)
        {
            var pt = linePoints[i];
            var dot = new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = balanceLine,
                Stroke = ThemeService.GetBrush("CardBgBrush", "#FFFFFF"),
                StrokeThickness = 1.5,
                ToolTip = BuildMonthTooltip(rows[i])
            };
            Canvas.SetLeft(dot, pt.X - 3.5);
            Canvas.SetTop(dot, pt.Y - 3.5);
            canvas.Children.Add(dot);
        }
    }

    public static void DrawProfitBreakdownChart(Canvas canvas, ApiClient.FinanceWorkspaceReportItem? report)
    {
        canvas.Children.Clear();
        if (report is null)
        {
            DrawEmptyMessage(canvas, "Chưa có dữ liệu lãi lỗ");
            return;
        }

        var width = canvas.ActualWidth;
        var height = canvas.ActualHeight;
        if (width < 200 || height < 120) return;

        var totalIn = report.PeriodTotalInflow;
        var teacherOut = report.Months.Sum(m => m.TeacherPayments);
        var otherOut = Math.Max(0m, report.PeriodTotalOutflow - teacherOut);
        var netProfit = report.PeriodNetProfit;

        var items = new (string Label, decimal Value, Brush Color)[]
        {
            ("Tổng Thu", totalIn, EmeraldBrush),
            ("Chi Giáo viên & Ứng", teacherOut, AmberBrush),
            ("Chi Vận hành & Khác", otherOut, RedBrush),
            (netProfit >= 0 ? "Lợi nhuận ròng (Lãi)" : "Lợi nhuận ròng (Lỗ)", Math.Abs(netProfit), netProfit >= 0 ? IndigoBrush : RedBrush)
        };

        var maxVal = Math.Max(1m, items.Max(x => x.Value));
        var labelBrush = ThemeService.GetBrush("TextDarkBrush", "#0F172A");
        var mutedBrush = ThemeService.GetBrush("TextMutedBrush", "#64748B");
        var trackBrush = ThemeService.GetBrush("BorderLightBrush", "#E2E8F0");

        var rowHeight = (height - 16) / items.Length;
        var barLeft = 148.0;
        var valueWidth = 132.0;
        var maxBarWidth = Math.Max(40, width - barLeft - valueWidth - 16);

        for (var i = 0; i < items.Length; i++)
        {
            var (label, val, color) = items[i];
            var y = 8 + i * rowHeight;
            var barH = Math.Clamp(rowHeight * 0.44, 14, 24);
            var barY = y + (rowHeight - barH) / 2;

            AddLabel(canvas, label, 6, barY + (barH - 15) / 2, labelBrush, 11.5, barLeft - 14, TextAlignment.Left,
                i == items.Length - 1 ? FontWeights.Bold : FontWeights.SemiBold);

            // Track background
            var track = new Rectangle
            {
                Width = maxBarWidth,
                Height = barH,
                RadiusX = 6,
                RadiusY = 6,
                Fill = trackBrush,
                Opacity = 0.45
            };
            Canvas.SetLeft(track, barLeft);
            Canvas.SetTop(track, barY);
            canvas.Children.Add(track);

            // Value bar
            var barW = maxVal > 0 ? (double)(val / maxVal) * maxBarWidth : 0;
            var fill = new Rectangle
            {
                Width = Math.Max(val > 0 ? 6 : 0, barW),
                Height = barH,
                RadiusX = 6,
                RadiusY = 6,
                Fill = color,
                ToolTip = $"{label}: {(i == 3 && netProfit < 0 ? "-" : "")}{val:N0} đ"
            };
            Canvas.SetLeft(fill, barLeft);
            Canvas.SetTop(fill, barY);
            canvas.Children.Add(fill);

            var pct = totalIn > 0 ? $" ({val / totalIn * 100m:0.#}%)" : "";
            var sign = i == 3 && netProfit < 0 ? "-" : "";
            AddLabel(canvas, $"{sign}{FormatShortMoney(val)}{pct}", barLeft + maxBarWidth + 8, barY + (barH - 15) / 2,
                i == items.Length - 1 ? color : mutedBrush, 11, valueWidth, TextAlignment.Left,
                i == items.Length - 1 ? FontWeights.Bold : FontWeights.SemiBold);
        }
    }

    public static void DrawTeacherPayrollChart(Canvas canvas, ApiClient.FinanceWorkspaceReportItem? report)
    {
        canvas.Children.Clear();
        var teachers = report?.TeacherCosts?
            .Where(t => t.EarnedAmount > 0 || t.RemainingAdvance > 0 || t.SettledAmount > 0)
            .OrderByDescending(t => t.UnpaidEarned)
            .ThenByDescending(t => t.EarnedAmount)
            .Take(6)
            .ToList();

        if (teachers is null || teachers.Count == 0)
        {
            DrawEmptyMessage(canvas, "Chưa có dữ liệu lương & ứng giáo viên");
            return;
        }

        var width = canvas.ActualWidth;
        var height = canvas.ActualHeight;
        if (width < 200 || height < 120) return;

        var maxTotal = Math.Max(1m, teachers.Max(t => Math.Max(t.EarnedAmount, t.SettledAmount + t.RemainingAdvance + t.UnpaidEarned)));
        var labelBrush = ThemeService.GetBrush("TextDarkBrush", "#0F172A");
        var mutedBrush = ThemeService.GetBrush("TextMutedBrush", "#64748B");
        var trackBrush = ThemeService.GetBrush("BorderLightBrush", "#E2E8F0");

        var rowHeight = (height - 12) / teachers.Count;
        var barLeft = 132.0;
        var rightLabelWidth = 108.0;
        var maxBarWidth = Math.Max(40, width - barLeft - rightLabelWidth - 14);

        for (var i = 0; i < teachers.Count; i++)
        {
            var t = teachers[i];
            var y = 6 + i * rowHeight;
            var barH = Math.Clamp(rowHeight * 0.46, 12, 20);
            var barY = y + (rowHeight - barH) / 2;

            var nameBlock = new TextBlock
            {
                Text = t.TeacherName,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = labelBrush,
                Width = barLeft - 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Canvas.SetLeft(nameBlock, 4);
            Canvas.SetTop(nameBlock, barY + (barH - 14) / 2);
            canvas.Children.Add(nameBlock);

            var track = new Rectangle
            {
                Width = maxBarWidth,
                Height = barH,
                RadiusX = 5,
                RadiusY = 5,
                Fill = trackBrush,
                Opacity = 0.45
            };
            Canvas.SetLeft(track, barLeft);
            Canvas.SetTop(track, barY);
            canvas.Children.Add(track);

            var tooltip = $"{t.TeacherName}\nCông thực tế: {t.EarnedAmount:N0} đ\nĐã quyết toán: {t.SettledAmount:N0} đ\nỨng còn dư: {t.RemainingAdvance:N0} đ\nCòn phải chuyển: {t.UnpaidEarned:N0} đ";
            var cursorX = barLeft;

            void AddSegment(decimal amount, Brush brush)
            {
                if (amount <= 0) return;
                var segW = (double)(amount / maxTotal) * maxBarWidth;
                if (segW < 2) segW = 2;
                var rect = new Rectangle
                {
                    Width = Math.Min(segW, barLeft + maxBarWidth - cursorX),
                    Height = barH,
                    RadiusX = 4,
                    RadiusY = 4,
                    Fill = brush,
                    ToolTip = tooltip
                };
                Canvas.SetLeft(rect, cursorX);
                Canvas.SetTop(rect, barY);
                canvas.Children.Add(rect);
                cursorX += rect.Width + 1;
            }

            AddSegment(t.SettledAmount, EmeraldBrush);
            AddSegment(t.RemainingAdvance, AmberBrush);
            AddSegment(t.UnpaidEarned, RedBrush);

            var rightText = t.UnpaidEarned > 0 ? $"Còn {FormatShortMoney(t.UnpaidEarned)}" : "Đã đủ";
            AddLabel(canvas, rightText, barLeft + maxBarWidth + 8, barY + (barH - 14) / 2,
                t.UnpaidEarned > 0 ? RedBrush : EmeraldBrush, 10.5, rightLabelWidth, TextAlignment.Left, FontWeights.Bold);
        }
    }

    public static void DrawReceivablesDonutChart(Canvas canvas, ApiClient.FinanceWorkspaceReportItem? report)
    {
        canvas.Children.Clear();
        var items = report?.Receivables?.Where(r => r.Status != "Cancelled").ToList();
        if (items is null || items.Count == 0)
        {
            DrawEmptyMessage(canvas, "Chưa có dữ liệu công nợ học viên");
            return;
        }

        var width = canvas.ActualWidth;
        var height = canvas.ActualHeight;
        if (width < 220 || height < 120) return;

        var totalPaid = items.Sum(r => Math.Max(0m, r.PaidAmount - r.RefundedAmount));
        var overdueDebt = items.Where(r => r.OutstandingAmount > 0 && r.DueDate.HasValue && r.DueDate.Value < DateTime.UtcNow).Sum(r => r.OutstandingAmount);
        var currentDebt = Math.Max(0m, items.Sum(r => r.OutstandingAmount) - overdueDebt);
        var total = totalPaid + currentDebt + overdueDebt;
        if (total <= 0)
        {
            DrawEmptyMessage(canvas, "Chưa phát sinh học phí cần thu");
            return;
        }

        var diameter = Math.Min(height - 24, Math.Min(width * 0.42, 150));
        var radius = diameter / 2;
        var cx = 20 + radius;
        var cy = height / 2;
        var thickness = Math.Max(14, radius * 0.28);

        var slices = new (string Label, decimal Value, Brush Color)[]
        {
            ("Đã thu", totalPaid, EmeraldBrush),
            ("Nợ trong hạn", currentDebt, SkyBrush),
            ("Nợ quá hạn", overdueDebt, RedBrush)
        };

        var startAngle = -90.0;
        foreach (var (label, val, brush) in slices)
        {
            if (val <= 0) continue;
            var sweep = (double)(val / total) * 360.0;
            if (sweep >= 359.9)
            {
                var ring = new Ellipse
                {
                    Width = diameter - thickness,
                    Height = diameter - thickness,
                    Stroke = brush,
                    StrokeThickness = thickness,
                    ToolTip = $"{label}: {val:N0} đ (100%)"
                };
                Canvas.SetLeft(ring, cx - (diameter - thickness) / 2);
                Canvas.SetTop(ring, cy - (diameter - thickness) / 2);
                canvas.Children.Add(ring);
            }
            else
            {
                var arcPath = CreateArcPath(cx, cy, radius - thickness / 2, startAngle, startAngle + sweep, brush, thickness);
                arcPath.ToolTip = $"{label}: {val:N0} đ ({val / total * 100m:0.#}%)";
                canvas.Children.Add(arcPath);
            }
            startAngle += sweep;
        }

        // Center percentage text
        var collectedPct = totalPaid / total * 100m;
        var darkBrush = ThemeService.GetBrush("TextDarkBrush", "#0F172A");
        var mutedBrush = ThemeService.GetBrush("TextMutedBrush", "#64748B");
        AddLabel(canvas, $"{collectedPct:0.#}%", cx - 34, cy - 13, darkBrush, 16, 68, TextAlignment.Center, FontWeights.Bold);
        AddLabel(canvas, "Đã thu", cx - 34, cy + 7, mutedBrush, 9.5, 68, TextAlignment.Center);

        // Right-side clean legend
        var legendX = cx + radius + 24;
        var legendY = Math.Max(12, cy - 42);
        var legendWidth = Math.Max(120, width - legendX - 10);
        for (var i = 0; i < slices.Length; i++)
        {
            var (label, val, brush) = slices[i];
            var rowY = legendY + i * 32;
            var dot = new Ellipse { Width = 9, Height = 9, Fill = brush };
            Canvas.SetLeft(dot, legendX);
            Canvas.SetTop(dot, rowY + 4);
            canvas.Children.Add(dot);

            AddLabel(canvas, label, legendX + 16, rowY, mutedBrush, 10.5, legendWidth - 16, TextAlignment.Left);
            AddLabel(canvas, $"{val:N0} đ ({val / total * 100m:0.#}%)", legendX + 16, rowY + 13, darkBrush, 11.5, legendWidth - 16, TextAlignment.Left, FontWeights.Bold);
        }
    }

    public static void DrawCompanyAreasComparisonChart(Canvas canvas, IReadOnlyList<ApiClient.FinancialAreaSummaryItem>? areas)
    {
        canvas.Children.Clear();
        if (areas is null || areas.Count == 0)
        {
            DrawEmptyMessage(canvas, "Chưa có dữ liệu so sánh theo mảng");
            return;
        }

        var width = canvas.ActualWidth;
        var height = canvas.ActualHeight;
        if (width < 220 || height < 110) return;

        const double left = 60, top = 16, right = 18, bottom = 30;
        var plotWidth = width - left - right;
        var plotHeight = height - top - bottom;

        var maxVal = Math.Max(1m, areas.SelectMany(a => new[] { a.TotalIncome, a.TotalExpense, Math.Max(0m, a.OperatingProfit) }).Max());
        var gridLine = ThemeService.GetBrush("BorderLightBrush", "#E2E8F0");
        var axisText = ThemeService.GetBrush("TextMutedBrush", "#64748B");

        double Y(decimal v) => top + (double)((maxVal - v) / maxVal) * plotHeight;
        for (var i = 0; i <= 3; i++)
        {
            var v = maxVal * i / 3m;
            var y = Y(v);
            canvas.Children.Add(new Line { X1 = left, X2 = width - right, Y1 = y, Y2 = y, Stroke = gridLine, StrokeThickness = 0.7 });
            AddLabel(canvas, FormatShortMoney(v), 4, y - 7, axisText, 9.5, 50, TextAlignment.Right);
        }

        var step = plotWidth / areas.Count;
        var barW = Math.Clamp(step * 0.2, 12, 34);
        var baseY = top + plotHeight;

        for (var i = 0; i < areas.Count; i++)
        {
            var a = areas[i];
            var cx = left + step * (i + 0.5);
            var tip = $"{a.AreaName}\nThu: {a.TotalIncome:N0} đ\nChi: {a.TotalExpense:N0} đ\nLợi nhuận: {a.OperatingProfit:N0} đ";

            void DrawBar(decimal val, double offsetX, Brush fill)
            {
                var clamped = Math.Max(0m, val);
                var barTop = Y(clamped);
                var rect = new Rectangle
                {
                    Width = barW,
                    Height = Math.Max(clamped > 0 ? 3 : 0, baseY - barTop),
                    RadiusX = 3,
                    RadiusY = 3,
                    Fill = fill,
                    ToolTip = tip
                };
                Canvas.SetLeft(rect, cx + offsetX);
                Canvas.SetTop(rect, barTop);
                canvas.Children.Add(rect);
            }

            DrawBar(a.TotalIncome, -barW * 1.5 - 3, EmeraldBrush);
            DrawBar(a.TotalExpense, -barW * 0.5, RedBrush);
            DrawBar(a.OperatingProfit, barW * 0.5 + 3, IndigoBrush);

            AddLabel(canvas, a.AreaName, cx - step / 2 + 4, height - 22, axisText, 10.5, step - 8, TextAlignment.Center, FontWeights.SemiBold);
        }
    }

    private static Path CreateArcPath(double cx, double cy, double r, double startAngleDeg, double endAngleDeg, Brush stroke, double thickness)
    {
        var startRad = startAngleDeg * Math.PI / 180.0;
        var endRad = endAngleDeg * Math.PI / 180.0;
        var startPt = new Point(cx + r * Math.Cos(startRad), cy + r * Math.Sin(startRad));
        var endPt = new Point(cx + r * Math.Cos(endRad), cy + r * Math.Sin(endRad));
        var isLargeArc = (endAngleDeg - startAngleDeg) > 180.0;

        var figure = new PathFigure { StartPoint = startPt, IsClosed = false };
        figure.Segments.Add(new ArcSegment(endPt, new Size(r, r), 0, isLargeArc, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        return new Path
        {
            Data = geometry,
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Flat,
            StrokeEndLineCap = PenLineCap.Flat
        };
    }

    private static string BuildMonthTooltip(ApiClient.FinanceMonthlyRowItem row) =>
        $"Tháng {row.MonthLabel} ({row.KindLabel})\n" +
        $"• Tổng Thu: {row.TotalInflow:N0} đ\n" +
        $"• Tổng Chi: {row.TotalOutflow:N0} đ\n" +
        $"• Lãi / Lỗ tháng: {row.NetProfitDisplay}\n" +
        $"• Số dư cuối kỳ: {row.ClosingCash:N0} đ";

    private static string FormatShortMoney(decimal amount)
    {
        var abs = Math.Abs(amount);
        var sign = amount < 0 ? "-" : "";
        if (abs >= 1_000_000_000m) return $"{sign}{abs / 1_000_000_000m:0.##}tỷ";
        if (abs >= 1_000_000m) return $"{sign}{abs / 1_000_000m:0.#}tr";
        if (abs >= 1_000m) return $"{sign}{abs / 1_000m:0.#}k";
        return $"{sign}{abs:N0}";
    }

    private static void DrawEmptyMessage(Canvas canvas, string message)
    {
        var block = new TextBlock
        {
            Text = message,
            FontSize = 12,
            Foreground = ThemeService.GetBrush("TextMutedBrush", "#64748B")
        };
        Canvas.SetLeft(block, 16);
        Canvas.SetTop(block, 18);
        canvas.Children.Add(block);
    }

    private static void AddLabel(
        Canvas canvas,
        string text,
        double x,
        double y,
        Brush brush,
        double fontSize,
        double width,
        TextAlignment align,
        FontWeight? weight = null)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            Foreground = brush,
            Width = width,
            TextAlignment = align,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = weight ?? FontWeights.Normal
        };
        Canvas.SetLeft(block, x);
        Canvas.SetTop(block, y);
        canvas.Children.Add(block);
    }
}
