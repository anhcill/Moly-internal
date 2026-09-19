# MOLY Internal Management — Acceptance Demo v0.1.0

Ngày lập: 2026-08-19  
Phạm vi: nghiệm thu kỹ thuật MVP và chuẩn bị demo nghiệp vụ Ngày 20.

Tài liệu này ghi lại bằng chứng đã có, cách chạy lại và các mục phải hoàn tất khi có quyền truy cập dữ liệu live. Không điền số liệu nguồn ngoài khi chưa có credential và không đưa token vào log, source hoặc file commit.

## 1. Tiêu chí nghiệm thu

| Mục | Tiêu chí | Bằng chứng hiện có | Trạng thái |
|---|---|---|---|
| Build | Solution build không có lỗi | `dotnet build InternalManagement.slnx --no-restore` — 0 lỗi | Đạt |
| Unit test | Nghiệp vụ finance, integration, costing và permission không regress | `InternalManagement.UnitTests` — 78/78 passed | Đạt |
| API integration test | API, auth, connector mock và sync flow chạy được | `InternalManagement.IntegrationTests` — 45/45 passed | Đạt |
| PostgreSQL thật | Migration và backup/restore path được kiểm tra trên PostgreSQL container | Chạy opt-in `PostgreSqlIntegrationTests` — 2/2 passed | Đạt |
| Backup | Tạo được PostgreSQL custom dump và SHA-256 từ database đang chạy | `docker/backup.ps1` đã chạy thành công | Đạt |
| Idempotency | Chạy lại cùng event/source identity không tạo bản ghi trùng | Covered by integration/unit tests; cần ghi thêm run ID khi demo live | Đạt kỹ thuật / cần evidence live |
| WPF | Điều hướng theo quyền, loading/error/empty, sync/dead-letter, retry, CSV export và refresh token | Tính năng đã bàn giao từ Ngày 17; cần smoke test trên máy demo | Cần demo |
| Railway health | API live có liveness/readiness và database healthy | Thực hiện trên URL Railway của môi trường đích | Cần xác nhận môi trường |
| Đối soát website | Count/tổng tiền nguồn ngoài khớp bản sao nội bộ theo từng entity | Chưa có credential/API payment nên chưa được phép chốt số | Chờ credential |

## 2. Chạy lại bằng chứng tự động

Chạy từ thư mục gốc repository:

```powershell
dotnet build InternalManagement.slnx --no-restore

dotnet test tests/InternalManagement.UnitTests/InternalManagement.UnitTests.csproj `
  --no-restore

dotnet test tests/InternalManagement.IntegrationTests/InternalManagement.IntegrationTests.csproj `
  --no-restore
```

Kiểm tra PostgreSQL thật (cần Docker đang chạy):

```powershell
$env:RUN_POSTGRES_TESTS = "true"
dotnet test tests/InternalManagement.IntegrationTests/InternalManagement.IntegrationTests.csproj `
  --filter FullyQualifiedName~PostgreSqlIntegrationTests
Remove-Item Env:RUN_POSTGRES_TESTS -ErrorAction SilentlyContinue
```

Backup/restore operator evidence được ghi tại [BACKUP-RESTORE.md](BACKUP-RESTORE.md). Không restore vào database production nếu chưa xác nhận đúng file dump, database đích và backup rollback.

## 3. Kịch bản demo WPF

Agent/owner demo ghi lại thời điểm, tài khoản role và ảnh chụp hoặc log cho từng bước:

1. Đăng nhập bằng tài khoản hợp lệ; xác nhận access token được cấp và refresh khi API trả 401.
2. Đăng nhập bằng role khác nhau; xác nhận menu chỉ hiện các quyền được cấp.
3. Mở màn hình EdTech, lớp CSCA và customer Interview; xác nhận có loading state, empty state và lỗi tiếng Việt dễ hiểu.
4. Xem sync runs; trigger một sync read-only; xác nhận run có status, cursor/checkpoint và số lượng xử lý.
5. Mở dead-letter; retry một item lỗi sau khi nguyên nhân được xử lý; xác nhận không tạo bản ghi trùng.
6. Export CSV một danh sách được phép xem; kiểm tra file có header, encoding UTF-8 và không lộ secret/token.

## 4. Quy trình đối soát live sau khi có credential

Credential chỉ được nạp vào Railway Variables/Secrets theo tên trong [INTEGRATIONS.md](INTEGRATIONS.md). Người chạy demo không copy token vào PowerShell history, screenshot hoặc ticket.

Thực hiện theo thứ tự:

1. Ghi `environment`, thời điểm UTC, commit/deployment identifier và khoảng thời gian đối soát.
2. Gọi health/readiness của API Railway; nếu không healthy thì dừng và ghi nhận lỗi.
3. Với từng nguồn, trigger một entity read-only trong phạm vi demo bằng quyền `SystemSync.Trigger`.
4. Lưu `runId`, status, cursor cuối, `processed`, `created`, `updated`, `skipped`, `failed` và dead-letter count.
5. Đối chiếu với dashboard/export của website tại cùng thời điểm và cùng timezone.
6. Chốt delta theo entity; delta khác 0 phải có giải thích hoặc mở issue, không tự sửa số liệu nguồn.

Mẫu biên bản:

| Source | Entity | Khoảng thời gian | Source count | Internal count | Delta | Source total | Internal total | Kết luận | Evidence |
|---|---|---|---:|---:|---:|---:|---:|---|---|
| `CSCA_MOLI_STUDIO` | Courses | Chưa chạy | — | — | — | — | — | Chờ token | run ID / export |
| `CSCA_MOLI_STUDIO` | Questions | Chưa chạy | — | — | — | — | — | Chờ token | run ID / export |
| `CSCA_MOLI_STUDIO` | Customers | Chưa chạy | — | — | — | — | — | Chờ token | run ID / export |
| `CSCA_MOLI_STUDIO` | Subscriptions | Chưa chạy | — | — | — | — | — | Chờ token | run ID / export |
| `CSCA_MOLI_STUDIO` | Payments | Chưa có endpoint read-only | — | — | — | — | — | Blocked | API contract |
| `WEBSITE_INTERVIEW` | InterviewCustomers | Chưa chạy | — | — | — | — | — | Chờ token | run ID / export |

## 5. Quyết định nghiệm thu MVP

MVP chỉ được ký đạt khi các mục “Cần demo”, “Cần xác nhận môi trường” và “Chờ credential” có evidence của môi trường đích. Với trạng thái hiện tại, phần build/test và nền tảng sync đã đạt; đối soát số liệu live chưa được ký và không được trình bày như đã hoàn tất.

Người nghiệm thu: ____________________  
Ngày/giờ UTC: ____________________  
Deployment/commit: ____________________  
Ghi chú hoặc issue còn mở: ____________________

## 6. Release handoff

Workspace hiện chưa có Git metadata (`.git`), vì vậy tag không được tạo trong Ngày 20. Sau khi source được đặt trong repository chính và commit đã được review, release owner chạy:

```powershell
git tag -a v0.1.0 -m "MOLY Internal Management MVP v0.1.0"
git push origin v0.1.0
```

Chỉ tag sau khi UAT live, migration review và production configuration của Ngày 19 đã được ký; tag không thay thế cho đối soát hoặc approval.
