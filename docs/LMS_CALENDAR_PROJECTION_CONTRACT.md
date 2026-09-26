# CSCA LMS Calendar Projection Contract

Phiên bản: 1.0  
Trạng thái: triển khai backend — LMS là nguồn lịch chính  
Timezone chuẩn: lưu instant UTC; lịch lặp hiển thị theo `Asia/Ho_Chi_Minh` mặc định.

## 1. Quyết định kiến trúc

Lịch học không được có hai nơi cùng cho phép sửa.

| Dữ liệu | Nguồn chính | Hệ thống còn lại |
|---|---|---|
| Lớp, học viên, giáo viên, học phí, quyền học | InternalManagement | LMS nhận mapping/quyền truy cập |
| Lịch cố định hằng tuần, ngày bắt đầu/kết thúc, timezone | LMS | InternalManagement lưu bản chiếu chỉ đọc |
| Buổi học theo ngày, đổi lịch, hủy, link Meet/Zoom | LMS | InternalManagement lưu bản chiếu chỉ đọc |
| Điểm danh, tài liệu, bài tập, tiến độ | LMS | InternalManagement nhận bản chiếu/báo cáo |

Vì vậy nhân viên không sửa `CscaClassSchedule` hoặc `CscaLessonSession` đã có `ExternalSource = CSCA_COURSE_LMS` từ màn CRUD Management. Mọi sửa lịch đi qua LMS, sau đó worker gửi webhook để Management cập nhật.

## 2. Mapping định danh

```text
InternalManagement.CscaClass.Id (GUID)
  <-> LMS.live_classes.management_class_source_id

LMS.class_schedules.id (BIGINT)
  <-> Management.CscaClassSchedule.ExternalScheduleId

LMS.class_sessions.id (BIGINT)
  <-> Management.CscaLessonSession.ExternalSessionId
```

Không map theo tên lớp, tiêu đề lịch hoặc giờ học. `ExternalVersion` là version tăng dần của LMS: bản tin có version nhỏ hơn hoặc bằng bản đã nhận phải bỏ qua để không ghi đè event mới bởi retry/event đến muộn.

## 3. Luồng vận hành

```text
Giáo viên/Admin sửa lịch trong LMS
  -> transaction LMS cập nhật schedule/session + ghi calendar outbox
  -> worker LMS ký HMAC và POST webhook
  -> InternalManagement xác minh HMAC, ghi inbox idempotent
  -> InternalManagement upsert bản chiếu theo external ID + version
```

Sửa một lịch cố định sẽ materialize buổi học theo ngày trong LMS. Các buổi cũ chưa diễn ra bị hủy có audit reason; các buổi thay thế được tạo mới. Management nhận cả event series và event từng buổi, không tự generate thêm buổi riêng.

## 4. Sự kiện

Webhook đích hiện có:

```text
POST /api/v1/system/webhooks/CSCA_COURSE_LMS
X-Hub-Signature-256: sha256=<HMAC-SHA256(payloadJson)>
```

| Event | Khi phát | Bản chiếu Management |
|---|---|---|
| `lms.schedule.upserted` | Tạo/sửa lịch cố định | upsert recurring schedule |
| `lms.schedule.archived` | Ngừng lịch cố định | archive recurring schedule |
| `lms.session.upserted` | Materialize/tạo hoặc đổi buổi | upsert dated lesson session |
| `lms.session.cancelled` | Hủy buổi chưa diễn ra | đặt dated session `Cancelled` |
| `lms.attendance.recorded` | Giáo viên chốt/sửa điểm danh | upsert attendance ledger |

## 5. Payload chuẩn

### 5.1 Lịch cố định

```json
{
  "schemaVersion": 1,
  "managementClassId": "csca-class-guid",
  "lmsSchedule": {
    "id": "42",
    "liveClassId": "12",
    "title": "Tối thứ 3",
    "dayOfWeek": 2,
    "startTime": "18:00:00",
    "endTime": "20:00:00",
    "timezone": "Asia/Ho_Chi_Minh",
    "startDate": "2026-10-01",
    "endDate": "2027-03-31",
    "status": "active",
    "version": 3
  }
}
```

`dayOfWeek` dùng ISO: Thứ Hai = `1`, Chủ nhật = `7`. Management dùng `System.DayOfWeek`: Chủ nhật = `0`, Thứ Hai–Thứ Bảy = `1`–`6`. Bridge phải đổi rõ ràng `7 -> 0`.

### 5.2 Buổi học theo ngày

```json
{
  "schemaVersion": 1,
  "managementClassId": "csca-class-guid",
  "lmsSession": {
    "id": "281",
    "liveClassId": "12",
    "scheduleId": "42",
    "title": "Tối thứ 3 — Buổi 5",
    "startTime": "2026-11-10T11:00:00.000Z",
    "endTime": "2026-11-10T13:00:00.000Z",
    "status": "rescheduled",
    "meetingUrl": "https://meet.google.com/...",
    "changeReason": "Đổi lịch do nghỉ lễ",
    "version": 2
  }
}
```

Thời gian buổi học là UTC instant. Management đổi sang timezone Việt Nam khi lưu `LessonDate`, `StartTime`, `EndTime`; đây là dữ liệu báo cáo, không phải nguồn quyết định lịch.

## 6. Retry, thứ tự và lỗi

- LMS ghi outbox cùng transaction với sửa lịch, nên không có tình huống sửa thành công nhưng không có bản tin.
- Worker claim job bằng `FOR UPDATE SKIP LOCKED`, timeout/409/429/5xx retry tối đa 12 lần với exponential backoff; 4xx dữ liệu không hợp lệ vào dead letter.
- Nếu session đến trước schedule, Management trả `409 DEPENDENCY_PENDING`; LMS retry sau. Không tự đoán schedule ID.
- Inbox unique theo `(sourceSystem, eventId)`. Gửi lại cùng `eventId` và cùng payload là thành công idempotent.
- `ExternalVersion` chống sự kiện cũ ghi đè bản chiếu mới.

## 7. Biến môi trường LMS

```text
MANAGEMENT_CALENDAR_WEBHOOK_URL=https://<internal>/api/v1/system/webhooks/CSCA_COURSE_LMS
MANAGEMENT_CALENDAR_WEBHOOK_SECRET=<same HMAC secret configured on IntegrationSource>
```

Trong giai đoạn chuyển tiếp, worker dùng `MANAGEMENT_ATTENDANCE_WEBHOOK_URL` và `MANAGEMENT_ATTENDANCE_WEBHOOK_SECRET` làm fallback vì calendar và attendance đi cùng endpoint/HMAC. Khi cấu hình biến calendar riêng, nó được ưu tiên.

## 8. Điều kiện triển khai production

1. Deploy InternalManagement có migration `AddCscaLmsCalendarProjection` trước hoặc cùng lúc với LMS.
2. Chạy LMS migration `023_management_calendar_delivery.sql`.
3. Xác nhận `live_classes.management_class_source_id` là GUID hợp lệ với lớp Management.
4. Tạo một lịch thử nghiệm, kiểm tra calendar outbox chuyển `SUCCESS` và Management có schedule/session external ID tương ứng.
5. Đổi một buổi, kiểm tra version tăng và lịch học viên trên LMS thay đổi ngay; Management chỉ phản ánh kết quả.
6. Không bật thao tác sửa lịch trực tiếp trong Management cho dữ liệu có external source.
