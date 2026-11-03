using Moq;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Employees;

namespace RetailPOS.UnitTests.HR;

public sealed class HrEmployeeCommandValidationTests
{
    [Fact]
    public async Task CreateRejectsInvalidInitialEmploymentDatesBeforeAccess()
    {
        var access = new Mock<IHrEmployeeAccess>();
        var handler = new CreateHrEmployeeCommandHandler(
            access.Object,
            Mock.Of<IHrEmployeeStore>(),
            Mock.Of<IRequiredAuditTransaction>());

        await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(
            new CreateHrEmployeeCommand(ValidData() with
            {
                JoiningDate = new DateOnly(2026, 10, 2),
                ConfirmationDate = new DateOnly(2026, 10, 1)
            }), CancellationToken.None));

        access.Verify(item => item.RequireBusinessId(), Times.Never);
    }

    [Fact]
    public async Task CreateRejectsUnsupportedInitialStatusAndMissingEmploymentType()
    {
        var access = new Mock<IHrEmployeeAccess>();
        var handler = new CreateHrEmployeeCommandHandler(
            access.Object,
            Mock.Of<IHrEmployeeStore>(),
            Mock.Of<IRequiredAuditTransaction>());

        await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(
            new CreateHrEmployeeCommand(ValidData() with { EmploymentStatus = "Inactive" }),
            CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(
            new CreateHrEmployeeCommand(ValidData() with { EmploymentTypeId = 0 }),
            CancellationToken.None));

        access.Verify(item => item.RequireBusinessId(), Times.Never);
    }

    [Fact]
    public async Task CreateRejectsSensitiveWritesWithoutFieldPermission()
    {
        var access = new Mock<IHrEmployeeAccess>();
        access.Setup(item => item.RequireBusinessId()).Returns(1);
        access.Setup(item => item.EnsureAsync(1, HrEmployeeOperation.Create, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        access.Setup(item => item.GetFieldAccess()).Returns(new HrEmployeeFieldAccess(false, false));
        var handler = new CreateHrEmployeeCommandHandler(
            access.Object,
            Mock.Of<IHrEmployeeStore>(),
            Mock.Of<IRequiredAuditTransaction>());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(
            new CreateHrEmployeeCommand(ValidData() with { NationalId = "restricted" }),
            CancellationToken.None));

        access.Verify(item => item.RequireRealUserId(), Times.Never);
    }

    [Fact]
    public async Task UserLinkCommandsRejectInvalidIdentityAndMissingReasonBeforeAccess()
    {
        var access = new Mock<IHrEmployeeAccess>();
        var link = new LinkHrEmployeeUserCommandHandler(
            access.Object,
            Mock.Of<IHrEmployeeStore>(),
            Mock.Of<IIdentityUserDirectory>(),
            Mock.Of<IRequiredAuditTransaction>());
        var unlink = new UnlinkHrEmployeeUserCommandHandler(
            access.Object, Mock.Of<IHrEmployeeStore>(), Mock.Of<IRequiredAuditTransaction>());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => link.Handle(
            new LinkHrEmployeeUserCommand(1, 0, "Reason"), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => link.Handle(
            new LinkHrEmployeeUserCommand(1, 2, " "), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => unlink.Handle(
            new UnlinkHrEmployeeUserCommand(1, 1, string.Empty), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => unlink.Handle(
            new UnlinkHrEmployeeUserCommand(1, 0, "Reason"), CancellationToken.None));

        access.Verify(item => item.RequireBusinessId(), Times.Never);
    }

    private static HrEmployeeCreateData ValidData() => new(
        "EMP-1", "Alex", null, "Employee", "Alex Employee",
        null, null, null, null, null, null, null, null, null, null, null,
        11, "Active", new DateOnly(2026, 10, 1), null);
}
