# MOLY Internal Management — API Contract Specification

Phiên bản: 1.1  
Môi trường: .NET 10 Web API  
Định dạng dữ liệu: JSON (UTF-8)  
Timezone: Mặc định lưu trữ UTC, hiển thị `Asia/Ho_Chi_Minh` (GMT+7)  
Tiền tệ: `VND` (Decimal)  

---

## 1. Chuẩn RESTful & Header chung

Mọi request từ WPF Desktop hoặc Client ứng dụng đến Backend API phải tuân thủ chuẩn sau:

### Request Headers
| Header | Kiểu | Bắt buộc | Mô tả |
|---|---|---|---|
| `Authorization` | `string` | Có (trừ login/health) | `Bearer <JWT_ACCESS_TOKEN>` |
| `X-Correlation-ID` | `string (GUID)` | Không | Tracking ID cho mỗi request, nếu không gửi server tự sinh |
| `X-Company-ID` | `string` | Tùy chọn | Được server xác thực khớp với token claim |
| `X-Business-Unit-ID` | `string` | Tùy chọn | Được server xác thực khớp với business unit trong token; không dùng để tự nâng quyền |
| `X-Integration-Key` | `string` | Chỉ service-to-service | Key ID của connector; không dùng thay cho authorization của WPF/user |
| `X-Event-Timestamp` | `ISO-8601` | Chỉ webhook/integration | Timestamp dùng để chống replay |
| `X-Signature` | `string` | Chỉ webhook/integration | HMAC SHA-256 trên timestamp và raw body |
| `Idempotency-Key` | `string` | Bắt buộc với mutation integration | Khóa ổn định để retry không tạo bản ghi trùng |
| `Content-Type` | `string` | Có với POST/PUT | `application/json; charset=utf-8` |

### Chuẩn Định dạng Response
Toàn bộ phản hồi chuẩn hóa theo wrapper:

```json
{
  "success": true,
  "data": { ... },
  "message": "Thao tác thành công",
  "errors": null,
  "timestamp": "2026-08-18T03:30:00Z"
}
```

Trường hợp lỗi tuân theo chuẩn RFC 7807 (`ProblemDetails`):
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "Validation Error",
  "status": 400,
  "detail": "Một hoặc nhiều trường dữ liệu không hợp lệ.",
  "instance": "/api/v1/courses",
  "errors": {
    "Title": ["Tiêu đề khóa học không được để trống."]
  },
  "correlationId": "8f3b2a1c-..."
}
```

### Phân trang (Pagination Standard)
Chuẩn phân trang query param: `pageIndex` (1-based), `pageSize` (mặc định 20, max 100).

Response phân trang:
```json
{
  "items": [ ... ],
  "pageIndex": 1,
  "pageSize": 20,
  "totalCount": 145,
  "totalPages": 8,
  "hasPreviousPage": false,
  "hasNextPage": true
}
```

---

## 2. Danh mục API Endpoints cốt lõi

### 2.0 Phạm vi hai mảng nghiệp vụ

Hệ thống tách dữ liệu nghiệp vụ thành hai mảng độc lập:

| Mảng | Mã chuẩn | Business Unit thuộc mảng | Segment dùng trên API nội bộ |
|---|---|---|---|
| Công nghệ - Giáo dục | `TECHNOLOGY_EDUCATION` | `EDTECH`, `CSCA`, `INTERVIEW` | `cong-nghe-giao-duc` |
| Thời trang | `FASHION` | `FASHION` | `thoi-trang` |

Các API nhân sự, chấm công và kỳ lương nhận `businessUnitId` hoặc `businessSegment` (alias `segment`) để lọc. Giá trị chuẩn nên dùng cho `businessSegment` là `TECHNOLOGY_EDUCATION` hoặc `FASHION`. Dữ liệu khách hàng/tài liệu nội bộ Công nghệ - Giáo dục được lưu tập trung tại Business Unit `EDTECH`; tài khoản thuộc `EDTECH`, `CSCA` hoặc `INTERVIEW` cùng truy cập kho chung này. Kho Thời trang chỉ thuộc `FASHION`.

### 2.1 Authentication & Profile (`/api/v1/auth`)
- `POST /api/v1/auth/login`: Đăng nhập với username/password, nhận access token & refresh token.
- `POST /api/v1/auth/refresh-token`: Cấp lại access token qua refresh token (Rotation).
- `POST /api/v1/auth/logout`: Thu hồi refresh token hiện tại.
- `GET /api/v1/auth/me`: Thông tin người dùng hiện tại, quyền (permissions) và danh sách Business Units được cấp phép.
- `POST /api/v1/auth/change-password`: Đổi mật khẩu người dùng.

### 2.2 EdTech & Khóa học (`/api/v1/edtech`)
- `GET /api/v1/edtech/courses`: Danh sách khóa học (Filter theo business_unit, status).
- `GET /api/v1/edtech/courses/{id}`: Chi tiết khóa học, module, bài học.
- `GET /api/v1/edtech/questions`: Ngân hàng câu hỏi/đề thi.
- `POST /api/v1/edtech/questions`: Tạo câu hỏi mới (version 1.0).
- `PUT /api/v1/edtech/questions/{id}/version`: Tạo phiên bản câu hỏi mới.
- `POST /api/v1/edtech/questions/{versionId}/publish`: Xuất bản phiên bản câu hỏi.
- `GET /api/v1/edtech/customers`: Danh sách học viên/khách hàng EdTech.
- `GET /api/v1/edtech/subscriptions`: Danh sách gói đăng ký và hạn dùng.
- `GET /api/v1/edtech/payments`: Lịch sử thanh toán của khách hàng.

### 2.3 CSCA Online & Interview (`/api/v1/csca`, `/api/v1/interview`)
- `GET /api/v1/csca/classes`: Danh sách lớp học CSCA (Khóa, đợt, lịch học).
- `POST /api/v1/csca/classes`: Tạo lớp học mới.
- `GET /api/v1/csca/classes/{id}/students`: Danh sách học viên và tình trạng đóng học phí.
- `POST /api/v1/csca/classes/{id}/students`: Thêm học viên vào lớp.
- `GET /api/v1/csca/classes/{id}/staff`: Phân công giáo viên, trợ giảng và mức thù lao.
- `GET /api/v1/csca/classes/{id}/financial-summary`: Báo cáo doanh thu, chi phí thù lao, lợi nhuận lớp.
- `GET /api/v1/interview/customers`: Danh sách khách hàng dịch vụ Mock Interview.
- `POST /api/v1/interview/customers`: Tiếp nhận khách hàng & gói phỏng vấn.
- `GET /api/v1/csca/online/materials`: Tài liệu đọc từ CSCA-MOLI.STUDIO.
- `GET /api/v1/csca/online/vocabulary`: Từ vựng đọc từ CSCA-MOLI.STUDIO.
- `GET /api/v1/csca/online/posts`: Bài viết cộng đồng đọc từ CSCA-MOLI.STUDIO.

### 2.4 Nhân sự, CV, chấm công & bảng lương (`/api/v1/employees`, `/api/v1/attendance`, `/api/v1/payroll`)

- `GET /api/v1/employees`: Danh sách nhân viên. Hỗ trợ `businessUnitId`, `businessSegment`/`segment`, `departmentId`, `status`, `search`, `pageIndex`, `pageSize`.
- `GET /api/v1/employees/{id}`: Chi tiết nhân viên, gồm số bản ghi chấm công và số phiếu lương.
- `POST /api/v1/employees`: Tạo hồ sơ nhân viên mới.
- `PUT /api/v1/employees/{id}`: Cập nhật hồ sơ nhân viên.
- `DELETE /api/v1/employees/{id}`: Xóa mềm hồ sơ nhân viên.
- `GET /api/v1/attendance`: Danh sách chấm công; hỗ trợ lọc theo mảng/Business Unit.
- `GET /api/v1/attendance/summary`: Tổng hợp chấm công; hỗ trợ lọc theo mảng/Business Unit.
- `POST /api/v1/attendance`: Ghi nhận chấm công.
- `POST /api/v1/attendance/import`: Nhập bảng chấm công.
- `GET /api/v1/payroll/periods`: Danh sách kỳ tính lương.
- `GET /api/v1/payroll/component-types`: Danh mục khoản lương chuẩn.
- `POST /api/v1/payroll/periods`: Tạo kỳ lương; `businessUnitId` xác định phạm vi nhân sự được tính.
- `POST /api/v1/payroll/periods/{id}/calculate`: Kích hoạt tính lương tự động theo công thức.
- `GET /api/v1/payroll/periods/{id}/payslips`: Danh sách phiếu lương trong kỳ.
- `POST /api/v1/payroll/periods/{id}/adjustments`: Thêm khoản thu nhập/khấu trừ cho một nhân viên trong kỳ.
- `DELETE /api/v1/payroll/adjustments/{id}`: Xóa khoản điều chỉnh khi kỳ lương còn cho phép sửa.
- `POST /api/v1/payroll/periods/{id}/submit-review`: Gửi duyệt kỳ lương.
- `POST /api/v1/payroll/periods/{id}/approve`: Duyệt bảng lương (Trạng thái REVIEWING -> APPROVED).
- `POST /api/v1/payroll/periods/{id}/mark-paid`: Xác nhận đã chi lương.
- `POST /api/v1/payroll/periods/{id}/publish`: Phát hành phiếu lương cho nhân viên.
- `GET /api/v1/payroll/my-payslips`: Nhân viên xem danh sách phiếu lương cá nhân đã phát hành.
- `GET /api/v1/payroll/my-payslips/{periodId}`: Xem phiếu lương cá nhân theo kỳ.

Trường mở rộng trong hồ sơ nhân viên:

| Trường | Ý nghĩa |
|---|---|
| `employmentType` | Enum: `0 = FULL_TIME`, `1 = PART_TIME` |
| `partTimeCalculationMethod` | Với part-time: `0 = HOURLY`, `1 = SHIFT` |
| `partTimeUnitRate` | Đơn giá theo giờ hoặc theo ca; bắt buộc lớn hơn 0 với part-time |
| `cvUrlOrPath` | URL/đường dẫn CV |
| `professionalSummary` | Tóm tắt chuyên môn |
| `skills` | Kỹ năng |
| `experience` | Kinh nghiệm |

API hiện chỉ cấu hình JSON camelCase và chưa đăng ký bộ chuyển enum thành chuỗi, vì vậy request/response JSON của hai trường enum trên dùng giá trị số. Tên `FULL_TIME`, `PART_TIME`, `HOURLY`, `SHIFT` là mã miền nghiệp vụ để UI hiển thị/ánh xạ.

Mã khoản lương từ `GET /api/v1/payroll/component-types`: `ALLOWANCE`, `KPI_BONUS`, `BONUS`, `OVERTIME` thuộc thu nhập; `HEALTH_INSURANCE`, `DEDUCTION` thuộc khấu trừ. BHYT hiện được nhập bằng khoản điều chỉnh `HEALTH_INSURANCE`, chưa tự tính theo phần trăm.

Công thức hiện hành:

- Full-time: lương công = `baseSalary × actualWorkDays / 22`; nếu kỳ hoàn toàn chưa có chấm công, hành vi tương thích hiện tại mặc định 22 ngày.
- Part-time theo giờ: `partTimeUnitRate × actualWorkHours`; theo ca: `partTimeUnitRate × actualShifts`. Không có chấm công thì số giờ/ca bằng 0.
- Tổng thu nhập = lương công + trợ cấp + KPI + thưởng khác + làm thêm giờ.
- Tổng khấu trừ = BHYT + khấu trừ khác; thực lĩnh = `max(0, tổng thu nhập - tổng khấu trừ)`.

### 2.5 Khách hàng & kho thông tin nội bộ (`/api/v1/noi-bo`)

Hai nhóm endpoint có cùng cấu trúc CRUD, nhưng dữ liệu được cô lập theo `{segment}`:

- `GET/POST /api/v1/noi-bo/{segment}/khach-hang`
- `GET/PUT/DELETE /api/v1/noi-bo/{segment}/khach-hang/{id}`
- `GET/POST /api/v1/noi-bo/{segment}/tai-lieu`
- `GET/PUT/DELETE /api/v1/noi-bo/{segment}/tai-lieu/{id}`

Quyền đọc/ghi tương ứng là `Permissions.InternalCustomers.View`, `Permissions.InternalCustomers.Manage`, `Permissions.InternalResources.View`, `Permissions.InternalResources.Manage`.

`{segment}` chỉ dùng `cong-nghe-giao-duc` hoặc `thoi-trang`. Danh sách khách hàng hỗ trợ `search`, `source`, `status`, `pageIndex`, `pageSize`; trạng thái gồm `LEAD`, `ACTIVE`, `INACTIVE`, `ARCHIVED`. Danh sách tài liệu hỗ trợ `search`, `resourceType`, `status`, `tag`, `pageIndex`, `pageSize`; trạng thái gồm `DRAFT`, `ACTIVE`, `ARCHIVED`.

Loại tài liệu hợp lệ:

- Công nghệ - Giáo dục: `EXAM`, `DOCUMENT`.
- Thời trang: `PLAN`, `DESIGN_SAMPLE`, `DOCUMENT`.

API tài liệu chỉ lưu metadata: `code`, `title`, `resourceType`, `storageUri`, `fileName`, `contentType`, `fileSizeBytes`, `checksumSha256`, `version`, `status`, `tags`, `metadataJson`, `notes`. Không gửi binary, base64 hoặc `data:` URI vào `storageUri`. Xóa tài liệu qua API chỉ xóa mềm metadata; tệp ở kho ngoài không bị xóa.

### 2.6 Fashion & Quản lý Kho (`/api/v1/fashion`, `/api/v1/inventory`)
- `GET /api/v1/fashion/products`: Danh sách sản phẩm và các biến thể (SKU, size, color).
- `POST /api/v1/fashion/products`: Tạo mới sản phẩm/biến thể.
- `GET /api/v1/inventory/balances`: Báo cáo số dư tồn kho tức thời (on-hand, reserved).
- `GET /api/v1/inventory/movements`: Sổ giao dịch kho bất biến (Lịch sử nhập, xuất, hoàn, điều chỉnh).
- `POST /api/v1/inventory/receipts`: Lập phiếu nhập kho từ nhà cung cấp.
- `GET/POST /api/v1/manufacturing/materials`: Xem/tạo nguyên vật liệu; `POST /receive` nhập theo lô và giá vốn.
- `POST/GET /api/v1/manufacturing/boms`: Tạo/xem BOM theo mẫu, size, phiên bản và định mức hao hụt.
- `POST /api/v1/manufacturing/production-orders`: Tạo lệnh sản xuất, tính standard cost theo BOM.
- `POST /api/v1/manufacturing/production-orders/{id}/complete`: Chốt actual cost gồm NVL, nhân công, gia công ngoài, overhead, hao hụt/sửa hàng lỗi và nhập thành phẩm.
- `POST /api/v1/pricing/policies`: Tạo chính sách phí versioned theo Store/Shopee/TikTok; có `effectiveFrom`/`effectiveTo`, không sửa tỷ lệ của kỳ cũ.
- `POST /api/v1/pricing/simulate`: Mô phỏng giá hòa vốn, giá đạt biên lợi nhuận mục tiêu và lợi nhuận sau phí/voucher/ship/thuế/quảng cáo/đóng gói.
- `POST /api/v1/orders`: Tạo đơn hàng thủ công với cost snapshot bắt buộc là giá vốn thực tế; nhận thêm `advertisingCost`, `packagingCost`, `otherSellingExpense` và trả các mức gross/after-fee/after-marketing/net profit.
- `POST /api/v1/orders/import`: Nhập đơn JSON từ Store/Shopee/TikTok theo SKU hoặc `productVariantId`, có chống trùng source order.
- `GET /api/v1/orders/{id}/cost`: Xem snapshot COGS, phí, refund và lợi nhuận của đơn.
- `POST /api/v1/orders/{id}/deliver`: Giao hàng, chuyển tồn `reserved -> delivered` và ghi sổ giao dịch xuất kho.
- `POST /api/v1/orders/returns`: Tạo phiếu đổi trả chờ kiểm tra.
- `POST /api/v1/orders/returns/{returnId}/inspect`: Duyệt/từ chối; hàng đủ điều kiện được nhập lại và snapshot refund/COGS/lợi nhuận được cập nhật.
- `POST /api/v1/orders/{id}/documents`: Phát hành hóa đơn hoặc phiếu bán lẻ snapshot.

### 2.7 Tài chính, dòng tiền & bảng tổng công ty (`/api/v1/finance`)
- `GET /api/v1/finance/transactions`: Danh sách phiếu thu/chi.
- `POST /api/v1/finance/transactions`: Lập phiếu thu / chi gắn với Business Unit & chứng từ gốc.
- `GET /api/v1/finance/cash-flow`: Báo cáo dòng tiền, nhận `from`, `to`, `businessUnitId`.
- `GET /api/v1/finance/profit-report`: Báo cáo lợi nhuận, nhận `from`, `to`, `businessUnitId`, `channel`, `productId`, `productVariantId`, `size`, `productionBatchCode`.
- `GET /api/v1/finance/overview`: Bảng tổng hai mảng và tổng toàn công ty, nhận `from`, `to`.

Ba endpoint báo cáo trên yêu cầu `Permissions.FinanceReports.View`. `overview` trả hai phần tử `areas` (`TECHNOLOGY_EDUCATION`, `FASHION`), `companyTotal` (`COMPANY_TOTAL`) và `unclassifiedCashFlow`. Thu, chi và dòng tiền ròng lấy từ `FinanceTransaction`. Lợi nhuận Công nghệ - Giáo dục lấy bản phân bổ mới nhất theo chứng từ; lợi nhuận Thời trang lấy snapshot mới nhất của từng đơn hàng. Chỉ snapshot Fashion có `costStatus = Actual` được tính vào `confirmedProfit`; phần còn lại nằm trong `provisionalProfit`. `companyTotal` chỉ cộng hai mảng; giao dịch không gán đúng Business Unit được trả riêng trong `unclassifiedCashFlow` để xử lý, không âm thầm cộng vào tổng.

### 2.8 Hệ thống, Đồng bộ & Health Checks (`/api/v1/system`, `/health`)
- `GET /health`: Health status tổng quan (PostgreSQL, MinIO, Connectors).
- `GET /health/live`: Liveness probe (API process running).
- `GET /health/ready`: Readiness probe (DB connection available).
- `GET /api/v1/system/sync/runs`: Lịch sử các đợt đồng bộ dữ liệu từ website ngoài.
- `POST /api/v1/system/sync/trigger`: Kích hoạt đồng bộ thủ công cho một connector.
- `GET /api/v1/system/sync/dead-letters`: Danh sách các bản ghi đồng bộ bị lỗi cần xử lý.
- `POST /api/v1/system/sync/dead-letters/{id}/retry`: Thử lại bản ghi dead-letter.
- `POST /api/v1/system/webhooks/{sourceSystem}`: Webhook inbox tiếp nhận sự kiện từ website/sàn.

### 2.9 CSCA Course LMS — data integration contract

Source system code của LMS là `CSCA_COURSE_LMS`. Contract chi tiết nằm tại [CSCA_COURSE_LMS_DATA_CONTRACT.md](CSCA_COURSE_LMS_DATA_CONTRACT.md).

#### Management nhận từ LMS

- `POST /api/v1/system/webhooks/CSCA_COURSE_LMS`: nhận event account, enrollment, progress, assignment, quiz, attendance và certificate.
- `GET /api/v1/system/sync/runs?sourceSystem=CSCA_COURSE_LMS`: xem checkpoint và kết quả pull.
- `GET /api/v1/system/sync/dead-letters?sourceSystem=CSCA_COURSE_LMS`: xem lỗi cần manual review.
- `POST /api/v1/system/sync/dead-letters/{id}/retry`: chạy lại event lỗi sau khi đã xử lý nguyên nhân.

#### Management gọi LMS

Các endpoint dưới đây là contract phía hệ thống ngoài; InternalManagement gọi bằng connector, không expose như API cho WPF:

```text
POST  /api/integrations/v1/students/provision
PATCH /api/integrations/v1/students/{externalStudentId}/access
GET   /api/integrations/v1/catalog/courses
GET   /api/integrations/v1/exports/users
GET   /api/integrations/v1/exports/enrollments
GET   /api/integrations/v1/exports/learning-records
```

#### Quy tắc quyền LMS

- `Pending` hoặc `Partial`: không cấp access grant.
- `Paid` và `PaidAmount >= TuitionFee`: cấp access grant theo course mapping.
- `Refunded`, `Cancelled`, `Failed`: revoke grant, giữ lại account/progress/audit.
- Tài khoản có thể được provision ở trạng thái `PendingPayment` nhưng không có activation credential hoạt động.
- LMS phải kiểm tra access grant ở backend cho mọi API học tập, video, live class, assignment và certificate; route guard frontend không phải cơ chế bảo mật.

#### Response và lỗi đặc thù

| Mã | Ý nghĩa | Xử lý |
|---|---|---|
| `200`/`201` | Provision hoặc access đã xử lý | Lưu mapping và correlation ID |
| `202` | Event đã vào inbox | Không xử lý lặp khi retry |
| `409` | External ID/email đã liên kết với entity khác | Đưa manual review, không tự gộp |
| `422` | Thiếu email, course mapping hoặc policy không hợp lệ | Dead-letter dữ liệu, không retry vô hạn |
| `401`/`403` | Token, key hoặc HMAC không hợp lệ | Đánh dấu connector unhealthy |
| `429`/`5xx` | Lỗi tạm thời | Retry backoff tối đa 3 lần |

Mọi mutation integration phải có `Idempotency-Key`, `X-Correlation-ID`, audit log và source ID. Không gửi password hash, JWT, refresh token hoặc signed playback URL.

#### Vận hành mapping và outbox từ InternalManagement

| Endpoint | Quyền | Mục đích |
|---|---|---|
| `GET /api/v1/lms-integration/overview` | `Permissions.SystemSync.View` | Số khóa học mapping, grant đang hoạt động và hàng chờ LMS theo tenant |
| `GET /api/v1/lms-integration/course-mappings` | `Permissions.SystemSync.View` | Danh sách course nội bộ, gồm cả course chưa mapping |
| `PUT /api/v1/lms-integration/course-mappings/{courseId}` | `Permissions.SystemSync.Trigger` | Lưu mapping; chỉ `EnableAccess=true` với LMS course ID hoặc slug mới đưa mapping thành `Success` |
| `GET /api/v1/lms-integration/outbox` | `Permissions.SystemSync.View` | Xem metadata outbox đã lọc, không gồm payload/idempotency key |
| `POST /api/v1/lms-integration/outbox/{outboxId}/retry` | `Permissions.SystemSync.Trigger` | Đưa failed/dead-letter về hàng chờ để worker hoặc dispatch xử lý lại |
| `POST /api/v1/lms-integration/outbox/dispatch` | `Permissions.SystemSync.Trigger` | Gửi batch hữu hạn; dùng khi đã xác minh HMAC/secrets/mapping |

Desktop hiển thị các API trên ở mục **Quyền học CSCA LMS**. Nút dispatch luôn có hộp xác nhận; đây là hành động gọi hệ thống LMS bên ngoài.
