using Moq;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Organisation;

namespace RetailPOS.UnitTests.HR;

public sealed class HrPositionCommandValidationTests
{
    [Fact]
    public async Task CreateRejectsMissingCodeBeforeAccessOrPersistence()
    {
        var state = CreateHandler();

        await Assert.ThrowsAsync<ArgumentException>(() => state.Handler.Handle(
            new CreateHrPositionCommand(ValidData() with { Code = " " }), CancellationToken.None));

        state.Access.Verify(access => access.RequireBusinessId(), Times.Never);
        state.Positions.Verify(store => store.CreateAsync(
            It.IsAny<long>(), It.IsAny<HrPositionWriteData>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateWithoutLocationOrDatesPassesValidation()
    {
        var state = CreateHandler();
        state.Access.Setup(access => access.RequireBusinessId())
            .Throws(new UnauthorizedAccessException("Business access checked."));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => state.Handler.Handle(
            new CreateHrPositionCommand(new HrPositionWriteData("SALES-EXEC", "Sales Executive", 1)),
            CancellationToken.None));

        state.Access.Verify(access => access.RequireBusinessId(), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreateRequiresDepartment(long departmentId)
    {
        var state = CreateHandler();
        await Assert.ThrowsAsync<ArgumentException>(() => state.Handler.Handle(
            new CreateHrPositionCommand(ValidData() with { OrgUnitId = departmentId }),
            CancellationToken.None));
        state.Access.Verify(access => access.RequireBusinessId(), Times.Never);
    }

    [Fact]
    public async Task DepartmentVisibilityIsStillChecked()
    {
        var state = CreateHandler();
        var orgUnits = new Mock<IHrOrgUnitWriteStore>();
        state.Access.Setup(access => access.RequireBusinessId()).Returns(1);
        state.Access.Setup(access => access.EnsureAsync(1, HrOrganisationOperation.ManagePositions,
                42, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Department is outside scope."));
        orgUnits.Setup(store => store.GetLocationProjectionIdAsync(1, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(42);
        var handler = new CreateHrPositionCommandHandler(state.Access.Object, orgUnits.Object,
            state.Positions.Object, Mock.Of<IRequiredAuditTransaction>());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(
            new CreateHrPositionCommand(ValidData()), CancellationToken.None));
        state.Positions.Verify(store => store.CreateAsync(It.IsAny<long>(),
            It.IsAny<HrPositionWriteData>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void SimplifiedJsonContractDoesNotRequireLegacyProperties()
    {
        var data = System.Text.Json.JsonSerializer.Deserialize<HrPositionWriteData>(
            """{"code":"SALES","name":"Sales Executive","orgUnitId":1,"reportsToPositionId":2,"status":"Vacant"}""",
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.NotNull(data);
        Assert.Null(data.OperationalLocationProjectionId);
        Assert.Null(data.EffectiveFrom);
        Assert.Null(data.EffectiveTo);
        Assert.Equal(2, data.ReportsToPositionId);
    }

    [Fact]
    public async Task CreateRejectsInactiveStatusOutsideDeactivateOperation()
    {
        var state = CreateHandler();

        await Assert.ThrowsAsync<ArgumentException>(() => state.Handler.Handle(
            new CreateHrPositionCommand(ValidData() with { Status = "Inactive" }), CancellationToken.None));

        state.Access.Verify(access => access.RequireBusinessId(), Times.Never);
    }

    [Fact]
    public async Task CreateRejectsUndefinedStatus()
    {
        var state = CreateHandler();
        await Assert.ThrowsAsync<ArgumentException>(() => state.Handler.Handle(
            new CreateHrPositionCommand(ValidData() with { Status = "99" }), CancellationToken.None));
    }

    private static (CreateHrPositionCommandHandler Handler, Mock<IHrOrganisationAccess> Access,
        Mock<IHrPositionStore> Positions) CreateHandler()
    {
        var access = new Mock<IHrOrganisationAccess>();
        var positions = new Mock<IHrPositionStore>();
        return (new CreateHrPositionCommandHandler(
            access.Object,
            Mock.Of<IHrOrgUnitWriteStore>(),
            positions.Object,
            Mock.Of<IRequiredAuditTransaction>()), access, positions);
    }

    private static HrPositionWriteData ValidData() => new(
        "POS-1",
        "Position",
        1,
        null,
        null,
        "Vacant",
        new DateOnly(2026, 1, 1),
        null);
}
