using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace InternalManagement.Desktop.Services;

public sealed record EmployeeDialogOption(Guid? Id, string Label);

public sealed record EmployeeDialogResult(
    string EmployeeCode,
    string FullName,
    string Email,
    string? Phone,
    string? Position,
    decimal BaseSalary,
    Guid? DepartmentId,
    Guid? BusinessUnitId,
    DateTime? JoinedDate,
    string Status,
    int EmploymentType,
    int? PartTimeCalculationMethod,
    decimal? PartTimeUnitRate,
    string? CvUrlOrPath,
    string? ProfessionalSummary,
    string? Skills,
    string? Experience);

public static class EmployeeDialog
{
    public static bool TryShow(
        Window owner,
        string segmentName,
        IReadOnlyList<EmployeeDialogOption> businessUnits,
        IReadOnlyList<EmployeeDialogOption> departments,
        out EmployeeDialogResult? result,
        ApiClient.EmployeeItem? employee = null)
    {
        result = null;
        EmployeeDialogResult? submitted = null;
        var editing = employee is not null;
        var dialog = new Window
        {
            Owner = owner,
            Title = editing ? "Sửa hồ sơ nhân sự" : "Thêm nhân sự",
            Width = 920,
            Height = Math.Min(700, Math.Max(620, SystemParameters.WorkArea.Height - 24)),
            MinWidth = 760,
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
            Text = editing ? "Sửa hồ sơ nhân sự" : "Tạo hồ sơ nhân sự mới",
            FontSize = 21,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#101936")
        });
        root.Children.Add(new TextBlock
        {
            Text = $"Mảng: {segmentName}. Hồ sơ sẽ chỉ xuất hiện trong đúng mảng này.",
            FontSize = 12,
            Foreground = Brush("#64748B"),
            Margin = new Thickness(0, 3, 0, 10)
        });

        var identity = TwoColumns(root);
        var code = TextField(identity.Left, "Mã nhân viên *", employee?.EmployeeCode, editing);
        var name = TextField(identity.Right, "Họ và tên *", employee?.FullName);

        var contact = TwoColumns(root);
        var email = TextField(contact.Left, "Email *", employee?.Email);
        var phone = TextField(contact.Right, "Số điện thoại", employee?.Phone);

        var job = TwoColumns(root);
        var position = TextField(job.Left, "Vị trí / chức vụ", employee?.Position);
        var joined = DateField(job.Right, "Ngày vào làm", employee?.JoinedDate ?? DateTime.Today);

        var assignment = TwoColumns(root);
        var bu = ComboField(assignment.Left, "Đơn vị kinh doanh *", businessUnits, employee?.BusinessUnitId);
        var dept = ComboField(assignment.Right, "Phòng ban", departments, employee?.DepartmentId);

        var compensation = TwoColumns(root);
        Label(compensation.Left, "Hình thức trả công *");
        var employmentType = new ComboBox
        {
            Height = 34,
            Margin = new Thickness(0, 0, 8, 8),
            ItemsSource = new[]
            {
                new KeyValuePair<int, string>(0, "Lương tháng — nhân sự chính thức"),
                new KeyValuePair<int, string>(1, "Tính theo thời gian thực tế — GV/CTV")
            },
            DisplayMemberPath = "Value",
            SelectedValuePath = "Key",
            SelectedValue = employee?.EmploymentType ?? 0
        };
        compensation.Left.Children.Add(employmentType);

        var salaryPanel = new StackPanel();
        var fullTimePanel = new StackPanel();
        var baseSalary = TextField(fullTimePanel, "Lương cơ bản theo tháng *", employee?.BaseSalary.ToString(CultureInfo.InvariantCulture) ?? "0");
        salaryPanel.Children.Add(fullTimePanel);

        var partTimePanel = new StackPanel { Visibility = Visibility.Collapsed };
        var partRow = TwoColumns(partTimePanel);
        var method = ComboField(partRow.Left, "Đơn vị tính *", new[]
        {
            new EmployeeDialogOption(Guid.Empty, "Theo giờ"),
            new EmployeeDialogOption(Guid.Parse("00000000-0000-0000-0000-000000000001"), "Theo ca")
        }, employee?.PartTimeCalculationMethod == 1
            ? Guid.Parse("00000000-0000-0000-0000-000000000001")
            : Guid.Empty);
        var unitRate = TextField(partRow.Right, "Đơn giá (đồng/giờ hoặc ca) *", employee?.PartTimeUnitRate?.ToString(CultureInfo.InvariantCulture) ?? "0");
        partTimePanel.Children.Add(new TextBlock
        {
            Text = "Tiền công = tổng giờ/ca có mặt trong kỳ × đơn giá đã khai báo.",
            FontSize = 10.5,
            Foreground = Brush("#64748B"),
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        });
        salaryPanel.Children.Add(partTimePanel);
        compensation.Right.Children.Add(salaryPanel);

        Label(root, "CV (đường dẫn tệp hoặc liên kết)");
        var cvGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        cvGrid.ColumnDefinitions.Add(new ColumnDefinition());
        cvGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var cv = NewTextBox(employee?.CvUrlOrPath);
        var browse = new Button
        {
            Content = "Chọn tệp CV",
            Height = 34,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(12, 0, 12, 0),
            Style = Application.Current.TryFindResource("OutlineButton") as Style
        };
        browse.Click += (_, _) =>
        {
            var picker = new OpenFileDialog
            {
                Title = "Chọn CV nhân sự",
                Filter = "CV (*.pdf;*.doc;*.docx)|*.pdf;*.doc;*.docx|Tất cả tệp (*.*)|*.*"
            };
            if (picker.ShowDialog(dialog) == true) cv.Text = picker.FileName;
        };
        Grid.SetColumn(browse, 1);
        cvGrid.Children.Add(cv);
        cvGrid.Children.Add(browse);
        root.Children.Add(cvGrid);

        var notes = ThreeColumns(root);
        var summary = TextArea(notes.Left, "Tóm tắt chuyên môn", employee?.ProfessionalSummary);
        var skills = TextArea(notes.Center, "Kỹ năng", employee?.Skills);
        var experience = TextArea(notes.Right, "Kinh nghiệm", employee?.Experience);

        employmentType.SelectionChanged += (_, _) =>
        {
            var isPartTime = (int?)employmentType.SelectedValue == 1;
            fullTimePanel.Visibility = isPartTime ? Visibility.Collapsed : Visibility.Visible;
            partTimePanel.Visibility = isPartTime ? Visibility.Visible : Visibility.Collapsed;
        };
        var initiallyPartTime = (int?)employmentType.SelectedValue == 1;
        fullTimePanel.Visibility = initiallyPartTime ? Visibility.Collapsed : Visibility.Visible;
        partTimePanel.Visibility = initiallyPartTime ? Visibility.Visible : Visibility.Collapsed;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 2, 0, 0)
        };
        var cancel = new Button
        {
            Content = "Hủy",
            Width = 90,
            Height = 34,
            Margin = new Thickness(0, 0, 8, 0),
            Style = Application.Current.TryFindResource("OutlineButton") as Style
        };
        var save = new Button
        {
            Content = editing ? "Lưu thay đổi" : "Tạo nhân sự",
            Width = 130,
            Height = 34,
            Style = Application.Current.TryFindResource("PrimaryGradientButton") as Style
        };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(code.Text) || string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(email.Text))
            {
                Warn("Vui lòng nhập mã nhân viên, họ tên và email.");
                return;
            }

            var isPart = (int?)employmentType.SelectedValue == 1;
            if (!TryParseMoney(isPart ? unitRate.Text : baseSalary.Text, out var payValue) || payValue < 0)
            {
                Warn(isPart ? "Đơn giá theo giờ/ca chưa hợp lệ." : "Lương cơ bản chưa hợp lệ.");
                return;
            }

            if (bu.SelectedItem is not EmployeeDialogOption selectedBu || !selectedBu.Id.HasValue)
            {
                Warn("Vui lòng chọn đơn vị kinh doanh thuộc mảng hiện tại.");
                return;
            }

            var selectedMethod = method.SelectedItem as EmployeeDialogOption;
            var partMethod = isPart && selectedMethod?.Id == Guid.Parse("00000000-0000-0000-0000-000000000001")
                ? 1
                : isPart ? 0 : (int?)null;
            submitted = new EmployeeDialogResult(
                code.Text.Trim(), name.Text.Trim(), email.Text.Trim(), Null(phone.Text), Null(position.Text),
                isPart ? 0 : payValue,
                (dept.SelectedItem as EmployeeDialogOption)?.Id,
                selectedBu.Id,
                joined.SelectedDate,
                employee?.Status ?? "Active",
                isPart ? 1 : 0,
                partMethod,
                isPart ? payValue : null,
                Null(cv.Text), Null(summary.Text), Null(skills.Text), Null(experience.Text));
            dialog.DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        root.Children.Add(buttons);

        dialog.Content = root;
        var accepted = dialog.ShowDialog() == true && submitted is not null;
        result = submitted;
        return accepted;

        void Warn(string message) => MessageBox.Show(dialog, message, "Thông tin chưa hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static (StackPanel Left, StackPanel Right) TwoColumns(Panel parent)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        var right = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        parent.Children.Add(grid);
        return (left, right);
    }

    private static (StackPanel Left, StackPanel Center, StackPanel Right) ThreeColumns(Panel parent)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
        var center = new StackPanel { Margin = new Thickness(3, 0, 3, 0) };
        var right = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
        Grid.SetColumn(center, 1);
        Grid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(center);
        grid.Children.Add(right);
        parent.Children.Add(grid);
        return (left, center, right);
    }

    private static TextBox TextField(Panel parent, string label, string? value, bool readOnly = false)
    {
        Label(parent, label);
        var box = NewTextBox(value);
        box.IsReadOnly = readOnly;
        box.Margin = new Thickness(0, 0, 0, 8);
        parent.Children.Add(box);
        return box;
    }

    private static TextBox TextArea(Panel parent, string label, string? value)
    {
        Label(parent, label);
        var box = NewTextBox(value);
        box.Height = 104;
        box.AcceptsReturn = true;
        box.TextWrapping = TextWrapping.Wrap;
        box.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        box.VerticalContentAlignment = VerticalAlignment.Top;
        box.Padding = new Thickness(10, 8, 10, 8);
        box.Margin = new Thickness(0, 0, 0, 8);
        parent.Children.Add(box);
        return box;
    }

    private static DatePicker DateField(Panel parent, string label, DateTime? value)
    {
        Label(parent, label);
        var picker = new DatePicker { Height = 34, SelectedDate = value, Margin = new Thickness(0, 0, 0, 8) };
        parent.Children.Add(picker);
        return picker;
    }

    private static ComboBox ComboField(Panel parent, string label, IReadOnlyList<EmployeeDialogOption> options, Guid? selectedId)
    {
        Label(parent, label);
        var combo = new ComboBox
        {
            Height = 34,
            ItemsSource = options,
            DisplayMemberPath = nameof(EmployeeDialogOption.Label),
            Margin = new Thickness(0, 0, 0, 8)
        };
        combo.SelectedItem = options.FirstOrDefault(x => x.Id == selectedId) ?? (options.Count > 0 ? options[0] : null);
        parent.Children.Add(combo);
        return combo;
    }

    private static void Label(Panel parent, string text) => parent.Children.Add(new TextBlock
    {
        Text = text,
        FontSize = 12,
        FontWeight = FontWeights.SemiBold,
        Foreground = Brush("#34405C"),
        Margin = new Thickness(0, 0, 0, 4)
    });

    private static TextBox NewTextBox(string? value) => new()
    {
        Text = value ?? string.Empty,
        Height = 34,
        Padding = new Thickness(10, 5, 10, 5),
        Style = Application.Current.TryFindResource("ModernTextBox") as Style
    };

    private static SolidColorBrush Brush(string value) => (SolidColorBrush)new BrushConverter().ConvertFromString(value)!;
    private static string? Null(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool TryParseMoney(string value, out decimal result)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.GetCultureInfo("vi-VN"), out result)
           || decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out result)
           || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
}
