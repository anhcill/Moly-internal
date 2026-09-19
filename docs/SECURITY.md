# MOLI Internal Management — Chính sách Bảo mật & Phân quyền (Security & RBAC)

Phiên bản: 1.0  
Mục tiêu: Đảm bảo kiểm soát an toàn dữ liệu, chống rò rỉ secret, cách ly phạm vi công ty/đơn vị kinh doanh (Multi-Tenant & Business Unit Isolation).

---

## 1. Mô hình Phân quyền 3 Lớp (3-Layer Authorization)

Mọi request gửi tới Backend API đều đi qua 3 tầng bảo vệ nghiêm ngặt:

1. **Authentication (Xác thực người dùng):**
   - JWT Access Token (hạn 15 phút, chứa `UserId`, `CompanyId`, `DefaultBusinessUnitId`, `Permissions`).
   - Refresh Token (hạn 7 ngày, **chỉ lưu SHA-256 hash trong DB**, cơ chế Token Rotation, thu hồi toàn bộ khi phát hiện tái sử dụng trái phép).
   - Password hashing sử dụng BCrypt work factor 12 kèm unique salt.

2. **Permission Check (Kiểm tra quyền chức năng):**
   - Hệ thống dựa trên mã quyền chi tiết (Fine-grained Permissions) thay vì chỉ kiểm tra Role name:
     - `Courses.Read`, `Courses.Write`, `Questions.Publish`
     - `Payroll.ViewAll`, `Payroll.Calculate`, `Payroll.Approve`, `Payroll.ViewPersonal`
     - `Inventory.Receipt`, `Inventory.Adjust`, `Finance.ViewReports`, v.v.

3. **Data Scope Check (Cách ly phạm vi dữ liệu):**
   - Người dùng chỉ thao tác trên dữ liệu thuộc `CompanyId` và `BusinessUnitId` được phân quyền.
   - Server tự động lấy scope từ Claims/Context của người dùng đã xác thực, **tuyệt đối không tin tưởng client truyền ID qua body/query để vượt quyền**.

---

## 2. Ma trận Vai trò MVP (Role Matrix)

| Vai trò (Role) | Phạm vi nghiệp vụ | Quyền tiêu biểu |
|---|---|---|
| `system_admin` | Toàn hệ thống | Quản trị user, role, cấu hình hệ thống, audit log |
| `director` | Toàn công ty | Xem toàn bộ dashboard, báo cáo tài chính P&L, duyệt lương cấp cao |
| `hr_staff` | Nhân sự | Quản lý hồ sơ nhân viên, import chấm công |
| `payroll_accountant` | Kế toán lương | Tính lương, điều chỉnh lương, lập bảng lương |
| `content_editor` | EdTech | Tạo/sửa câu hỏi, đề thi, xem danh mục khóa học |
| `teacher` | CSCA | Xem danh sách học viên lớp được phân công, lịch dạy |
| `customer_service` | CSKH | Xem danh sách học viên/khách hàng, gói đăng ký, tiếp nhận yêu cầu |
| `warehouse_manager` | Kho / Fashion | Lập phiếu nhập kho, kiểm tra tồn kho, kiểm hàng hoàn |
| `order_manager` | Đơn hàng / Fashion | Quản lý đơn hàng, tiếp nhận đơn từ sàn |
| `payment_accountant` | Kế toán thanh toán | Lập phiếu thu chi, xác nhận thanh toán, báo cáo dòng tiền |
| `employee` | Cá nhân | Xem thông tin cá nhân, xem phiếu lương của chính mình |

---

## 3. Nhật ký Thao tác (Audit Log) & Bảo vệ Dữ liệu

- Mọi thao tác Thêm, Sửa, Xóa, Duyệt, Xuất bản, Đồng bộ đều được ghi vào bảng `audit_logs` gồm: `user_id`, `action`, `entity_name`, `entity_id`, `before_payload`, `after_payload`, `ip_address`, `created_at`.
- **Masking dữ liệu nhạy cảm**: Tuyệt đối không lưu mật khẩu rõ, số thẻ ngân hàng đầy đủ, hoặc access token bên ngoài vào audit log.
- **Quản lý Secrets**: Secret kết nối Database, MinIO, JWT Secret Key được lưu trong biến môi trường / Docker Secret, không commit lên git repository.
