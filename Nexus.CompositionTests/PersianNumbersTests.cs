using Nexus.ProjectManagement.Contracts.Application.Persian;

namespace Nexus.CompositionTests;

public sealed class PersianNumbersTests
{
    [Theory]
    [InlineData(0, "صفر")]
    [InlineData(1, "یک")]
    [InlineData(9, "نه")]
    [InlineData(10, "ده")]
    [InlineData(11, "یازده")]
    [InlineData(19, "نوزده")]
    [InlineData(20, "بیست")]
    [InlineData(21, "بیست و یک")]
    [InlineData(40, "چهل")]
    [InlineData(99, "نود و نه")]
    [InlineData(100, "صد")]
    [InlineData(101, "صد و یک")]
    [InlineData(110, "صد و ده")]
    [InlineData(115, "صد و پانزده")]
    [InlineData(123, "صد و بیست و سه")]
    [InlineData(200, "دویست")]
    [InlineData(300, "سیصد")]
    [InlineData(500, "پانصد")]
    [InlineData(999, "نهصد و نود و نه")]
    public void UnderAThousand(long value, string expected) => Assert.Equal(expected, PersianNumbers.ToWords(value));

    [Theory]
    [InlineData(1_000, "یک هزار")]
    [InlineData(1_001, "یک هزار و یک")]
    [InlineData(1_500, "یک هزار و پانصد")]
    [InlineData(2_000, "دو هزار")]
    [InlineData(12_345, "دوازده هزار و سیصد و چهل و پنج")]
    [InlineData(100_000, "صد هزار")]
    [InlineData(999_999, "نهصد و نود و نه هزار و نهصد و نود و نه")]
    [InlineData(1_000_000, "یک میلیون")]
    [InlineData(1_000_001, "یک میلیون و یک")]
    [InlineData(1_250_000, "یک میلیون و دویست و پنجاه هزار")]
    [InlineData(50_000_000, "پنجاه میلیون")]
    [InlineData(1_000_000_000, "یک میلیارد")]
    [InlineData(1_234_567_890, "یک میلیارد و دویست و سی و چهار میلیون و پانصد و شصت و هفت هزار و هشتصد و نود")]
    [InlineData(1_000_000_000_000, "یک تریلیون")]
    [InlineData(2_000_000_000_000_000 - 1, "نهصد و نود و نه تریلیون و نهصد و نود و نه میلیارد و نهصد و نود و نه میلیون و نهصد و نود و نه هزار و نهصد و نود و نه")]
    public void Scales(long value, string expected)
    {
        if (value > PersianNumbers.MaxValue)
        {
            value = PersianNumbers.MaxValue;
        }

        Assert.Equal(expected, PersianNumbers.ToWords(value));
    }

    [Fact]
    public void ZeroGroupsInTheMiddleAreSkipped()
    {
        Assert.Equal("یک میلیارد و پنج", PersianNumbers.ToWords(1_000_000_005));
        Assert.Equal("دو میلیون و سیصد", PersianNumbers.ToWords(2_000_300));
        Assert.Equal("پنج هزار و یک", PersianNumbers.ToWords(5_001));
    }

    [Fact]
    public void NegativeNumbers_StartWithMinus_AndFractionsRoundAwayFromZero()
    {
        Assert.Equal("منفی پنج", PersianNumbers.ToWords(-5));
        Assert.Equal("منفی یک هزار و دویست", PersianNumbers.ToWords(-1200));
        Assert.Equal("سه", PersianNumbers.ToWords(2.5m));
        Assert.Equal("دو", PersianNumbers.ToWords(2.4m));
        Assert.Equal("منفی سه", PersianNumbers.ToWords(-2.5m));
    }

    [Fact]
    public void TooLargeANumber_IsRefused_NotWrittenWrong()
    {
        Assert.Equal(
            "نهصد و نود و نه تریلیون و نهصد و نود و نه میلیارد و نهصد و نود و نه میلیون و نهصد و نود و نه هزار و نهصد و نود و نه",
            PersianNumbers.ToWords(PersianNumbers.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => PersianNumbers.ToWords(PersianNumbers.MaxValue + 1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => PersianNumbers.ToWords(-(PersianNumbers.MaxValue + 1m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => PersianNumbers.TomansInWords(PersianNumbers.MaxValue * 10m + 10));
    }

    [Fact]
    public void Rials_AreWrittenWithTheirUnit()
    {
        Assert.Equal("یک میلیون و دویست و پنجاه هزار ریال", PersianNumbers.RialsInWords(1_250_000));
        Assert.Equal("صفر ریال", PersianNumbers.RialsInWords(0));
    }

    [Theory]
    [InlineData(12_500, "یک هزار و دویست و پنجاه تومان")]
    [InlineData(1_250_000, "صد و بیست و پنج هزار تومان")]
    [InlineData(12_505, "یک هزار و دویست و پنجاه تومان و پنج ریال")]
    [InlineData(7, "هفت ریال")]
    [InlineData(0, "صفر تومان")]
    [InlineData(10, "یک تومان")]
    [InlineData(-12_500, "منفی یک هزار و دویست و پنجاه تومان")]
    public void Tomans_AreTenRials_WithALeftoverInRials(long rials, string expected)
    {
        Assert.Equal(expected, PersianNumbers.TomansInWords(rials));
    }

    [Fact]
    public void Digits_ConvertBetweenScripts_AndLeaveEverythingElseAlone()
    {
        Assert.Equal("1405/01/12", PersianNumbers.ToLatinDigits("۱۴۰۵/۰۱/۱۲"));
        Assert.Equal("1405/01/12", PersianNumbers.ToLatinDigits("١٤٠٥/٠١/١٢")); // Arabic-Indic
        Assert.Equal("INV-0042", PersianNumbers.ToLatinDigits("INV-۰۰۴۲"));
        Assert.Equal("۱۴۰۵/۰۱/۱۲", PersianNumbers.ToPersianDigits("1405/01/12"));
        Assert.Equal("۱۴۰۵", PersianNumbers.ToPersianDigits("۱۴۰۵")); // already Persian
        Assert.Equal("ش-۱۲", PersianNumbers.ToPersianDigits("ش-12"));
        Assert.Equal(string.Empty, PersianNumbers.ToLatinDigits(null));
        Assert.Equal(string.Empty, PersianNumbers.ToPersianDigits(""));
        Assert.Equal("12", PersianNumbers.ToLatinDigits(PersianNumbers.ToPersianDigits("12")));
    }

    [Fact]
    public void Money_IsGroupedInPersianDigits()
    {
        Assert.Equal("۱,۲۵۰,۰۰۰", PersianNumbers.FormatMoney(1_250_000));
        Assert.Equal("۰", PersianNumbers.FormatMoney(0));
        Assert.Equal("۹۹۹", PersianNumbers.FormatMoney(999));
        Assert.Equal("-۱,۵۰۰", PersianNumbers.FormatMoney(-1500));
        Assert.Equal("۱,۲۵۰,۰۰۰", PersianNumbers.FormatMoney(1_250_000.4m)); // rounds to whole rials
        Assert.Equal("۱,۲۵۰,۰۰۱", PersianNumbers.FormatMoney(1_250_000.6m));
        Assert.Equal("۱,۲۵۰,۰۰۰ ریال", PersianNumbers.FormatRials(1_250_000));
        Assert.Equal("۱۲۵,۰۰۰ تومان", PersianNumbers.FormatTomans(1_250_000));
        Assert.Equal("۱,۲۵۰.۵ تومان", PersianNumbers.FormatTomans(12_505));
    }

    [Fact]
    public void JalaliDates_AreWrittenInPersianDigits()
    {
        // Nowruz 1405 falls on 21 March 2026; the last day of Esfand 1404 is the day before.
        Assert.Equal("۱۴۰۵/۰۱/۰۱", PersianDates.Format(new DateOnly(2026, 3, 21)));
        Assert.Equal("۱۴۰۴/۱۲/۲۹", PersianDates.Format(new DateOnly(2026, 3, 20)));
        Assert.Equal("۱۴۰۴/۱۲/۱۱", PersianDates.Format(new DateOnly(2026, 3, 2)));
        Assert.Equal("۱۴۰۳/۱۰/۱۲", PersianDates.Format(new DateOnly(2025, 1, 1))); // 1 Dey is 21 December
        Assert.Null(PersianDates.Format((DateOnly?)null));
        Assert.Equal("۱۴۰۵/۰۱/۰۱", PersianDates.Format((DateOnly?)new DateOnly(2026, 3, 21)));
    }
}
