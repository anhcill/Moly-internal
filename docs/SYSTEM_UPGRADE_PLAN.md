# Kế hoạch nâng cấp hệ thống MOLY — liên kết dữ liệu & vận hành liên mảng

> Trạng thái: nền P0 và các hook thu/chi P1 cho Education/CSCA/Mock Interview/Payroll đã triển khai trong mã nguồn; chờ chạy migration và backfill trên staging/production.  
> Phạm vi: Công nghệ - Giáo dục, CSCA, Mock Interview, Fashion, Nhân sự - lương và Tài chính  
> Nguyên tắc: hoàn thiện liên kết nghiệp vụ trước khi mở rộng thêm màn hình hoặc tích hợp mới.

## 1. Mục tiêu đích

Mỗi nghiệp vụ phải đi từ nguồn phát sinh đến báo cáo mà không nhập tay lặp lại và không mất khả năng truy vết.

```text
Khách hàng / nhân sự / sản phẩm (master data)
             ↓
Nghiệp vụ phát sinh: đơn hàng, thanh toán, lớp học, sản xuất, chấm công
             ↓
Chứng từ bất biến: giao hàng, hoàn tiền, xuất kho, phiếu lương, thu/chi
             ↓
Sổ tài chính + snapshot giá vốn/lợi nhuận
             ↓
Báo cáo theo công ty → mảng → đơn vị → chứng từ gốc
```

Hệ thống sau nâng cấp cần trả lời ngay được các câu hỏi sau:

- Một khách hàng đã mua gì, học lớp nào, còn công nợ bao nhiêu và đã thanh toán qua đâu?
- Một đơn Fashion đã giao/chưa giao, giá vốn lấy từ lệnh sản xuất nào, đã thu tiền chưa và lợi nhuận thực tế bao nhiêu?
- Một kỳ lương đã chi bao nhiêu, đã tạo chứng từ chi chưa và thuộc đơn vị nào?
- Một số trên dashboard mở ra được danh sách chứng từ gốc, không chỉ là một con số tổng.

## 0. Phần đã triển khai trong P0

Các hạng mục dưới đây đã có mã nguồn, migration và kiểm thử; chúng chưa tự thay đổi dữ liệu của môi trường thật cho đến khi quản trị viên chạy migration và backfill theo runbook ở mục 5.1.

| Hạng mục | Cách hoạt động hiện tại | Phạm vi |
|---|---|---|
| Party master | Thêm `Party`, contact chuẩn hóa, external identity và business profile. Resolver ưu tiên `sourceSystem + sourceId`, chỉ dùng email/điện thoại khi kết quả chỉ có đúng một party. | Education, Interview, CSCA student, khách nội bộ, supplier, Fashion order. |
| Registry chứng từ | Thêm `BusinessDocument` và `BusinessDocumentLink`, có khóa unique `(Company, DocumentType, SourceEntityType, SourceEntityId)` để gọi lại không tạo trùng. | Payment EdTech, ghi danh CSCA, dịch vụ Interview, đơn/phiếu bán/đổi trả/settlement Fashion, phiếu nhập, kỳ lương, giao dịch Finance. |
| Luồng tạo mới | Đồng bộ customer/Interview/payment, ghi danh CSCA, tạo/cập nhật Mock Interview, tạo Fashion sales order/receipt/return/settlement, tạo purchase receipt, tạo kỳ lương và tạo Finance transaction đều gắn Party/BusinessDocument phù hợp. | Dữ liệu phát sinh sau cutover. |
| Finance posting tự động | `FinancePostingService` tạo/cập nhật duy nhất một `FinanceTransaction` theo `(Company, SourceEntityType, SourceEntityId, TransactionType)`, đồng thời tạo registry document và link `Settlement`. | Payment EdTech, học phí CSCA, dịch vụ Mock Interview `Paid/Partial` sinh Thu; `Refunded` sinh Chi; payroll `Paid` sinh Chi tổng kỳ; Fashion settlement `Confirmed` sinh Thu/Chi theo loại. Chưa gán `CashAccount` hoặc đối soát sao kê tự động. |
| Backfill có kiểm soát | `POST /api/v1/system/sync/master-data/backfill?batchSize=100`, yêu cầu quyền `SystemSyncTrigger`; chạy lặp an toàn cho link còn thiếu và transaction tài chính chưa có. | Dữ liệu cũ, chạy theo batch 1–500; response có thêm `financeTransactionsPosted`. |

Kiểm thử tự động đã xác nhận: backfill Customer → Payment → Finance transaction và backfill CSCA/Mock Interview tạo Party, registry document, posting và settlement link đúng một lần; chạy lại cho kết quả `0` bản ghi mới.

## 2. Hiện trạng đã xác nhận

| Nhóm | Hiện có | Khoảng trống cần xử lý |
|---|---|---|
| Phạm vi dữ liệu | `CompanyId`, `BusinessUnitId`, RBAC và phân hệ tách mảng | Chưa có một chuẩn tham chiếu nghiệp vụ dùng chung giữa mọi module. |
| Tài chính | `FinanceTransaction` có `ReferenceType`/`ReferenceId`, báo cáo công ty đã tách Education/Fashion | Tham chiếu là chuỗi tự do, không có foreign key; các giao dịch Fashion, payment EdTech và chi lương chưa tự sinh sổ tiền. |
| Fashion | SKU, kho, BOM, sản xuất, đơn hàng, giao hàng, hoàn trả và snapshot giá vốn/lợi nhuận đã có | `SalesOrder` mới lưu tên/số điện thoại khách dạng snapshot, chưa liên kết khách hàng chuẩn; trạng thái đơn và trạng thái thu tiền chưa được đối soát bằng chứng từ tiền. |
| Giáo dục / CSCA | Khóa học, lớp, lịch, học viên, nhân sự lớp, payment và lợi nhuận theo lớp đã có | `CscaClassStudent` lưu thông tin học viên trực tiếp, chưa liên kết `EdTechCustomer`; payment, công nợ và ghi nhận doanh thu chưa đi qua sổ tài chính thống nhất. |
| Mock Interview | Khách hàng/phí dịch vụ và profit allocation đã có | `InterviewCustomer` là silo riêng, chưa dùng khách hàng chuẩn và chưa có lịch buổi dịch vụ/chứng từ thu tiền chuẩn. |
| HR / Payroll | Nhân sự, chấm công, kỳ lương, phiếu lương và workflow duyệt đã có | Khi đánh dấu đã chi lương chưa tạo chứng từ chi tài chính; chưa đối soát một-một với giao dịch ngân hàng/tiền mặt. |
| Đồng bộ ngoài | Inbox, run, dead-letter, connector abstraction đã có | Entity mapper và xử lý retry dead-letter còn stub; chưa có quy trình đối soát dữ liệu nguồn với dữ liệu nội bộ. |

Các khoảng trống trên là lý do dashboard có thể hiển thị số liệu nhưng chưa bảo đảm mọi số liệu đi được về chứng từ gốc.

## 3. Quy tắc kiến trúc cần khóa trước

1. **Nguồn dữ liệu duy nhất theo loại số liệu**
   - Tiền đã thu/đã chi: `FinanceTransaction` và đối soát ngân hàng.
   - Tồn kho: `InventoryMovement`; `InventoryBalance` chỉ là số dư tối ưu hóa.
   - Giá vốn/lợi nhuận Fashion: `OrderCostSnapshot` phiên bản mới nhất của từng đơn.
   - Lương phải trả: `Payslip` đã duyệt; tiền đã chi: chứng từ `FinanceTransaction` tham chiếu kỳ lương/phiếu chi.
2. **Không sửa lịch sử đã chốt.** Sửa bằng adjustment, reversal hoặc snapshot phiên bản mới; không ghi đè chứng từ cũ.
3. **Mọi chứng từ có mảng/đơn vị, trạng thái, người tạo, thời điểm tạo và mã tham chiếu.**
4. **Mỗi job tạo chứng từ tài chính phải idempotent.** Cùng một `SourceType + SourceId + EntryType` chỉ sinh một chứng từ; reversal dùng mã riêng.
5. **Snapshot giữ dữ liệu hiển thị lịch sử, foreign key giữ truy vết chuẩn.** Ví dụ hóa đơn giữ tên/địa chỉ giao lúc bán, đồng thời liên kết `PartyId` nếu nhận diện được khách.
6. **Không dùng `ReferenceType` chuỗi tùy ý làm quan hệ lõi mới.** Trong giai đoạn chuyển tiếp nó vẫn tồn tại, nhưng phải dùng bộ mã cố định và bảng liên kết chứng từ.
7. **Tách quyền học khỏi việc tồn tại tài khoản.** Học sinh có thể được provision ở trạng thái `PendingPayment`, nhưng chỉ grant `Active` mới được vào LMS.
8. **InternalManagement là nguồn chính cho học sinh, lớp, học phí và access policy; CSCA Course LMS là nguồn chính cho nội dung và learning records.** Hai nguồn không được cùng sửa một field nghiệp vụ.
9. **Provisioning LMS phải qua API machine-to-machine có HMAC, Idempotency-Key và outbox/inbox.** Không truy cập trực tiếp database LMS, không gửi password hoặc token giữa hai hệ thống.

## 4. Mô hình dữ liệu đích

### 4.1. Master party — hợp nhất khách hàng và đối tác

Tạo các bảng:

```text
parties(id, company_id, party_type, display_name, tax_code, status, ...)
party_contacts(id, party_id, contact_type, value_normalized, is_primary, ...)
party_external_identities(id, party_id, source_system, source_id, ...)
party_business_profiles(id, party_id, business_unit_id, lifecycle_status, owner_employee_id, ...)
```

`party_type` hiện tại: `Individual`, `Organization`. Vai trò nghiệp vụ được tách trong `PartyBusinessProfile`: `Customer`, `Student`, `Supplier`, `Partner`; một Party có thể có nhiều vai trò.

Ánh xạ chuyển đổi:

| Bảng cũ | Liên kết mới bắt buộc | Dữ liệu snapshot vẫn giữ |
|---|---|---|
| `EdTechCustomer` | `PartyId` | Tên/email/số điện thoại đồng bộ tại thời điểm nhận dữ liệu |
| `CscaClassStudent` | `PartyId` hoặc `StudentProfileId` liên kết `PartyId` | Tên, lớp, tuổi tại thời điểm ghi danh |
| `InterviewCustomer` | `PartyId` | Gói dịch vụ và thông tin lúc mua |
| `InternalCustomer` | `PartyId` | Mã nội bộ, ghi chú nghiệp vụ |
| `SalesOrder`, `SalesDocument` | `CustomerPartyId` nullable với cờ `CustomerMatchStatus` | Tên, điện thoại, địa chỉ giao hàng tại thời điểm bán |
| `Supplier` | `PartyId` | Mã NCC và thông tin nhập kho lịch sử |

Quy tắc ghép dữ liệu: ưu tiên `source_system + source_id`, tiếp theo email/số điện thoại đã chuẩn hóa; trường hợp nghi ngờ tạo hàng chờ để người dùng hợp nhất, tuyệt đối không tự gộp theo tên.

### 4.2. Sổ chứng từ nghiệp vụ và tài chính

Thay vì để từng module tự gắn chuỗi `ReferenceType`, bổ sung:

```text
business_documents(id, company_id, business_unit_id, document_type,
                   document_number, status, issued_at, source_type, source_id,
                   party_id, currency, total_amount, ...)
business_document_links(id, from_document_id, to_document_id, link_type, ...)
finance_transactions(..., business_document_id, cash_account_id, reconciliation_status)
finance_transaction_lines(id, finance_transaction_id, category_id, amount, tax_amount, ...)
bank_statement_lines(id, cash_account_id, transaction_at, amount, bank_reference, ...)
bank_reconciliations(id, bank_statement_line_id, finance_transaction_id, matched_by, ...)
```

`business_documents` là registry/chứng từ bao bọc, không thay thế bảng chi tiết như `SalesOrder`, `Payslip`, `Payment` hay `PurchaseReceipt`. Nó tạo đường liên kết thống nhất tới tài chính, file đính kèm, audit và báo cáo.

### 4.3. Liên kết cần có theo từng luồng

#### Giáo dục và CSCA

```text
Party
  → Enrollment / CscaClassStudent
  → Course hoặc CscaClass
  → TuitionInvoice / PaymentSchedule
  → Payment
  → FinanceTransaction (Income)
  → ProfitAllocation (nếu là snapshot P&L của lớp)
```

- Thêm `Enrollment` chuẩn cho ghi danh; một khách có thể học nhiều khóa/lớp.
- Tách `Invoice/Receivable` và `Payment`: công nợ không được suy ra chỉ từ `PaidAmount`.
- `CscaClassStaff` phải có quy tắc chi phí: `included_in_payroll`, `payable_amount`, `payroll_period_id` hoặc `finance_transaction_id` để tránh ghi chi hai lần.
- `Payment` từ website/sync phải tạo transaction tài chính idempotent khi trạng thái là paid/settled; refund tạo reversal hoặc expense riêng.
- `CscaClassStudent` liên kết với LMS qua `CSCA_COURSE_LMS` bằng `sourceSystem + sourceId`, không ghép chỉ theo tên.
- Khi `PaymentStatus = Paid` và `PaidAmount >= TuitionFee`, Management phát event provision/access sau khi transaction thanh toán commit; LMS tạo hoặc kích hoạt user, grant và enrollment theo course mapping.
- `Pending`/`Partial` không có quyền học; `Refunded`/`Cancelled`/`Failed` revoke grant nhưng không xóa account hoặc learning progress.
- Dữ liệu LMS trả về Management gồm course metadata, enrollment, progress summary, assignment/quiz result, attendance và certificate; password, token, signed video URL và binary video không đồng bộ.
- Contract chi tiết nằm tại [CSCA_COURSE_LMS_DATA_CONTRACT.md](CSCA_COURSE_LMS_DATA_CONTRACT.md).

#### Mock Interview

```text
Party → ServicePackage → ServiceOrder → SessionSchedule
      → ServiceInvoice / Payment → FinanceTransaction
      → ProfitAllocation
```

- Chuyển `InterviewCustomer` thành profile/record dịch vụ liên kết `PartyId`, không xóa dữ liệu cũ.
- Thêm lịch buổi, người thực hiện, trạng thái hoàn thành/cancel/no-show để số buổi và chi phí có chứng cứ.

#### Fashion

```text
Supplier Party → PurchaseReceipt → MaterialLot → MaterialMovement
Product/SKU → BOM → ProductionOrder → ProductionOutput → InventoryMovement
Customer Party → SalesOrder → Delivery → SalesDocument → Payment/Settlement
                    ↓                       ↓
              OrderCostSnapshot        FinanceTransaction
                    ↓                       ↓
              Profit report            Bank reconciliation
```

- `SalesOrder` và `SalesDocument` thêm `CustomerPartyId`; trường dữ liệu người mua hiện có tiếp tục là snapshot.
- Tách rõ `OrderStatus`, `FulfillmentStatus`, `PaymentStatus`, `RefundStatus`; không dùng một status để diễn đạt cả giao hàng và đã thu tiền.
- Khi giao hàng: tạo inventory movement, chỉ ghi COGS theo số lượng thực giao.
- Khi payment được xác nhận: sinh `FinanceTransaction` thu tiền, liên kết sales document/order; fee của sàn, shipping subsidy và refund sinh các dòng chi/reversal có mã tham chiếu.
- Khi hoàn hàng được duyệt: cập nhật tồn theo kết quả kiểm định, sinh refund/reversal, snapshot lại lợi nhuận.
- Material/production/purchase phải có liên kết chi phí tài chính để đối soát `actual manufacturing cost` với tiền đã chi, nhưng không tự coi toàn bộ tiền mua là COGS trước khi NVL được tiêu hao.

#### Nhân sự và lương

```text
Employee → AttendanceRecord → PayrollPeriod → Payslip → PayrollApproval
                                              ↓
                         PayrollDisbursement / FinanceTransaction (Expense)
                                              ↓
                                   Bank reconciliation / payment proof
```

- Khi kỳ lương chuyển `Paid`, tạo một `PayrollDisbursement` và transaction chi tổng; tùy chính sách có thể thêm transaction chi theo từng nhân viên.
- Khóa `Payslip` đã chi, mọi hoàn/điều chỉnh sau đó qua adjustment kỳ sau hoặc reversal chứng từ chi.
- Liên kết chi phí nhân công của `CscaClassStaff` / `ProductionOperation` với payroll hoặc chi ngoài để tránh P&L thiếu hay ghi đúp.

## 5. Thứ tự triển khai bắt buộc

### Giai đoạn 0 — Chuẩn bị và làm sạch dữ liệu

1. Chốt data dictionary: mã trạng thái, document type, payment status, channel, currency, time zone và danh mục thu/chi.
2. Lập báo cáo chất lượng dữ liệu theo từng mảng: null business unit, mã trùng, khách trùng email/số điện thoại, chứng từ không tham chiếu, tồn kho âm, đơn không có snapshot giá vốn.
3. Sao lưu database, chạy migration trên staging với bản sao dữ liệu thật đã ẩn thông tin nhạy cảm.
4. Gắn `row_version`/optimistic concurrency cho các entity còn thiếu để tránh ghi đè khi nhiều người thao tác.
5. Tạo dashboard kiểm soát dữ liệu: số bản ghi chưa phân loại, giao dịch không đối soát, đơn không có khách chuẩn, payment chưa vào sổ tiền.

**Điều kiện hoàn tất:** có baseline trước nâng cấp và rollback script được diễn tập.

### Giai đoạn 1 — Party master và registry chứng từ (P0)

1. **Đã làm:** migration `AddPartyMasterAndBusinessDocumentLinks` tạo `parties`, `party_contacts`, `party_external_identities`, `party_business_profiles`, `business_documents`, `business_document_links`.
2. **Đã làm:** bổ sung foreign key nullable cho bảng cũ; không xóa dữ liệu snapshot hiện tại.
3. **Đã làm:** batch backfill theo thứ tự source identity → email/số điện thoại chuẩn hóa; trường hợp không có định danh tin cậy vẫn tạo Party riêng thay vì tự gộp theo tên.
4. Xây API tra cứu/hợp nhất party, lịch sử 360° và màn hình review trùng dữ liệu.
5. Chỉ cho phép merge qua service có audit, lock và bản ghi alias; không xóa party đã có chứng từ.

**Kiểm thử bắt buộc:** merge hai party có payment, class enrollment và sales order; lịch sử vẫn đầy đủ, không làm thay đổi chứng từ cũ.

### 5.1. Runbook cutover P0

1. Sao lưu database và khôi phục thử vào staging. Không chạy backfill trên production trước khi xác nhận bản sao staging.
2. Cập nhật API/Desktop cùng phiên bản mã nguồn rồi chạy lần lượt migration `20260829113804_AddPartyMasterAndBusinessDocumentLinks` và `20260829124906_AddCscaEnrollmentAndFashionSettlementLinks` bằng connection string production được quản lý trong secret store.
3. Kiểm tra migration thành công: các bảng `parties`, `business_documents`, `business_document_links`, `sales_settlements` tồn tại; các cột `party_id`/`customer_party_id`/`business_document_id` mới đều nullable, gồm `csca_class_students.business_document_id` và `returns.business_document_id`.
4. Với tài khoản có quyền `SystemSyncTrigger`, gọi `POST /api/v1/system/sync/master-data/backfill?batchSize=100`. Bắt đầu `100`; với dữ liệu lớn theo dõi CPU, thời gian lock và logs rồi mới tăng tối đa `500`.
5. Ghi lại response `partiesLinked`, `documentsLinked`, `documentLinksCreated`, `financeTransactionsPosted`. Gọi lại cùng endpoint: response kỳ vọng là toàn bộ `0`, chứng minh job idempotent.
6. Đối soát tối thiểu: số Payment, CscaClassStudent, InterviewCustomer, PayrollPeriod và Return có `BusinessDocumentId`; các bản ghi `Paid/Partial` có đúng một `FinanceTransaction` Thu, `Refunded` có đúng một Chi và payroll `Paid` có đúng một Chi. Fashion chỉ tạo Thu/Chi sau khi có `SalesSettlement` trạng thái `Confirmed`; mỗi giao dịch có link settlement về chứng từ gốc.
7. Nếu có số liệu bất thường, dừng endpoint; foreign key mới là nullable nên luồng cũ vẫn hoạt động. Phân tích dữ liệu trên staging trước khi chạy batch tiếp theo. Không xóa Party hay BusinessDocument trực tiếp để “làm lại”; dùng job backfill idempotent hoặc một migration sửa dữ liệu có audit.

### Giai đoạn 2 — Sổ tài chính tự động và đối soát (P0)

1. **Đã làm một phần:** `FinanceTransaction` có `BusinessDocumentId`; `CashAccountId` và `ReconciliationStatus` còn thuộc đợt đối soát ngân hàng.
2. **Đã làm:** `FinancePostingService` dùng khóa idempotent theo chứng từ gốc và loại Thu/Chi. Retry cùng event không tạo dòng mới; nguồn điều chỉnh số tiền sẽ cập nhật đúng dòng nguồn trước khi module đối soát ngân hàng được bổ sung.
3. **Đã gắn hook:**
   - EdTech payment, học phí CSCA và dịch vụ Mock Interview: `Paid`/`Partial` → Thu, `Refunded` → Chi;
   - Fashion `SalesSettlement`: `CustomerPayment + Confirmed` → Thu; `CustomerRefund + Confirmed` → Chi. Duyệt đổi trả không tự coi là đã hoàn tiền.
   - payroll `Paid` → Chi tổng kỳ lương.
   Backfill cũng tạo các posting còn thiếu cho các nguồn này sau khi registry document đã được gắn.
   **Còn lại:** phí sàn/shipping/marketing và chi mua NVL/gia công Fashion sau khi các phân hệ này có event thanh toán xác nhận.
4. Import sao kê ngân hàng, rule matching và hàng đợi đối soát thủ công.
5. Bổ sung màn hình drill-down: dashboard → finance transaction → business document → entity gốc.

**Quy tắc hiện tại:** auto-post không tự tăng/giảm số dư `CashAccount`, vì chưa biết chính xác tiền đi qua tài khoản ngân hàng/tiền mặt nào. Chỉ khi import sao kê hoặc người dùng xác nhận tài khoản tiền thì mới được cập nhật số dư và đánh dấu đối soát.

**Kiểm thử bắt buộc:** gửi lại cùng webhook/payment hai lần chỉ tạo một posting; refund tạo reversal; transaction không được phép đổi số tiền sau khi đối soát.

### Giai đoạn 3 — Hoàn thiện doanh thu và công nợ Giáo dục / Interview (P1)

1. Thêm enrollment, invoice/receivable, payment allocation và lịch sử công nợ.
2. **Đã làm phần liên kết:** CSCA student vào Party, chứng từ ghi danh và posting thu/hoàn idempotent. Còn quản lý chuyển lớp, bảo lưu, hủy học và `TuitionInvoice/PaymentSchedule` chuẩn.
3. **Đã làm phần liên kết:** Mock Interview tạo tay và đồng bộ ngoài đều gắn Party, chứng từ dịch vụ và posting thu/hoàn idempotent. Còn tách service order/session, gắn nhân sự thực hiện và chi phí.
4. Tính P&L theo lớp/dịch vụ từ chứng từ, với snapshot nhằm khóa báo cáo kỳ đã đóng.
5. Hiển thị danh sách “chưa thu đủ”, “sắp đến hạn”, “đã thu nhưng chưa đối soát”.
6. Bổ sung contract và connector `CSCA_COURSE_LMS`: provision account `PendingPayment`, cấp/revoke access theo payment, đồng bộ enrollment và learning summary.
7. Tạo hàng chờ manual review cho email/external ID bị trùng; không tự gộp học sinh hoặc cấp nhầm quyền.

### Giai đoạn 4 — Fashion order-to-cash và production-to-cost (P1)

1. **Đã làm phần settlement:** thêm `SalesSettlement` với `PaymentReference` unique theo công ty, loại Thu khách/Hoàn khách và trạng thái `Pending/Confirmed/Failed/Voided`; không suy diễn tiền từ trạng thái đơn hoặc phiếu đổi trả.
2. **Đã làm phần lifecycle tiền:** `POST /api/v1/orders/{id}/settlements` chỉ posting sổ tài chính khi `Confirmed`; settlement đã xác nhận không cho sửa và retry cùng `PaymentReference` trả về cùng bản ghi. Lifecycle vận hành vẫn là reserve → deliver → invoice/receipt → return, còn settlement là bước tiền tách riêng.
3. Liên kết `PurchaseReceipt`, material lots, production orders, inventory movements và financial expense theo chứng từ gốc.
4. Tạo reports theo SKU/size/kênh/lệnh sản xuất: doanh thu, COGS actual/provisional, phí sàn, hoàn, margin.
5. Mở submenu Fashion mới trong phiên bản sau: `Đơn hàng & giao vận`, `Đổi trả & hoàn tiền`, `Báo cáo lợi nhuận`; không nhồi các bảng này vào tab sản xuất.

### Giai đoạn 5 — Chi phí nhân sự và đóng kỳ (P1)

1. Tạo payroll disbursement và liên kết payslip/period với chứng từ chi.
2. Quy định mapping chi phí nhân sự vào lớp CSCA, Mock Interview, sản xuất hoặc overhead; một chi phí chỉ có một nguồn ghi nhận chính.
3. Bổ sung monthly closing: kiểm tra dữ liệu chưa đối soát, snapshot P&L, khóa kỳ, tạo adjustment period nếu cần.
4. Cảnh báo blocking trước khi đóng kỳ: đơn chưa giao nhưng đã ghi doanh thu, payment chưa vào finance, payroll paid chưa có chứng từ chi, cost provisional.

### Giai đoạn 6 — Đồng bộ, quản trị dữ liệu và vận hành (P1)

1. Hoàn tất entity mapper cho từng nguồn; dead-letter retry phải chạy lại mapper thật, không chỉ đánh dấu resolved.
2. Dùng outbox/inbox transactionally cho posting tài chính và cập nhật báo cáo để tránh mất sự kiện.
3. Thêm reconciliation report giữa website/sàn, đơn nội bộ, thanh toán và sổ tài chính.
4. Thiết lập retention, quyền xem PII, masking log, backup restore drill và runbook sự cố.
5. Theo dõi metrics: độ trễ đồng bộ, số dead-letter, tỷ lệ match ngân hàng, giao dịch chưa phân loại, tỷ lệ cost actual.

## 6. Hợp đồng API và phân quyền

- Client không gửi `CompanyId` để quyết định tenant; API lấy từ user context.
- Các endpoint `POST /payments`, `POST /deliveries`, `POST /payroll-disbursements` phải nhận `Idempotency-Key`.
- Mọi endpoint chuyển trạng thái yêu cầu `rowVersion` và trả `409 Conflict` khi dữ liệu đã đổi.
- Party merge, finance posting, reconcile, close month và payroll paid cần permission riêng; không dùng quyền xem để cấp quyền chốt số liệu.
- API chi tiết phải trả `links`: `party`, `businessDocument`, `financeTransactions`, `attachments`, `auditTrail` để desktop mở drill-down mà không tự suy luận bằng text.
- Connector LMS dùng source code `CSCA_COURSE_LMS`; các mutation bắt buộc có `Idempotency-Key`, `X-Correlation-ID`, HMAC signature và audit.
- Quyền nên tách riêng: `LmsIntegration.View`, `LmsIntegration.Provision`, `LmsIntegration.AccessManage`, `LmsIntegration.Reconcile`, `LmsIntegration.DeadLettersManage`. Nhân viên chỉ xem học sinh không được tự cấp hoặc thu hồi quyền LMS.
- API nhận webhook phải lưu inbox trước khi xử lý; event trùng `(source_system, event_id)` trả kết quả idempotent.

## 7. Chỉ số nghiệm thu

| Chỉ số | Mục tiêu nghiệm thu |
|---|---|
| Giao dịch tiền có chứng từ gốc | 100% giao dịch tạo mới sau cutover có `BusinessDocumentId` hoặc loại manual được phê duyệt |
| Payment trùng | 0 posting trùng khi retry cùng event/idempotency key |
| Đơn Fashion có khách chuẩn | 100% đơn nhập mới có `CustomerPartyId` hoặc trạng thái `UNMATCHED` được đưa vào hàng chờ |
| Lương đã chi | 100% payroll period `Paid` có disbursement/finance posting tương ứng |
| P&L Fashion | 100% đơn đã giao hiển thị cost status; tổng profit drill-down được về snapshot và đơn gốc |
| Công nợ Education | Mỗi payment phân bổ vào invoice/receivable; không chỉ ghi tổng `PaidAmount` |
| Dữ liệu chưa liên kết | Dashboard hiển thị backlog theo loại và có owner xử lý |
| Đồng bộ | Event trùng không tạo trùng; dead-letter retry thực thi mapper và lưu kết quả |
| LMS account provisioning | 100% học sinh `Paid` đủ điều kiện có đúng một account link, access grant và enrollment tương ứng |
| LMS access safety | 100% `Pending`/`Partial` không lấy được LMS content hoặc signed playback URL |
| LMS revocation | `Refunded`/`Cancelled`/`Failed` thu hồi quyền nhưng giữ lại progress và audit |
| LMS reconciliation | Chênh lệch account, enrollment, access grant và payment hiển thị thành backlog có owner |

## 8. Điều không làm trong đợt đầu

- Không thay toàn bộ database bằng microservice hoặc event sourcing.
- Không xóa cột/bảng cũ trong cùng migration với backfill; chỉ đánh dấu deprecated sau khi báo cáo đối chiếu ổn định.
- Không tự hợp nhất khách trùng chỉ dựa vào tên.
- Không tính lợi nhuận Fashion từ `SellingPrice - CostPrice` nếu `CostStatus` chưa actual.
- Không ghi thẳng vào `FinanceTransaction` từ WPF; mọi posting đi qua API/service có idempotency và audit.

## 9. Thứ tự ưu tiên khi bắt đầu code

1. Data quality report + registry `BusinessDocument`.
2. Party master và backfill có duyệt thủ công.
3. ~~`FinancePostingService` + payment/paid payroll hooks.~~ **Đã hoàn thành cho EdTech payment và payroll paid.**
4. Fashion order-to-cash và party link.
5. Education receivable/payment allocation và CSCA enrollment link.
6. Bank reconciliation, monthly close, full integration mapper.

Thứ tự này tạo nền tảng để các màn hình mới đều dùng chung dữ liệu, thay vì tiếp tục tạo thêm các bảng tách rời.
