using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace InternalManagement.Desktop.Services;

public sealed record CscaStudentDialogResult(
    string StudentName,
    int? Age,
    string? Hometown,
    string? Email,
    string? PhoneNumber,
    decimal PaidAmount,
    int PaymentStatus,
    DateTime? DebtDueDate,
    string? Notes);

public static class CscaStudentDialog
{
    public static bool TryShow(
        Window owner,
        string classCode,
        string className,
        decimal classTuitionFee,
        out CscaStudentDialogResult? result,
        ApiClient.CscaStudentItem? student = null)
    {
        result = null;
        CscaStudentDialogResult? submitted = null;
        var isEditing = student != null;

        var dialog = new Window
        {
            Owner = owner,
            Title = isEditing ? $"Sửa thông tin học viên — {student?.StudentName}" : $"Thêm học viên vào lớp {classCode}",
            Width = 660,
            Height = 690,
            MinWidth = 600,
            MinHeight = 620,
            MaxHeight = 760,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            ShowInTaskbar = false
        };

        var rootGrid = new Grid { Margin = new Thickness(22, 18, 22, 18) };
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Form content
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer buttons

        // ── 1. HEADER & CLASS TUITION SUMMARY ──
        var headerBorder = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 0, 0, 14)
        };
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titlePanel = new StackPanel();
        titlePanel.Children.Add(new TextBlock
        {
            Text = isEditing ? $"Sửa thông tin: {student?.StudentName}" : "Thêm học viên mới",
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42))
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = $"Lớp: {classCode} — {className}",
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            Margin = new Thickness(0, 2, 0, 0)
        });
        Grid.SetColumn(titlePanel, 0);
        headerGrid.Children.Add(titlePanel);

        var tuitionBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(238, 242, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(199, 210, 254)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 6, 12, 6),
            VerticalAlignment = VerticalAlignment.Center
        };
        var tuitionPanel = new StackPanel { Orientation = Orientation.Horizontal };
        tuitionPanel.Children.Add(new TextBlock
        {
            Text = "Học phí lớp: ",
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(67, 56, 202))
        });
        tuitionPanel.Children.Add(new TextBlock
        {
            Text = $"{classTuitionFee:N0} đ",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(49, 46, 129))
        });
        tuitionBadge.Child = tuitionPanel;
        Grid.SetColumn(tuitionBadge, 1);
        headerGrid.Children.Add(tuitionBadge);
        headerBorder.Child = headerGrid;

        Grid.SetRow(headerBorder, 0);
        rootGrid.Children.Add(headerBorder);

        // ── 2. FORM BODY (SCROLLABLE) ──
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 12)
        };
        var formPanel = new StackPanel();

        // Row 1: Họ tên + Tuổi
        var row1 = CreateTwoColumnGrid();
        var nameBox = CreateTextBox(student?.StudentName);
        var ageBox = CreateTextBox(student?.Age?.ToString(CultureInfo.InvariantCulture));
        AddFormField(row1.Left, "Họ và tên *", nameBox);
        AddFormField(row1.Right, "Tuổi", ageBox);
        formPanel.Children.Add(row1.Grid);

        // Row 2: Số điện thoại + Email
        var row2 = CreateTwoColumnGrid();
        var phoneBox = CreateTextBox(student?.PhoneNumber);
        var emailBox = CreateTextBox(student?.Email);
        AddFormField(row2.Left, "Số điện thoại", phoneBox);
        AddFormField(row2.Right, "Email", emailBox);
        formPanel.Children.Add(row2.Grid);

        // Row 3: Quê quán
        var hometownBox = CreateTextBox(student?.Hometown);
        var hometownPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        AddFormField(hometownPanel, "Quê quán", hometownBox);
        formPanel.Children.Add(hometownPanel);

        // ── TUITION & DEBT CALCULATION BOX ──
        var financeCard = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 4, 0, 12)
        };
        var financeContent = new StackPanel();

        var financeTitle = new TextBlock
        {
            Text = "THANH TOÁN HỌC PHÍ & TỰ ĐỘNG TÍNH CÔNG NỢ",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            Margin = new Thickness(0, 0, 0, 10)
        };
        financeContent.Children.Add(financeTitle);

        // Row Payment Status & Paid Amount
        var payRow = CreateTwoColumnGrid();
        var statusCombo = new ComboBox
        {
            Height = 36,
            Padding = new Thickness(8, 6, 8, 6),
            ItemsSource = new[]
            {
                new KeyValuePair<int, string>(0, "Chưa đóng / Pending"),
                new KeyValuePair<int, string>(1, "Đóng một phần / Partial"),
                new KeyValuePair<int, string>(2, "Đã đóng đủ / Paid"),
                new KeyValuePair<int, string>(3, "Thất bại / Failed"),
                new KeyValuePair<int, string>(4, "Hoàn tiền / Refunded"),
                new KeyValuePair<int, string>(5, "Đã hủy / Cancelled")
            },
            DisplayMemberPath = "Value",
            SelectedValuePath = "Key",
            SelectedValue = student?.PaymentStatus ?? 0,
            Margin = new Thickness(0, 0, 0, 6)
        };
        var initialPaid = student?.PaidAmount ?? 0;
        var paidAmountBox = CreateTextBox(initialPaid.ToString("0", CultureInfo.InvariantCulture));
        paidAmountBox.ToolTip = "Nhập số tiền học sinh đã thanh toán";

        AddFormField(payRow.Left, "Trạng thái học phí *", statusCombo);
        AddFormField(payRow.Right, "Số tiền đã đóng (VNĐ) *", paidAmountBox);
        financeContent.Children.Add(payRow.Grid);

        // Quick Preset Buttons
        var presetPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 2, 0, 10)
        };
        var btnFull = CreatePresetButton("Đóng đủ 100%", () =>
        {
            paidAmountBox.Text = classTuitionFee.ToString("0", CultureInfo.InvariantCulture);
            statusCombo.SelectedValue = 2;
        });
        var btnHalf = CreatePresetButton("Đóng 50%", () =>
        {
            paidAmountBox.Text = Math.Round(classTuitionFee / 2).ToString("0", CultureInfo.InvariantCulture);
            statusCombo.SelectedValue = 1;
        });
        var btnZero = CreatePresetButton("Chưa đóng (0đ)", () =>
        {
            paidAmountBox.Text = "0";
            statusCombo.SelectedValue = 0;
        });
        presetPanel.Children.Add(btnFull);
        presetPanel.Children.Add(btnHalf);
        presetPanel.Children.Add(btnZero);
        financeContent.Children.Add(presetPanel);

        // Live Debt Calculation Display Banner
        var debtBanner = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 10),
            Background = new SolidColorBrush(Color.FromRgb(254, 243, 199)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(253, 230, 138)),
            BorderThickness = new Thickness(1)
        };
        var debtGrid = new Grid();
        debtGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        debtGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var debtLabelPanel = new StackPanel();
        debtLabelPanel.Children.Add(new TextBlock
        {
            Text = "SỐ TIỀN CÒN LẠI PHẢI TRẢ (HỆ THỐNG TỰ TÍNH):",
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(146, 64, 14))
        });
        var debtCalcFormulaText = new TextBlock
        {
            Text = $"Học phí {classTuitionFee:N0} đ - Đã đóng {initialPaid:N0} đ",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9)),
            Margin = new Thickness(0, 2, 0, 0)
        };
        debtLabelPanel.Children.Add(debtCalcFormulaText);
        Grid.SetColumn(debtLabelPanel, 0);
        debtGrid.Children.Add(debtLabelPanel);

        var debtValueText = new TextBlock
        {
            Text = $"{Math.Max(0, classTuitionFee - initialPaid):N0} đ",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(debtValueText, 1);
        debtGrid.Children.Add(debtValueText);
        debtBanner.Child = debtGrid;
        financeContent.Children.Add(debtBanner);

        // Row Debt Due Date
        var debtDueDateBox = CreateTextBox(student?.DebtDueDate?.ToString("yyyy-MM-dd"));
        debtDueDateBox.ToolTip = "Định dạng: yyyy-MM-dd (ví dụ: 2026-09-30)";
        var dueDatePanel = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        AddFormField(dueDatePanel, "Ngày hẹn trả nợ (yyyy-MM-dd nếu nợ)", debtDueDateBox);
        financeContent.Children.Add(dueDatePanel);

        financeCard.Child = financeContent;
        formPanel.Children.Add(financeCard);

        // Notes Box
        var notesBox = CreateTextBox(student?.Notes);
        var notesPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        AddFormField(notesPanel, "Ghi chú thêm", notesBox);
        formPanel.Children.Add(notesPanel);

        scrollViewer.Content = formPanel;
        Grid.SetRow(scrollViewer, 1);
        rootGrid.Children.Add(scrollViewer);

        // ── 3. LIVE INTERACTION & AUTO-CALCULATION LOGIC ──
        var isUpdatingInternally = false;

        void UpdateDebtCalculation()
        {
            var rawPaid = paidAmountBox.Text.Trim().Replace(" ", "").Replace(",", "").Replace(".", "");
            _ = decimal.TryParse(rawPaid, NumberStyles.Number, CultureInfo.InvariantCulture, out var paid);
            if (paid < 0) paid = 0;

            var debt = Math.Max(0, classTuitionFee - paid);
            debtCalcFormulaText.Text = $"Học phí {classTuitionFee:N0} đ - Đã đóng {paid:N0} đ";
            debtValueText.Text = $"{debt:N0} đ";

            if (debt <= 0)
            {
                debtBanner.Background = new SolidColorBrush(Color.FromRgb(236, 253, 245));
                debtBanner.BorderBrush = new SolidColorBrush(Color.FromRgb(167, 243, 208));
                debtValueText.Foreground = new SolidColorBrush(Color.FromRgb(5, 150, 105));
            }
            else
            {
                debtBanner.Background = new SolidColorBrush(Color.FromRgb(254, 243, 199));
                debtBanner.BorderBrush = new SolidColorBrush(Color.FromRgb(253, 230, 138));
                debtValueText.Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9));
            }

            if (!isUpdatingInternally)
            {
                isUpdatingInternally = true;
                try
                {
                    if (paid >= classTuitionFee)
                    {
                        statusCombo.SelectedValue = 2; // Paid
                    }
                    else if (paid == 0)
                    {
                        statusCombo.SelectedValue = 0; // Pending
                    }
                    else
                    {
                        statusCombo.SelectedValue = 1; // Partial
                    }
                }
                finally
                {
                    isUpdatingInternally = false;
                }
            }
        }

        paidAmountBox.TextChanged += (_, _) => UpdateDebtCalculation();

        statusCombo.SelectionChanged += (_, _) =>
        {
            if (isUpdatingInternally) return;
            if (statusCombo.SelectedValue is int statusVal)
            {
                isUpdatingInternally = true;
                try
                {
                    if (statusVal == 2) // Paid
                    {
                        paidAmountBox.Text = classTuitionFee.ToString("0", CultureInfo.InvariantCulture);
                    }
                    else if (statusVal == 0) // Pending
                    {
                        paidAmountBox.Text = "0";
                    }
                    else if (statusVal == 1) // Partial
                    {
                        var rawPaid = paidAmountBox.Text.Trim().Replace(" ", "").Replace(",", "").Replace(".", "");
                        if (!decimal.TryParse(rawPaid, NumberStyles.Number, CultureInfo.InvariantCulture, out var currentPaid) || currentPaid <= 0 || currentPaid >= classTuitionFee)
                        {
                            paidAmountBox.Text = Math.Round(classTuitionFee / 2).ToString("0", CultureInfo.InvariantCulture);
                        }
                    }
                }
                finally
                {
                    isUpdatingInternally = false;
                }
                UpdateDebtCalculation();
            }
        };

        UpdateDebtCalculation();

        // ── 4. FOOTER BUTTONS ──
        var buttonsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var cancelButton = new Button
        {
            Content = "Hủy bỏ",
            Width = 95,
            Height = 36,
            Margin = new Thickness(0, 0, 10, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            BorderThickness = new Thickness(1),
            Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            FontWeight = FontWeights.Medium
        };
        cancelButton.Click += (_, _) => dialog.DialogResult = false;

        var saveButton = new Button
        {
            Content = isEditing ? "Lưu thay đổi" : "Thêm học viên",
            Width = 140,
            Height = 36,
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = new SolidColorBrush(Color.FromRgb(79, 70, 229)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(79, 70, 229)),
            BorderThickness = new Thickness(1),
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold
        };
        saveButton.Click += (_, _) =>
        {
            var studentName = nameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(studentName))
            {
                MessageBox.Show(dialog, "Vui lòng nhập họ và tên học viên.", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
                nameBox.Focus();
                return;
            }

            int? age = null;
            if (!string.IsNullOrWhiteSpace(ageBox.Text))
            {
                if (!int.TryParse(ageBox.Text.Trim(), out var parsedAge) || parsedAge < 1 || parsedAge > 120)
                {
                    MessageBox.Show(dialog, "Tuổi học viên phải là số từ 1 đến 120.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
                    ageBox.Focus();
                    return;
                }
                age = parsedAge;
            }

            var rawPaid = paidAmountBox.Text.Trim().Replace(" ", "").Replace(",", "").Replace(".", "");
            if (!decimal.TryParse(rawPaid, NumberStyles.Number, CultureInfo.InvariantCulture, out var paidAmount) || paidAmount < 0)
            {
                MessageBox.Show(dialog, "Số tiền đã đóng phải là số không âm.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
                paidAmountBox.Focus();
                return;
            }

            var paymentStatus = statusCombo.SelectedValue is int sVal ? sVal : 0;

            DateTime? debtDueDate = null;
            if (!string.IsNullOrWhiteSpace(debtDueDateBox.Text))
            {
                if (!DateTime.TryParseExact(debtDueDateBox.Text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                {
                    MessageBox.Show(dialog, "Ngày hẹn trả nợ phải đúng định dạng yyyy-MM-dd (ví dụ: 2026-09-30).", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
                    debtDueDateBox.Focus();
                    return;
                }
                debtDueDate = parsedDate;
            }

            submitted = new CscaStudentDialogResult(
                studentName,
                age,
                NullIfEmpty(hometownBox.Text),
                NullIfEmpty(emailBox.Text),
                NullIfEmpty(phoneBox.Text),
                paidAmount,
                paymentStatus,
                debtDueDate,
                NullIfEmpty(notesBox.Text));

            dialog.DialogResult = true;
        };

        buttonsPanel.Children.Add(cancelButton);
        buttonsPanel.Children.Add(saveButton);
        Grid.SetRow(buttonsPanel, 2);
        rootGrid.Children.Add(buttonsPanel);

        dialog.Content = rootGrid;
        var accepted = dialog.ShowDialog() == true;
        result = submitted;
        return accepted;
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
            Cursor = System.Windows.Input.Cursors.Hand,
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
