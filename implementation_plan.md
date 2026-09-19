# MOLI Internal Management — Kế hoạch triển khai MVP trong 1 tháng

> Phiên bản: 1.0 — 18/08/2026  
> Phạm vi: bản MVP chạy được, có thể mở rộng sau tháng đầu tiên  
> Mục tiêu: quản trị dữ liệu EdTech, CSCA/Interview, Fashion, nhân sự/lương và tài chính; dữ liệu từ website/sàn đi qua backend API trung tâm.

## 1. Mục tiêu và nguyên tắc bắt buộc

### Mục tiêu cuối tháng

- Đăng nhập WPF bằng tài khoản riêng, phân quyền theo vai trò và phạm vi dữ liệu.
- Có backend ASP.NET Core làm lớp trung gian duy nhất giữa WPF, database và các website/sàn.
- Xem dữ liệu EdTech từ website qua API: khóa học, đề/câu hỏi, khách hàng, gói dịch vụ, thanh toán và hạn sử dụng.
- Quản lý lớp CSCA online, học sinh, lịch học, nhân sự và chi phí/lợi nhuận theo lớp.
- Quản lý khách hàng và doanh thu website Interview.
- Quản lý nhân viên, chấm công tối thiểu, bảng lương MVP và trạng thái duyệt.
- Quản lý sản phẩm/biến thể, tồn kho theo sổ giao dịch, đơn hàng, hoàn hàng và nhà cung cấp cho Fashion.
- Quản lý thu/chi, dòng tiền và báo cáo theo tháng/quý/năm.
- Có log đồng bộ, log thao tác, backup/restore thử nghiệm và bộ test nghiệm thu.

### Nguyên tắc kiến trúc

1. WPF chỉ là client. WPF không kết nối trực tiếp PostgreSQL và không chứa secret của website/sàn.
2. Backend API là điểm vào duy nhất của WPF và là nơi gọi API bên ngoài.
3. Không scrape HTML. Chỉ dùng API chính thức, webhook được xác thực, hoặc file import được phê duyệt.
4. Mỗi bản ghi nghiệp vụ phải có `business_unit_id` (`EDTECH`, `CSCA`, `INTERVIEW`, `FASHION`) và `company_id`.
5. Dữ liệu giao dịch không bị ghi đè để “tính lại lịch sử”. Tồn kho, thanh toán và đồng bộ phải truy vết được.
6. Tích hợp bên ngoài phải chịu được timeout, rate limit, lỗi tạm thời, chạy lại và webhook gửi trùng.
7. MVP chỉ cam kết các flow được mô tả trong mục nghiệm thu; tính năng nâng cao để backlog.

## 2. Phạm vi MVP và giới hạn tháng đầu

### Có trong MVP

| Nhóm | Chức năng bắt buộc |
|---|---|
| Nền tảng | Login, refresh token, đổi mật khẩu, khóa tài khoản, RBAC, tenant/business-unit scope, audit log |
| EdTech | Đồng bộ khách hàng/gói/thanh toán; danh mục khóa học; đề/câu hỏi phiên bản hóa; quyền xem/sửa/xuất bản |
| CSCA | Lớp, đợt, lịch học, học sinh, giáo viên/trợ giảng, doanh thu, chi phí và lợi nhuận theo lớp |
| Interview | Khách hàng, gói, số buổi, tiền đã thu, doanh thu/chi phí/lợi nhuận |
| HR/Payroll | Hồ sơ nhân viên, chấm công import Excel, bảng lương MVP, quy trình duyệt và phát hành phiếu lương |
| Fashion | Sản phẩm + biến thể SKU, nguyên vật liệu theo lô, BOM theo size, sản xuất áo dài, giá thành thực tế, nhập/xuất kho, tồn kho, đơn hàng nhập tay/API, hoàn hàng |
| Tài chính | Phiếu thu/chi, danh mục, dòng tiền và báo cáo tháng/quý/năm theo business unit |
| Vận hành | Dashboard cơ bản, đồng bộ thủ công/tự động, trang lỗi đồng bộ, backup database, health check |

### Chưa cam kết trong MVP

- Tự động kết nối mọi sàn nếu chưa có credential/API contract được cung cấp.
- Tính thuế/lương theo mọi trường hợp pháp lý đặc biệt; công thức phải cấu hình và được người dùng duyệt.
- Sản xuất nâng cao như BOM nhiều cấp, lập kế hoạch năng lực và FIFO phức tạp; **costing lõi cho áo dài tự sản xuất là bắt buộc trong MVP**, không được coi là tính năng nâng cao.
- Mobile app, offline-first, chatbot, tự động gửi email hàng loạt.

Nếu API website chưa sẵn sàng, tuần 1 phải có mock server/fixture theo đúng contract để không chặn tiến độ; connector thật được bật khi có credential hợp lệ.

## 3. Kiến trúc đích

```text
WPF Desktop (.NET 10)
        | HTTPS + JWT/Refresh Token
        v
ASP.NET Core API (.NET 10)
  ├─ Auth/RBAC/Tenant scope
  ├─ EdTech, Content, CSCA, Interview
  ├─ HR/Payroll, Fashion, Finance
  ├─ Sync Orchestrator + Webhook Inbox/Outbox
  ├─ Audit, Reports, Health checks
  └─ Connector adapters (Website, Shopee/TikTok khi có quyền)
        | EF Core
        v
PostgreSQL 15+
        ├─ Redis/queue (tùy chọn; MVP có thể dùng Hangfire PostgreSQL)
        ├─ MinIO/S3: tài liệu, hóa đơn, file import/export
        └─ SMTP: thông báo tùy chọn
```

### Cấu trúc solution hiện tại

Không tạo thêm solution Desktop trùng lặp. Giữ `InternalManagement.slnx` và project WPF hiện tại, bổ sung:

```text
InternalManagement.slnx
├─ InternalManagement.Desktop/       # WPF hiện có, MVVM
├─ src/InternalManagement.Domain/    # Entity, enum, business rule
├─ src/InternalManagement.Application/ # Use case, DTO, validator
├─ src/InternalManagement.Infrastructure/ # EF Core, storage, connector
├─ src/InternalManagement.Api/        # ASP.NET Core Web API
├─ tests/InternalManagement.UnitTests/
├─ tests/InternalManagement.IntegrationTests/
├─ docker/docker-compose.yml
└─ docs/
   ├─ API-CONTRACT.md
   ├─ INTEGRATIONS.md
   ├─ SECURITY.md
   └─ RUNBOOK.md
```

## 4. Tích hợp website và sàn — thiết kế an toàn

### 4.1 Nguồn dữ liệu và quyền sở hữu dữ liệu

Mỗi tích hợp phải được ghi rõ:

| Trường | Nội dung cần chốt trước khi bật connector |
|---|---|
| `source_system` | Tên website/sàn |
| `source_entity` | customers, courses, questions, payments, orders... |
| `source_id` | ID gốc không đổi của hệ thống ngoài |
| `sync_direction` | read-only, import, hoặc hai chiều |
| `source_of_truth` | Website hay backoffice là nguồn chính |
| `last_modified` | Trường dùng để đồng bộ tăng dần |
| `credential_owner` | Ai cấp/thu hồi secret |
| `retention` | Thời hạn lưu dữ liệu và file |

Mặc định theo từng miền dữ liệu:

- CSCA Course LMS là nguồn chính cho nội dung khóa học và learning records.
- InternalManagement là nguồn chính cho hồ sơ học sinh, lớp, học phí, công nợ và điều kiện cấp quyền LMS.
- Website/sàn vẫn là nguồn chính cho nghiệp vụ bán hàng nào chưa có contract hai chiều.

Backoffice chỉ ghi ngược sang LMS qua contract machine-to-machine đã ký. Không truy cập database LMS trực tiếp.

### 4.2 API contract tối thiểu

Website EdTech cần cung cấp API được xác thực, ví dụ:

```text
GET  /api/admin/v1/courses?updated_since=&cursor=
GET  /api/admin/v1/questions?updated_since=&cursor=
GET  /api/admin/v1/customers?updated_since=&cursor=
GET  /api/admin/v1/subscriptions?updated_since=&cursor=
GET  /api/admin/v1/payments?updated_since=&cursor=
POST /api/admin/v1/webhooks/backoffice   # nếu cần gửi sự kiện
```

Contract phải quy định JSON schema, pagination/cursor, timezone, currency, mã lỗi, versioning, giới hạn tốc độ, quyền tối thiểu và cách thu hồi token. Không phụ thuộc vào giao diện HTML của website.

CSCA Course LMS phải cung cấp thêm contract riêng:

```text
POST  /api/integrations/v1/students/provision
PATCH /api/integrations/v1/students/{externalStudentId}/access
GET   /api/integrations/v1/catalog/courses
GET   /api/integrations/v1/exports/users
GET   /api/integrations/v1/exports/enrollments
GET   /api/integrations/v1/exports/learning-records
```

Provision/access dùng HMAC, `Idempotency-Key`, `X-Correlation-ID` và external ID bất biến. Học sinh `Pending`/`Partial` không có access grant; chỉ `Paid` và đủ học phí mới được cấp quyền. Chi tiết nằm tại [CSCA_COURSE_LMS_DATA_CONTRACT.md](docs/CSCA_COURSE_LMS_DATA_CONTRACT.md).

### 4.3 Đồng bộ định kỳ và webhook

- Webhook nhận vào bảng `integration_inbox` trước; kiểm tra chữ ký, timestamp và event ID.
- Event trùng không được tạo giao dịch trùng: unique key theo `(source_system, event_id)` và `(source_system, entity_type, source_id)`.
- Job định kỳ dùng `updated_since + cursor` để bù sự kiện webhook thất lạc.
- Mỗi lần đồng bộ lưu `integration_runs`, số bản ghi đọc/ghi/bỏ qua/lỗi và checkpoint.
- Lỗi tạm thời dùng exponential backoff; lỗi dữ liệu vào `integration_dead_letters` để người có quyền xem và chạy lại.
- Không retry lỗi 4xx cố định vô hạn; không ghi log access token hoặc dữ liệu nhạy cảm.
- Tất cả timestamp lưu UTC, hiển thị Asia/Ho_Chi_Minh; tiền dùng decimal và currency rõ ràng.

### 4.4 Connector abstraction

```csharp
public interface IExternalConnector
{
    string SourceSystem { get; }
    Task<SyncPage<T>> PullAsync<T>(SyncCursor cursor, CancellationToken ct);
    Task<ConnectorHealth> CheckHealthAsync(CancellationToken ct);
}
```

Mỗi website/sàn là một adapter riêng, có mock adapter cho test. Không để logic Shopee/website lọt vào domain hoặc WPF.

## 5. Mô hình dữ liệu cốt lõi

### Dùng chung

```text
companies
business_units(id, company_id, code, name)
branches, departments
users, roles, permissions, user_roles
audit_logs
documents
integration_sources
integration_credentials_reference   # chỉ tham chiếu secret manager, không lưu secret rõ
integration_runs
integration_inbox
integration_dead_letters
```

Unique và khóa ngoại quan trọng:

- `(company_id, code)` cho business unit, role, SKU, employee code.
- `(source_system, source_id)` cho mọi bản ghi đồng bộ.
- `created_at`, `updated_at`, `row_version` cho entity có cập nhật.
- Không cho client truyền `company_id`/`employee_id` để vượt scope; server lấy từ token và policy.

### EdTech/Content

```text
courses(course_source_id, title, status, version, ...)
course_modules(course_id, ...)
subjects, topics, difficulty_levels
question_banks, questions, question_versions
question_choices, question_tags
content_publications(question_version_id, published_by, published_at)
edtech_customers(source_system, source_id, ...)
subscriptions(customer_id, package, starts_at, expires_at, status)
payments(customer_id, source_id, amount, paid_at, status)
```

Phần liên kết LMS cần bổ sung ở migration sau khi contract được duyệt:

```text
lms_account_links(internal_student_id, lms_user_id, status, last_sync_at, last_error)
lms_access_grants(student_id, course_id, status, valid_from, valid_until, source_payment_id)
lms_course_links(internal_course_id, lms_course_id, source_course_id, sync_status)
integration_outbox(event_id, event_type, aggregate_type, aggregate_id, payload, status, retry_count)
```

Không lưu password, JWT, refresh token hoặc signed playback URL trong các bảng trên.

Không xóa cứng câu hỏi/phiên bản đã xuất bản; dùng trạng thái và version.

### CSCA/Interview

```text
csca_classes(class, batch, schedule, tuition_fee, ...)
csca_class_students(class_id, student, payment_status, paid_amount, ...)
csca_class_staff(class_id, employee_id, role, compensation, ...)
interview_customers(...)
profit_allocations(business_unit_id, reference_type, reference_id, income, expense)
```

### HR/Payroll

```text
employees, attendance_records, payroll_periods
payroll_adjustments, payslips, payroll_approvals
payroll_policy_versions
```

Khi chốt kỳ lương phải lưu snapshot công thức/tham số áp dụng. Mọi thay đổi sau đó chỉ áp dụng kỳ mới hoặc adjustment batch.

### Fashion/Inventory

```text
products
product_variants(product_id, sku, barcode, color, size, sourcing_type)
suppliers
materials, material_lots, material_movements
boms, bom_versions, bom_items, bom_size_overrides
production_orders, production_order_materials, production_order_operations
production_outputs, production_scrap, production_cost_snapshots
purchase_receipts, purchase_receipt_items                 # chủ yếu cho NVL/phụ liệu
inventory_movements(reference_type, reference_id, item_id, qty_delta, unit_cost)
inventory_balances(warehouse_id, variant_id, on_hand, reserved, version)
sales_orders, sales_order_items
returns, return_items, return_inspections
channel_fee_policies, order_cost_snapshots
```

`inventory_movements` là sổ giao dịch bất biến. `inventory_balances` chỉ là số dư tối ưu hóa, cập nhật trong transaction và kiểm tra không cho âm kho nếu policy không cho phép.

`sourcing_type` tối thiểu có `MAKE` (tự sản xuất) và `BUY` (mua thành phẩm). Với áo dài MOLY mặc định là `MAKE`; không dùng `BUY` để lách qua BOM và actual costing.

### 5.1. [P0 — bắt buộc] Áo dài tự may/sản xuất: logic giá thành và lợi nhuận

Đây là quyết định nghiệp vụ cần khóa trước khi tiếp tục hoàn thiện Fashion. Áo dài MOLY là hàng **tự may/sản xuất**, nên không được mô hình hóa mặc định như hàng mua từ xưởng rồi bán lại. `Supplier` và `PurchaseReceipt` trong flow chính dùng cho vải, phụ liệu hoặc gia công ngoài; nhập một chiếc áo thành phẩm từ đối tác chỉ là trường hợp ngoại lệ phải có `sourcing_type = BUY` và không được trộn với `MAKE`.

#### 5.1.1. Chuỗi nghiệp vụ chuẩn

```text
Nhập NVL theo lô
        ↓
Giá NVL thực tế theo lô / chính sách costing
        ↓
BOM theo mẫu + size + phiên bản hiệu lực
        ↓
Lệnh sản xuất (Production Order)
        ↓
Xuất NVL thực tế + ghi công đoạn + gia công ngoài
        ↓
Ghi nhận hàng tốt / hàng lỗi / sửa lại / hao hụt
        ↓
Đóng lệnh và chốt Actual Manufacturing Cost
        ↓
Nhập kho thành phẩm theo SKU/size/màu và unit cost
        ↓
Đơn hàng theo kênh + phí/chiết khấu/ship/thuế
        ↓
Doanh thu thuần - COGS thực tế - chi phí bán hàng
        ↓
Lợi nhuận và margin có thể đối soát
```

Không cho phép kết luận lợi nhuận từ `SellingPrice - CostPrice` nếu `CostPrice` chỉ là giá nhập cũ, giá ước lượng hoặc chưa gồm hao hụt/chi phí xưởng. Mọi báo cáo phải phân biệt `STANDARD_COST`, `ESTIMATED_COST` và `ACTUAL_COST`.

#### 5.1.2. Công thức phải dùng thống nhất

```text
Direct Material Cost
  = Σ (lượng NVL thực tế xuất × đơn giá NVL được chọn)

Direct Labor Cost
  = Σ (giờ/công đoạn thực tế × đơn giá công đoạn)
  hoặc đơn giá công đoạn × số lượng hoàn thành,
  theo policy đã cấu hình

Outside Processing Cost
  = thêu/đính/giặt/gia công ngoài thực tế theo lô hoặc theo sản phẩm

Manufacturing Overhead
  = chi phí xưởng được phân bổ theo policy của kỳ
  (tiền thuê, điện/nước, khấu hao máy, QC, quản lý xưởng, rập/thiết kế...)

Actual Manufacturing Cost
  = Direct Material Cost
  + Direct Labor Cost
  + Outside Processing Cost
  + Manufacturing Overhead
  + Scrap/Rework Cost

Gross Profit per Order
  = Net Revenue - Actual COGS

Net/Contribution Profit per Order
  = Net Revenue - Actual COGS
  - phí sàn - affiliate - thanh toán
  - chi phí ship shop chịu - chi phí đóng gói bán hàng
  - thuế/chi phí bán hàng khác
```

`Net Revenue` và `Cash Collected` phải là hai chỉ số khác nhau: đơn đã giao/đối soát dùng để tính hiệu quả bán hàng, còn tiền đã thu dùng cho dòng tiền. Chính sách thuế (đã gồm/chưa gồm VAT, thuế theo kênh) phải cấu hình theo phiên bản, không hard-code vào công thức.

Quy tắc phân bổ phải khóa rõ: tổng cost của production order bao gồm cả nguồn lực đã tiêu hao cho hàng lỗi, phế liệu và sửa lại; unit cost thành phẩm bán được = tổng cost cần phân bổ / số lượng thành phẩm tốt, hoặc policy phân bổ theo output đã cấu hình. Không chia cho số lượng kế hoạch nếu số lượng tốt thực tế thấp hơn. `Scrap/Rework Cost` chỉ là phần chi phí tăng thêm chưa nằm trong direct material/labor để tránh cộng trùng.

#### 5.1.3. BOM và nguyên vật liệu

Mỗi mẫu phải có BOM version bất biến theo thời gian, tối thiểu gồm:

- Mã mẫu, phiên bản rập, màu, size, đơn vị đo và ngày hiệu lực.
- Vải chính, vải lót, ren, cườm/đá, nút, dây kéo, chỉ, mếch, tag/nhãn và bao bì; phân biệt vật tư cấu thành giá thành với bao bì/chi phí bán hàng.
- Định mức, tỷ lệ hao hụt dự kiến, công đoạn sử dụng, kho xuất và ghi chú thay thế.
- Override theo size; ví dụ vải S/M/L/XL là 2,8m/3,0m/3,2m/3,4m, không ép một unit cost cho mọi size.
- Giá NVL theo lô, ngày nhập, nhà cung cấp, currency và đơn vị quy đổi; không lấy giá mua mới nhất một cách âm thầm.

MVP có thể dùng moving weighted-average cho NVL; nếu chọn FIFO hoặc lot-specific thì phải khóa thành costing policy và dùng nhất quán. Không được thay đổi lịch sử khi giá vải tăng: lệnh đã chốt giữ snapshot giá và BOM tại thời điểm phát sinh, còn lệnh mới dùng giá/BOM mới.

#### 5.1.4. Công đoạn, nhân công, gia công ngoài và chi phí xưởng

Route sản xuất tối thiểu phải hỗ trợ CẮT → MAY → THÊU/ĐÍNH → ỦI → QC → ĐÓNG GÓI. Mỗi công đoạn cần có loại đơn giá (theo áo/giờ), người thực hiện hoặc nhà thầu, thời gian/công thực tế, trạng thái hoàn thành và chi phí sửa lại.

Chi phí thêu/đính/giặt thuê ngoài phải là dòng chi phí riêng, liên kết được với nhà cung cấp, phiếu gia công và lô sản xuất. Không gộp vào “giá vải” để che mất nguyên nhân biến động.

Chi phí xưởng cần có kỳ phân bổ, số tiền, cost center và basis (sản lượng, giờ công, chi phí nhân công hoặc basis khác). Hệ thống phải lưu cả tổng chi phí kỳ và mẫu số phân bổ; không cho phép sửa một unit overhead cũ làm thay đổi lợi nhuận lịch sử. Nếu công suất thực tế thấp, cần hiển thị phần overhead chưa phân bổ thay vì đẩy toàn bộ vào một số áo bất thường mà không có dấu vết.

#### 5.1.5. Lệnh sản xuất và giá thành thực tế

Production Order phải lưu mẫu, BOM version, size/màu, số lượng kế hoạch và phân bổ S/M/L/XL. Khi phát hành lệnh, hệ thống snapshot BOM và standard cost dự kiến. Khi thực hiện, tạo movement cho NVL xuất kho, công đoạn, gia công ngoài, thành phẩm tốt, hàng lỗi, sửa lại và phế liệu.

Khi đóng lệnh, hệ thống phải so sánh Planned/Standard với Actual theo từng nhóm:

| Nhóm lệch | Ví dụ cần hiển thị |
|---|---|
| Material quantity variance | BOM 3,0m nhưng thực tế xuất 3,2m |
| Material price variance | Giá vải theo lô mới cao hơn giá dự toán |
| Labor/rate variance | Công may thực tế 220k thay vì 200k |
| Outside processing variance | Đính đá phát sinh thêm theo lô |
| Scrap/rework variance | Cắt lỗi, áo sửa lại, vải hỏng |
| Overhead variance | Sản lượng thấp hoặc chi phí xưởng tăng |

Không đưa hàng lỗi vào thành phẩm bán được như hàng tốt. Hàng sửa lại phải giữ chi phí sửa; hàng không thể bán phải ghi nhận scrap/write-off hoặc tồn kho loại lỗi theo policy. Nếu lệnh chưa đủ hóa đơn công đoạn/overhead, chỉ được ghi `ESTIMATED_COST` và phải có cơ chế true-up; không được đưa cost 0 vào COGS.

#### 5.1.6. Costing snapshot và tính lợi nhuận theo đơn

Tại thời điểm chốt đơn/giao hàng, phải lưu snapshot bất biến gồm SKU/size, quantity, unit selling price, voucher/shop discount, tax, shipping subsidy, platform fee, affiliate fee, payment fee, refund/return adjustment và unit COGS. Nếu sau đó BOM hoặc giá NVL đổi, đơn lịch sử không được tính lại theo dữ liệu hiện tại.

Đơn hàng phải cho phép tách ít nhất:

```text
Gross Order Amount
- shop-funded discount / voucher
- refund / return
= Net Sales Amount
- platform commission
- affiliate / payment fee
- shop-funded shipping and selling expense
- applicable tax
- Actual COGS by production batch / finished-goods unit cost
= Profit
```

Đối với hàng hoàn: hàng tốt nhập lại kho theo trạng thái kiểm định; hàng hỏng hoặc không bán lại được ghi giảm tồn kho/chi phí hàng lỗi; refund và phí không được bỏ khỏi profit calculation.

#### 5.1.7. Pricing Simulator và các chốt bảo vệ lợi nhuận

Pricing Simulator là P0 của phần pricing, không chỉ hiển thị giá bán hiện tại. Người dùng chọn mẫu/size, production batch hoặc cost basis, kênh bán, voucher, affiliate, shipping, tax và target margin; hệ thống trả về giá hòa vốn, giá tối thiểu đạt target margin, giá niêm yết đề xuất và cảnh báo lỗ.

Mọi kênh phải có `ChannelFeePolicy` versioned, gồm phí phần trăm, phí cố định, affiliate, payment fee, shipping subsidy, cách tính voucher và cơ sở tính margin. Công thức phải giải được cả trường hợp phí là phần trăm của giá bán và trường hợp chi phí cố định; không dùng một tỷ lệ phí chung cho Shopee, TikTok và cửa hàng.

Các cảnh báo bắt buộc:

- SKU/size không có BOM hoặc actual cost chưa chốt.
- Giá bán sau voucher thấp hơn giá hòa vốn.
- Margin dưới ngưỡng cấu hình theo kênh hoặc theo mẫu.
- Chương trình sale làm phí sàn/affiliate/ship ăn hết contribution margin.
- Actual cost vượt standard cost theo ngưỡng; hao hụt/hàng lỗi tăng bất thường.
- Giá đang dùng là estimated/provisional hoặc dữ liệu chưa đối soát.

#### 5.1.8. Báo cáo và acceptance test tối thiểu

Phải xem được lãi/lỗ theo mẫu, size, màu, production order/batch, kênh bán, đơn hàng và kỳ thời gian; đồng thời drill-down từ profit về cost snapshot, BOM, movement NVL, công đoạn, phí kênh và return.

Các test nghiệp vụ bắt buộc:

1. Một áo có NVL 600k + nhân công 350k + gia công 150k + hao hụt 50k + overhead 100k phải ra actual manufacturing cost 1.250k; không dùng giá nhập thành phẩm.
2. Nếu lệnh kế hoạch 50 áo nhưng chỉ có 46 áo tốt, 2 áo sửa được và 2 áo loại, chi phí phải được phân bổ theo policy trên output tốt/đầu ra tương ứng; không chia 50 để làm COGS thấp giả.
3. BOM size M 3,0m và XL 3,4m phải tạo COGS khác nhau; đổi giá vải chỉ ảnh hưởng lệnh mới hoặc lệnh chưa chốt.
4. Định mức 3,0m nhưng thực tế 3,2m phải hiển thị material quantity variance và tăng cost tương ứng.
5. Xuất 100m, dùng 95m, thừa 3m, hao hụt 2m phải phân bổ đúng 2m vào cost/scrap, không biến mất khỏi sổ.
6. Lệnh 50 áo với mix S/M/L/XL phải xuất đúng NVL theo từng size, ghi đủ thành phẩm tốt/lỗi và đối chiếu tồn kho.
7. Cùng một giá bán nhưng TikTok/Shopee/cửa hàng cho profit khác nhau theo policy phí; voucher/affiliate/ship làm thay đổi margin.
8. Đơn hoàn hỏng phải ghi refund và write-off; đơn hoàn tốt mới được restock; không tạo COGS âm hoặc doanh thu ảo.
9. Không cho chốt lợi nhuận “đẹp” khi unit cost bằng 0, BOM thiếu, production order chưa đóng hoặc fee chưa đối soát; phải gắn cờ dữ liệu chưa hoàn tất.

### Tài chính

```text
finance_categories
finance_transactions(type, amount, business_unit_id, reference_type, reference_id, ...)
cash_accounts
monthly_closings
```

Doanh thu/chi phí phải liên kết được với payment, order, class, payroll hoặc khoản chi thủ công để tính lợi nhuận theo business unit.

## 6. Phân quyền và bảo mật

Vai trò MVP nên tách như sau. Một người có thể có nhiều role, nhưng quyền dữ liệu luôn bị giới hạn theo `business_unit_id`, phòng ban, lớp hoặc bản ghi được phân công:

| Role | Phạm vi và quyền chính |
|---|---|
| `super_admin` | Admin tổng; cấu hình hệ thống, công ty, business unit, tài khoản và mọi module. Có thể cấp/thu hồi quyền; mọi thao tác bắt buộc audit. |
| `system_admin` | Quản trị kỹ thuật, user, role, connector, backup và cấu hình. Mặc định không được xem lương, payment chi tiết hoặc dữ liệu khách hàng nếu không được cấp thêm permission. |
| `director` | Xem báo cáo tổng, doanh thu, chi phí, lợi nhuận; duyệt payroll, tài chính và các nghiệp vụ cần phê duyệt. |
| `hr_staff` | Hồ sơ nhân sự, hợp đồng, phòng ban, chấm công; không tự duyệt lương cuối cùng. |
| `payroll_accountant` | Nhập/kiểm tra công thức, tính bảng lương, tạo payslip nháp; không sửa dữ liệu sau khi đã khóa. |
| `finance_accountant` | Thu/chi, đối soát payment, dòng tiền, báo cáo tài chính; không sửa đề/nội dung và không xem lương nếu không được cấp. |
| `content_manager` | Quản lý môn, khóa học, ngân hàng đề, version và phân công người làm đề. |
| `question_author` | Người làm đề; tạo/sửa câu hỏi được giao, không tự xuất bản và không xem dữ liệu lương/khách hàng. |
| `content_reviewer` | Duyệt chất lượng đề/nội dung; được approve/reject version, không sửa dữ liệu tài chính. |
| `teacher` | Giáo viên nội bộ hoặc giáo viên được thuê; chỉ xem lớp, lịch, học sinh và tài liệu được phân công; có thể nhập nhận xét/điểm nếu được cấp. |
| `teaching_assistant` | Trợ giảng; phạm vi hẹp hơn teacher, chỉ lớp/ca được phân công. |
| `class_coordinator` | Tạo đợt CSCA, xếp lịch, phân giáo viên, quản lý học sinh và chi phí lớp; không duyệt payroll tổng. |
| `customer_service` | Xem khách hàng, gói, trạng thái thanh toán/hạn sử dụng theo business unit; không xem bảng lương và không sửa số tiền đã đối soát. |
| `warehouse_manager` | Sản phẩm, SKU, nhà cung cấp, nhập/xuất/kiểm kê và tồn kho. |
| `order_manager` | Đơn hàng các sàn/website, đóng gói, trạng thái giao hàng và hoàn hàng; không sửa số dư thanh toán gốc. |
| `purchasing_staff` | Nhà cung cấp, đơn mua, hóa đơn đầu vào và đề nghị nhập kho. |
| `production_staff` | BOM, nguyên vật liệu và lệnh sản xuất được phân công. |
| `payment_accountant` | Đối soát và xác nhận đã thanh toán các khoản đã được duyệt; không sửa công thức hoặc số tiền gốc. |
| `employee` | Xem hồ sơ cá nhân, lịch làm việc và payslip của chính mình sau khi được phát hành. |

Giáo viên thuê ngoài dùng cùng role `teacher` nhưng thêm cờ `external_contractor = true`, ngày bắt đầu/kết thúc hợp đồng và danh sách lớp được phân công. Không tạo role `admin_teacher` vì giáo viên không cần quyền quản trị hệ thống.

### Quy tắc xem và nhập lương

| Đối tượng | Được làm gì với lương |
|---|---|
| `hr_staff` | Nhập lương cơ bản, chấm công, phụ cấp, khấu trừ và hồ sơ lương cho từng người trong phạm vi được cấp. |
| `payroll_accountant` | Tính, kiểm tra và tạo bảng lương nháp; không tự phát hành cho nhân viên. |
| `director`/người duyệt | Duyệt bảng lương theo workflow; xem tổng hợp theo phạm vi được cấp. |
| `payment_accountant` | Xác nhận khoản đã thanh toán; không sửa công thức/số tiền gốc. |
| `teacher`, `teaching_assistant`, `question_author`, `employee` | Chỉ xem payslip/settlement của chính mình sau khi trạng thái là `PUBLISHED`; không xem lương người khác, bảng lương tổng hoặc công thức nội bộ không cần thiết. |
| `super_admin` | Quản trị toàn hệ thống; quyền xem dữ liệu lương phải được audit và có thể tách thành permission riêng. |

Lương giáo viên thuê ngoài hoặc người làm đề có thể được nhập theo hợp đồng, lớp/đợt hoặc sản phẩm công việc. Hệ thống lưu `employee_id`/`contractor_id`, `reference_type`, `reference_id`, số tiền, người nhập, người duyệt và thời điểm phát hành. API “lương của tôi” luôn lấy danh tính từ token, không nhận `employee_id` tùy ý từ WPF.

Mỗi request được kiểm tra ba lớp:

1. Authentication: access token còn hạn, refresh token có rotation và revoke.
2. Permission: endpoint yêu cầu permission cụ thể, không chỉ kiểm tra tên role.
3. Data scope: `company_id`, `business_unit_id`, `department_id`, `employee_id` từ server-side policy.

Yêu cầu bắt buộc:

- Password hash bằng Argon2id hoặc BCrypt có cost phù hợp; không lưu mật khẩu rõ.
- Secret API ngoài lưu trong secret store/environment bảo vệ, database chỉ giữ reference.
- HTTPS, CORS giới hạn, rate limit cho login/webhook, giới hạn kích thước file.
- Audit log cho xem/sửa/xóa/duyệt/xuất bản/chạy lại đồng bộ; không ghi secret và dữ liệu thẻ.
- Backup mã hóa, có kiểm tra restore định kỳ; quyền MinIO bucket tối thiểu.
- Khi nhân viên nghỉ, khóa user nhưng vẫn giữ dữ liệu lịch sử.
- Cần test tenant isolation và permission matrix bằng integration test.

## 7. Kế hoạch thực hiện 20 ngày làm việc

### Tuần 1 — Nền tảng chạy được và khóa API contract

**Ngày 1:** Chốt business units, vai trò, nguồn dữ liệu, nguồn chính, phạm vi MVP; tạo issue/backlog và môi trường dev.

**Ngày 2:** Tạo các project trong solution hiện tại; cấu hình .NET 10, nullable, analyzers, appsettings theo environment, Docker PostgreSQL/MinIO.

**Ngày 3:** Tạo Domain/Application/Infrastructure/API skeleton, EF Core DbContext, migration đầu tiên, health checks.

**Ngày 4:** Implement login, password hashing, JWT access token, refresh token rotation, logout/revoke.

**Ngày 5:** Implement RBAC + tenant/business-unit/class/department scope; tạo permission matrix cho các role ở mục 6, seed `super_admin` và tài khoản demo theo role; audit middleware; viết test giáo viên thuê ngoài chỉ thấy lớp được phân công, question author không được xuất bản, nhân viên/giáo viên/người làm đề chỉ xem payslip của mình sau `PUBLISHED`, HR nhập được lương trong scope, và user không đọc chéo business unit.

**Đầu ra tuần 1:** API chạy bằng Docker, WPF gọi được `/health` và `/auth/login`; `API-CONTRACT.md` được ký/chốt; mock connector chạy được.

### Tuần 2 — Tích hợp website và EdTech/CSCA/Interview

**Ngày 6:** Tạo connector abstraction, `integration_runs`, inbox/dead-letter, cursor và idempotency key.

**Ngày 7:** Làm mock website adapter; test pagination, timeout, retry, rate limit, duplicate event, partial failure.

**Ngày 8:** Kết nối API thật đầu tiên (read-only): courses, questions, customers, subscriptions, payments; mapping và validation.

**Ngày 9:** Làm content version/publication, màn hình EdTech trong WPF; phân quyền content editor/teacher/customer service.

**Ngày 10:** Làm CSCA class/student/staff/schedule và Interview customer; ghi doanh thu/chi phí theo business unit; chạy sync end-to-end.

**Đầu ra tuần 2:** Một lần đồng bộ thật có log/checkpoint; chạy lại không tạo bản ghi trùng; WPF xem được dữ liệu EdTech và lớp.

### Tuần 3 — HR/Payroll và Fashion/Inventory

**Ngày 11:** Employee, department, attendance import Excel, validation và báo cáo lỗi từng dòng.

**Ngày 12:** Payroll MVP: policy version, tính lương, adjustment, payslip snapshot; chưa mở rộng các trường hợp pháp lý ngoài phạm vi đã duyệt.

**Ngày 13:** Payroll workflow DRAFT → CALCULATED → REVIEWING → APPROVED → PAID → PUBLISHED; quyền xem phiếu lương cá nhân.

**Ngày 14:** Fashion foundation theo đúng mô hình áo dài tự sản xuất: material/material lot, nhập NVL/phụ liệu, BOM version theo mẫu + size, costing policy và inventory movement cho cả NVL/WIP/thành phẩm. `PurchaseReceipt` trong flow chính chỉ dành cho NVL/gia công; không coi giá nhập xưởng là COGS của áo tự may.

**Ngày 15 (đã hoàn tất):** Production order và actual costing: snapshot BOM/standard cost, xuất NVL thực tế, công đoạn, gia công ngoài, hao hụt/hàng lỗi, nhập thành phẩm theo unit cost thực tế; sales order đa kênh, snapshot COGS/phí/lợi nhuận, tồn `on-hand -> reserved -> delivered`, giao hàng, return/inspection, nhập lại hàng đủ điều kiện, hóa đơn/phiếu bán lẻ snapshot và import order adapter JSON theo SKU hoặc `productVariantId`. Có kiểm soát cost 0/provisional, SKU trùng, duplicate source order và lifecycle giao hàng → đổi trả → chứng từ; nhãn thao tác Fashion được Việt hóa.

**Đầu ra tuần 3:** Flow chấm công → lương → duyệt → phát hành và flow NVL → BOM → sản xuất → actual cost → thành phẩm → bán/hoàn → cập nhật tồn kho chạy được; mỗi đơn có cost snapshot truy vết được.

### Tuần 4 — Tài chính, WPF hoàn thiện, hardening và nghiệm thu

**Ngày 16 (đã hoàn tất):** Finance transactions, cash accounts, channel fee policy, pricing simulator và liên kết reference; báo cáo thu/chi/dòng tiền/lợi nhuận theo business unit, mẫu, size, batch và kênh. Chỉ đưa vào báo cáo lợi nhuận các cost/fee đã có snapshot hoặc gắn cờ provisional rõ ràng.

**Ngày 17 (đã hoàn tất):** WPF shell có menu tự ẩn theo permission, overlay trạng thái đang tải, trạng thái lỗi/trống thân thiện, trang lịch sử đồng bộ và Dead-Letter Queue có retry, xuất CSV lịch sử đồng bộ/tồn kho, import chấm công và thông báo lỗi tiếng Việt. `ApiClient` có refresh access token tự động khi gặp 401 và chuyển lỗi mất mạng thành trạng thái có thể hiển thị cho người dùng.

**Ngày 18 (đã hoàn tất):** Integration tests PostgreSQL/Testcontainers, security hardening tests, backup/restore PostgreSQL thật và tài liệu script backup/restore; đã xác minh 2/2 PostgreSQL Testcontainers, 45/45 integration, 78/78 unit và build solution.

**Ngày 19 (Đã hoàn tất phần engineering):** UAT checklist, migration review, production secret/config hardening, fail-fast migration/seed, deploy/runbook và hướng dẫn người dùng. UAT live/đối soát website vẫn là cổng nghiệm thu có điều kiện vì chưa có credential/API Payments hợp lệ.

**Ngày 20 (Đã chuẩn bị artifact nghiệm thu):** Demo/evidence, mẫu đối soát nguồn ngoài không bịa số liệu, danh sách giới hạn MVP, hướng dẫn release `v0.1.0` và backlog tháng 2. Việc ký nghiệm thu live và tạo git tag chính thức chỉ thực hiện sau khi owner xác nhận evidence môi trường đích.

**Đầu ra cuối tháng:** bản cài WPF, API Docker, migration, tài liệu API/integration/security, test report, backup đã restore thử, checklist vận hành.

## 8. Tiêu chí nghiệm thu bắt buộc

### API và đồng bộ

- API website xác thực đúng; token hết hạn/thu hồi bị từ chối.
- Đồng bộ 2 lần cùng checkpoint không tạo bản ghi hoặc payment/order trùng.
- Webhook sai chữ ký bị từ chối; event hợp lệ được lưu inbox trước khi xử lý.
- API ngoài timeout/429/5xx có retry giới hạn và hiển thị trạng thái; lỗi dữ liệu có dead-letter và nút chạy lại.
- Có thể đối soát số lượng và tổng tiền giữa nguồn ngoài và backoffice theo một khoảng thời gian.

### Phân quyền

- Content editor không xem bảng lương; customer service không sửa payment; employee chỉ xem phiếu lương của mình.
- User không thể thay `company_id`, `business_unit_id` hoặc `employee_id` trong request để đọc dữ liệu ngoài scope.
- Mọi thao tác duyệt/xuất bản/điều chỉnh đều có audit log.

### Kho và tài chính

- Mọi nhập/xuất/hoàn tạo inventory movement; số dư khớp sổ giao dịch.
- Không thể trừ tồn hai lần khi order/webhook lặp.
- Báo cáo thu/chi và lợi nhuận có bộ lọc business unit, kỳ thời gian và nguồn tham chiếu.

### Costing và lợi nhuận áo dài tự sản xuất

- Có thể đi từ phiếu nhập NVL theo lô → BOM version theo mẫu/size → production order → xuất NVL/công đoạn/gia công → thành phẩm tốt/lỗi → actual manufacturing cost.
- Giá thành được cấu thành tối thiểu bởi NVL trực tiếp, nhân công, gia công ngoài, hao hụt/sửa lại và overhead xưởng phân bổ; từng khoản có nguồn tham chiếu và không bị tính trùng.
- Standard/estimated/actual cost được phân biệt; lệnh đã chốt và đơn đã giao giữ snapshot lịch sử khi giá NVL, BOM, phí kênh hoặc chính sách thuế thay đổi.
- Actual cost phải đối chiếu được với tồn kho thành phẩm và COGS của đơn; cost 0, BOM thiếu, lệnh chưa đóng hoặc phí chưa đối soát không được âm thầm biến thành lợi nhuận.
- Báo cáo hiển thị được gross sales, net sales, COGS, phí sàn/affiliate/payment, ship shop chịu, thuế, refund/return và profit/margin theo mẫu, size, batch, kênh và đơn.
- Pricing Simulator trả về break-even/target-margin price và cảnh báo chương trình voucher/affiliate/ship khiến đơn bị lỗ.
- Có test variance định mức-vs-thực tế, hao hụt/hàng lỗi, mix size, hoàn hàng tốt/hỏng, fee khác nhau theo kênh và duplicate order.

### Vận hành

- `dotnet test` pass; integration test dùng database riêng.
- Backup tạo được và restore thành công trên môi trường test.
- Health endpoint phân biệt API sống và database/connector lỗi.
- Không có secret trong source, log, file publish hoặc WPF binary.

## 9. Kiểm thử và chất lượng

```text
Unit: payroll, permissions, mapping, inventory calculation
Integration: auth/RBAC, tenant isolation, EF migrations, webhook inbox, sync retry
Contract: JSON schema và version của từng connector
E2E: WPF login → API → database → sync → báo cáo
Resilience: timeout, 429, 5xx, duplicate event, mất mạng, restart job
Security: secret scan, file upload, rate limit, audit redaction
```

Mỗi connector có fixture JSON được che dữ liệu thật. Không dùng database production cho test.

## 10. Rủi ro và quyết định cần chốt ngay ngày 1

1. Website chưa có API admin: phải cung cấp endpoint/credential hoặc chấp nhận mock + import file trong MVP; không chuyển sang scrape.
2. API sàn yêu cầu xét duyệt: triển khai adapter ở trạng thái disabled, dùng import chuẩn hóa và bật sau khi được cấp quyền.
3. Quy tắc lương/thuế chưa được duyệt: chốt policy bằng văn bản, lưu version, không coi tỷ lệ trong code là cố định.
4. Dữ liệu cũ thiếu ID/updated_at: lập mapping table, chạy initial import có dry-run và báo cáo bản ghi không map được.
5. Giá thành áo dài tự may bị hiểu thành giá nhập xưởng: khóa `sourcing_type` MAKE/BUY, chặn purchase receipt thành phẩm trong flow MAKE và bắt buộc actual-cost snapshot trước khi ghi nhận COGS/lợi nhuận.
6. BOM, hao hụt, công đoạn hoặc overhead chưa đủ dữ liệu: cho phép estimated cost có cờ và quy trình true-up, nhưng không hiển thị như lợi nhuận đã chốt; dashboard phải tách provisional khỏi actual.
7. Phí sàn/voucher/affiliate/ship/thuế thay đổi theo kênh: dùng `ChannelFeePolicy` versioned và snapshot theo đơn, không hard-code một tỷ lệ phí chung.
8. Một tháng không đủ cho toàn bộ phạm vi: ưu tiên flow NVL → BOM → sản xuất → actual cost → COGS → đối soát trước các connector sàn; mọi tính năng không đạt tiêu chí nghiệm thu chuyển backlog, không kéo dài bằng cách bỏ test bảo mật.

## 11. Danh sách thông tin chủ dự án cần cung cấp

- Base URL và tài liệu API của website EdTech/Interview.
- Cơ chế auth: OAuth2/API key/HMAC webhook; tài khoản sandbox và production tách riêng.
- Danh sách endpoint, JSON mẫu, timezone, currency, pagination, `updated_at`, mã lỗi.
- Quyền API của Shopee/TikTok/Facebook và credential sandbox nếu muốn tích hợp trong tháng đầu.
- Danh sách công ty/business unit, vai trò, phòng ban và người duyệt lương.
- Quy tắc lương, kỳ lương, mẫu phiếu lương, chính sách lưu file và thời gian lưu dữ liệu.
- Dữ liệu mẫu đã ẩn thông tin nhạy cảm để viết fixture/test.

## Kết luận

Đây là kế hoạch MVP 20 ngày làm việc. Kiến trúc cho phép nối nhiều website/sàn nhưng mỗi connector chỉ được bật sau khi có API contract và credential hợp lệ. Trong tháng đầu, ưu tiên dữ liệu đúng, truy vết được, không trùng và phân quyền an toàn; các tích hợp hoặc nghiệp vụ chưa đủ thông tin sẽ chạy bằng mock/import có kiểm soát và được đưa vào backlog, không dùng giải pháp cào HTML hay kết nối database trực tiếp.

## 12. Kế hoạch tăng tốc hệ thống — đã triển khai ngày 25/08/2026

### Nguyên nhân đã xác nhận

- Sau đăng nhập, Desktop tải tuần tự gần như toàn bộ phân hệ dù người dùng chưa mở đến.
- Mỗi lần chuyển menu lại gọi API lại, không có cache theo phiên.
- Luồng lấy SKU mua ngoài gọi chi tiết từng sản phẩm (N+1 HTTP request).
- Các màn hình kho lọc theo công ty, business unit, kho và thời gian nhưng schema thiếu index tổng hợp tương ứng.
- Database Railway thiếu migration Cloudinary nên truy vấn `documents.entity_id` lỗi; đây là lỗi schema, không phải lỗi dữ liệu.

### Thay đổi đã làm

1. Đăng nhập chỉ tải dashboard; các màn hình còn lại lazy-load khi mở lần đầu và không gọi lại khi chuyển tab.
2. Các tab Sản phẩm, SKU, Nhà cung cấp, Phiếu nhập, Tồn kho, Nhật ký kho và Sản xuất tải độc lập theo tab.
3. Thêm cache chống gọi trùng, bộ đếm trạng thái bận và debounce 280 ms cho ô tìm kiếm.
4. Gom danh sách SKU BUY thành một endpoint/query; loại bỏ N+1 request tuần tự.
5. Chạy song song các truy vấn độc lập ở dashboard và dữ liệu sản xuất.
6. Đồng bộ timeout Desktop với `ApiSettings:TimeoutSeconds` thay vì hard-code 15 giây.
7. Áp dụng migration Cloudinary và index cho sản phẩm, SKU, nhà cung cấp, phiếu nhập, tồn kho và nhật ký giao dịch.
8. API Development/Production dừng khởi động khi không kết nối được database hoặc migration lỗi, tránh chạy với schema nửa cũ rồi báo lỗi đăng nhập mơ hồ.

### Kiểm tra và vận hành

- PostgreSQL Railway đã áp dụng: `20260825150000_AddCloudinaryReceiptAttachments`, `20260825153000_AddFashionPerformanceIndexes`, `20260825154739_ReconcilePending`.
- Đã xác nhận 4 cột Cloudinary và 7 index hiệu năng tồn tại trên database.
- `dotnet ef migrations has-pending-model-changes`: không còn thay đổi chưa tạo migration.
- Unit test: 100/100; API và Desktop build không có lỗi.
- Các connection string/secret chỉ dùng qua biến môi trường hoặc secret manager, không commit vào source.
