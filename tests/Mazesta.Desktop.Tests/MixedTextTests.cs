using Mazesta.Desktop.Controls; using Xunit;
namespace Mazesta.Desktop.Tests;

public class MixedTextTests
{
    [Fact] public void Persian_and_latin_parts_are_cut_where_the_script_changes_and_numbers_keep_their_unit()
        => Assert.Equal([("هسته ‎+", false), ("120 MHz‎ · ", true), ("سقف ‎", false), ("1905 MHz‎", true)],
            MixedText.Split("هسته ‎+120 MHz‎ · سقف ‎1905 MHz‎"));

    [Fact] public void Text_of_one_script_is_one_run()
    {
        Assert.Equal([("1950 MHz · 348 W", true)], MixedText.Split("1950 MHz · 348 W"));
        Assert.Equal([("حالت کارخانه", false)], MixedText.Split("حالت کارخانه"));
    }
}
