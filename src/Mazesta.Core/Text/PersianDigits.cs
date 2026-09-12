using System.Globalization;
using System.Text;
namespace Mazesta.Core.Text;

public static class PersianDigits
{
    private const string Persian = "۰۱۲۳۴۵۶۷۸۹", ArabicIndic = "٠١٢٣٤٥٦٧٨٩";
    public static string Normalize(string input)
    {
        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            // U+060C (،) is the Persian comma and U+066C (٬) the Arabic thousands separator: both
            // group digits and carry no value, so they are dropped the way an ASCII thousands comma
            // would be. Mapping them to '.' (as this did before) read "۱،۵۰۰" - 1500 - as 1.500.
            if (c is '،' or '٬') continue;
            int p = Persian.IndexOf(c), a = ArabicIndic.IndexOf(c);
            // U+066B (٫) is the Persian DECIMAL separator.
            sb.Append(p >= 0 ? (char)('0' + p) : a >= 0 ? (char)('0' + a) : c == '٫' ? '.' : c);
        }
        return sb.ToString();
    }
    public static bool TryParseDouble(string input, out double value)
        => double.TryParse(Normalize(input).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    public static bool TryParseInt(string input, out int value)
        => int.TryParse(Normalize(input).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
}
