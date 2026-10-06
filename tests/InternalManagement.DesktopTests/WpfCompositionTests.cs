using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using InternalManagement.Desktop;
using InternalManagement.Desktop.Behaviors;
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
                var coursesView = Assert.IsType<CourseManagementView>(window.FindName("ViewCoursesContainer"));
                var coursesDataGrid = Assert.IsType<System.Windows.Controls.DataGrid>(coursesView.FindName("CoursesDataGrid"));
                Assert.True(DataGridColumnWidthPersistence.GetIsEnabled(coursesDataGrid));
                VerifyColumnWidthPersistence();
                var employeesView = Assert.IsType<EmployeesView>(window.FindName("ViewEmployeesContainer"));
                Assert.NotNull(employeesView.ViewModel);
                var attendanceView = Assert.IsType<AttendanceView>(window.FindName("ViewAttendanceContainer"));
                var attendanceDataGrid = Assert.IsType<DataGrid>(attendanceView.FindName("AttendanceDataGrid"));
                Assert.Equal(ScrollBarVisibility.Auto, ScrollViewer.GetHorizontalScrollBarVisibility(attendanceDataGrid));
                Assert.Equal(90, attendanceDataGrid.MinColumnWidth);
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

    private static void VerifyColumnWidthPersistence()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"moly-grid-behavior-tests-{Guid.NewGuid():N}");
        var storagePath = Path.Combine(directory, "table-layouts.json");

        try
        {
            var (firstWindow, firstColumn) = CreatePersistenceTestWindow(storagePath);
            firstWindow.Show();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            firstColumn.Width = new DataGridLength(321, DataGridLengthUnitType.Pixel);
            Thread.Sleep(350);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.True(File.Exists(storagePath));
            firstWindow.Close();

            var (secondWindow, secondColumn) = CreatePersistenceTestWindow(storagePath);
            secondWindow.Show();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            Assert.Equal(DataGridLengthUnitType.Pixel, secondColumn.Width.UnitType);
            Assert.Equal(321, secondColumn.Width.Value, precision: 1);
            secondWindow.Close();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static (Window Window, DataGridColumn Column) CreatePersistenceTestWindow(string storagePath)
    {
        var column = new DataGridTextColumn
        {
            Header = "TÊN KHÓA HỌC",
            Binding = new Binding("Title"),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        };
        var dataGrid = new DataGrid
        {
            Name = "PersistenceTestGrid",
            AutoGenerateColumns = false
        };
        dataGrid.Columns.Add(column);
        DataGridColumnWidthPersistence.SetIsEnabled(dataGrid, true);
        DataGridColumnWidthPersistence.SetStoragePath(dataGrid, storagePath);

        var owner = new UserControl { Content = dataGrid };
        var window = new Window
        {
            Content = owner,
            Width = 640,
            Height = 480,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Opacity = 0
        };

        return (window, column);
    }
}
