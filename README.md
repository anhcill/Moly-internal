# MOLY Internal Management

Hệ thống quản trị nội bộ gồm API ASP.NET Core, PostgreSQL và ứng dụng Desktop WPF.

## Bắt đầu phát triển

Yêu cầu .NET 10 SDK. Desktop và bộ test Desktop chạy trên Windows. Cấu hình PostgreSQL cho API theo [runbook](docs/RUNBOOK.md); môi trường development không tự áp dụng migration.

```powershell
dotnet restore InternalManagement.slnx
dotnet build InternalManagement.slnx --configuration Release
dotnet test tests/InternalManagement.UnitTests --configuration Release
dotnet test tests/InternalManagement.IntegrationTests --configuration Release --filter "FullyQualifiedName!~PostgreSqlIntegrationTests"
dotnet test tests/InternalManagement.DesktopTests --configuration Release
```

Khởi động API và Desktop trong hai terminal:

```powershell
dotnet run --project src/InternalManagement.Api --launch-profile InternalManagement.Api
dotnet run --project InternalManagement.Desktop
```

Xem [kiến trúc và quy tắc mở rộng](docs/ARCHITECTURE.md), [API contract](docs/API-CONTRACT.md), [hướng dẫn vận hành](docs/RUNBOOK.md) và [hướng dẫn người dùng](docs/USER-GUIDE.md).
