# MOLY Internal Management — Hướng dẫn thao tác hai mảng

## 1. Đăng nhập

1. Mở MOLY Internal Management Desktop.
2. Nhập tài khoản được cấp; không chia sẻ mật khẩu hoặc refresh token.
3. Nếu gặp lỗi mạng, chờ trạng thái tự thử lại; nếu token hết hạn, ứng dụng sẽ refresh hoặc yêu cầu đăng nhập lại.
4. Liên hệ quản trị viên nếu tài khoản bị khóa hoặc thiếu quyền.

## 2. Quyền thao tác chính

| Vai trò | Khu vực chính |
|---|---|
| Giám đốc | Dashboard, báo cáo, duyệt cấp cao |
| Nhân sự | Nhân viên, phòng ban, chấm công |
| Kế toán lương | Chính sách, tính và phát hành lương |
| Biên tập viên | Khóa học, ngân hàng câu hỏi, xuất bản |
| Thủ kho Fashion | Sản phẩm, nguyên liệu, nhập/xuất/tồn |
| Kế toán thu-chi | Giao dịch tài chính, dòng tiền, đối soát |
| Nhân viên | Thông tin cá nhân và phiếu lương của mình |

Menu và nút không thuộc quyền sẽ bị ẩn hoặc trả thông báo không đủ quyền.

## 3. Chọn đúng phạm vi dữ liệu

Hệ thống có ba khu vực hiển thị độc lập:

- **Tổng công ty**: chỉ dùng để xem dòng tiền và lợi nhuận tổng hợp.
- **Công nghệ - Giáo dục**: gồm EDTECH, CSCA và Interview; có nhân sự, chấm công, lương, khách hàng, đề thi/tài liệu và các nghiệp vụ đào tạo.
- **Thời trang**: có nhân sự, chấm công, lương, khách hàng, kế hoạch/mẫu thiết kế/tài liệu, sản phẩm, kho, sản xuất và đơn hàng.

Luôn kiểm tra tên mảng hoặc Business Unit đang chọn trước khi tạo/sửa dữ liệu. Không dùng mảng Công nghệ - Giáo dục để lưu kế hoạch/mẫu thiết kế thời trang và ngược lại.

## 4. Nhân sự, CV và chấm công

1. Chọn đúng mảng rồi mở danh sách nhân sự.
2. Tạo/cập nhật hồ sơ, gán Business Unit và phòng ban.
3. Chọn loại làm việc: **Toàn thời gian** (`FULL_TIME`) hoặc **Bán thời gian** (`PART_TIME`).
4. Với nhân sự bán thời gian, bắt buộc chọn **Theo giờ** (`HOURLY`) hoặc **Theo ca** (`SHIFT`) và nhập đơn giá lớn hơn 0.
5. Bổ sung CV bằng đường dẫn/tệp tham chiếu, tóm tắt chuyên môn, kỹ năng và kinh nghiệm.
6. Ghi hoặc nhập chấm công trong đúng mảng; kiểm tra tổng giờ, ngày công và số ca trước khi tính lương.

CV hiện được lưu qua trường đường dẫn `cvUrlOrPath` và các trường mô tả; không nhập CV vào mảng khác chỉ để tiện tìm kiếm.

## 5. Tính và duyệt lương

1. Tạo kỳ lương và chọn `businessUnitId`; kỳ đó chỉ tính nhân viên và chấm công thuộc Business Unit tương ứng.
2. Với nhân sự part-time/cộng tác viên theo sản phẩm, chọn cách tính **Theo đầu việc** trong hồ sơ; có thể lưu ngân hàng, số tài khoản và tên chủ tài khoản để phục vụ chi trả. Thêm từng đề đã đăng/đã hoàn thành, dự án, hoa hồng sale, học viên giới thiệu/tuyển được, marketing hoặc đầu việc khác bằng mã tham chiếu, tên việc, số lượng, đơn giá và chứng từ nếu có. Với khoản học viên, nhập mã học viên hoặc mã ghi danh làm tham chiếu và kiểm tra chứng từ trước khi chốt lương; hiện hệ thống chưa tự đối soát nguồn giới thiệu từ hồ sơ học viên. Tiền công tự cộng ngay khi nhập vào kỳ đã tính; kỳ nháp được cộng lúc tính lương. Mã tham chiếu không được ghi trùng cho cùng nhân sự và loại việc.
3. Thêm các khoản điều chỉnh nếu có: Trợ cấp, thưởng KPI, thưởng khác, làm thêm giờ, BHYT hoặc khấu trừ khác.
4. Chạy **Tính lương**, kiểm tra tiền công đầu việc riêng trên từng phiếu rồi thực hiện **Gửi duyệt → Phê duyệt → Đã chi → Phát hành**. Nếu nhập sai đầu việc trước khi gửi duyệt, dùng **Hủy** để đảo tiền công và giữ lịch sử; không xóa hẳn.
5. Nhân viên chỉ xem được phiếu cá nhân sau khi phiếu đã phát hành. Số tài khoản chỉ được trả qua mục thông tin thanh toán dành cho người có quyền quản lý nhân sự, không nằm trong danh sách nhân sự thông thường.

Cách tính hiện tại:

- Toàn thời gian: lương công theo ngày công trên chuẩn 22 ngày. Nếu hoàn toàn chưa có chấm công trong kỳ, hệ thống chặn tính lương và yêu cầu nhập/chốt chấm công.
- Bán thời gian: theo giờ, ca hoặc đầu việc. Theo giờ/ca không có chấm công thì lương công bằng 0; theo đầu việc cộng các dòng việc chưa hủy, không tự áp một đơn giá chung cho mọi loại việc.
- BHYT là khoản khấu trừ nhập bằng mã `HEALTH_INSURANCE`, chưa tự tính theo tỷ lệ.
- Thực lĩnh không âm: tổng thu nhập trừ BHYT và các khoản khấu trừ khác, tối thiểu bằng 0.

## 6. Khách hàng và thông tin nội bộ

Mỗi mảng có danh mục khách hàng riêng. Tìm kiếm/lọc theo nguồn và trạng thái, không sao chép khách hàng sang mảng còn lại nếu không có nghiệp vụ thực tế.

Kho thông tin nội bộ lưu metadata và vị trí tệp:

- Công nghệ - Giáo dục: **Đề thi/Đề bài** (`EXAM`) và **Tài liệu** (`DOCUMENT`).
- Thời trang: **Bản kế hoạch** (`PLAN`), **Mẫu thiết kế** (`DESIGN_SAMPLE`) và **Tài liệu** (`DOCUMENT`).

Khi tạo tài liệu, nhập mã, tiêu đề, loại, đường dẫn lưu trữ, phiên bản, trạng thái và tags. Có thể thêm tên tệp, MIME type, dung lượng, SHA-256 và metadata JSON. Không dán base64/data URI vào đường dẫn. Khi xóa metadata trong MOLY, tệp bên ngoài vẫn còn và phải được quản lý theo quy trình lưu trữ riêng.

## 7. Quy trình áo dài tự may

Áo dài MOLY mặc định là **MAKE — tự may/sản xuất**:

1. Tạo sản phẩm và biến thể theo mẫu, size, màu; kiểm tra SKU không trùng.
2. Nhập vải/phụ liệu theo `MaterialLot`, nhà cung cấp, đơn vị và giá lô.
3. Tạo BOM version đúng mẫu/size/màu, định mức và hao hụt dự kiến.
4. Phát hành `ProductionOrder`; thực hiện xuất nguyên liệu và ghi công đoạn.
5. Ghi nhân công, gia công ngoài, hao hụt/sửa lại, hàng tốt/hàng lỗi và overhead.
6. Đóng lệnh để chốt `ACTUAL_COST`; kiểm tra variance so với BOM/standard cost.
7. Chỉ sau khi cost đủ dữ liệu mới dùng thành phẩm cho COGS/lợi nhuận đã chốt.

Không nhập áo dài tự may như hàng mua xưởng để làm giảm giả tạo giá vốn.

## 8. Bán hàng và đổi trả

1. Tạo/import đơn theo SKU hoặc product variant; kiểm tra source reference không trùng.
2. Reserve tồn → xác nhận giao → trừ tồn; không chỉnh trực tiếp số dư thay cho movement. Nếu hai đơn cùng tranh số lượng cuối cùng, một đơn sẽ nhận thông báo tồn kho vừa thay đổi và cần tải lại trước khi thử lại.
3. Kiểm hàng hoàn: hàng đủ điều kiện nhập lại; hàng lỗi/hỏng đi theo trạng thái riêng.
4. Kiểm tra snapshot giá bán, phí kênh, voucher, affiliate, ship, tax, refund và COGS trong đơn.

## 9. Tài chính và lợi nhuận

- Dùng Thu/Chi với reference/idempotency; không tạo lại giao dịch khi retry.
- Gán đúng Business Unit cho từng phiếu thu/chi để dòng tiền đi vào đúng mảng.
- Xem **Bảng tổng công ty** để đối chiếu Công nghệ - Giáo dục, Thời trang và tổng hai mảng trong cùng kỳ.
- Giao dịch chưa gán đúng mảng nằm ở mục **Dòng tiền chưa phân loại**, không được tính vào tổng hai mảng; phải phân loại trước khi chốt báo cáo.
- Xem báo cáo theo Business Unit, mẫu, size, production batch, kênh và đơn.
- Phân biệt `STANDARD_COST`, `ESTIMATED_COST`, `ACTUAL_COST`.
- Lợi nhuận Fashion chưa có giá vốn `Actual` được hiển thị là tạm tính, không được coi là lợi nhuận đã chốt.
- Pricing Simulator dùng để xem giá hòa vốn, target margin và cảnh báo chương trình làm lỗ.

## 10. Khi có lỗi

- Ghi lại màn hình, thời gian, tài khoản/role, mã đơn/reference và thông báo tiếng Việt.
- Không tự xóa dữ liệu, sửa số dư kho hoặc sửa migration history.
- Với lỗi đồng bộ, xem Integration Runs/Dead-Letter và dùng Retry sau khi nguyên nhân đã rõ.
- Với lỗi quyền, liên hệ quản trị viên; không dùng tài khoản admin để “lách” quy trình.
