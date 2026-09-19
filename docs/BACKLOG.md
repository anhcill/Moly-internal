# MOLY Internal Management — Kế hoạch 20 Ngày & Quản lý Backlog MVP

> [!NOTE]
> **Thương hiệu:** **MOLY** (chữ Y dài). Toàn bộ hệ thống & giao diện áp dụng chuẩn MOLY.

---

## 1. Phân bổ Công việc Tuần 1: Nền tảng Chạy được & Khóa API Contract

- [x] **Ngày 1 (Xong):** Chốt Business Units, vai trò, nguồn dữ liệu, phạm vi MVP, tài liệu kiến trúc kỹ thuật (`API-CONTRACT.md`, `INTEGRATIONS.md`, `SECURITY.md`, `RUNBOOK.md`, `BACKLOG.md`).
- [x] **Ngày 2 (Xong):** Tạo các project trong solution hiện tại; cấu hình .NET 10, nullable, analyzers, appsettings theo environment, Docker PostgreSQL/MinIO.
- [x] **Ngày 3 (Xong):** Tạo Domain/Application/Infrastructure/API skeleton, EF Core DbContext, migration đầu tiên, health checks.
- [x] **Ngày 4 (Xong):** Implement login, password hashing (Argon2id/BCrypt), JWT access token, refresh token rotation, logout/revoke.
- [x] **Ngày 5 (Xong):** Implement RBAC + tenant/business-unit policy, audit middleware, seed admin/dev data; viết test login và truy cập chéo.

**Trạng thái cần lưu ý:** Source đã có Clean Architecture, migration, health checks, authentication, RBAC, audit middleware, seeder, WPF Login/Shell và test project. Ngày 17–18 đã được triển khai và xác minh bằng solution build, unit/integration tests, PostgreSQL Testcontainers và backup/restore thật. Các giới hạn runtime/production còn lại được theo dõi ở Ngày 19–20; không coi đây là cam kết thay thế cho UAT production.

### Checklist xác minh Tuần 1

```powershell
dotnet --info
docker compose -f docker/docker-compose.yml up -d
dotnet run --project src/InternalManagement.Api
dotnet test
```

- Mở Swagger theo URL hiển thị trong terminal, gọi `GET /health` và `GET /health/ready`.
- Gọi `POST /api/v1/auth/login` bằng tài khoản seed trong `DatabaseSeeder.cs`.
- Kiểm tra refresh-token, logout, `GET /api/v1/auth/me` và quyền truy cập chéo business unit.
- Chỉ sau khi các bước trên pass mới chuyển trạng thái Tuần 1 thành “đã nghiệm thu”.

---

## 2. Phân bổ Công việc Tuần 2: Tích hợp Website & EdTech/CSCA/Interview

- [x] **Ngày 6 (Xong):** Tạo connector abstraction, `integration_runs`, inbox/dead-letter, cursor và idempotency key.
- [ ] **Ngày 7:** Làm mock website adapter; test pagination, timeout, retry, rate limit, duplicate event, partial failure.
- [ ] **Ngày 8:** Kết nối API thật đầu tiên (read-only): courses, questions, customers, subscriptions, payments; mapping và validation. Production chỉ dùng connector HTTP thật; mock connector đã được giới hạn cho Testing/InMemory.
- [x] **Ngày 9 (Xong):** Làm content version/publication, màn hình EdTech trong WPF; phân quyền content editor/teacher/customer service.
- [x] **Ngày 10 (Xong):** Làm CSCA class/student/staff/schedule và Interview customer; ghi doanh thu/chi phí và phân bổ lợi nhuận theo business unit (`CSCA`, `INTERVIEW`, `EDTECH`, `FASHION`); tích hợp giao diện WPF Desktop; thêm migration lịch học và test end-to-end.

### Kết quả audit lại Tuần 2

- **Ngày 6 — nền tảng đồng bộ:** Có connector abstraction, `IntegrationRun`, inbox/dead-letter, cursor, unique event ID và mapper/upsert. Phần vận hành còn TODO: xử lý inbox hiện mới chuyển trạng thái, còn retry dead-letter chưa gọi mapper để reprocess payload.
- **Ngày 7 — resilience:** Có mock adapter dành cho Testing, cursor, paging, error handling và dead-letter queue; còn thiếu retry exponential backoff, timeout, 429/`Retry-After` và test partial failure thực tế.
- **Ngày 8 — API website:** Có pipeline/mapping/validation cho courses, questions, customers, subscriptions, payments; production không còn đăng ký `WebsiteEdtechConnector` và sẽ báo rõ khi API thật/credential chưa sẵn sàng.
- **Ngày 9 — content/WPF:** Hoàn thành trọn vẹn cả Backend (Content versioning, Question publication workflow, RBAC) và WPF Desktop (Màn hình quản lý Khóa học, Ngân hàng câu hỏi, Phê duyệt xuất bản).
- **Ngày 10 — CSCA/Interview:** Đã nghiệm thu source, migration và API: lớp/học viên/nhân sự/lịch, Interview, profit allocation theo BU; test end-to-end chạy đạt. Bộ test hiện tại: 46 unit + 24 integration pass. Cần lưu ý migration unique index sẽ cần rà dữ liệu trùng trước khi áp dụng lên database đã có dữ liệu.

---

## 3. Phân bổ Công việc Tuần 3: HR/Payroll & Fashion/Inventory (Áo Dài & Quản Lý Kho Đa Kênh)

> [!IMPORTANT]
> **Định Hướng Ngành Hàng & Quản Trị Vận Hành Fashion (Áo Dài MOLY):**
> 1. **Sản phẩm cốt lõi:** Ngành hàng Thời trang MOLY tập trung vào **Áo Dài** (Áo Dài Truyền Thống, Áo Dài Cách Tân, Áo Dài Lụa/Nhung/Gấm, bộ áo kèm quần/phụ kiện theo size S/M/L/XL và màu sắc).
> 2. **Mô hình Bán hàng (Omnichannel):** Bán lẻ đa kênh gồm **TikTok Shop**, **Shopee** và **Bán trực tiếp tại cửa hàng / Bán ngoài**.
> 3. **Lộ trình tích hợp Sàn TMĐT:** Kết nối API trực tiếp 2 chiều với TikTok Shop / Shopee sẽ triển khai ở Giai đoạn 2 sau khi có Sandbox & Credential chính thức. Giai đoạn MVP tập trung tối đa vào **tính chính xác tuyệt đối của dữ liệu lõi**:
>    - **Sản xuất và nhập / xuất kho chuẩn xác:** Áo dài MOLY mặc định là hàng **MAKE — tự may/sản xuất**. Mọi biến động phải đi theo nguyên liệu/phụ liệu → BOM → lệnh sản xuất → thành phẩm; `PurchaseReceipt` chủ yếu dùng cho NVL hoặc gia công ngoài. Nhập áo thành phẩm từ xưởng đối tác chỉ là ngoại lệ `BUY`, không được dùng làm logic COGS chính.
>    - **Truy vết kho:** Xuất NVL, WIP, thành phẩm tốt/lỗi, giữ hàng reserved, xuất giao, hoàn hàng inspection và điều chỉnh kiểm kê đều ghi vết bất biến vào `InventoryMovement`, cập nhật `InventoryBalance` trong transaction.
>    - **Tính toán giá cả & Giá vốn (Costing & Pricing):** Quản lý giá vốn thực tế (COGS / Unit Cost), giá niêm yết, chiết khấu khuyến mãi, phí sàn (commission fees), phụ phí vận chuyển và doanh thu thuần chuẩn xác từng đồng.
>    - **Xuất hóa đơn & Chứng từ bán hàng (Invoicing & Receipts):** Hỗ trợ lập phiếu bán hàng / hóa đơn bán lẻ snapshot bất biến (`SalesInvoice`/`SalesReceipt`), lưu vết giá bán & thuế tại thời điểm chốt đơn, sẵn sàng cho công tác đối soát kế toán và ghi nhận dòng tiền (Ngày 16).

### 3.1. Plan note bắt buộc — áo dài tự may và lợi nhuận

Đây là gate P0 trước khi coi Fashion/Finance là hoàn tất. Không được dùng công thức đơn giản `giá bán - giá nhập xưởng` cho áo dài tự may. Phải tách rõ ba lớp:

```text
Giá thành sản xuất thực tế
  = NVL trực tiếp + nhân công + gia công ngoài
  + hao hụt/sửa lại + chi phí xưởng phân bổ

Lợi nhuận gộp
  = Doanh thu thuần - COGS thực tế của thành phẩm

Lợi nhuận theo đơn/kênh
  = Lợi nhuận gộp - phí sàn - affiliate - thanh toán
  - ship shop chịu - đóng gói bán hàng - thuế - tác động hoàn hàng
```

Các hạng mục phải có trong plan/code trước khi chốt pricing:

- `Material`/`MaterialLot` cho vải, lót, ren, cườm/đá, nút, dây kéo, chỉ, mếch, tag/nhãn và bao bì; lưu đơn vị, quy đổi, giá theo lô, ngày nhập và nhà cung cấp.
- `BOM version` theo mẫu, rập, màu, size và ngày hiệu lực; có định mức, hao hụt dự kiến và override theo size. Size M và XL không mặc định cùng một cost nếu dùng lượng vải khác nhau.
- `ProductionOrder` có mix size/màu, snapshot BOM/standard cost lúc phát hành, xuất NVL thực tế, công đoạn CẮT → MAY → THÊU/ĐÍNH → ỦI → QC → ĐÓNG GÓI, gia công ngoài, hàng tốt/lỗi, sửa lại và phế liệu.
- `Actual Cost` phải gồm NVL, nhân công, gia công ngoài, hao hụt/sửa lại và overhead (thuê xưởng, điện/nước, khấu hao máy, QC, quản lý, rập/thiết kế) theo policy phân bổ có kỳ và basis.
- Phân biệt `STANDARD_COST`, `ESTIMATED_COST`, `ACTUAL_COST`; lệnh chưa đóng hoặc phí chưa đối soát không được ghi COGS bằng 0. Lệnh/đơn đã chốt phải giữ cost snapshot khi BOM hoặc giá NVL thay đổi.
- So sánh định mức-vs-thực tế theo material quantity/price, labor, gia công ngoài, scrap/rework và overhead; ví dụ BOM 3,0m nhưng dùng 3,2m phải làm tăng cost và hiển thị variance.
- `ChannelFeePolicy` versioned theo TikTok/Shopee/cửa hàng: commission, affiliate, payment, voucher, ship shop chịu, thuế và cách tính margin; không dùng một tỷ lệ phí chung.
- Pricing Simulator bắt buộc trả về giá hòa vốn, giá tối thiểu đạt target margin, giá đề xuất và cảnh báo voucher/affiliate/ship làm lỗ. Báo cáo drill-down được từ profit → order → cost snapshot → production batch → BOM/movement/fee.

**Ví dụ kiểm soát bắt buộc:** một áo có NVL 600k, nhân công 350k, gia công 150k, hao hụt 50k, overhead 100k thì COGS cơ sở là 1.250k. Bán 2.500k chưa có nghĩa lãi 1.250k; phải trừ voucher, phí kênh, affiliate, ship, thuế, refund/return và chi phí bán hàng liên quan.

**Definition of Done cho costing:** có test mix size, actual vượt BOM, hao hụt/hàng lỗi, hoàn tốt/hỏng, phí khác nhau theo kênh, duplicate order và cảnh báo không cho báo cáo “lãi” khi cost/fee còn provisional.

- [x] **Ngày 11 (Xong):** Employee, department, attendance import Excel/CSV, validation và báo cáo lỗi từng dòng (Row-by-row validation & error reporting), REST API, Desktop Client WPF integration, 84/84 automated tests pass (100%).
- [x] **Ngày 12 (Xong):** Payroll MVP: policy version, calculation engine tính lương tự động theo ngày công chấm công, adjustments (thưởng/phụ cấp/tăng ca/khấu trừ), payslip snapshot bất biến, REST API, WPF Desktop UI, 90/90 automated tests pass (100%).
- [x] **Ngày 13 (Xong):** Payroll workflow DRAFT → CALCULATED → REVIEWING → APPROVED → PAID → PUBLISHED; nhật ký duyệt lương PayrollApproval; phân quyền & API xem phiếu lương cá nhân My Payslips, WPF Desktop buttons & 94/94 automated tests pass (100%).
- [x] **Ngày 14 (Xong baseline):** Fashion Product & Variant SKU (Áo Dài MOLY), Supplier, Purchase Receipt, Sổ giao dịch kho bất biến (InventoryMovement append-only ledger), Real-time Balance (InventoryBalance on-hand/reserved/available), REST APIs, RBAC, Database Seeder, WPF Desktop Client & 109/109 automated tests pass (100%). **Lưu ý:** baseline này chưa đủ để kết luận costing áo tự sản xuất; phải bổ sung/điều chỉnh theo Plan note 3.1 trước khi dùng cho COGS/lợi nhuận.
- [x] **Ngày 15 (Xong):** Sales order và snapshot COGS/lợi nhuận theo Store / Shopee / TikTok; giữ tồn `on-hand -> reserved -> delivered`; giao hàng trừ tồn và sổ giao dịch; đổi trả/kiểm tra với nhập lại hàng đủ điều kiện; cập nhật refund/COGS/lợi nhuận; xuất hóa đơn/phiếu bán lẻ snapshot; import order adapter JSON theo SKU hoặc `productVariantId`; chặn SKU trùng, duplicate source order và cost provisional/0. Đã có test costing, phí kênh, duplicate order và lifecycle giao hàng → đổi trả → chứng từ.

---

## 4. Phân bổ Công việc Tuần 4: Tài chính, WPF Hoàn thiện, Hardening & Nghiệm thu

- [x] **Ngày 16 (Xong):** Finance transactions, cash accounts, `ChannelFeePolicy`, Pricing Simulator và liên kết reference; báo cáo thu/chi/dòng tiền/lợi nhuận theo business unit, mẫu, size, production batch và kênh. Không coi cost/fee provisional là lợi nhuận đã chốt.
- [x] **Ngày 17 (Xong):** WPF shell theo quyền, overlay loading, trạng thái lỗi/trống, sync runs + Dead-Letter Queue/retry, import chấm công, xuất CSV lịch sử đồng bộ/tồn kho, thông báo lỗi tiếng Việt và tự refresh access token khi API trả 401.
- [x] **Ngày 18 (Xong):** PostgreSQL/Testcontainers integration, security hardening, backup/restore thật và tài liệu vận hành; 2/2 PostgreSQL tests, 45/45 integration, 78/78 unit và solution build pass.
- [x] **Ngày 19 (Xong phần engineering):** UAT checklist, migration review, production secret/config hardening, fail-fast migration/seed, deploy/runbook và hướng dẫn người dùng. UAT live/đối soát website còn chờ credential/API Payments hợp lệ.
- [x] **Ngày 20 (Xong artifact):** Acceptance demo/evidence, mẫu đối soát không bịa số liệu, MVP limitations, hướng dẫn release `v0.1.0` và backlog tháng 2. Chưa ký nghiệm thu live hoặc tạo git tag chính thức khi chưa có evidence môi trường đích.

---

## 5. Backlog Giai đoạn 2 (Sau 1 tháng MVP)

- Tích hợp trực tiếp hai chiều sàn TMĐT Shopee/TikTok Shop khi có credential chính thức.
- Module sản xuất nâng cao sau MVP: BOM nhiều cấp, lập kế hoạch năng lực, routing nâng cao và tối ưu lịch sản xuất. **BOM theo size, lệnh sản xuất và costing thực tế của áo dài tự may không nằm trong backlog; đây là P0 của MVP.**
- Pricing Simulator nâng cao: mô phỏng nhiều kịch bản sale, target margin theo kênh, sensitivity theo giá NVL/hao hụt/phí sàn và cảnh báo chương trình làm âm contribution margin.
- Tự động hóa gửi email phiếu lương hàng loạt qua SMTP/SendGrid.
- Ứng dụng di động (Mobile App) dành cho học viên và nhân viên.
