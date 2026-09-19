# MOLY Internal Management — Backup và Restore PostgreSQL

## Backup

Khởi động PostgreSQL trước:

```powershell
docker compose -f docker/docker-compose.yml up -d postgres
```

Tạo file backup dạng PostgreSQL custom format:

```powershell
powershell -ExecutionPolicy Bypass -File docker/backup.ps1
```

Có thể chỉ định thư mục lưu riêng, tránh đưa dữ liệu nhạy cảm vào source:

```powershell
powershell -ExecutionPolicy Bypass -File docker/backup.ps1 `
  -OutputDirectory C:\MOLY-Backups
```

Script trả về đường dẫn file, kích thước và SHA-256 để lưu vào nhật ký vận hành.

## Restore

Restore có `--clean --if-exists`, nghĩa là dữ liệu hiện tại trong database sẽ bị thay thế bởi backup. Hãy kiểm tra đúng file và giữ lại backup hiện tại trước khi chạy.

```powershell
powershell -ExecutionPolicy Bypass -File docker/restore.ps1 `
  -BackupFile C:\MOLY-Backups\moli-20260819-120000.dump `
  -ConfirmRestore
```

Không ghi mật khẩu vào command line. Script lấy `POSTGRES_USER`, `POSTGRES_PASSWORD` và `POSTGRES_DB` từ biến môi trường; nếu không có sẽ dùng giá trị mặc định của `docker-compose.yml`.

## Kiểm thử tự động

Test backup/restore thật chạy trong PostgreSQL Testcontainer và mặc định không chạy khi chưa bật Docker:

```powershell
$env:RUN_POSTGRES_TESTS = "true"
dotnet test tests/InternalManagement.IntegrationTests/InternalManagement.IntegrationTests.csproj `
  --filter FullyQualifiedName~PostgreSqlIntegrationTests
```

Test sẽ chạy migration, ghi dữ liệu đánh dấu, backup bằng `pg_dump`, xóa dữ liệu, restore bằng `pg_restore` rồi xác nhận bản ghi quay lại.
