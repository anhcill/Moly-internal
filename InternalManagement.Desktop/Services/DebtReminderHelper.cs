using System.Globalization;

namespace InternalManagement.Desktop.Services;

public sealed record DebtAlertSummary(
    int OverdueCount,
    decimal OverdueAmount,
    int DueSoonCount,
    decimal DueSoonAmount);

public static class DebtReminderHelper
{
    private const int DueSoonWindowDays = 3;

    public static DebtAlertSummary Summarize(
        IEnumerable<ApiClient.FinanceReceivableItem>? receivables,
        DateTime? utcNow = null)
    {
        if (receivables is null)
        {
            return new DebtAlertSummary(0, 0m, 0, 0m);
        }

        var now = utcNow ?? DateTime.UtcNow;
        var dueSoonCutoff = now.AddDays(DueSoonWindowDays);

        var overdueCount = 0;
        var overdueAmount = 0m;
        var dueSoonCount = 0;
        var dueSoonAmount = 0m;

        foreach (var item in receivables)
        {
            if (item is null
                || string.Equals(item.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)
                || item.OutstandingAmount <= 0
                || !item.DueDate.HasValue)
            {
                continue;
            }

            var dueDate = item.DueDate.Value;
            if (dueDate < now)
            {
                overdueCount++;
                overdueAmount += item.OutstandingAmount;
            }
            else if (dueDate <= dueSoonCutoff)
            {
                dueSoonCount++;
                dueSoonAmount += item.OutstandingAmount;
            }
        }

        return new DebtAlertSummary(overdueCount, overdueAmount, dueSoonCount, dueSoonAmount);
    }

    public static string BuildZaloReminderMessage(
        ApiClient.FinanceReceivableItem row,
        DateTime? utcNow = null)
    {
        ArgumentNullException.ThrowIfNull(row);

        var now = utcNow ?? DateTime.UtcNow;
        var studentName = string.IsNullOrWhiteSpace(row.StudentName) ? "bạn" : row.StudentName.Trim();
        var classSegment = string.IsNullOrWhiteSpace(row.ClassName)
            ? "khóa học tại MOLY"
            : $"lớp {row.ClassName.Trim()}";
        var amountText = $"{row.OutstandingAmount:N0} đ";

        if (row.DueDate.HasValue)
        {
            var localDueDate = (row.LocalDueDate ?? row.DueDate.Value).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

            if (row.DueDate.Value < now)
            {
                return
                    $"Chào {studentName}, trung tâm MOLY xin gửi lời nhắc học phí {classSegment}. " +
                    $"Hiện khoản học phí còn lại là {amountText} và đã quá hạn thanh toán từ ngày {localDueDate}. " +
                    $"Anh/chị vui lòng sắp xếp hoàn tất sớm và gửi ảnh biên lai chuyển khoản giúp trung tâm nhé. " +
                    $"Nếu anh/chị đã thanh toán, xin vui lòng bỏ qua tin nhắn này. Cảm ơn anh/chị ạ!";
            }

            return
                $"Chào {studentName}, trung tâm MOLY xin nhẹ nhàng nhắc lịch học phí {classSegment}. " +
                $"Số tiền học phí còn lại là {amountText}, hạn thanh toán vào ngày {localDueDate}. " +
                $"Anh/chị vui lòng sắp xếp hoàn tất trước hạn và nhắn lại giúp trung tâm khi đã chuyển khoản nhé. Cảm ơn anh/chị ạ!";
        }

        return
            $"Chào {studentName}, trung tâm MOLY xin gửi thông tin học phí {classSegment}. " +
            $"Số tiền học phí còn lại hiện tại là {amountText}. " +
            $"Anh/chị vui lòng sắp xếp hoàn tất khi thuận tiện và nhắn lại giúp trung tâm sau khi chuyển khoản nhé. Cảm ơn anh/chị ạ!";
    }
}
