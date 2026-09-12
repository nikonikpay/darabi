using Mazesta.Core.Text;
using Xunit;
namespace Mazesta.Core.Tests;
public class PersianDigitsTests
{
    [Fact] public void Persian_digits_become_ascii() => Assert.Equal("1234567890", PersianDigits.Normalize("۱۲۳۴۵۶۷۸۹۰"));
    [Fact] public void Arabic_indic_digits_become_ascii() => Assert.Equal("0123", PersianDigits.Normalize("٠١٢٣"));
    [Fact] public void Persian_decimal_separator_is_dot() => Assert.True(PersianDigits.TryParseDouble("۰٫۵", out var v) && v == 0.5);
    [Fact] public void Mixed_input_parses_as_int() => Assert.True(PersianDigits.TryParseInt(" ۹۰0 ", out var v) && v == 900);
    [Fact] public void Garbage_fails() => Assert.False(PersianDigits.TryParseInt("abc", out _));
    // U+060C is the Persian comma (thousands separator), not a decimal point: "۱،۵۰۰" is 1500.
    [Fact] public void Persian_thousands_separator_is_dropped_not_turned_into_a_dot()
    { Assert.Equal("1500", PersianDigits.Normalize("۱،۵۰۰")); Assert.True(PersianDigits.TryParseInt("۱،۵۰۰", out var v) && v == 1500); }
    [Fact] public void Arabic_thousands_separator_is_dropped() => Assert.Equal("1500", PersianDigits.Normalize("۱٬۵۰۰"));
    [Fact] public void Decimal_separator_still_wins_over_the_comma()
    { Assert.True(PersianDigits.TryParseDouble("۱،۵۰۰٫۲۵", out var v) && v == 1500.25); }
}
