using RetailPOS.Application.Exchanges;
using Xunit;

namespace RetailPOS.UnitTests.Exchanges;

/// <summary>L1b: the lockout keys normalise the typed login, stay inside one business and never hold the login in clear.</summary>
public sealed class ExchangeApprovalLockoutKeyTests
{
    [Fact]
    public void SupervisorKey_IgnoresCaseAndSurroundingSpaces()
    {
        Assert.Equal(
            GrantExchangeApprovalsCommandHandler.SupervisorKey(7, "Boss@Shop.test"),
            GrantExchangeApprovalsCommandHandler.SupervisorKey(7, "  boss@shop.TEST "));
    }

    [Fact]
    public void Keys_AreScopedToTheBusiness_AndDistinctPerSubjectKind()
    {
        Assert.NotEqual(
            GrantExchangeApprovalsCommandHandler.SupervisorKey(7, "boss@shop.test"),
            GrantExchangeApprovalsCommandHandler.SupervisorKey(8, "boss@shop.test"));
        Assert.NotEqual(
            GrantExchangeApprovalsCommandHandler.SupervisorKey(7, "42"),
            GrantExchangeApprovalsCommandHandler.CashierKey(7, 42));
    }

    [Fact]
    public void Keys_AreSha256Hex_WithoutTheLogin()
    {
        var key = GrantExchangeApprovalsCommandHandler.SupervisorKey(7, "boss@shop.test");
        Assert.Matches("^[0-9a-f]{64}$", key);
        Assert.DoesNotContain("boss", key);
        Assert.Equal(GrantExchangeApprovalsCommandHandler.SupervisorKey(7, null), GrantExchangeApprovalsCommandHandler.SupervisorKey(7, "   "));
    }
}
