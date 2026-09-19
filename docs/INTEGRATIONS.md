# MOLI Internal Management — Tài liệu Tích hợp & Đồng bộ (Integrations)

Phiên bản: 1.1  
Mục tiêu: Đảm bảo tích hợp an toàn, chịu lỗi (resilient), không trùng lặp (idempotent) với CSCA Course LMS, website Interview và các sàn thương mại điện tử. Hợp đồng chi tiết của CSCA Course nằm tại [CSCA_COURSE_LMS_DATA_CONTRACT.md](CSCA_COURSE_LMS_DATA_CONTRACT.md).

---

## 1. Nguyên tắc cốt lõi (Core Principles)

1. **Source of Truth theo từng miền dữ liệu**: InternalManagement là nguồn chính cho học sinh, lớp, học phí, công nợ và điều kiện cấp quyền LMS; CSCA Course LMS là nguồn chính cho nội dung, credential, tiến độ và hồ sơ học tập. Backoffice lưu mirror có kiểm soát để báo cáo và đối soát.
2. **Không Scrape HTML**: Mọi kết nối bắt buộc thông qua REST API có xác thực, Webhook có chữ ký bảo mật (HMAC SHA256) hoặc file Import Excel chuẩn hóa.
3. **Tính Bất biến & Không trùng lặp (Idempotency)**:
   - Webhook inbox có unique index `(source_system, event_id)`.
   - Entity đồng bộ có unique index `(source_system, entity_type, source_id)`.
   - Provision/access/enrollment dùng `Idempotency-Key` ổn định theo học sinh, khóa học và phiên bản trạng thái.
   - Khi nhận lại payload cùng key, hệ thống tự động nhận diện và bỏ qua (Skip) mà không sinh bản ghi thừa hoặc ghi đè sai dữ liệu lịch sử.

## 1.1. Lưu chứng từ kho lên Cloudinary

Phiếu nhập kho cho phép đính kèm nhiều ảnh hóa đơn hoặc PDF. Desktop gửi tệp lên API; API ký request và tải lên Cloudinary, sau đó lưu metadata vào bảng `documents` gắn với `PurchaseReceipt`. API secret không bao giờ được đưa xuống Desktop.

### Cấu hình secret cho API

Đặt các biến môi trường sau trên máy chạy `InternalManagement.Api`:

```text
Cloudinary__CloudName=<cloud name>
Cloudinary__ApiKey=<api key>
Cloudinary__ApiSecret=<api secret>
Cloudinary__Folder=moli/attachments
Cloudinary__MaxFileSizeBytes=15728640
```

Hệ thống nhận ảnh `JPG/JPEG/PNG/WEBP/GIF` và `PDF`, tối đa 15 MB mỗi tệp. Nếu chưa cấu hình Cloudinary, phiếu nhập vẫn được tạo nhưng phần đính kèm sẽ báo rõ là chưa tải được để người dùng thử lại.

Luồng upload dùng signed upload server-side theo [Cloudinary Upload API](https://cloudinary.com/documentation/image_upload_api_reference). Khi mở chứng từ, Desktop dùng `secure_url` được trả về từ Cloudinary; thông tin `public_id`, loại lưu trữ và kích thước được lưu cùng bản ghi để có thể mở rộng cho phiếu xuất hoặc các chứng từ khác.

---

## 2. Mô hình Xử lý Đồng bộ (Sync Architecture)

```text
[ Website EdTech / Shopee / TikTok ]
        |
        +---> [ Webhook POST ] ---> [ IntegrationInbox Table ] (Kiểm tra HMAC signature + Event ID)
        |                                    |
        |                             (Background Worker)
        |                                    v
        |                           [ Process & Upsert Entity ]
        |                                    |
        |                           (Lỗi dữ liệu / Schema)
        |                                    v
        |                           [ Dead-Letter Queue ]
        |
        +<--- [ Periodic Pull Job ] <-------+
              (Cursor + UpdatedSince)
```

---

## 3. Quản lý Connector Abstraction

Trong Backend .NET 10, mọi connector kết nối hệ thống ngoài đều phải implement interface chuẩn:

```csharp
namespace InternalManagement.Domain.Interfaces;

public interface IExternalConnector
{
    string SourceSystem { get; }
    Task<SyncPage<T>> PullAsync<T>(SyncCursor cursor, CancellationToken ct);
    Task<ConnectorHealth> CheckHealthAsync(CancellationToken ct);
}
```

### Connector kiểm thử:
- `WebsiteEdtechConnector` và `NullConnector` chỉ được đăng ký khi host dùng InMemory/Testing. Không được dùng chúng để đồng bộ production.
- Production chỉ đăng ký các connector HTTP thật; khi thiếu credential hoặc contract, sync phải thất bại rõ ràng và không ghi dữ liệu.

### 3.1. Hai connector Railway đã nối

| SourceSystem | Railway API | Entity sync | Ghi chú |
|---|---|---|---|
| `CSCA_MOLI_STUDIO` | `https://csca-molistudio-production.up.railway.app` | `Courses`, `Questions`, `Customers`, `Subscriptions`, `Payments` | Các entity integration cần `X-Integration-Key`; `Payments` chờ endpoint read-only |
| `CSCA_COURSE_LMS` | API integration riêng của CSCA Course | `Courses`, `Users`, `Enrollments`, `LearningRecords`, `AccessGrants` | Hai chiều có kiểm soát; Management quyết định quyền theo học phí, LMS thực thi quyền học |
| `WEBSITE_INTERVIEW` | `https://api.molyinterview.online` | `InterviewCustomers` | Mặc định dùng `/api/integrations/v1/customers` + `X-Integration-Key`; nếu website cấp export admin thì đổi `CustomersPath` sang `/api/admin/users` và dùng Bearer token admin |

Các connector dùng HTTP read-only, cursor `page/limit` khi nguồn hỗ trợ, timeout và không ghi token vào repository. Connector đồng bộ integration có retry tối đa 3 lần cho timeout/429/5xx. Token đưa vào Railway bằng:

```text
Integrations__CscaMoliStudio__BearerToken
Integrations__CscaInterview__BearerToken
Integrations__CscaInterview__IntegrationKey
Integrations__CscaCourseLms__BaseUrl
Integrations__CscaCourseLms__ServiceToken
Integrations__CscaCourseLms__IntegrationKey
Integrations__CscaCourseLms__HmacSecret
Integrations__CscaCourseLms__OutboxWorker__Enabled=true
```

`CscaCourseLms` outbox worker mặc định tắt. Chỉ bật sau khi LMS đã triển khai hai endpoint provision/access, các secret đã nằm trong secret store và `LmsCourseLink` đã được đối chiếu theo `CourseSourceId`. Worker gửi lệnh đã commit theo HMAC và `Idempotency-Key`; không gọi LMS trong transaction ghi danh hoặc thu học phí.

Có thể đổi endpoint mà không sửa code bằng các biến cấu hình tương ứng, ví dụ `Integrations__CscaMoliStudio__PaymentsPath` sau khi website cung cấp endpoint export payment chỉ đọc.

Màn hình Mock Interview trong Desktop có nút **Đồng bộ website**. Nút này chạy connector `WEBSITE_INTERVIEW / InterviewCustomers`, ghi dữ liệu vào bảng khách hàng nội bộ theo `SourceId` để có thể chạy lại an toàn. Không tạo dữ liệu giả khi endpoint hoặc credential website chưa sẵn sàng.

Backoffice cũng đọc ba nguồn public đã kiểm chứng của CSCA-MOLI.STUDIO qua backend trung tâm:

| Nguồn web | Endpoint nội bộ |
|---|---|
| Tài liệu | `GET /api/v1/csca/online/materials` → `/api/materials` |
| Từ vựng | `GET /api/v1/csca/online/vocabulary` → `/api/vocabulary` |
| Cộng đồng | `GET /api/v1/csca/online/posts` → `/api/posts` |

Các route private như users/exams/stats vẫn cần Bearer token admin của website; không dùng dữ liệu giả hoặc scrape HTML để thay thế.

### 3.2. CSCA Course LMS — contract hai chiều

CSCA Course phải cung cấp API machine-to-machine, không dùng cookie JWT của giao diện Admin:

```text
POST  /api/integrations/v1/students/provision
PATCH /api/integrations/v1/students/{externalStudentId}/access
GET   /api/integrations/v1/catalog/courses
GET   /api/integrations/v1/exports/users
GET   /api/integrations/v1/exports/enrollments
GET   /api/integrations/v1/exports/learning-records
```

Management gửi event sau khi commit học sinh hoặc thanh toán; LMS trả kết quả bằng response đồng bộ và webhook về:

```text
POST /api/v1/system/webhooks/CSCA_COURSE_LMS
```

Event tối thiểu: `lms.account.provisioned`, `lms.account.activation_sent`, `lms.account.access_changed`, `lms.enrollment.changed`, `lms.progress.updated`, `lms.assignment.submitted`, `lms.quiz.completed`, `lms.attendance.recorded`, `lms.certificate.issued`.

Quy tắc nghiệp vụ bắt buộc:

- `Pending`/`Partial` không có quyền học LMS.
- `Paid` chỉ cấp quyền khi `PaidAmount >= TuitionFee` và khóa học còn hiệu lực.
- `Refunded`/`Cancelled`/`Failed` thu hồi grant nhưng không xóa progress.
- Thiếu email hoặc chưa có `LmsCourseLink` cho khóa học: tạo dead-letter/manual review, không tự đoán email/course và không cấp quyền.
- Provision, access grant/revoke và enrollment phải idempotent.
- Không gửi password, JWT, refresh token, password hash hoặc signed playback URL qua integration.
- Không cho tự đăng ký vào khóa học trả phí từ giao diện LMS; enrollment trả phí do provisioning service tạo.

Chi tiết field, envelope, header HMAC và response contract xem tại `CSCA_COURSE_LMS_DATA_CONTRACT.md`.

### 3.3. Vận hành CSCA Course LMS từ InternalManagement

Nhân sự không được sửa trực tiếp database của LMS để mapping course hoặc retry lệnh. API quản trị theo tenant cung cấp:

```text
GET  /api/v1/lms-integration/overview
GET  /api/v1/lms-integration/course-mappings
PUT  /api/v1/lms-integration/course-mappings/{courseId}
GET  /api/v1/lms-integration/outbox
POST /api/v1/lms-integration/outbox/{outboxId}/retry
POST /api/v1/lms-integration/outbox/dispatch?batchSize=20
```

- `SystemSync.View` chỉ xem dashboard, mapping và outbox; `SystemSync.Trigger` mới được lưu mapping, retry hoặc dispatch.
- Mapping chỉ đạt `Success` (ready) khi người vận hành bật `EnableAccess` và xác nhận ít nhất một trong `LmsCourseId` hoặc `LmsCourseSlug`. Chỉ mapping ready mới đủ điều kiện cấp quyền cho học viên đã thanh toán đủ.
- Không được đổi `ExternalCourseId` sau khi mapping đã phát sinh access grant, nhằm giữ toàn vẹn entitlement lịch sử.
- Danh sách outbox không trả payload hoặc idempotency key; retry đưa lệnh failed/dead-letter về `Pending` và xóa lỗi hiển thị cũ. Dead-letter liên quan được tự đánh dấu resolved nếu lần dispatch sau thành công.
- `dispatch` là thao tác chủ động có kiểm soát. Production ưu tiên bật `LmsOutboxWorker` sau khi mapping, token, integration key và HMAC secret đã cấu hình đúng.

---

## 4. Xử lý Lỗi & Dead-Letter Queue

| Loại lỗi | HTTP / Exception | Chiến lược xử lý |
|---|---|---|
| Mạng / Timeout | `HttpRequestException`, `TimeoutException` | Exponential backoff retry (tối đa 3 lần: 2s, 8s, 30s) |
| Rate Limit | HTTP 429 | Đọc header `Retry-After` hoặc tạm dừng connector worker |
| Lỗi dữ liệu / Schema | Format JSON không hợp lệ, thiếu trường bắt buộc | Chuyển ngay vào `IntegrationDeadLetter` để quản trị viên đối soát |
| Authentication | HTTP 401, 403 hoặc HMAC không hợp lệ | Đánh dấu Connector Unhealthy, ghi log cảnh báo, không retry vô hạn |
| Email/external ID conflict | HTTP 409 | Không tự gộp user; tạo dead-letter/manual review |
| Access policy conflict | HTTP 422 | Không cấp quyền; giữ trạng thái payment và yêu cầu xử lý policy |

Quản trị viên có thể xem danh sách `IntegrationDeadLetter` trên màn hình WPF Desktop và kích hoạt Retry thủ công sau khi xử lý dữ liệu.

---

## 5. Kiến trúc Tích hợp Bán Hàng Đa Kênh (Omnichannel: TikTok Shop, Shopee, Cửa Hàng Offline)

### 5.1. Định hướng Ngành Hàng Áo Dài MOLY
Ngành hàng Thời trang MOLY tập trung vào dòng sản phẩm **Áo Dài** (Áo Dài Truyền Thống, Áo Dài Cách Tân, Áo Dài Lụa Tơ Tằm, Áo Dài Nhung Gấm) được phân phối đa kênh:
- **Kênh Sàn TMĐT:** TikTok Shop, Shopee.
- **Kênh Trực Tiếp:** Cửa hàng Offline / Bán ngoài (Store POS & Direct Sales).

### 5.2. Nguyên tắc Quản lý Dữ liệu Lõi
1. **Chuẩn hóa SKU Đa kênh:** Mã SKU (`ProductVariant.Sku`) được sử dụng thống nhất trên hệ thống nội bộ và mapping với Seller SKU trên TikTok Shop / Shopee.
2. **Quản lý Tồn kho & Giữ hàng (Reserved Inventory):**
   - Khi đơn hàng được tạo trên sàn TMĐT hoặc tại quầy, hệ thống tự động tăng `ReservedQuantity`, giảm `AvailableQuantity`.
   - Khi đơn hoàn tất đóng gói và xuất giao, hệ thống ghi bản ghi `InventoryMovement` (Loại `SalesOrder`) và giảm `OnHandQuantity`.
   - Khi phát sinh trả hàng / hủy đơn (Return / Inspection), hệ thống hoàn lại số dư kho theo quy trình kiểm tra chất lượng.
3. **Tính toán Giá cả, Giá vốn & Lợi nhuận chuẩn xác (Pricing & Costing Integrity):**
   - **Giá vốn (Unit Cost / COGS):** Với áo dài MOLY tự may/sản xuất (`sourcing_type = MAKE`), giá vốn thành phẩm được tính chính xác từ **Giá thành sản xuất thực tế (Actual Manufacturing Cost)** = NVL trực tiếp theo lô + Nhân công công đoạn + Gia công ngoài + Hao hụt/sửa lại + Chi phí xưởng phân bổ theo kỳ; không dùng giá nhập xưởng giản đơn.
   - **Giá bán & Chiết khấu:** Lưu vết giá niêm yết, voucher giảm giá, chiết khấu đại lý.
   - **Chi phí sàn, Affiliate & Thuế:** Hạch toán chi tiết theo `ChannelFeePolicy` từng kênh (TikTok Shop / Shopee commission, affiliate, payment fee, shop-funded ship subsidy, thuế VAT).
   - **Pricing Simulator & Cảnh báo:** Tính giá hòa vốn, giá tối thiểu theo Target Margin, cảnh báo các chương trình sale/voucher khiến đơn bị lỗ.
4. **Hóa đơn & Chứng từ Bán hàng (Sales Invoicing & Receipts):**
   - Mọi đơn hàng đều sinh chứng từ bán hàng / hóa đơn bán lẻ snapshot bất biến (`SalesInvoice`).
   - Lưu snapshot bất biến: SKU, size, đơn giá bán, chiết khấu voucher, thuế, phí sàn, phí vận chuyển và COGS thực tế tại thời điểm chốt đơn, phục vụ báo cáo tài chính và đối soát kế toán Ngày 16.
5. **Lộ trình kỹ thuật:**
   - **Giai đoạn MVP (Hiện tại - Ngày 14 & 15):** Chuẩn hóa chuỗi dữ liệu sản xuất & giá thành (NVL theo lô → BOM theo size → Lệnh sản xuất → Actual Cost → Kho thành phẩm), đơn hàng đa kênh (`SalesOrder`), xuất kho giao hàng (`InventoryMovement`), số dư (`InventoryBalance`), hóa đơn bán hàng snapshot, hỗ trợ lập đơn tại quầy và import file đối soát đơn từ TikTok Shop / Shopee.
   - **Giai đoạn 2 (Sau MVP):** Tích hợp Webhook & REST API chính thức 2 chiều với Shopee Open Platform và TikTok Shop Partner API.
