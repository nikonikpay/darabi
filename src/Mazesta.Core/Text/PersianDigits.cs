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
            int p = Persian.IndexOf(c), a = ArabicIndic.IndexOf(c);
            sb.Append(p >= 0 ? (char)('0' + p) : a >= 0 ? (char)('0' + a) : c is '٫' or '،' ? '.' : c);
        }
        return sb.ToString();
    }
    public static bool TryParseDouble(string input, out double value)
        => double.TryParse(Normalize(input).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    public static bool TryParseInt(string input, out int value)
        => int.TryParse(Normalize(input).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
}
