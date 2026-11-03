using Moq;
using RetailPOS.Application.BusinessAdministration.Abstractions;
using RetailPOS.Application.BusinessAdministration.Commands;
using RetailPOS.Application.BusinessAdministration.ReadModels;

namespace RetailPOS.UnitTests.BusinessAdministration;

public sealed class CreateBusinessCommandHandlerTests
{
    [Fact]
    public async Task Handle_RejectsMissingRequiredBusinessNameBeforeCallingStore()
    {
        var store = new Mock<IBusinessOnboardingWriteStore>(MockBehavior.Strict);
        var handler = new CreateBusinessCommandHandler(store.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(CreateCommand() with { BusinessName = "  " }, CancellationToken.None));

        Assert.Equal("BusinessName is required.", exception.Message);
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_RejectsMissingWarehouseManagerWhenWarehouseIsRequested()
    {
        var store = new Mock<IBusinessOnboardingWriteStore>(MockBehavior.Strict);
        var handler = new CreateBusinessCommandHandler(store.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(
                CreateCommand() with { CreateDefaultWarehouse = true, WarehouseManagerEmail = null },
                CancellationToken.None));

        Assert.Equal(
            "Warehouse manager name and email are required when CreateDefaultWarehouse is true.",
            exception.Message);
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_MapsDuplicatePayloadEmailResultToEstablishedError()
    {
        var store = new Mock<IBusinessOnboardingWriteStore>(MockBehavior.Strict);
        var request = CreateCommand();
        store.Setup(writeStore => writeStore.CreateBusinessAsync(request, CancellationToken.None))
            .ReturnsAsync(new BusinessOnboardingWriteResult(
                BusinessOnboardingStatus.DuplicateEmailsInRequest,
                Emails: ["owner@example.test"]));
        var handler = new CreateBusinessCommandHandler(store.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(request, CancellationToken.None));

        Assert.Equal("Duplicate emails in request: owner@example.test", exception.Message);
        store.VerifyAll();
    }

    [Fact]
    public async Task Handle_ReturnsCreatedModelFromStore()
    {
        var result = new BusinessOnboardingReadModel(
            17,
            "Northwind",
            23,
            null,
            Array.Empty<OnboardedUserReadModel>());
        var store = new Mock<IBusinessOnboardingWriteStore>(MockBehavior.Strict);
        var request = CreateCommand();
        store.Setup(writeStore => writeStore.CreateBusinessAsync(request, CancellationToken.None))
            .ReturnsAsync(new BusinessOnboardingWriteResult(BusinessOnboardingStatus.Created, result));
        var handler = new CreateBusinessCommandHandler(store.Object);

        var created = await handler.Handle(request, CancellationToken.None);

        Assert.Same(result, created);
        store.VerifyAll();
    }

    private static CreateBusinessCommand CreateCommand() => new(
        "Northwind",
        null,
        null,
        null,
        "Owner",
        "owner@example.test",
        "Manager",
        "manager@example.test",
        "Sales",
        "sales@example.test",
        "Accounts",
        "accounts@example.test",
        "Main Outlet",
        false,
        null,
        null,
        null);
}
