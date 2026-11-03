using Moq;
using RetailPOS.Application.Authentication.Abstractions;
using RetailPOS.Application.Authentication.Queries;
using RetailPOS.Application.BusinessAdministration.Abstractions;
using RetailPOS.Application.BusinessAdministration.ReadModels;

namespace RetailPOS.UnitTests.Auth;

public sealed class ValidateBusinessContextQueryHandlerTests
{
    [Theory]
    [InlineData("BusinessOwner", true)]
    [InlineData("Cashier", true)]
    [InlineData("Super Admin", false)]
    public async Task RejectsNonPlatformOrInactivePersistedUser(string role, bool active)
    {
        var profiles = new Mock<IAuthenticationProfileStore>();
        profiles.Setup(store => store.FindProfileAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Profile(role, active));
        var businesses = new Mock<IBusinessAdministrationReadStore>(MockBehavior.Strict);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new ValidateBusinessContextQueryHandler(profiles.Object, businesses.Object)
                .Handle(new ValidateBusinessContextQuery(7, 4), CancellationToken.None));
    }

    [Fact]
    public async Task NullSelectionClearsContextWithoutChoosingAnyBusiness()
    {
        var profiles = new Mock<IAuthenticationProfileStore>();
        profiles.Setup(store => store.FindProfileAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Profile("Super Admin", true));
        var businesses = new Mock<IBusinessAdministrationReadStore>(MockBehavior.Strict);
        var result = await new ValidateBusinessContextQueryHandler(profiles.Object, businesses.Object)
            .Handle(new ValidateBusinessContextQuery(7, null), CancellationToken.None);
        Assert.Null(result.BusinessId);
        Assert.Null(result.BusinessName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ValidatesExplicitBusinessActivity(bool active)
    {
        var profiles = new Mock<IAuthenticationProfileStore>();
        profiles.Setup(store => store.FindProfileAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Profile("Super Admin", true));
        var businesses = new Mock<IBusinessAdministrationReadStore>();
        businesses.Setup(store => store.FindBusinessAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessSummaryReadModel(4, "Selected", null, null, active,
                null, null, null, null, null, 0, 0, 0, null));
        var operation = new ValidateBusinessContextQueryHandler(profiles.Object, businesses.Object)
            .Handle(new ValidateBusinessContextQuery(7, 4), CancellationToken.None);
        if (active)
        {
            var result = await operation;
            Assert.Equal(4, result.BusinessId);
            Assert.Equal("Selected", result.BusinessName);
        }
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operation);
    }

    private static AuthenticationProfileData Profile(string role, bool active) =>
        new(7, null, "Platform Admin", "admin@example.test", 1, role, "[\"*\"]",
            null, null, null, false, active);
}
