# MOLY Internal Management — MVP Limitations v0.1.0

Ngày lập: 2026-08-19

Đây là danh sách giới hạn có chủ ý của bản MVP. Mục đích là để demo, vận hành và release không tạo kỳ vọng sai về dữ liệu hoặc mức độ tự động hóa.

## Giới hạn và tác động

| Mã | Giới hạn | Tác động | Cách xử lý trong MVP | Điều kiện đóng giới hạn |
|---|---|---|---|---|
| L-01 | Chưa có credential live trong repository/workspace hiện tại | Chưa thể xác nhận count và tổng tiền thật từ hai website | Nạp secret trực tiếp trong Railway; ghi run ID và kết quả, không ghi token | Có biên bản đối soát live cho từng entity |
| L-02 | Endpoint Payments của `CSCA_MOLI_STUDIO` chưa được website xác nhận là read-only | Không thể nghiệm thu đầy đủ dòng tiền EdTech | Không gọi fallback/scrape; giữ Payments ở trạng thái blocked | Có endpoint, schema, auth scope và test read-only |
| L-03 | Connector chỉ dùng REST API được công bố | Không hỗ trợ đọc trực tiếp database hoặc scrape HTML | Nếu chưa có API thì ghi nhận limitation hoặc dùng file import được phê duyệt | API chính thức có rate limit, schema và owner |
| L-04 | Đồng bộ là read-only replica, website vẫn là source of truth giai đoạn đầu | Có thể có độ trễ hoặc delta tạm thời giữa nguồn và nội bộ | Lưu run, cursor, checkpoint, idempotency và dead-letter | Có lịch sync, SLA freshness và quy trình xử lý delta |
| L-05 | Shopee/TikTok chưa phải connector production trong MVP | Đơn sàn chưa tự động kéo bằng webhook/API chính thức | Dùng import chuẩn hóa và mô hình domain hiện có | Hoàn tất app approval, webhook signature, retry và reconciliation |
| L-06 | CSCA users/exams/stats là route private và chưa có credential trong workspace | Admin mới đọc được tài liệu, từ vựng và cộng đồng public | Nạp Bearer token/integration key qua secret store rồi chốt schema admin | Có contract + scope + test read-only cho từng route |
| L-07 | WPF có smoke flow nhưng chưa phải installer ký số production | Phân phối máy trạm còn cần thao tác vận hành | Build/publish nội bộ theo môi trường; không coi binary debug là release | Có publish profile, installer, signing và hướng dẫn nâng cấp |
| L-08 | Backup/restore đã có script và test path nhưng retention/DR production chưa chốt | Chưa có cam kết RPO/RTO production | Lưu dump ngoài source, kiểm tra SHA-256, restore thử trước khi dùng | Chốt retention, off-site copy, lịch chạy và restore drill |
| L-09 | Số liệu giá vốn/lợi nhuận phụ thuộc dữ liệu chi phí đầu vào và kỳ phân bổ | Báo cáo provisional nếu thiếu NVL, nhân công, gia công hoặc overhead | Hiển thị nguồn/ghi chú; không gọi số provisional là actual | Đủ source documents và phê duyệt kỳ costing |
| L-10 | Phân quyền được kiểm tra ở API/WPF nhưng quyền kinh doanh chi tiết còn cần UAT | Người dùng có thể xem đúng module nhưng vẫn cần xác nhận workflow | Dùng role/permission hiện có và checklist demo | Owner nghiệp vụ ký matrix quyền và negative test |
| L-11 | Monitoring/alert production chưa hoàn tất trong MVP | Lỗi connector có thể cần kiểm tra thủ công qua sync runs/dead-letter | Dùng health endpoint, log và màn hình vận hành | Có metrics, alert owner, ngưỡng và escalation |

## Không được làm trong MVP

- Không đưa Bearer token, IntegrationKey, JWT secret, database password hoặc refresh token vào source, log, screenshot hay CSV export.
- Không đánh dấu “đối soát khớp” dựa trên số liệu ước tính hoặc số trong tài liệu thiết kế.
- Không retry vô hạn khi API trả 401/403; cần sửa secret/scope trước khi retry.
- Không chạy restore destructive lên production nếu chưa có backup hiện tại và xác nhận database đích.
- Không tự động ghi ngược website; toàn bộ connector MVP chỉ read-only.

## Tiêu chí nâng từ MVP lên production-ready

1. Có credential/scope tách riêng cho staging và production, rotation owner và audit trail.
2. Có đối soát live cho Courses, Questions, Customers, Subscriptions, InterviewCustomers; Payments chỉ ký sau khi endpoint read-only được xác nhận.
3. Có test replay/idempotency trên run thật, không chỉ trên mock.
4. Có migration review và rollback/restore drill trong môi trường production-like.
5. Có installer WPF, logging/monitoring, alerting và quy trình hỗ trợ người dùng.
