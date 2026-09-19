# MOLY Internal Management — Production Runbook

Phiên bản: 1.0  
Phạm vi: API, PostgreSQL Railway/PostgreSQL managed và vận hành sau deploy.

## 1. Nguyên tắc bảo mật

- Không ghi connection string, JWT secret, MinIO key hoặc connector token vào source, `appsettings.json`, log, ảnh UAT hay WPF binary.
- Production phải cung cấp secret qua environment/secret store. API sẽ fail-fast nếu thiếu `ConnectionStrings:DefaultConnection` hoặc `JwtSettings:Secret`.
- `JwtSettings:Secret` production phải có tối thiểu 32 ký tự ngẫu nhiên; nên dùng secret manager và xoay định kỳ.
- Production không tự tạo tài khoản hoặc dữ liệu demo khi API khởi động. Tài khoản quản trị phải được provision bằng quy trình bootstrap được phê duyệt, không đặt mật khẩu trong source.

## 2. Biến môi trường bắt buộc

PowerShell mẫu dưới đây chỉ minh họa tên biến; thay giá trị tại secret store, không commit file chứa giá trị thật:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:ConnectionStrings__DefaultConnection = "Host=<host>;Port=<port>;Database=<db>;Username=<user>;Password=<secret>;SSL Mode=Require;Trust Server Certificate=true"
$env:JwtSettings__Secret = "<random-secret-at-least-32-characters>"
$env:JwtSettings__Issuer = "MoliBackendApi"
$env:JwtSettings__Audience = "MoliClients"
```

Với Railway, dùng TCP proxy public (`Host`, `Port`, `Database`, `Username`, `Password`) và bật SSL. Không dùng hostname `.railway.internal` từ máy local ngoài Railway network.

Connector thật chỉ bật sau khi có credential hợp lệ:

```text
CSCA_MOLI_STUDIO_BEARER_TOKEN
CSCA_MOLI_STUDIO_INTEGRATION_KEY
WEBSITE_INTERVIEW_BEARER_TOKEN
WEBSITE_INTERVIEW_INTEGRATION_KEY
```

## 3. Quy trình deploy an toàn

1. Tạo backup trước khi thay đổi schema.
2. Kiểm tra version migration trong artifact và database; không sửa tay `__EFMigrationsHistory`.
3. Chạy migration bằng design-time factory:

```powershell
dotnet ef database update `
  --project src/InternalManagement.Infrastructure `
  --startup-project src/InternalManagement.Infrastructure `
  --context ApplicationDbContext
```

4. Chạy API với environment Production. Startup chỉ kiểm tra kết nối và áp migration; không tự seed dữ liệu mẫu.
5. Kiểm tra `/health/live`, `/health/ready`, `/health` rồi mới mở traffic.
6. Chạy UAT tối thiểu UAT-01, UAT-02, UAT-07, UAT-08, UAT-09, UAT-10 và UAT-14.

## 4. Kiểm tra sau deploy

- `__EFMigrationsHistory` khớp migration trong artifact.
- `questions` có `source_id`, `source_system` và index identity.
- Login/refresh/logout hoạt động; password/token không xuất hiện trong log.
- Sau migration `HardenRefreshTokenStorage`, toàn bộ refresh session cũ bị thu hồi một lần; người dùng phải đăng nhập lại.
- Áo dài MAKE đi qua MaterialLot → BOM → ProductionOrder → actual cost; không dùng giá nhập xưởng để thay COGS.
- Báo cáo tách gross sales, net sales, COGS, phí kênh, quảng cáo, đóng gói, chi phí bán hàng khác, ship, tax, refund/return và provisional/actual.
- Không bật connector website khi chưa có contract/credential và chưa có test sandbox.

## 5. Backup và restore

```powershell
.\docker\backup.ps1 -OutputDirectory .\backups
.\docker\restore.ps1 -BackupFile .\backups\moli-<timestamp>.dump -TargetDatabase moli_restore_verify
```

Restore luôn vào database kiểm chứng riêng trước khi dùng cho production. Ghi lại thời điểm backup, checksum, migration version và kết quả đọc thử các bảng chính.

## 6. Xử lý sự cố

| Triệu chứng | Kiểm tra | Xử lý |
|---|---|---|
| API fail-fast khi khởi động | `ConnectionStrings__DefaultConnection`, `JwtSettings__Secret` | Bổ sung secret store rồi restart instance |
| `q.source_id does not exist` | Migration list và schema `questions` | Chạy `database update`; không sửa tay query để né migration |
| Readiness đỏ | PostgreSQL/SSL/network/credential | Kiểm tra host proxy, port, firewall và SSL |
| Sai lợi nhuận | cost status, production order, fee policy, return | Chuyển provisional, đối soát snapshot trước khi chốt |
| Duplicate order/thu-chi | source reference/idempotency key | Không xóa tay; kiểm tra unique index và inbox/ledger |

Rollback ứng dụng phải đi cùng đánh giá tương thích schema. Không tự động downgrade migration trên database production; nếu migration lỗi, giữ backup và khôi phục vào instance kiểm chứng trước.

## 7. Xoay secret

1. Tạo secret mới trong secret store.
2. Cập nhật `JwtSettings__Secret`/connector token theo cửa sổ bảo trì.
3. Restart API và kiểm tra login/refresh.
4. Thu hồi token cũ nếu chính sách yêu cầu.
5. Xóa secret cũ khỏi log, shell history, ticket và file tạm.
