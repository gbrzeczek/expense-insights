using ExpenseInsights.Core.Accounts;

namespace ExpenseInsights.Core.Tests.Accounts;

public sealed class AccountNumberTests
{
    [Fact]
    public void Formats_OfTheSameNumber_HashTheSame()
    {
        var preamble = AccountNumber.Hash("12 1043 3218 1960 0133 8908 3863");

        Assert.NotNull(preamble);
        Assert.Equal(preamble, AccountNumber.Hash("12104332181960013389083863"));
        Assert.Equal(preamble, AccountNumber.Hash("PL12104332181960013389083863"));
        Assert.NotEqual(preamble, AccountNumber.Hash("74 7940 2654 2351 1615 5940 7816"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1915031/19730")] // a card acquirer id, not an account
    public void NonAccounts_HaveNoHash(string? value)
    {
        Assert.Null(AccountNumber.Hash(value));
    }

    [Fact]
    public void Mask_KeepsOnlyTheLastFourDigits()
    {
        Assert.Equal("•••• 3863", AccountNumber.Mask("12 1043 3218 1960 0133 8908 3863"));
    }
}
