# MOLY Internal Management — Backlog Tháng 2

Ngày lập: 2026-08-19  
Mục tiêu: đóng các giới hạn MVP và đưa đồng bộ read-only từ mức demo lên vận hành production có kiểm soát.

## Ưu tiên P0 — cần hoàn tất trước khi ký production

| ID | Hạng mục | Kết quả cần có | Phụ thuộc |
|---|---|---|---|
| M2-001 | Chốt Payments read-only cho CSCA Moli Studio | Endpoint/path, schema, auth scope, mapping, validation và test contract; sync có run/checkpoint | Website CSCA, owner API |
| M2-002 | Đối soát live hai website | Biên bản count/tổng tiền theo entity, delta có giải thích, lưu run ID và evidence | Railway secrets, quyền admin, API availability |
| M2-003 | Credential lifecycle | Tách secret staging/production, owner, rotation, revoke khi nghỉ việc và không lộ trong log | Railway access, security owner |
| M2-004 | Production migration/rollback drill | Chạy migration trên database production-like, backup trước/sau, restore drill và rollback decision tree | Production-like PostgreSQL |
| M2-005 | Reliability dashboard | Health, sync success/failure, duration, records, rate-limit, dead-letter và alert owner | Logging/metrics platform |

## Ưu tiên P1 — vận hành hằng ngày

| ID | Hạng mục | Kết quả cần có | Phụ thuộc |
|---|---|---|---|
| M2-006 | Dead-letter operations | Filter theo source/entity/error, retry có giới hạn, bulk review và audit reason | M2-005 |
| M2-007 | Freshness/SLA | Lịch sync, watermark/cursor, cảnh báo stale data và báo cáo lần chạy gần nhất | M2-005, connector owner |
| M2-008 | Import reconciliation | Import file chuẩn hóa có preview, validation, duplicate report, error export và đối soát sau import | Sales/channel owners |
| M2-009 | WPF release package | Publish profile, installer, versioning, code signing, upgrade/uninstall và hướng dẫn triển khai | Windows packaging owner |
| M2-010 | Permission UAT | Matrix role-permission có owner ký, positive/negative test cho Finance, EdTech, CSCA, Interview và sync | Business owners |
| M2-011 | Data quality report | Báo missing mapping, duplicate source identity, invalid amount/date và record bị dead-letter | M2-006, domain owners |

## Ưu tiên P2 — mở rộng kênh và trải nghiệm

| ID | Hạng mục | Kết quả cần có | Phụ thuộc |
|---|---|---|---|
| M2-012 | Shopee Open Platform connector | OAuth/app approval, pull order/product/inventory read-only, retry, cursor và reconciliation | Shopee approval |
| M2-013 | TikTok Shop Partner connector | OAuth/app approval, pull order/product/inventory read-only, webhook signature và replay test | TikTok approval |
| M2-014 | Revenue/expense allocation | Business-unit mapping, period close, adjustment audit và export cho kế toán | Finance owner |
| M2-015 | Customer 360 | Hợp nhất customer identity từ EdTech, Interview và sales channel với merge audit | Data owner |
| M2-016 | Vietnamese operational UX | Chuẩn hóa label, tooltip, error catalog, keyboard flow và localized date/number format | WPF owner |
| M2-017 | Audit/export governance | Retention, redaction, export permission, watermark và audit event cho file tải xuống | Security owner |

## Definition of Done dùng chung

Một backlog item chỉ được đóng khi:

1. Có acceptance criteria và owner được ghi trong ticket/release note.
2. Có unit/integration test phù hợp; connector phải có timeout, retry, rate-limit, duplicate và partial-failure test.
3. Có log không chứa secret và có correlation/run ID để truy vết.
4. Có migration/rollback impact review nếu chạm schema hoặc dữ liệu đã tồn tại.
5. Có tài liệu vận hành và evidence trên staging; hạng mục P0 phải có evidence production-like trước khi ký.

## Thứ tự đề xuất trong tháng 2

`M2-001 → M2-002 → M2-003 → M2-004 → M2-005 → M2-006/M2-007 → M2-008/M2-009/M2-010 → M2-011 → M2-012/M2-013 → M2-014…M2-017`

Nếu Payments hoặc credential chưa sẵn sàng, không lấy số liệu mock để thay thế biên bản live; chuyển M2-002 sang trạng thái blocked và ưu tiên M2-003/M2-004/M2-005.
