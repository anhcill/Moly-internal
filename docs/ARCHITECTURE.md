# Kiến trúc và quy tắc mở rộng

## Ranh giới hiện tại

| Thành phần | Trách nhiệm |
| --- | --- |
| `Domain` | Entity, enum và quy tắc dữ liệu cốt lõi; không tham chiếu project khác. |
| `Application` | DTO, hợp đồng service, context theo feature và phép tính nghiệp vụ thuần, ví dụ `OrderCostCalculator`. |
| `Infrastructure` | EF Core, migration, seeder, connector, lưu trữ và triển khai service. |
| `Api` | HTTP controller, xác thực, phân quyền và composition root. |
| `Desktop` | WPF view, ViewModel, client HTTP và trạng thái phiên. |

Các feature lớn là EdTech, CSCA/Interview, HR/Payroll, Fashion/Orders, Finance và Integration. Giữ cùng tên feature xuyên suốt các layer. Khi thêm luồng mới, ưu tiên tạo DTO/use case và hợp đồng dữ liệu trong `Application/Features/<Feature>`; triển khai truy cập dữ liệu trong `Infrastructure`; controller chỉ nhận/trả HTTP. Với Desktop, đặt view và ViewModel theo feature; dùng `ApiClient.<Feature>.cs` cho endpoint tương ứng.

## Quy tắc cho thay đổi mới

1. Không thêm nghiệp vụ mới vào `MainWindow.xaml.cs` hoặc một service tổng hợp khác. Code-behind của màn hình chỉ xử lý sự kiện giao diện; trạng thái có thể bind và luồng tải dữ liệu thuộc ViewModel.
2. Service chỉ nhận tập dữ liệu nó cần qua context theo feature. Bốn service CSCA, Orders, Finance và Payroll đã dùng hợp đồng hẹp; các service còn lại sẽ được chuyển dần.
3. Response HTTP của Desktop phải được dispose ngay tại nơi gọi. Mọi request có refresh token đi qua `SendWithRefreshAsync`.
4. Entity và index database thay đổi phải đi cùng migration. Scaffold bằng EF, sau đó kiểm tra diff/snapshot và SQL để migration chỉ thay đổi đúng phạm vi nghiệp vụ; nếu snapshot cũ đã lệch model, phải kiểm tra thật kỹ trước khi phát hành.
5. Thay đổi tính toán tài chính, tồn kho, lương hoặc phân quyền cần unit test ở ranh giới nghiệp vụ và integration test ở API. Thay đổi giao diện dùng Desktop test cho ViewModel hoặc smoke test WPF.
6. Mọi PR phải qua workflow `Build and test`: build solution, unit tests, Desktop tests, API integration tests trên host in-memory và job PostgreSQL/Testcontainers thật với `RUN_POSTGRES_TESTS=true`. Release Desktop cũng phải qua job PostgreSQL này.
7. Mã C# viết tay không vượt 1.000 dòng mỗi file; CI chạy `tools/VerifySourceSize.ps1` để giữ ngân sách này. Nếu một feature lớn lên, tách theo use case hoặc view thay vì nới ngưỡng.

Version NuGet được quản lý tại `Directory.Packages.props`. Tăng version tại một nơi, kiểm tra restore/build/test trước khi phát hành.

## Phần cần tiếp tục chuyển đổi

- `MainWindow.xaml` hiện chỉ giữ khung ứng dụng, điều hướng và trạng thái toàn cục; đăng nhập và toàn bộ màn hình nghiệp vụ đã chuyển sang `Views/`. Dashboard, Ngân hàng câu hỏi, Khách hàng phỏng vấn và Học viên/Khách hàng dùng ViewModel riêng. Các view lớn còn nối với `MainWindow.<Feature>.cs` qua `MainWindow.ViewActions.cs` và các accessor tạm trong `MainWindow.ViewControls.cs`; bước tiếp theo là chuyển trạng thái và use case giao diện vào ViewModel/service theo feature rồi xóa lớp tương thích này, không thêm phụ thuộc mới vào đó.
- Các partial service backend giúp phân vùng mã theo use case; tiếp tục chuyển quy tắc nghiệp vụ thuần sang `Application` và giữ EF/orchestration trong `Infrastructure`. `IApplicationDbContext` vẫn còn phục vụ các service cũ.
- Đặt chỗ tồn kho khi tạo đơn dùng concurrency token `xmin` của PostgreSQL trên từng dòng `InventoryBalance` cùng `SaveChanges` giao dịch: hai instance đọc cùng số dư thì chỉ một cập nhật thành công. Bài test hai `DbContext` độc lập trong `PostgreSqlIntegrationTests` phải được chạy với Docker trước khi triển khai nhiều instance; test in-memory không xác nhận được khóa này.
- Workflow CI đã chạy PostgreSQL/Testcontainers riêng; ngưỡng coverage vẫn chưa áp dụng.
