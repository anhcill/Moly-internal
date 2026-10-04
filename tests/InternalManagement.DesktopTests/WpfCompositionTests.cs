using System.Runtime.ExceptionServices;
using System.Windows;
using InternalManagement.Desktop;
using InternalManagement.Desktop.Services;
using InternalManagement.Desktop.Views;

namespace InternalManagement.DesktopTests;

public sealed class WpfCompositionTests
{
    [Fact]
    public void MainWindow_LoadsMergedStylesAndFeatureViews()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App();
                app.InitializeComponent();
                Assert.NotNull(app.TryFindResource("ModernPrimaryButton"));
                Assert.NotNull(app.TryFindResource("DataGridTextCellStyle"));

                var window = new MainWindow();
                Assert.IsType<LoginView>(window.FindName("LoginContainer"));
                Assert.NotNull(window.FindName("ViewDashboardContainer"));
                Assert.NotNull(window.FindName("ViewQuestionsContainer"));
                Assert.NotNull(window.FindName("ViewInterviewContainer"));
                Assert.NotNull(window.FindName("ViewCustomersContainer"));
                Assert.IsType<CourseManagementView>(window.FindName("ViewCoursesContainer"));
                var employeesView = Assert.IsType<EmployeesView>(window.FindName("ViewEmployeesContainer"));
                Assert.NotNull(employeesView.ViewModel);
                Assert.IsType<AttendanceView>(window.FindName("ViewAttendanceContainer"));
                Assert.IsType<SyncRunsView>(window.FindName("ViewSyncContainer"));
                Assert.IsType<LmsOperationsView>(window.FindName("ViewLmsContainer"));
                var payrollView = Assert.IsType<PayrollView>(window.FindName("ViewPayrollContainer"));
                Assert.NotNull(payrollView.FindName("PayrollGuideView"));
                Assert.IsType<FashionView>(window.FindName("ViewFashionContainer"));
                Assert.IsType<CompanyFinanceView>(window.FindName("ViewCompanyFinanceContainer"));
                Assert.IsType<InternalDataView>(window.FindName("ViewInternalDataContainer"));
                using var api = new ApiClient("http://localhost:5000/");
                var period = new ApiClient.PayrollPeriodItem(
                    Guid.NewGuid(), Guid.NewGuid(), null, "Kỳ lương kiểm thử",
                    new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31),
                    0, 0m, 0m, 0, null, null, DateTime.UtcNow);
                var workWindow = new PayrollWorkEntriesWindow(api, period, "TECHNOLOGY_EDUCATION");
                Assert.NotNull(workWindow.FindName("EntriesGrid"));
                workWindow.Close();
                window.Close();
                app.Shutdown();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "WPF composition did not finish loading.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
