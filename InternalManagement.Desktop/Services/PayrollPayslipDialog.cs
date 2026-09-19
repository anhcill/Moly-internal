using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace InternalManagement.Desktop.Services;

public sealed record PayrollAdjustmentDraft(string Type, decimal Amount, string Reason);

public static class PayrollPayslipDialog
{
    private sealed record AdjustmentOption(string Type, string Label);
    private sealed record AdjustmentDisplayItem(
        ApiClient.PayrollAdjustmentItem Item,
        string TypeLabel,
        string AmountLabel,
        string Reason);

    private static readonly IReadOnlyList<AdjustmentOption> AdjustmentOptions =
    [
        new("ALLOWANCE", "Trợ cấp"),
        new("KPI_BONUS", "Thưởng KPI"),
        new("BONUS", "Thưởng khác"),
        new("OVERTIME", "Tiền làm thêm giờ"),
        new("HEALTH_INSURANCE", "BHYT khấu trừ"),
        new("DEDUCTION", "Khấu trừ khác")
    ];

    public static bool TryShow(
        Window owner,
        ApiClient.PayslipItem payslip,
        int periodStatus,
        IReadOnlyList<ApiClient.PayrollAdjustmentItem> adjustments,
        out PayrollAdjustmentDraft? newAdjustment,
        out Guid? adjustmentToDelete)
    {
        newAdjustment = null;
        adjustmentToDelete = null;
        PayrollAdjustmentDraft? requestedAdjustment = null;
        Guid? requestedAdjustmentToDelete = null;
        var canEdit = periodStatus < 2;
        var employeeAdjustments = adjustments
            .Where(a => a.EmployeeId == payslip.EmployeeId)
            .OrderBy(a => a.CreatedAt)
            .ToList();
        var adjustmentRows = employeeAdjustments
            .Select(a => new AdjustmentDisplayItem(
                a,
                AdjustmentLabel(a.Type),
                FormatMoney(a.Amount),
                a.Reason))
            .ToList();

        var dialog = new Window
        {
            Owner = owner,
            Title = $"Phiếu lương — {payslip.EmployeeName}",
            Width = 920,
            Height = Math.Min(700, Math.Max(620, SystemParameters.WorkArea.Height - 24)),
            MinWidth = 780,
            MinHeight = 620,
            MaxHeight = Math.Max(620, SystemParameters.WorkArea.Height - 20),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = Brushes.White,
            ShowInTaskbar = false
        };

        var root = new StackPanel { Margin = new Thickness(24, 16, 24, 16) };
        root.Children.Add(new TextBlock
        {
            Text = $"Phiếu lương — {payslip.EmployeeName} ({payslip.EmployeeCode})",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#101936")
        });
        root.Children.Add(new TextBlock
        {
            Text = $"{payslip.PayrollPeriodName} · {payslip.DepartmentName ?? "Chưa có phòng ban"} · {payslip.Position ?? "Chưa có chức vụ"}",
            FontSize = 12,
            Foreground = Brush("#64748B"),
            Margin = new Thickness(0, 3, 0, 10)
        });

        var summary = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        for (var i = 0; i < 4; i++) summary.ColumnDefinitions.Add(new ColumnDefinition());
        var rateLabel = payslip.EmploymentType == 1 ? "ĐƠN GIÁ" : "LƯƠNG CƠ BẢN";
        var rateValue = payslip.EmploymentType == 1
            ? $"{payslip.PartTimeUnitRate.GetValueOrDefault():N0} đ/{(payslip.PartTimeCalculationMethod == 1 ? "ca" : "giờ")}"
            : FormatMoney(payslip.BaseSalary);
        AddSummaryCard(summary, 0, rateLabel, rateValue, "#2563EB");
        AddSummaryCard(summary, 1, "TỔNG THU NHẬP", FormatMoney(payslip.TotalIncome != 0 ? payslip.TotalIncome : payslip.GrossSalary), "#0EA5E9");
        AddSummaryCard(summary, 2, "TỔNG KHẤU TRỪ", FormatMoney(payslip.TotalDeductions != 0 ? payslip.TotalDeductions : payslip.Deductions), "#DC2626");
        AddSummaryCard(summary, 3, "THỰC LĨNH (NET)", FormatMoney(payslip.NetSalary), "#059669");
        root.Children.Add(summary);

        var detailColumns = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        detailColumns.ColumnDefinitions.Add(new ColumnDefinition());
        detailColumns.ColumnDefinitions.Add(new ColumnDefinition());

        var workAndIncome = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        AddSectionTitle(workAndIncome, "1–2. THÔNG TIN, CÔNG VÀ THU NHẬP");
        AddValue(workAndIncome, "Loại hợp đồng", payslip.EmploymentTypeNameVi);
        AddValue(workAndIncome, "Công / giờ / ca", payslip.WorkQuantityDisplay);
        if (payslip.EmploymentType == 1)
        {
            AddValue(workAndIncome, "Đơn vị tính", payslip.PartTimeCalculationMethod == 1 ? "Theo ca" : "Theo giờ");
            AddValue(workAndIncome, "Đơn giá", rateValue);
        }
        else
        {
            AddValue(workAndIncome, "Công chuẩn tháng", $"{payslip.StandardWorkDays:0.##} ngày");
        }
        AddValue(workAndIncome, "Giờ làm thực tế", $"{payslip.ActualWorkHours:0.##} giờ");
        AddValue(workAndIncome, "Lương theo công / đơn vị", FormatMoney(Math.Max(0, payslip.GrossSalary - payslip.Allowances - payslip.KpiBonus)));
        AddValue(workAndIncome, "Trợ cấp + thưởng khác", FormatMoney(payslip.Allowances), valueBrush: Brush("#059669"));
        AddValue(workAndIncome, "Thưởng KPI", FormatMoney(payslip.KpiBonus), valueBrush: Brush("#059669"));
        AddValue(workAndIncome, "Tổng Gross", FormatMoney(payslip.GrossSalary), bold: true, valueBrush: Brush("#2563EB"));
        detailColumns.ChildrenAdd(workAndIncome, 0);

        var deductionsAndHistory = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        AddSectionTitle(deductionsAndHistory, "3–4. GIẢM TRỪ, THỰC LĨNH VÀ LỊCH SỬ");
        AddValue(deductionsAndHistory, "BHYT khấu trừ", FormatMoney(payslip.HealthInsurance), valueBrush: Brush("#DC2626"));
        AddValue(deductionsAndHistory, "Tổng giảm trừ", FormatMoney(payslip.TotalDeductions != 0 ? payslip.TotalDeductions : payslip.Deductions), bold: true, valueBrush: Brush("#DC2626"));
        AddValue(deductionsAndHistory, "Thực lĩnh Net", FormatMoney(payslip.NetSalary), bold: true, valueBrush: Brush("#059669"));
        AddValue(deductionsAndHistory, "Trạng thái", payslip.StatusNameVi);

        var historyLabel = new TextBlock
        {
            Text = "CÁC KHOẢN ĐIỀU CHỈNH ĐÃ GHI",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#64748B"),
            Margin = new Thickness(0, 6, 0, 4)
        };
        deductionsAndHistory.Children.Add(historyLabel);
        var historyHeader = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        DefineAdjustmentColumns(historyHeader);
        AddHeaderText(historyHeader, "Loại khoản", 0);
        AddHeaderText(historyHeader, "Số tiền", 1);
        AddHeaderText(historyHeader, "Lý do", 2);
        deductionsAndHistory.Children.Add(historyHeader);
        var historyList = new ListBox
        {
            Height = 94,
            ItemsSource = adjustmentRows,
            BorderBrush = Brush("#E2E8F0"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4)
        };
        RoutedEventHandler deleteHistoryItem = (_, e) =>
        {
            if (e.OriginalSource is Button button && button.Tag is ApiClient.PayrollAdjustmentItem selectedAdjustment)
            {
                requestedAdjustmentToDelete = selectedAdjustment.Id;
                dialog.DialogResult = true;
            }
        };
        historyList.ItemTemplate = new DataTemplate
        {
            VisualTree = AdjustmentTemplate(deleteHistoryItem)
        };
        deductionsAndHistory.Children.Add(historyList);
        detailColumns.ChildrenAdd(deductionsAndHistory, 1);
        root.Children.Add(detailColumns);

        if (canEdit)
        {
            var editBorder = new Border
            {
                Background = Brush("#F8FAFC"),
                BorderBrush = Brush("#E2E8F0"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var editPanel = new StackPanel();
            editPanel.Children.Add(new TextBlock
            {
                Text = "ĐIỀU CHỈNH THỦ CÔNG — SẼ ĐƯỢC GHI AUDIT VÀ TÍNH LẠI",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = Brush("#475569"),
                Margin = new Thickness(0, 0, 0, 7)
            });
            var inputHeader = new Grid { Margin = new Thickness(0, 0, 0, 3) };
            DefineAdjustmentColumns(inputHeader);
            AddHeaderText(inputHeader, "Loại khoản điều chỉnh", 0);
            AddHeaderText(inputHeader, "Số tiền (VND) *", 1);
            AddHeaderText(inputHeader, "Lý do điều chỉnh *", 2);
            editPanel.Children.Add(inputHeader);
            var editRow = new Grid();
            DefineAdjustmentColumns(editRow);

            var type = new ComboBox
            {
                Height = 34,
                ItemsSource = AdjustmentOptions,
                DisplayMemberPath = nameof(AdjustmentOption.Label),
                SelectedIndex = 0,
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = "Chọn nhóm thu nhập hoặc giảm trừ"
            };
            var amount = NewTextBox(string.Empty);
            amount.Margin = new Thickness(0, 0, 6, 0);
            amount.ToolTip = "Nhập số tiền không âm (VND)";
            var reason = NewTextBox(string.Empty);
            reason.ToolTip = "Nhập lý do bắt buộc để lưu dấu vết thay đổi";
            var addButton = new Button
            {
                Content = "+ Ghi khoản",
                Height = 34,
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(12, 0, 12, 0),
                Style = Application.Current.TryFindResource("PrimaryGradientButton") as Style
            };
            Grid.SetColumn(type, 0);
            Grid.SetColumn(amount, 1);
            Grid.SetColumn(reason, 2);
            Grid.SetColumn(addButton, 3);
            editRow.Children.Add(type);
            editRow.Children.Add(amount);
            editRow.Children.Add(reason);
            editRow.Children.Add(addButton);
            editPanel.Children.Add(editRow);
            editBorder.Child = editPanel;
            root.Children.Add(editBorder);

            addButton.Click += (_, _) =>
            {
                if (type.SelectedItem is not AdjustmentOption selectedType)
                {
                    Warn("Vui lòng chọn loại khoản điều chỉnh.");
                    return;
                }
                if (!TryParseMoney(amount.Text, out var parsedAmount) || parsedAmount < 0)
                {
                    Warn("Số tiền điều chỉnh phải là số không âm.");
                    return;
                }
                if (string.IsNullOrWhiteSpace(reason.Text))
                {
                    Warn("Vui lòng nhập lý do điều chỉnh để lưu audit.");
                    return;
                }

                requestedAdjustment = new PayrollAdjustmentDraft(selectedType.Type, parsedAmount, reason.Text.Trim());
                dialog.DialogResult = true;
            };
        }
        else
        {
            root.Children.Add(new TextBlock
            {
                Text = "Kỳ lương đã gửi duyệt hoặc khóa. Phiếu lương chỉ được xem, không thể chỉnh sửa.",
                FontSize = 11,
                Foreground = Brush("#B45309"),
                Background = Brush("#FFFBEB"),
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 0, 0, 10),
                TextWrapping = TextWrapping.Wrap
            });
        }

        var close = new Button
        {
            Content = "Đóng",
            Width = 90,
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Right,
            Style = Application.Current.TryFindResource("OutlineButton") as Style
        };
        close.Click += (_, _) => dialog.DialogResult = false;
        root.Children.Add(close);

        dialog.Content = root;
        var accepted = dialog.ShowDialog() == true && (requestedAdjustment is not null || requestedAdjustmentToDelete.HasValue);
        newAdjustment = requestedAdjustment;
        adjustmentToDelete = requestedAdjustmentToDelete;
        return accepted;

        void Warn(string message) => MessageBox.Show(dialog, message, "Thông tin chưa hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static FrameworkElementFactory AdjustmentTemplate(RoutedEventHandler deleteHandler)
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        panel.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 2));

        var type = new FrameworkElementFactory(typeof(TextBlock));
        type.SetValue(TextBlock.WidthProperty, 104d);
        type.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        type.SetValue(TextBlock.ForegroundProperty, Brush("#334155"));
        type.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        type.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(AdjustmentDisplayItem.TypeLabel)));
        type.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 0, 4, 0));
        panel.AppendChild(type);

        var amount = new FrameworkElementFactory(typeof(TextBlock));
        amount.SetValue(TextBlock.WidthProperty, 82d);
        amount.SetValue(TextBlock.ForegroundProperty, Brush("#2563EB"));
        amount.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(AdjustmentDisplayItem.AmountLabel)));
        amount.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 0, 4, 0));
        amount.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        panel.AppendChild(amount);

        var reason = new FrameworkElementFactory(typeof(TextBlock));
        reason.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        reason.SetValue(TextBlock.WidthProperty, 180d);
        reason.SetValue(TextBlock.ForegroundProperty, Brush("#64748B"));
        reason.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(AdjustmentDisplayItem.Reason)));
        reason.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 0, 4, 0));
        panel.AppendChild(reason);

        var delete = new FrameworkElementFactory(typeof(Button));
        delete.SetValue(Button.ContentProperty, "Xóa");
        delete.SetValue(FrameworkElement.WidthProperty, 44d);
        delete.SetValue(FrameworkElement.HeightProperty, 24d);
        delete.SetValue(Control.FontSizeProperty, 10d);
        delete.SetValue(Control.PaddingProperty, new Thickness(4, 0, 4, 0));
        delete.SetValue(Control.ToolTipProperty, "Xóa khoản điều chỉnh này và tính lại phiếu lương");
        delete.SetBinding(FrameworkElement.TagProperty, new System.Windows.Data.Binding(nameof(AdjustmentDisplayItem.Item)));
        delete.AddHandler(Button.ClickEvent, deleteHandler);
        panel.AppendChild(delete);
        return panel;
    }

    private static void DefineAdjustmentColumns(Grid grid)
    {
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    }

    private static void AddHeaderText(Grid parent, string text, int column)
    {
        var header = new TextBlock
        {
            Text = text,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#64748B"),
            Margin = new Thickness(2, 0, 4, 0)
        };
        Grid.SetColumn(header, column);
        parent.Children.Add(header);
    }

    private static void AddSummaryCard(Grid parent, int column, string label, string value, string color)
    {
        var border = new Border
        {
            Background = Brush("#F8FAFC"),
            BorderBrush = Brush("#E2E8F0"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(column == 0 ? 0 : 5, 0, column == 3 ? 0 : 5, 0)
        };
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = label, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brush("#64748B") });
        content.Children.Add(new TextBlock { Text = value, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brush(color), Margin = new Thickness(0, 4, 0, 0) });
        border.Child = content;
        Grid.SetColumn(border, column);
        parent.Children.Add(border);
    }

    private static void AddSectionTitle(Panel parent, string text) => parent.Children.Add(new TextBlock
    {
        Text = text,
        FontSize = 11,
        FontWeight = FontWeights.Bold,
        Foreground = Brush("#475569"),
        Margin = new Thickness(0, 0, 0, 5)
    });

    private static void AddValue(Panel parent, string label, string value, bool bold = false, Brush? valueBrush = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = Brush("#64748B") });
        var valueText = new TextBlock
        {
            Text = value,
            FontSize = 11,
            FontWeight = bold ? FontWeights.Bold : FontWeights.SemiBold,
            Foreground = valueBrush ?? Brush("#1E293B"),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Right
        };
        Grid.SetColumn(valueText, 1);
        grid.Children.Add(valueText);
        parent.Children.Add(grid);
    }

    private static TextBox NewTextBox(string? value) => new()
    {
        Text = value ?? string.Empty,
        Height = 34,
        Padding = new Thickness(9, 5, 9, 5),
        Style = Application.Current.TryFindResource("ModernTextBox") as Style
    };

    private static string FormatMoney(decimal value) => $"{value:N0} đ";
    private static SolidColorBrush Brush(string value) => (SolidColorBrush)new BrushConverter().ConvertFromString(value)!;

    private static string AdjustmentLabel(string type) => type.Trim().ToUpperInvariant() switch
    {
        "ALLOWANCE" or "TRO_CAP" => "Trợ cấp",
        "KPI" or "KPI_BONUS" or "THUONG_KPI" => "Thưởng KPI",
        "BONUS" or "THUONG" => "Thưởng khác",
        "OVERTIME" or "OT" or "LAM_THEM_GIO" => "Tiền làm thêm giờ",
        "HEALTH_INSURANCE" or "BHYT" => "BHYT khấu trừ",
        "DEDUCTION" or "KHAU_TRU" => "Khấu trừ khác",
        _ => type
    };

    private static bool TryParseMoney(string value, out decimal result)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.GetCultureInfo("vi-VN"), out result)
           || decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out result)
           || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);

    private static void ChildrenAdd(this Grid grid, UIElement child, int column)
    {
        Grid.SetColumn(child, column);
        grid.Children.Add(child);
    }
}
