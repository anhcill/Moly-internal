# MOLY Internal Management — Checklist UAT hai mảng

Phiên bản: 1.0  
Mục tiêu: xác nhận luồng nghiệp vụ quan trọng trên API/WPF trước demo nghiệm thu.  
Nguyên tắc: mỗi mục phải có người chạy, thời điểm, môi trường, kết quả và bằng chứng; không đánh dấu Đạt chỉ vì endpoint trả HTTP 200.

## 1. Thông tin phiên UAT

| Trường | Giá trị cần điền |
|---|---|
| Môi trường | `Development` / `Staging` / `Production` |
| API base URL | URL thực tế |
| Database | Tên DB và thời điểm kiểm tra, không ghi mật khẩu |
| Người kiểm tra | Họ tên |
| Thời gian bắt đầu/kết thúc | ISO 8601 |
| Commit/build | Mã commit hoặc gói cài đặt |
| Kết luận | Đạt / Đạt có điều kiện / Chưa đạt |

## 2. Điều kiện trước khi chạy

- [ ] API khởi động không có lỗi migration/seed; `/health/live` trả `Healthy`.
- [ ] `/health/ready` xác nhận kết nối PostgreSQL và `/health` không có dependency lỗi.
- [ ] Đã áp dụng đủ migration; API đọc/ghi được trường CV, loại làm việc, cấu hình part-time và các thành phần phiếu lương mới.
- [ ] Công ty test có đủ Business Unit `EDTECH`, `CSCA`, `INTERVIEW`, `FASHION` và tài khoản theo từng phạm vi.
- [ ] Có tài khoản UAT theo từng role; không dùng mật khẩu mặc định trong phiên production.
- [ ] Có backup gần nhất và biết cách restore vào database test riêng.
- [ ] Dữ liệu đối soát từ website chỉ được dùng khi có API/credential hợp lệ; không nhập số liệu giả.

## 3. Ma trận test bắt buộc

| ID | Luồng | Cách chạy ngắn gọn | Kết quả bắt buộc | Bằng chứng |
|---|---|---|---|---|
| UAT-01 | Login và token | Login, gọi `/api/v1/auth/me`, refresh token, logout | Login đúng; token cũ sau logout/rotation bị từ chối | Request/response đã che token |
| UAT-02 | RBAC | Editor, HR, employee và payment accountant gọi đúng/sai endpoint, gồm bốn quyền InternalCustomers/InternalResources và `FinanceReports.View` | Đúng quyền được 2xx; sai quyền 403; chưa login 401 | Bảng status code |
| UAT-03 | Tenant/scope | Thử thay `company_id`, `business_unit_id`, `employee_id`, segment trên URL/query | Không đọc/sửa được dữ liệu ngoài tenant hoặc ngoài mảng được cấp | ID scope trước/sau |
| UAT-04 | EdTech | Tạo/sửa version câu hỏi, duyệt/xuất bản; phân trang danh sách | Version không mất; câu hỏi có `source_id` không gây lỗi truy vấn | Ảnh màn hình + response |
| UAT-05 | CSCA/Interview | Tạo lớp, lịch, phân công; tạo khách hàng Interview | Dữ liệu đúng business unit, không trùng reference | ID bản ghi |
| UAT-06 | HR/CV | Tạo full-time và part-time ở từng mảng; nhập CV, kỹ năng, kinh nghiệm; lọc `businessSegment` | Hồ sơ/CV đúng Business Unit; danh sách hai mảng không lẫn nhau; part-time thiếu phương thức/đơn giá bị từ chối | Employee ID + response/ảnh |
| UAT-07 | Chấm công | Ghi/import chấm công; lọc `businessSegment`; kiểm tra summary theo từng mảng | Dữ liệu và tổng hợp không lẫn Business Unit; import báo lỗi từng dòng | File import + report lỗi |
| UAT-08 | Payroll full-time | Tạo kỳ có `businessUnitId`, thêm `ALLOWANCE`, `KPI_BONUS`, `HEALTH_INSURANCE`, tính → duyệt → phát hành | Chỉ nhân viên cùng BU được tính; lương công theo ngày/22; tổng thu nhập, khấu trừ và thực lĩnh đúng | Period/payslip + phép tính tay |
| UAT-09 | Payroll part-time | Chạy một nhân sự `HOURLY` và một nhân sự `SHIFT`, có và không có chấm công | Lương công = đơn giá × giờ/ca; không chấm công bằng 0; không mặc định 22 ngày | Attendance + payslip |
| UAT-10 | Khách hàng hai mảng | CRUD `/api/v1/noi-bo/{segment}/khach-hang`, tìm theo nguồn/trạng thái; thử ID chéo mảng | Công nghệ - Giáo dục và Thời trang độc lập; ID chéo không đọc/sửa/xóa được | Request/response hai segment |
| UAT-11 | Kho nội bộ | Tạo `EXAM`/`DOCUMENT` ở Công nghệ - Giáo dục; `PLAN`/`DESIGN_SAMPLE` ở Thời trang; thử loại sai và data URI | Loại đúng được lưu; loại chéo và base64/data URI bị từ chối; xóa metadata không xóa tệp ngoài | Resource ID + lỗi validation |
| UAT-12 | Fashion nhập kho | Nhập NVL theo supplier/lot, kiểm tra movement và balance | Số dư available/on-hand/reserved khớp sổ bất biến | Mã receipt/movement |
| UAT-13 | Áo dài MAKE | Chọn mẫu/size/màu → BOM → production order → xuất NVL → hoàn tất QC | Thành phẩm tốt/lỗi, labor, gia công, scrap/rework, overhead được truy vết | BOM/order/cost detail |
| UAT-14 | Bán hàng/đổi trả | Tạo order, reserve, deliver, return tốt/hỏng | Không trừ tồn hai lần; COGS/profit giữ snapshot và cập nhật đúng return | Order + movements |
| UAT-15 | Tài chính từng mảng | Tạo thu/chi gắn BU thuộc từng mảng; gọi `/api/v1/finance/cash-flow` và `/profit-report` | Thu/chi đúng BU; reference không tạo trùng; phần tạm tính không hiện là lãi chốt | Transaction/report |
| UAT-16 | Bảng tổng công ty | Gọi `/api/v1/finance/overview?from=...&to=...`, đối chiếu hai `areas`, `companyTotal` và giao dịch không gán BU | Tổng công ty bằng tổng hai mảng; giao dịch chưa phân loại nằm riêng; snapshot Fashion không `Actual` vào `provisionalProfit` | Response + bảng đối soát tay |
| UAT-17 | Pricing | Chạy simulator theo kênh, voucher, affiliate, ship, tax | Có break-even/target-margin và cảnh báo lỗ | Input/output simulator |
| UAT-18 | Sync/resilience | Chạy sync lặp, webhook trùng/sai chữ ký, dead-letter retry | Không trùng; lỗi có trạng thái và retry có kiểm soát | integration run/inbox |
| UAT-19 | WPF phạm vi/lỗi | Chuyển ba khu vực, kiểm tra badge/mục đang chọn; ngắt API/mạng và thử 401 | Không trộn dữ liệu; tiếng Việt rõ; không treo UI; refresh token hoặc yêu cầu login lại | Video/ảnh màn hình |
| UAT-20 | Backup/restore | Tạo backup rồi restore vào DB test riêng | Restore đọc được migration, seed/schema và dữ liệu kiểm tra | Log backup/restore |

## 4. Cổng P0/P1

- P0: lỗi làm sai COGS/lợi nhuận, mất dữ liệu, lộ secret, vượt tenant/RBAC, migration không áp dụng được, duplicate order/transaction.
- P1: lỗi chặn một luồng nghiệp vụ chính, sai snapshot, sai số dư kho/tài chính hoặc WPF không thao tác được.
- Chỉ được kết luận Đạt khi không còn P0/P1 mở. P2/P3 phải có issue, owner và hạn xử lý.

## 5. Đối soát số liệu

Đối soát theo cùng timezone, currency và khoảng thời gian:

1. Website/source: số đơn, doanh thu gộp, voucher/refund, phí và trạng thái đơn.
2. MOLY: số order import, gross/net sales, COGS, fee, refund/return, profit/margin.
3. Chênh lệch: ghi rõ reference/order/source ID, nguyên nhân và trạng thái xử lý.

Khi chưa có credential/API contract website thật, mục đối soát phải ghi **Chưa thực hiện — thiếu dữ liệu nguồn**, không được dùng mock làm bằng chứng production.

### Đối soát bảng tổng hai mảng

- `companyTotal.totalIncome` = tổng `totalIncome` của hai phần tử trong `areas`; tương tự với `totalExpense` và `netCashFlow`.
- `companyTotal.confirmedProfit` và `companyTotal.provisionalProfit` = tổng hai mảng tương ứng.
- `unclassifiedCashFlow` được đối soát riêng và không cộng vào `companyTotal`.
- Với Fashion, chỉ snapshot mới nhất của mỗi đơn được dùng; `Actual` là đã chốt, trạng thái khác là tạm tính.
- Với Công nghệ - Giáo dục, chỉ bản phân bổ lợi nhuận mới nhất của mỗi chứng từ được dùng.

## 6. Kết luận phiên

| Hạng mục | Đạt | Chưa đạt | Issue/owner |
|---|---:|---:|---|
| P0/P1 |  |  |  |
| API và database |  |  |  |
| WPF |  |  |  |
| Kho/costing/lợi nhuận |  |  |  |
| Backup/restore |  |  |  |
| Đối soát website |  |  |  |

Người kiểm tra: ____________________  
Người duyệt: ____________________  
Ngày: ____________________
