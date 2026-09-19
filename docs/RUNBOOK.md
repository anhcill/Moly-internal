# MOLY Internal Management — Runbook & Hướng dẫn Vận hành Dev

Phiên bản: 1.0  
Hệ điều hành hỗ trợ: Windows 10/11, Linux, macOS  
Yêu cầu công cụ: .NET 10 SDK, Docker & Docker Compose  

> Production: xem [PRODUCTION-RUNBOOK.md](PRODUCTION-RUNBOOK.md). Không dùng mật khẩu mẫu trong production; secret phải qua environment/secret store.
> UAT: dùng [UAT-CHECKLIST.md](UAT-CHECKLIST.md). Hướng dẫn người dùng: [USER-GUIDE.md](USER-GUIDE.md).
> Demo/release: xem [ACCEPTANCE-DEMO.md](ACCEPTANCE-DEMO.md), [MVP-LIMITATIONS.md](MVP-LIMITATIONS.md) và [BACKLOG-MONTH-2.md](BACKLOG-MONTH-2.md).

> [!NOTE]
> **Quy chuẩn Thương hiệu & Tên gọi:** Tên thương hiệu chính thức là **MOLY** (viết bằng chữ **Y** dài). Tất cả giao diện người dùng (WPF Desktop), tài liệu kỹ thuật, báo cáo và truyền thông đều sử dụng danh xưng chuẩn **MOLY Group / MOLY Internal Management**.

---

## 1. Khởi động Môi trường Cơ sở dữ liệu & Lưu trữ (Docker)

Toàn bộ dịch vụ phụ thuộc (PostgreSQL 17, MinIO S3) được đóng gói trong thư mục `docker/`.

### Khởi chạy dịch vụ:
```powershell
# Chuyển vào thư mục docker hoặc chỉ định file cấu hình:
docker compose -f docker/docker-compose.yml up -d
```

### Kiểm tra trạng thái:
- **PostgreSQL 17**: `localhost:5432` (database local lấy từ `docker/.env`; không dùng credential local này cho production)
- **MinIO Console**: `http://localhost:9001` (credential local lấy từ `docker/.env`; không dùng cho production)
- **MinIO S3 API**: `http://localhost:9000`

### Tắt dịch vụ:
```powershell
docker compose -f docker/docker-compose.yml down
```

---

## 2. Quản lý EF Core Database Migrations

Khi có thay đổi entity trong Domain layer, thực hiện tạo và áp dụng migration như sau:

### Tạo Migration mới:
```powershell
dotnet ef migrations add <TenMigration> --project src/InternalManagement.Infrastructure --startup-project src/InternalManagement.Api --output-dir Persistence/Migrations
```

### Áp dụng Migration vào Database:
```powershell
dotnet ef database update --project src/InternalManagement.Infrastructure --startup-project src/InternalManagement.Api
```

---

## 3. Khởi chạy Backend API

Mở **một terminal riêng** và để lệnh này chạy xuyên suốt khi lập trình. `dotnet watch` tự biên dịch và áp dụng Hot Reload cho các thay đổi C# được hỗ trợ; với thay đổi không thể Hot Reload, nó tự khởi động lại API — không cần tự tắt/mở.

```powershell
dotnet watch --project src/InternalManagement.Api --launch-profile InternalManagement.Api
```
- API Endpoint cho Desktop: `http://localhost:59724`
- OpenAPI/Swagger: `http://localhost:59724/swagger`
- Health Checks:
  - Liveness: `http://localhost:59724/health/live`
  - Readiness: `http://localhost:59724/health/ready`
  - Chi tiết: `http://localhost:59724/health`

`localhost:59723` (HTTPS) và `localhost:59724` (HTTP) là hai cổng cố định của API, không phải lỗi. Desktop đã được cấu hình dùng cổng HTTP `59724` trong `InternalManagement.Desktop/appsettings.json`.

### Vòng lặp sửa lỗi nhanh

1. Chạy API bằng `dotnet watch` ở terminal thứ nhất và giữ nguyên terminal đó.
2. Mở `InternalManagement.Desktop` bằng Visual Studio, nhấn **F5** một lần. Bật **Hot Reload on Save**; các thay đổi XAML/code được hỗ trợ sẽ áp dụng ngay khi lưu.
3. Nếu không dùng Visual Studio, ở terminal thứ hai dùng `dotnet watch --project InternalManagement.Desktop`. Với các thay đổi WPF không hỗ trợ Hot Reload, watcher sẽ tự restart ứng dụng; không cần tự dừng API.

Các thay đổi như `Program.cs`, đăng ký DI, package/project file, entity/EF mapping hoặc migration thường vẫn cần restart. `dotnet watch` sẽ báo rõ và tự xử lý việc restart khi cần.

### Khi thay đổi database

Để khởi động dev nhanh và không bị Railway chậm chặn lại, API Development không còn tự migrate hoặc seed dữ liệu ở mỗi lần chạy. Sau khi tạo migration, áp dụng **một lần** bằng lệnh ở phần 2, rồi quay lại chạy `dotnet watch`. Muốn tạm bật lại hành vi cũ cho một phiên terminal:

```powershell
$env:DatabaseStartup__ApplyMigrationsOnStartup = "true"
$env:DatabaseStartup__RequireConnectionOnStartup = "true"
dotnet watch --project src/InternalManagement.Api --launch-profile InternalManagement.Api
```

### Cấu hình Cloudinary cho chứng từ kho

Trước khi dùng nút **Tạo phiếu nhập** và chọn ảnh/PDF hóa đơn, đặt secret cho API bằng environment variables hoặc secret store:

```powershell
$env:Cloudinary__CloudName = "<cloud-name>"
$env:Cloudinary__ApiKey = "<api-key>"
$env:Cloudinary__ApiSecret = "<api-secret>"
```

Không đặt `ApiSecret` trong Desktop hoặc commit vào repository. Mặc định mỗi tệp tối đa 15 MB; có thể đổi bằng `Cloudinary__MaxFileSizeBytes`.

---

## 4. Khởi chạy WPF Desktop Client

```powershell
dotnet run --project InternalManagement.Desktop
```
- Cấu hình API gateway được khai báo trong `InternalManagement.Desktop/appsettings.json`.

---

## 5. Chạy Toàn bộ Bộ Kiểm thử (Unit & Integration Tests)

```powershell
dotnet test
```
