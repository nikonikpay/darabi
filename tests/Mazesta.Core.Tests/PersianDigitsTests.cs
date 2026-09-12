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
}
