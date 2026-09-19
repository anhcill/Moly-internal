# CSCA Course LMS — Hợp đồng dữ liệu và tích hợp

Phiên bản: 1.0 (Giai đoạn 1)  
Trạng thái: baseline để triển khai API, migration và connector  
Source system code: `CSCA_COURSE_LMS`  
Timezone lưu trữ: UTC; hiển thị: `Asia/Ho_Chi_Minh`  
Tiền tệ mặc định: `VND`

## 1. Phạm vi

Hợp đồng này kết nối:

- `InternalManagement`: quản lý học sinh, lớp, học phí, công nợ, chứng từ và điều kiện cấp quyền.
- `CSCA Course LMS`: tài khoản LMS, nội dung khóa học, enrollment, tiến độ, bài tập, live class và chứng chỉ.

Hai hệ thống giữ database riêng. Không truy cập trực tiếp database của hệ thống kia, không đồng bộ bằng cách scrape HTML và không gửi password hoặc secret qua integration API.

## 2. Quyền sở hữu dữ liệu

| Nhóm dữ liệu | Nguồn chính | Hệ thống còn lại |
|---|---|---|
| Học sinh, Party, lớp, học phí, công nợ | InternalManagement | LMS nhận bản sao cần thiết |
| Điều kiện cấp/thu hồi quyền học | InternalManagement | LMS thực thi quyền |
| Khóa học, section, lesson, video, quiz content | CSCA Course LMS | Management lưu metadata để tra cứu/báo cáo |
| User credential, activation token, session | CSCA Course LMS | Management chỉ lưu liên kết và trạng thái |
| Enrollment LMS | LMS, theo grant do Management cấp | Management lưu mirror |
| Progress, assignment, quiz attempt, attendance | LMS | Management lưu summary/read model |
| Finance transaction và chứng từ thu/hoàn | InternalManagement | LMS nhận trạng thái quyền, không làm sổ tài chính |

Nguyên tắc: một loại dữ liệu chỉ có một nơi được phép sửa. Dữ liệu mirror không được ghi ngược về nguồn bằng thao tác CRUD thông thường.

## 3. Khóa định danh và mapping

### 3.1. Học sinh

Khóa liên kết bắt buộc:

```text
InternalManagement.CscaClassStudent.Id
  ↔ sourceSystem = CSCA_INTERNAL_MANAGEMENT
  ↔ sourceId = CscaClassStudent.Id
  ↔ LMS.users.external_student_id
```

`PartyId` dùng để hợp nhất thông tin khách hàng/học sinh trong Management. Không ghép học sinh chỉ theo tên. Khi không có `sourceId`, hệ thống có thể đối chiếu email hoặc số điện thoại đã chuẩn hóa nhưng phải tạo manual-review item nếu có nhiều kết quả.

### 3.2. Khóa học và lớp

```text
InternalManagement.Course.CourseSourceId
  ↔ LMS.courses.external_source_id

InternalManagement.CscaClass.Id
  ↔ LMS live class/class mapping (nếu lớp có live class riêng)
```

Course mapping phải có unique key theo `(source_system, source_id)`. Không dùng title hoặc slug làm khóa liên kết chính.

### 3.3. Thanh toán

```text
InternalManagement.Payment.SourcePaymentId
  ↔ LMS event/payment_reference (nếu LMS có bản ghi payment)
InternalManagement.CscaClassStudent.BusinessDocumentId
  ↔ chứng từ ghi danh và finance transaction
```

Quyền LMS mặc định chỉ được cấp khi:

```text
PaymentStatus = Paid
AND PaidAmount >= TuitionFee
```

`Partial` không được truy cập LMS trong policy mặc định. Nếu sau này cho phép học trước khi trả đủ, đó phải là policy riêng theo khóa học và có audit.

## 4. Vòng đời học sinh và quyền LMS

| Sự kiện Management | Trạng thái account | Grant/enrollment LMS |
|---|---|---|
| Thêm học sinh mới | `PendingPayment` | Chưa active |
| Cập nhật `Pending` hoặc `Partial` | `PendingPayment` | Không có quyền học |
| Chuyển sang `Paid` | `Active` | Tạo hoặc kích hoạt grant + enrollment |
| Đổi email/số điện thoại | Giữ trạng thái hiện tại | Upsert profile, không tạo user mới |
| `Refunded`, `Cancelled`, `Failed` | `Suspended` hoặc `Revoked` | Thu hồi grant; giữ progress |
| Lớp/khóa kết thúc | `Active` đến hết hạn | Grant hết hạn theo `valid_until` |
| Admin khóa bảo mật | `Locked` | Từ chối login/API bất kể payment |

Account `PendingPayment` có thể tồn tại để giữ mapping nhưng không có password hoạt động và không nhận activation link. Sau khi thanh toán đủ, LMS phát activation token một lần. Không gửi password từ Management sang LMS.

## 5. Integration API phía LMS

Các endpoint dưới đây là API machine-to-machine, không dùng cookie JWT của người dùng và không dùng endpoint admin trên giao diện.

### Header bắt buộc

```text
Authorization: Bearer <service-token>
X-Integration-Key: <key-id>
X-Event-Timestamp: 2026-09-16T10:00:00Z
X-Signature: sha256=<hmac>
X-Correlation-ID: <uuid>
Idempotency-Key: <stable-key>
```

Chữ ký tính trên `timestamp + "." + rawRequestBody`. Server từ chối timestamp quá cũ, chữ ký sai hoặc key đã bị thu hồi.

### 5.1. Provision hoặc upsert học sinh

```text
POST /api/integrations/v1/students/provision
```

Request tối thiểu:

```json
{
  "externalStudentId": "csca-class-student-guid",
  "externalPartyId": "party-guid",
  "fullName": "Nguyen Van A",
  "email": "student@example.com",
  "phone": "0900000000",
  "accountStatus": "PendingPayment",
  "paymentStatus": "Pending",
  "courseSourceIds": ["course-source-id"],
  "classSourceId": "class-guid",
  "sourceUpdatedAt": "2026-09-16T03:00:00Z"
}
```

Response phải trả về `alreadyExists`, `lmsUserId`, `provisionStatus` và `correlationId`. Gọi lại cùng `Idempotency-Key` phải trả cùng kết quả logic, không tạo user mới.

### 5.2. Cấp hoặc thu hồi quyền

```text
PATCH /api/integrations/v1/students/{externalStudentId}/access
```

Request:

```json
{
  "accessStatus": "Active",
  "reason": "PAYMENT_PAID",
  "sourcePaymentId": "payment-guid-or-source-id",
  "validFrom": "2026-09-16T03:00:00Z",
  "validUntil": null,
  "courseSourceIds": ["course-source-id"]
}
```

Các giá trị `accessStatus`: `PendingPayment`, `Active`, `Suspended`, `Revoked`, `Expired`.

### 5.3. Đồng bộ danh mục và thay đổi

```text
GET /api/integrations/v1/catalog/courses?cursor=&updatedSince=&limit=100
GET /api/integrations/v1/exports/users?cursor=&updatedSince=&limit=100
GET /api/integrations/v1/exports/enrollments?cursor=&updatedSince=&limit=100
GET /api/integrations/v1/exports/learning-records?cursor=&updatedSince=&limit=100
```

`learning-records` trả summary phục vụ quản lý, gồm progress, last activity, assignment/quiz result, attendance và certificate. Không trả token, password hash hoặc signed playback URL.

## 6. Event từ LMS về Management

Management nhận event tại:

```text
POST /api/v1/system/webhooks/CSCA_COURSE_LMS
```

Envelope chuẩn:

```json
{
  "eventId": "evt_01J...",
  "eventType": "lms.account.provisioned",
  "occurredAt": "2026-09-16T03:00:00Z",
  "sourceSystem": "CSCA_COURSE_LMS",
  "entityType": "LmsUser",
  "entityId": "12345",
  "externalStudentId": "csca-class-student-guid",
  "schemaVersion": 1,
  "data": {},
  "correlationId": "uuid"
}
```

Event tối thiểu:

```text
lms.account.provisioned
lms.account.activation_sent
lms.account.access_changed
lms.enrollment.changed
lms.progress.updated
lms.assignment.submitted
lms.quiz.completed
lms.attendance.recorded
lms.certificate.issued
```

Management ghi inbox trước khi xử lý. `eventId` trùng phải trả kết quả thành công/idempotent và không tạo bản ghi mirror hoặc finance transaction thứ hai.

## 7. Luồng đồng bộ và xử lý lỗi

1. Management commit thay đổi học sinh/thanh toán.
2. Transaction tạo outbox event cùng transaction nghiệp vụ.
3. Worker gửi API provisioning tới LMS.
4. LMS upsert account/grant/enrollment trong transaction của LMS.
5. LMS gửi event kết quả về Management.
6. Management cập nhật link và trạng thái sync.
7. Lỗi timeout/429/5xx retry tối đa 3 lần với backoff.
8. Lỗi dữ liệu hoặc conflict email đưa vào `IntegrationDeadLetter`.
9. Job pull định kỳ đối soát lại để bù webhook thất lạc.

### 7.1. Cấu hình worker Management

Management chỉ bật worker sau khi LMS đã triển khai đúng hai mutation endpoint. Các biến secret-only cần có:

```text
Integrations__CscaCourseLms__BaseUrl
Integrations__CscaCourseLms__ServiceToken
Integrations__CscaCourseLms__IntegrationKey
Integrations__CscaCourseLms__HmacSecret
Integrations__CscaCourseLms__OutboxWorker__Enabled=true
```

Nếu học viên thiếu email, course mapping chưa có, hoặc `PartyId` đang liên kết với external student ID khác, Management tạo manual-review dead-letter và không gửi lệnh có thể cấp nhầm quyền.

Mọi thao tác provision, access grant/revoke và enrollment phải có audit trail.

## 8. Bảo mật và dữ liệu cá nhân

- Service credential chỉ nằm trong secret store/environment, không lưu trong WPF hoặc repository.
- HMAC key có `keyId`, ngày tạo, ngày hết hạn và quy trình rotation.
- Chỉ gửi các trường PII cần thiết; log phải mask email, phone và token.
- Không cho client tự truyền `companyId`, `businessUnitId`, `lmsUserId` để vượt scope.
- API LMS phải kiểm tra access grant ở backend; UI guard chỉ là lớp UX.
- Người bị thu hồi quyền không được lấy playback URL, assignment, live-session access hoặc certificate private.

## 9. Definition of Done cho Giai đoạn 1

- Đã chốt source of truth theo từng nhóm dữ liệu.
- Đã chốt `CSCA_COURSE_LMS` và quy tắc external ID.
- Đã chốt lifecycle Pending/Partial/Paid/Refunded/Cancelled.
- Đã chốt API provision, access, catalog và export.
- Đã chốt webhook envelope, event names và idempotency.
- Đã chốt nguyên tắc HMAC, retry, dead-letter và reconciliation.
- Không còn yêu cầu truy cập trực tiếp database hoặc scrape HTML.
- Giai đoạn 2 có thể bắt đầu viết migration và connector mà không phải tự suy đoán contract.
