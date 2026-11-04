using NexusCore.Domain.Identity;

namespace NexusCore.Tests.Identity;

/// <summary>
/// A new or changed username must be a real national code (its check digit right); signing in
/// still accepts any 10 digits, so accounts made before the check keep working.
/// </summary>
public sealed class NationalCodeTests
{
    [Theory]
    [InlineData("0499370899", true)]   // check digit 9 (remainder 2 → 11 - 2)
    [InlineData("1234567890", false)]
    [InlineData("1234567891", true)]   // remainder 1 → the check digit is 1
    [InlineData("1234567892", false)]
    [InlineData("0000000000", false)]  // all the same digit
    [InlineData("1111111111", false)]
    [InlineData("123456789", false)]   // nine digits
    [InlineData("12345678901", false)]
    [InlineData("abcdefghij", false)]
    [InlineData(null, false)]
    public void TheCheckDigit_IsChecked(string? value, bool valid) => Assert.Equal(valid, Username.IsValidNationalCode(value));

    [Fact]
    public void SigningIn_StillAcceptsAnyTenDigits() => Assert.True(Username.IsValid("1234567890"));
}
