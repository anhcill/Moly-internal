using System.IO;
using System.Net.Http;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop;

public partial class MainWindow
{
    // ── Dữ liệu nội bộ tách mảng & bảng tổng tài chính ──

    private async Task LoadCompanyFinanceAsync()
    {
        var from = ViewCompanyFinanceContainer.FromDate;
        var to = ViewCompanyFinanceContainer.ToDateInclusive;
        if (from.HasValue && to.HasValue && from > to)
        {
            ShowToast("Ngày bắt đầu phải trước hoặc bằng ngày kết thúc.", isError: true);
            return;
        }

        var overview = await _apiClient.GetCompanyFinancialOverviewAsync(from, to);
        if (overview is null)
        {
            ViewCompanyFinanceContainer.SetOverview(null);
            SetViewStatus("Không tải được bảng tổng tài chính. Vui lòng kiểm tra quyền báo cáo.", isError: true);
            return;
        }

        ViewCompanyFinanceContainer.SetOverview(overview);
        SetViewStatus($"Đã tổng hợp tài chính từ {overview.From:dd/MM/yyyy} đến {overview.To:dd/MM/yyyy}");
    }

    private void RefreshCompanyFinance_Click(object sender, RoutedEventArgs e)
        => _ = RunWithBusyAsync("Đang tổng hợp tài chính hai mảng...", LoadCompanyFinanceAsync);

    private async Task LoadInternalDataAsync()
    {
        var search = ViewInternalDataContainer.SearchText;
        if (_internalDataMode == InternalDataViewMode.Customers)
        {
            var data = await _apiClient.GetInternalCustomersAsync(_activeSegment, search);
            if (data is null)
            {
                ViewInternalDataContainer.SetCustomers(null);
                SetViewStatus("Không tải được danh mục khách hàng.", isError: true);
                return;
            }

            ViewInternalDataContainer.SetCustomers(data.Items);
            SetLoadedStatus($"Khách hàng {ActiveSegmentName}", data.Items.Count);
            return;
        }

        var resources = await _apiClient.GetInternalResourcesAsync(_activeSegment, search);
        if (resources is null)
        {
            ViewInternalDataContainer.SetResources(null);
            SetViewStatus("Không tải được kho thông tin nội bộ.", isError: true);
            return;
        }

        ViewInternalDataContainer.SetResources(resources.Items);
        SetLoadedStatus(_activeSegment == FashionSegment ? "Kế hoạch và mẫu thiết kế" : "Đề và tài liệu", resources.Items.Count);
    }

    private void InternalDataSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUiReady && ViewInternalDataContainer.Visibility == Visibility.Visible) _ = LoadInternalDataAsync();
    }

    private void RefreshInternalData_Click(object sender, RoutedEventArgs e)
        => _ = RunWithBusyAsync("Đang tải dữ liệu nội bộ...", LoadInternalDataAsync);

    private async void CreateInternalData_Click(object sender, RoutedEventArgs e)
    {
        if (_internalDataMode == InternalDataViewMode.Customers)
            await ShowCustomerDialogAsync();
        else
            await ShowResourceDialogAsync();
    }

    private async void EditInternalData_Click(object sender, RoutedEventArgs e)
    {
        if (_internalDataMode == InternalDataViewMode.Customers)
        {
            if (ViewInternalDataContainer.SelectedCustomer is not { } item)
            {
                ShowToast("Hãy chọn khách hàng cần sửa.");
                return;
            }
            await ShowCustomerDialogAsync(item);
        }
        else
        {
            if (ViewInternalDataContainer.SelectedResource is not { } item)
            {
                ShowToast("Hãy chọn tài liệu cần sửa.");
                return;
            }
            await ShowResourceDialogAsync(item);
        }
    }

    private async void DeleteInternalData_Click(object sender, RoutedEventArgs e)
    {
        if (_internalDataMode == InternalDataViewMode.Customers)
        {
            if (ViewInternalDataContainer.SelectedCustomer is not { } item)
            {
                ShowToast("Hãy chọn khách hàng cần xóa.");
                return;
            }
            if (MessageBox.Show(this, $"Xóa khách hàng “{item.Name}” khỏi mảng {ActiveSegmentName}?", "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            if (!await _apiClient.DeleteInternalCustomerAsync(_activeSegment, item.Id))
            {
                ShowToast("Xóa khách hàng thất bại. Vui lòng kiểm tra quyền quản lý.", true);
                return;
            }
        }
        else
        {
            if (ViewInternalDataContainer.SelectedResource is not { } item)
            {
                ShowToast("Hãy chọn tài liệu cần xóa.");
                return;
            }
            if (MessageBox.Show(this, $"Xóa metadata “{item.Title}”? Tệp bên ngoài cơ sở dữ liệu sẽ không bị xóa.", "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            if (!await _apiClient.DeleteInternalResourceAsync(_activeSegment, item.Id))
            {
                ShowToast("Xóa thông tin tài liệu thất bại. Vui lòng kiểm tra quyền quản lý.", true);
                return;
            }
        }

        ShowToast("Đã xóa dữ liệu thành công.");
        await LoadInternalDataAsync();
    }

    private async Task ShowCustomerDialogAsync(ApiClient.InternalCustomerItem? item = null)
    {
        var fields = new[]
        {
            new PromptField("code", "Mã khách hàng", item?.Code),
            new PromptField("name", "Tên khách hàng", item?.Name),
            new PromptField("contact", "Người liên hệ", item?.ContactPerson, false),
            new PromptField("phone", "Số điện thoại", item?.Phone, false),
            new PromptField("email", "Email", item?.Email, false),
            new PromptField("address", "Địa chỉ", item?.Address, false),
            new PromptField("source", "Nguồn khách hàng", item?.Source, false),
            new PromptField("status", "Trạng thái", item?.Status ?? "ACTIVE", true, new[] { new PromptOption("LEAD", "Tiềm năng"), new PromptOption("ACTIVE", "Đang hoạt động"), new PromptOption("INACTIVE", "Ngừng hoạt động"), new PromptOption("ARCHIVED", "Đã lưu trữ") }),
            new PromptField("notes", "Ghi chú", item?.Notes, false)
        };
        if (!PromptDialog.TryShow(this, item is null ? $"Thêm khách hàng · {ActiveSegmentName}" : "Sửa khách hàng", fields, out var values)) return;
        var model = new ApiClient.InternalCustomerModel(values["code"], values["name"], Null(values["contact"]), Null(values["email"]), Null(values["phone"]), Null(values["address"]), Null(values["source"]), values["status"], Null(values["notes"]));
        var saved = item is null
            ? await _apiClient.CreateInternalCustomerAsync(_activeSegment, model)
            : await _apiClient.UpdateInternalCustomerAsync(_activeSegment, item.Id, model);
        if (saved is null) { ShowToast("Không lưu được khách hàng. Vui lòng kiểm tra dữ liệu và quyền quản lý.", true); return; }
        ShowToast(item is null ? "Đã thêm khách hàng." : "Đã cập nhật khách hàng.");
        await LoadInternalDataAsync();
    }

    private async Task ShowResourceDialogAsync(ApiClient.InternalResourceItem? item = null)
    {
        var typeOptions = _activeSegment == FashionSegment
            ? new[] { new PromptOption("PLAN", "Bản kế hoạch"), new PromptOption("DESIGN_SAMPLE", "Mẫu thiết kế") }
            : new[] { new PromptOption("EXAM", "Đề thi / đề bài"), new PromptOption("DOCUMENT", "Tài liệu") };
        var fields = new[]
        {
            new PromptField("code", "Mã tài liệu", item?.Code),
            new PromptField("title", "Tên tài liệu / mẫu", item?.Title),
            new PromptField("type", "Loại", item?.ResourceType ?? typeOptions[0].Value, true, typeOptions),
            new PromptField("uri", "Đường dẫn lưu trữ hoặc liên kết", item?.StorageUri),
            new PromptField("file", "Tên tệp", item?.FileName, false),
            new PromptField("version", "Phiên bản", item?.Version ?? "1.0"),
            new PromptField("tags", "Thẻ (phân cách bằng dấu phẩy)", item?.TagsDisplay, false),
            new PromptField("status", "Trạng thái", item?.Status ?? "DRAFT", true, new[] { new PromptOption("DRAFT", "Bản nháp"), new PromptOption("ACTIVE", "Đang sử dụng"), new PromptOption("ARCHIVED", "Đã lưu trữ") }),
            new PromptField("notes", "Ghi chú", item?.Notes, false)
        };
        if (!PromptDialog.TryShow(this, item is null ? $"Thêm thông tin · {ActiveSegmentName}" : "Sửa thông tin tài liệu", fields, out var values)) return;
        var tags = values["tags"].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var model = new ApiClient.InternalResourceModel(values["code"], values["title"], values["type"], values["uri"], Null(values["file"]), null, null, null, values["version"], values["status"], tags, null, Null(values["notes"]));
        var saved = item is null
            ? await _apiClient.CreateInternalResourceAsync(_activeSegment, model)
            : await _apiClient.UpdateInternalResourceAsync(_activeSegment, item.Id, model);
        if (saved is null) { ShowToast("Không lưu được thông tin tài liệu. Vui lòng kiểm tra dữ liệu và quyền quản lý.", true); return; }
        ShowToast(item is null ? "Đã thêm thông tin tài liệu." : "Đã cập nhật thông tin tài liệu.");
        await LoadInternalDataAsync();
    }

    private ApiClient.EmployeeItem? GetSelectedEmployee()
        => ViewEmployeesContainer.SelectedEmployee;

    private void EmployeesTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUiReady || e.Source != sender || sender is not TabControl tabs)
            return;

        if (!_isEmployeeFunctionSelectionSyncing)
        {
            _isEmployeeFunctionSelectionSyncing = true;
            try
            {
                var isFashion = _activeSegment == FashionSegment;
                var children = isFashion
                    ? new[] { NavFashionEmployeesWorking, NavFashionEmployeesProfiles, NavFashionEmployeesMissingCv, NavFashionEmployeesResigned, NavFashionEmployeesBlacklist }
                    : new[] { NavTechEmployeesWorking, NavTechEmployeesProfiles, NavTechEmployeesMissingCv, NavTechEmployeesResigned, NavTechEmployeesBlacklist };
                for (var i = 0; i < children.Length; i++)
                {
                    children[i].IsChecked = (i == tabs.SelectedIndex);
                }
                if (!_isSidebarCollapsed && (isFashion ? FashionNavigationGroup.Visibility : TechnologyNavigationGroup.Visibility) == Visibility.Visible)
                {
                    SetEmployeesSubmenuVisibility(isFashion, true);
                }
            }
            finally
            {
                _isEmployeeFunctionSelectionSyncing = false;
            }
        }
    }

    private async void CreateEmployee_Click(object sender, RoutedEventArgs e) => await ShowEmployeeDialogAsync();

    private async void EditEmployee_Click(object sender, RoutedEventArgs e)
    {
        var employee = GetSelectedEmployee();
        if (employee is null) { ShowToast("Hãy chọn một nhân sự cần sửa hồ sơ."); return; }
        await ShowEmployeeDialogAsync(employee);
    }

    private async Task ShowEmployeeDialogAsync(ApiClient.EmployeeItem? employee = null)
    {
        ApiClient.EmployeePaymentDetails? paymentDetails = null;
        if (employee is not null)
        {
            paymentDetails = await _apiClient.GetEmployeePaymentDetailsAsync(employee.Id);
            if (paymentDetails is null)
            {
                ShowToast("Không tải được thông tin chuyển khoản hoặc tài khoản chưa có quyền quản lý nhân sự.", true);
                return;
            }
        }
        var units = GetSegmentBusinessUnitOptions(employee);
        if (units.Count == 0)
        {
            ShowToast($"Tài khoản chưa được cấp đơn vị kinh doanh thuộc mảng {ActiveSegmentName}.", true);
            return;
        }
        var departments = new List<EmployeeDialogOption> { new(null, "Không chọn phòng ban") };
        departments.AddRange(EmployeeViewModel.Departments.Select(d => new EmployeeDialogOption(d.Id, $"{d.Code} - {d.Name}")));
        if (!EmployeeDialog.TryShow(this, ActiveSegmentName, units, departments, out var value, employee, paymentDetails) || value is null) return;

        await RunWithBusyAsync(employee is null ? "Đang tạo hồ sơ nhân sự..." : "Đang cập nhật hồ sơ nhân sự...", async () =>
        {
            ApiClient.EmployeeItem? saved;
            if (employee is null)
            {
                var model = new ApiClient.CreateEmployeeModel(value.EmployeeCode, value.FullName, value.Email, value.Phone, value.Position, value.BaseSalary, value.DepartmentId, value.BusinessUnitId, value.JoinedDate, value.Status, value.EmploymentType, value.PartTimeCalculationMethod, value.PartTimeUnitRate, value.CvUrlOrPath, value.ProfessionalSummary, value.Skills, value.Experience, BankName: value.BankName, BankAccountNumber: value.BankAccountNumber ?? string.Empty, BankAccountHolder: value.BankAccountHolder);
                saved = await _apiClient.CreateEmployeeAsync(model);
            }
            else
            {
                var model = new ApiClient.UpdateEmployeeModel(value.FullName, value.Email, value.Phone, value.Position, value.BaseSalary, value.DepartmentId, value.BusinessUnitId, value.JoinedDate, value.Status, value.EmploymentType, value.PartTimeCalculationMethod, value.PartTimeUnitRate, value.CvUrlOrPath, value.ProfessionalSummary, value.Skills, value.Experience, BankName: value.BankName ?? string.Empty, BankAccountNumber: value.BankAccountNumber ?? string.Empty, BankAccountHolder: value.BankAccountHolder ?? string.Empty);
                saved = await _apiClient.UpdateEmployeeAsync(employee.Id, model);
            }
            if (saved is null) { ShowToast("Không lưu được hồ sơ nhân sự. Vui lòng kiểm tra dữ liệu và quyền quản lý.", true); return; }
            ShowToast(employee is null ? "Đã tạo hồ sơ nhân sự." : "Đã cập nhật hồ sơ nhân sự.");
            await LoadEmployeesAsync();
        });
    }

    private List<EmployeeDialogOption> GetSegmentBusinessUnitOptions(ApiClient.EmployeeItem? employee)
    {
        var fromSession = _currentUser?.AccessibleBusinessUnits ?? Array.Empty<ApiClient.BusinessUnitItem>();
        var options = fromSession
            .Where(x => _activeSegment == FashionSegment
                ? string.Equals(x.Code, "FASHION", StringComparison.OrdinalIgnoreCase)
                : IsTechnologyBusinessUnit(x.Code))
            .Select(x => new EmployeeDialogOption(x.Id, $"{x.Code} - {x.Name}"))
            .ToList();
        if (options.Count == 0)
        {
            options.AddRange(_cachedBusinessUnits
                .Where(x => x.BusinessUnitId.HasValue && (_activeSegment == FashionSegment
                    ? string.Equals(x.BusinessUnitCode, "FASHION", StringComparison.OrdinalIgnoreCase)
                    : IsTechnologyBusinessUnit(x.BusinessUnitCode)))
                .Select(x => new EmployeeDialogOption(x.BusinessUnitId, $"{x.BusinessUnitCode} - {x.BusinessUnitName}")));
        }
        if (employee?.BusinessUnitId is Guid id && options.All(x => x.Id != id)) options.Insert(0, new EmployeeDialogOption(id, employee.BusinessUnitName ?? "Đơn vị hiện tại"));
        return options;
    }

    private static bool IsTechnologyBusinessUnit(string code)
        => code.Equals("EDTECH", StringComparison.OrdinalIgnoreCase)
           || code.Equals("CSCA", StringComparison.OrdinalIgnoreCase)
           || code.Equals("INTERVIEW", StringComparison.OrdinalIgnoreCase);

    private async void DeleteEmployee_Click(object sender, RoutedEventArgs e)
    {
        var employee = GetSelectedEmployee();
        if (employee is null) { ShowToast("Hãy chọn nhân sự cần xóa."); return; }
        if (MessageBox.Show(this, $"Xóa hồ sơ nhân sự “{employee.FullName}”?", "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await RunWithBusyAsync("Đang xóa hồ sơ nhân sự...", async () =>
        {
            if (!await _apiClient.DeleteEmployeeAsync(employee.Id)) { ShowToast("Xóa nhân sự thất bại. Vui lòng kiểm tra ràng buộc dữ liệu và quyền quản lý.", true); return; }
            ShowToast("Đã xóa hồ sơ nhân sự.");
            await LoadEmployeesAsync();
        });
    }

    private void OpenEmployeeCv_Click(object sender, RoutedEventArgs e)
    {
        var employee = GetSelectedEmployee();
        if (employee is null || string.IsNullOrWhiteSpace(employee.CvUrlOrPath)) { ShowToast("Nhân sự đã chọn chưa có CV."); return; }
        try { Process.Start(new ProcessStartInfo(employee.CvUrlOrPath) { UseShellExecute = true }); }
        catch { ShowToast("Không mở được CV. Hãy kiểm tra lại đường dẫn hoặc liên kết.", true); }
    }

    private async void ResignEmployee_Click(object sender, RoutedEventArgs e)
    {
        var employee = GetSelectedEmployee();
        if (employee is null)
        {
            ShowToast("Hãy chọn nhân sự cần chuyển trạng thái nghỉ việc.");
            return;
        }

        var fields = new[]
        {
            new PromptField("date", "Ngày nghỉ việc (dd/MM/yyyy)", DateTime.Today.ToString("dd/MM/yyyy")),
            new PromptField("reason", "Lý do nghỉ việc", employee.StatusReason ?? "Nghỉ việc theo nguyện vọng cá nhân")
        };

        if (!PromptDialog.TryShow(this, $"Chuyển nghỉ việc — {employee.FullName} ({employee.EmployeeCode})", fields, out var values))
            return;

        DateTime resignDate = DateTime.UtcNow;
        if (DateTime.TryParseExact(values["date"], "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            resignDate = DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc);
        }

        var updateModel = new ApiClient.UpdateEmployeeModel(
            employee.FullName,
            employee.Email,
            employee.Phone,
            employee.Position,
            employee.BaseSalary,
            employee.DepartmentId,
            employee.BusinessUnitId,
            employee.JoinedDate,
            "Resigned",
            employee.EmploymentType,
            employee.PartTimeCalculationMethod,
            employee.PartTimeUnitRate,
            employee.CvUrlOrPath,
            employee.ProfessionalSummary,
            employee.Skills,
            employee.Experience,
            resignDate,
            values["reason"]);

        var updated = await _apiClient.UpdateEmployeeAsync(employee.Id, updateModel);
        if (updated is null)
        {
            ShowToast("Không thể cập nhật trạng thái nghỉ việc. Vui lòng kiểm tra quyền quản trị.", true);
            return;
        }

        ShowToast($"Đã chuyển nhân sự '{employee.FullName}' sang trạng thái Đã nghỉ việc.");
        await LoadEmployeesAsync();
    }

    private async void ReactivateEmployee_Click(object sender, RoutedEventArgs e)
    {
        var employee = GetSelectedEmployee();
        if (employee is null)
        {
            ShowToast("Hãy chọn nhân sự cần khôi phục lại công tác.");
            return;
        }

        if (MessageBox.Show(this, $"Khôi phục nhân sự '{employee.FullName}' ({employee.EmployeeCode}) trở lại làm việc?", "Xác nhận khôi phục", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var updateModel = new ApiClient.UpdateEmployeeModel(
            employee.FullName,
            employee.Email,
            employee.Phone,
            employee.Position,
            employee.BaseSalary,
            employee.DepartmentId,
            employee.BusinessUnitId,
            employee.JoinedDate,
            "Active",
            employee.EmploymentType,
            employee.PartTimeCalculationMethod,
            employee.PartTimeUnitRate,
            employee.CvUrlOrPath,
            employee.ProfessionalSummary,
            employee.Skills,
            employee.Experience,
            DateTime.UtcNow,
            "Khôi phục công tác làm việc");

        var updated = await _apiClient.UpdateEmployeeAsync(employee.Id, updateModel);
        if (updated is null)
        {
            ShowToast("Không thể khôi phục nhân sự. Vui lòng kiểm tra quyền quản trị.", true);
            return;
        }

        ShowToast($"Đã khôi phục nhân sự '{employee.FullName}' trở lại làm việc.");
        await LoadEmployeesAsync();
    }

    private async void BlacklistEmployee_Click(object sender, RoutedEventArgs e)
    {
        var employee = GetSelectedEmployee();
        if (employee is null)
        {
            ShowToast("Hãy chọn nhân sự cần đưa vào danh sách Blacklist.");
            return;
        }

        var fields = new[]
        {
            new PromptField("date", "Ngày ghi nhận vi phạm (dd/MM/yyyy)", DateTime.Today.ToString("dd/MM/yyyy")),
            new PromptField("reason", "Lý do đưa vào Blacklist / Vi phạm kỷ luật", employee.StatusReason ?? "Vi phạm quy chế doanh nghiệp hoặc không tiếp nhận lại")
        };

        if (!PromptDialog.TryShow(this, $"Đưa vào Blacklist — {employee.FullName} ({employee.EmployeeCode})", fields, out var values))
            return;

        DateTime recordDate = DateTime.UtcNow;
        if (DateTime.TryParseExact(values["date"], "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            recordDate = DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc);
        }

        var updateModel = new ApiClient.UpdateEmployeeModel(
            employee.FullName,
            employee.Email,
            employee.Phone,
            employee.Position,
            employee.BaseSalary,
            employee.DepartmentId,
            employee.BusinessUnitId,
            employee.JoinedDate,
            "Blacklisted",
            employee.EmploymentType,
            employee.PartTimeCalculationMethod,
            employee.PartTimeUnitRate,
            employee.CvUrlOrPath,
            employee.ProfessionalSummary,
            employee.Skills,
            employee.Experience,
            recordDate,
            values["reason"]);

        var updated = await _apiClient.UpdateEmployeeAsync(employee.Id, updateModel);
        if (updated is null)
        {
            ShowToast("Không thể cập nhật danh sách Blacklist. Vui lòng kiểm tra quyền quản trị.", true);
            return;
        }

        ShowToast($"Đã chuyển nhân sự '{employee.FullName}' vào danh sách Blacklist.");
        await LoadEmployeesAsync();
    }

    private async void UpdateResignedReason_Click(object sender, RoutedEventArgs e)
    {
        var employee = GetSelectedEmployee();
        if (employee is null)
        {
            ShowToast("Hãy chọn nhân sự đã nghỉ việc cần sửa lý do.");
            return;
        }

        var initialDate = employee.StatusChangedAt.HasValue
            ? employee.StatusChangedAt.Value.ToString("dd/MM/yyyy")
            : DateTime.Today.ToString("dd/MM/yyyy");

        var fields = new[]
        {
            new PromptField("date", "Ngày nghỉ việc (dd/MM/yyyy)", initialDate),
            new PromptField("reason", "Lý do nghỉ việc", employee.StatusReason ?? string.Empty)
        };

        if (!PromptDialog.TryShow(this, $"Sửa lý do nghỉ — {employee.FullName}", fields, out var values))
            return;

        DateTime resignDate = employee.StatusChangedAt ?? DateTime.UtcNow;
        if (DateTime.TryParseExact(values["date"], "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            resignDate = DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc);
        }

        var updateModel = new ApiClient.UpdateEmployeeModel(
            employee.FullName,
            employee.Email,
            employee.Phone,
            employee.Position,
            employee.BaseSalary,
            employee.DepartmentId,
            employee.BusinessUnitId,
            employee.JoinedDate,
            "Resigned",
            employee.EmploymentType,
            employee.PartTimeCalculationMethod,
            employee.PartTimeUnitRate,
            employee.CvUrlOrPath,
            employee.ProfessionalSummary,
            employee.Skills,
            employee.Experience,
            resignDate,
            values["reason"]);

        var updated = await _apiClient.UpdateEmployeeAsync(employee.Id, updateModel);
        if (updated is null)
        {
            ShowToast("Không thể cập nhật thông tin nghỉ việc.", true);
            return;
        }

        ShowToast("Đã cập nhật ngày và lý do nghỉ việc.");
        await LoadEmployeesAsync();
    }

    private async void UpdateBlacklistReason_Click(object sender, RoutedEventArgs e)
    {
        var employee = GetSelectedEmployee();
        if (employee is null)
        {
            ShowToast("Hãy chọn nhân sự trong Blacklist cần cập nhật lý do.");
            return;
        }

        var initialDate = employee.StatusChangedAt.HasValue
            ? employee.StatusChangedAt.Value.ToString("dd/MM/yyyy")
            : DateTime.Today.ToString("dd/MM/yyyy");

        var fields = new[]
        {
            new PromptField("date", "Ngày ghi nhận (dd/MM/yyyy)", initialDate),
            new PromptField("reason", "Lý do Blacklist", employee.StatusReason ?? string.Empty)
        };

        if (!PromptDialog.TryShow(this, $"Cập nhật lý do Blacklist — {employee.FullName}", fields, out var values))
            return;

        DateTime recordDate = employee.StatusChangedAt ?? DateTime.UtcNow;
        if (DateTime.TryParseExact(values["date"], "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            recordDate = DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc);
        }

        var updateModel = new ApiClient.UpdateEmployeeModel(
            employee.FullName,
            employee.Email,
            employee.Phone,
            employee.Position,
            employee.BaseSalary,
            employee.DepartmentId,
            employee.BusinessUnitId,
            employee.JoinedDate,
            "Blacklisted",
            employee.EmploymentType,
            employee.PartTimeCalculationMethod,
            employee.PartTimeUnitRate,
            employee.CvUrlOrPath,
            employee.ProfessionalSummary,
            employee.Skills,
            employee.Experience,
            recordDate,
            values["reason"]);

        var updated = await _apiClient.UpdateEmployeeAsync(employee.Id, updateModel);
        if (updated is null)
        {
            ShowToast("Không thể cập nhật thông tin Blacklist.", true);
            return;
        }

        ShowToast("Đã cập nhật lý do Blacklist.");
        await LoadEmployeesAsync();
    }

    private async void QuickAssignCvFile_Click(object sender, RoutedEventArgs e)
    {
        var employee = GetSelectedEmployee();
        if (employee is null)
        {
            ShowToast("Hãy chọn nhân sự cần gán tệp CV.");
            return;
        }

        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Chọn tệp CV cho {employee.FullName}",
            Filter = "Tệp CV (*.pdf;*.doc;*.docx)|*.pdf;*.doc;*.docx|Tất cả tệp (*.*)|*.*"
        };

        if (picker.ShowDialog(this) == true)
        {
            var updateModel = new ApiClient.UpdateEmployeeModel(
                employee.FullName,
                employee.Email,
                employee.Phone,
                employee.Position,
                employee.BaseSalary,
                employee.DepartmentId,
                employee.BusinessUnitId,
                employee.JoinedDate,
                employee.Status,
                employee.EmploymentType,
                employee.PartTimeCalculationMethod,
                employee.PartTimeUnitRate,
                picker.FileName,
                employee.ProfessionalSummary,
                employee.Skills,
                employee.Experience,
                employee.StatusChangedAt,
                employee.StatusReason);

            var updated = await _apiClient.UpdateEmployeeAsync(employee.Id, updateModel);
            if (updated is null)
            {
                ShowToast("Không thể gán tệp CV cho nhân sự.", true);
                return;
            }

            ShowToast($"Đã gán tệp CV thành công cho '{employee.FullName}'.");
            await LoadEmployeesAsync();
        }
    }

    private async void QuickAssignCvUrl_Click(object sender, RoutedEventArgs e)
    {
        var employee = GetSelectedEmployee();
        if (employee is null)
        {
            ShowToast("Hãy chọn nhân sự cần nhập liên kết CV.");
            return;
        }

        var fields = new[]
        {
            new PromptField("cvUrl", "Đường dẫn hoặc liên kết CV", employee.CvUrlOrPath)
        };

        if (!PromptDialog.TryShow(this, $"Nhập liên kết CV — {employee.FullName}", fields, out var values))
            return;

        var updateModel = new ApiClient.UpdateEmployeeModel(
            employee.FullName,
            employee.Email,
            employee.Phone,
            employee.Position,
            employee.BaseSalary,
            employee.DepartmentId,
            employee.BusinessUnitId,
            employee.JoinedDate,
            employee.Status,
            employee.EmploymentType,
            employee.PartTimeCalculationMethod,
            employee.PartTimeUnitRate,
            values["cvUrl"],
            employee.ProfessionalSummary,
            employee.Skills,
            employee.Experience,
            employee.StatusChangedAt,
            employee.StatusReason);

        var updated = await _apiClient.UpdateEmployeeAsync(employee.Id, updateModel);
        if (updated is null)
        {
            ShowToast("Không thể cập nhật liên kết CV.", true);
            return;
        }

        ShowToast($"Đã cập nhật liên kết CV cho '{employee.FullName}'.");
        await LoadEmployeesAsync();
    }

    private async void QuickUpdateProfile_Click(object sender, RoutedEventArgs e)
    {
        var employee = GetSelectedEmployee();
        if (employee is null)
        {
            ShowToast("Hãy chọn nhân sự cần cập nhật hồ sơ chuyên môn.");
            return;
        }

        var fields = new[]
        {
            new PromptField("cv", "Đường dẫn / liên kết CV", employee.CvUrlOrPath, false),
            new PromptField("summary", "Tóm tắt chuyên môn", employee.ProfessionalSummary, false),
            new PromptField("skills", "Kỹ năng chính", employee.Skills, false),
            new PromptField("experience", "Kinh nghiệm làm việc", employee.Experience, false)
        };

        if (!PromptDialog.TryShow(this, $"Cập nhật hồ sơ chuyên môn — {employee.FullName}", fields, out var values))
            return;

        var updateModel = new ApiClient.UpdateEmployeeModel(
            employee.FullName,
            employee.Email,
            employee.Phone,
            employee.Position,
            employee.BaseSalary,
            employee.DepartmentId,
            employee.BusinessUnitId,
            employee.JoinedDate,
            employee.Status,
            employee.EmploymentType,
            employee.PartTimeCalculationMethod,
            employee.PartTimeUnitRate,
            Null(values["cv"]),
            Null(values["summary"]),
            Null(values["skills"]),
            Null(values["experience"]),
            employee.StatusChangedAt,
            employee.StatusReason);

        var updated = await _apiClient.UpdateEmployeeAsync(employee.Id, updateModel);
        if (updated is null)
        {
            ShowToast("Không thể cập nhật hồ sơ chuyên môn.", true);
            return;
        }

        ShowToast($"Đã cập nhật hồ sơ cho '{employee.FullName}'.");
        await LoadEmployeesAsync();
    }

    private static string? Null(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

}
