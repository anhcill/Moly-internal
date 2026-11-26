using System.Text;

namespace InternalManagement.Desktop.Services;

/// <summary>
/// Converts VND amounts into standard Vietnamese accounting words ("Số tiền bằng chữ")
/// for official receipts, payroll statements, and financial reports.
/// </summary>
public static class VietnameseNumberToWords
{
    private static readonly string[] Digits = ["không", "một", "hai", "ba", "bốn", "năm", "sáu", "bảy", "tám", "chín"];
    private static readonly string[] Scales = ["", "nghìn", "triệu", "tỷ", "nghìn tỷ", "triệu tỷ"];

    public static string ToVndWords(decimal amount)
    {
        var rounded = (long)Math.Round(amount, 0, MidpointRounding.AwayFromZero);
        if (rounded == 0)
            return "Không đồng";

        var isNegative = rounded < 0;
        var abs = isNegative ? (ulong)(-rounded) : (ulong)rounded;

        var groups = new List<int>();
        while (abs > 0)
        {
            groups.Add((int)(abs % 1000));
            abs /= 1000;
        }

        var parts = new List<string>();
        for (var i = groups.Count - 1; i >= 0; i--)
        {
            var group = groups[i];
            if (group == 0) continue;

            var isHighestGroup = i == groups.Count - 1;
            var groupText = ReadThreeDigits(group, readZeroHundred: !isHighestGroup);
            var scale = i < Scales.Length ? Scales[i] : string.Empty;
            parts.Add(string.IsNullOrEmpty(scale) ? groupText : $"{groupText} {scale}");
        }

        var body = string.Join(" ", parts);
        var prefix = isNegative ? "Âm " : string.Empty;
        var sentence = $"{prefix}{body} đồng";
        if (!isNegative && rounded % 1000 == 0)
            sentence += " chẵn";

        return char.ToUpper(sentence[0]) + sentence[1..];
    }

    private static string ReadThreeDigits(int number, bool readZeroHundred)
    {
        var hundred = number / 100;
        var ten = (number % 100) / 10;
        var unit = number % 10;

        var sb = new StringBuilder();
        if (hundred > 0 || readZeroHundred)
        {
            sb.Append(Digits[hundred]).Append(" trăm");
        }

        if (ten == 0)
        {
            if (unit > 0)
            {
                if (sb.Length > 0) sb.Append(" linh ");
                sb.Append(Digits[unit]);
            }
        }
        else if (ten == 1)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append("mười");
            if (unit == 5) sb.Append(" lăm");
            else if (unit > 0) sb.Append(' ').Append(Digits[unit]);
        }
        else
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(Digits[ten]).Append(" mươi");
            if (unit == 1) sb.Append(" mốt");
            else if (unit == 4) sb.Append(" tư");
            else if (unit == 5) sb.Append(" lăm");
            else if (unit > 0) sb.Append(' ').Append(Digits[unit]);
        }

        return sb.ToString();
    }
}
