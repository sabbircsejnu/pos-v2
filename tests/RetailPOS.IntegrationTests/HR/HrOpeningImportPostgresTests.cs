using System.Collections.Concurrent;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.ConfidentialFiles;
using RetailPOS.Application.HR.Employees;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AuditEvents;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Employees;
using RetailPOS.Infrastructure.HR.Payroll;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.IntegrationTests.HR;

public sealed class HrOpeningImportPostgresTests(HrOpeningImportDatabase fixture)
    : IClassFixture<HrOpeningImportDatabase>
{
    private static string Code() => "IMP-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
    private static string EmployeeCsv(string code, string type = "123", string position = "",
        string org = "", string location = "", string assignment = "", string identity = "", string mobile = "") =>
        new HrOpeningImportCsvParser().Template("Employee", "1")
        + $"row-{code},{code},Alex,Alex Employee,{type},Active,{DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10):yyyy-MM-dd},{position},{org},{location},{assignment},{identity},{mobile}";

    [Fact]
    public async Task ReusablePositionImportUsesEmployeeLocationEvidenceAndStillRejectsUnauthorizedTargets()
    {
        await using var session = fixture.Session();
        var position = new HrPosition
        {
            BusinessId = 1, Code = Code(), Name = "Reusable import role", OrgUnitId = 502,
            Status = HrPositionStatus.Vacant
        };
        session.Db.HrPositions.Add(position);
        await session.Db.SaveChangesAsync();
        session.Access.Setup(access => access.GetLocationReadScopeAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HrOperationalLocationReadScope(1, [new("Outlet", "711")]));
        session.Access.Setup(access => access.EnsureAsync(1,
                HrOrganisationOperation.ManageEmployeeLocationAssignments, 78, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Target outside employee location scope."));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.Upload(
            EmployeeCsv(Code(), position: position.Id.ToString(), org: "502", location: "78", assignment: "Primary")));

        var code = Code();
        var batch = await session.Upload(EmployeeCsv(code, position: position.Id.ToString(),
            org: "502", location: "77", assignment: "Primary"));
        Assert.Equal("Ready", (await session.Service.ValidateAsync(batch.Id, default)).Status);
        Assert.Equal("Completed", (await session.Service.ExecuteAsync(batch.Id, default)).Status);
        var employee = await session.Db.HrEmployees.SingleAsync(row => row.EmployeeCode == code);
        Assert.Equal(position.Id, employee.CurrentPositionId);
        Assert.Equal(77, employee.CurrentPrimaryLocationProjectionId);
        session.EmployeeAccess.Verify(access => access.EnsureAsync(1,
            HrEmployeeOperation.ManagePositionAssignments, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        Assert.Null((await session.Db.HrPositions.SingleAsync(row => row.Id == position.Id)).EffectiveFrom);
    }

    [Fact]
    public async Task DryRunCreatesOnlyImmutableEvidenceAndExecutionReusesDomainRules()
    {
        await using var session = fixture.Session();
        var code = Code();
        var batch = await session.Upload(EmployeeCsv(code, position: "501", org: "502", location: "77", assignment: "Primary"));
        var ready = await session.Service.ValidateAsync(batch.Id, default);
        Assert.Equal("Ready", ready.Status);
        Assert.True(ready.IsDryRun);
        Assert.False(await session.Db.HrEmployees.AnyAsync(e => e.EmployeeCode == code));
        Assert.Single(ready.Rows);
        var completed = await session.Service.ExecuteAsync(batch.Id, default);
        Assert.Equal("Completed", completed.Status);
        var employee = await session.Db.HrEmployees.SingleAsync(e => e.EmployeeCode == code);
        Assert.Equal(77, employee.CurrentPrimaryLocationProjectionId);
        Assert.Equal(501, employee.CurrentPositionId);
        Assert.Equal(502, employee.CurrentOrgUnitId);
        Assert.Single(await session.Db.HrEmploymentPeriods.Where(p => p.EmployeeId == employee.Id).ToArrayAsync());
        Assert.Single(await session.Db.HrEmployeeLifecycleEvents.Where(p => p.EmployeeId == employee.Id && p.EventType == "Joined").ToArrayAsync());
        Assert.Single(await session.Db.HrEmployeeLocationAssignments.Where(p => p.EmployeeId == employee.Id).ToArrayAsync());
        Assert.Equal(employee.Id, completed.Rows[0].CreatedEntityId);
        var replay = await session.Service.ExecuteAsync(batch.Id, default);
        Assert.Equal(completed.Revision, replay.Revision);
        Assert.Equal(1, await session.Db.HrEmployees.CountAsync(e => e.EmployeeCode == code));
        Assert.DoesNotContain("StorageKey", System.Text.Json.JsonSerializer.Serialize(completed));
        Assert.True(await session.Db.AuditEvents.AnyAsync(a => a.ActionType == "hr.import.executed"));
        var evidence = await session.Db.HrImportRows.FirstAsync(r => r.ImportBatchId == batch.Id);
        evidence.ErrorJson = "[] changed";
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task SalaryOpeningImportCreatesDraftSetupWithoutEffectiveSalary()
    {
        await using var session = fixture.Session();
        var code = Code();
        var employee = await session.Upload(EmployeeCsv(code, location: "77", assignment: "Primary"));
        await session.Service.ValidateAsync(employee.Id, default);
        await session.Service.ExecuteAsync(employee.Id, default);
        var component = new PayrollSalaryComponent
        {
            BusinessId = 1, Code = "OPEN_BASE", Name = "Opening base",
            ComponentType = "Earning", ValueType = "Fixed", IsRecurring = true,
            CalculationOrder = 1
        };
        session.Db.PayrollSalaryComponents.Add(component);
        await session.Db.SaveChangesAsync();
        var structure = new PayrollSalaryStructure
        {
            BusinessId = 1, Code = "OPEN_STANDARD", Name = "Opening standard",
            Lines = [new PayrollSalaryStructureLine
            {
                BusinessId = 1, SalaryComponentId = component.Id,
                DefaultValue = 1000m, CalculationOrder = 1
            }]
        };
        session.Db.PayrollSalaryStructures.Add(structure);
        await session.Db.SaveChangesAsync();

        var csv = new HrOpeningImportCsvParser().Template("Salary", "1")
            + $"salary-{code},{code},{structure.Id},{DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd},"
            + $"\"[{{\"\"salaryComponentId\"\":{component.Id},\"\"value\"\":1250}}]\",Opening salary,REF-1";
        var batch = await session.Upload(csv, importType: "Salary");
        Assert.Equal("Ready", (await session.Service.ValidateAsync(batch.Id, default)).Status);
        var completed = await session.Service.ExecuteAsync(batch.Id, default);

        var request = await session.Db.PayrollSalarySetupRequests.SingleAsync();
        Assert.Equal("Draft", request.Status);
        Assert.Equal("PayrollSalarySetupRequest", Assert.Single(completed.Rows).CreatedEntityType);
        Assert.Empty(await session.Db.PayrollEmployeeSalaryVersions.ToArrayAsync());
    }

    [Fact]
    public async Task LoanOpeningImportCreatesDraftWithHistoricalEvidenceAndNoNewDisbursement()
    {
        await using var session = fixture.Session();
        var code = Code();
        var employee = await session.Upload(EmployeeCsv(code, location: "77", assignment: "Primary"));
        await session.Service.ValidateAsync(employee.Id, default);
        await session.Service.ExecuteAsync(employee.Id, default);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var csv = new HrOpeningImportCsvParser().Template("LoanOpening", "1")
            + $"loan-{code},{code},Loan,1000,600,2025-04-01,{today:yyyy-MM-dd},FixedMonthly,200,,Opening loan,REF-1";
        var batch = await session.Upload(csv, importType: "LoanOpening");
        Assert.Equal("Ready", (await session.Service.ValidateAsync(batch.Id, default)).Status);
        var completed = await session.Service.ExecuteAsync(batch.Id, default);

        var loan = await session.Db.PayrollEmployeeLoans.SingleAsync();
        Assert.Equal("Draft", loan.Status);
        Assert.True(loan.IsOpeningBalance);
        Assert.Equal(new DateOnly(2025, 4, 1), loan.OpeningDisbursedDate);
        Assert.Equal(600m, loan.OpeningBalanceAmount);
        Assert.Null(loan.DisbursedDate);
        Assert.Equal("PayrollEmployeeLoan", Assert.Single(completed.Rows).CreatedEntityType);

        var policy = new ApprovalPolicy
        {
            BusinessId = 1, Code = "TEST_LOAN", ProcessType = ApprovalProcessType.EmployeeLoan,
            Name = "Test loan policy", VersionNo = 1, ScopeType = ApprovalScopeType.Location,
            ScopeReference = "77", EffectiveFrom = today.AddYears(-1), IsActive = true
        };
        session.Db.ApprovalPolicies.Add(policy);
        await session.Db.SaveChangesAsync();
        var approval = new ApprovalInstance
        {
            BusinessId = 1, PolicyId = policy.Id, PolicyCodeSnapshot = policy.Code,
            PolicyVersionSnapshot = 1, ProcessType = ApprovalProcessType.EmployeeLoan,
            TargetType = "PayrollEmployeeLoan", TargetId = loan.Id.ToString(),
            RequestedByUserId = 101, RequestedAtUtc = DateTime.UtcNow,
            Status = ApprovalInstanceStatus.Approved, CorrelationId = Guid.NewGuid(),
            ScopeType = ApprovalScopeType.Location, ScopeReference = "77",
            TargetVersion = loan.Version, RequestHash = loan.RequestHash
        };
        loan.Status = "PendingApproval";
        loan.SubmittedByUserId = 101;
        loan.SubmittedAtUtc = DateTime.UtcNow;
        loan.SubmittedTargetVersion = loan.Version;
        loan.Version++;
        loan.ApprovalInstance = approval;
        await session.Db.SaveChangesAsync();
        var salaryStore = new PayrollSalarySetupStore(session.Db);
        var loanStore = new PayrollEmployeeLoanStore(session.Db, salaryStore);
        await using var transaction = await session.Db.Database.BeginTransactionAsync();
        await loanStore.DecisionCompletedAsync(1, approval, 202, "Approved", default);
        await session.Db.SaveChangesAsync();
        await transaction.CommitAsync();

        Assert.Equal("Active", loan.Status);
        Assert.Equal(600m, loan.OutstandingAmount);
        Assert.Null(loan.DisbursedDate);
        Assert.Single(await session.Db.PayrollLoanScheduleVersions.Where(row => row.LoanId == loan.Id).ToArrayAsync());
    }

    [Fact]
    public async Task RetainedSourcePreservesItsUtcInstantUnderLegacyProviderReads()
    {
        await using var session = fixture.Session();
        var batch = await session.Upload(EmployeeCsv(Code()));
        session.Db.ChangeTracker.Clear();
        var stored = await session.Db.HrImportBatches.AsNoTracking().SingleAsync(row => row.Id == batch.Id);
        var source = await new HrOpeningImportStore(session.Db, session.Employees,
            new HrEmployeeLocationAssignmentStore(session.Db), TimeProvider.System)
            .GetSourceAsync(1, batch.Id, default);
        Assert.Equal(TimeSpan.Zero, source.File.RetainUntilUtc.Offset);
        Assert.Equal(stored.RetainUntilUtc.ToUniversalTime(), source.File.RetainUntilUtc.UtcDateTime);
    }

    [Theory]
    [InlineData("type", "9999", "", "", "", "")]
    [InlineData("position", "123", "9999", "502", "", "")]
    [InlineData("org", "123", "501", "9999", "", "")]
    [InlineData("location", "123", "", "", "9999", "Primary")]
    [InlineData("cross_business", "123", "", "", "88", "Primary")]
    [InlineData("inactive", "123", "", "", "79", "Primary")]
    [InlineData("unowned", "123", "", "", "80", "Primary")]
    [InlineData("factory", "123", "", "", "81", "Primary")]
    public async Task InvalidReferencesRetainErrorsAndCreateNothing(string scenario, string type,
        string position, string org, string location, string assignment)
    {
        await using var session = fixture.Session();
        var code = Code();
        var batch = await session.Upload(EmployeeCsv(code, type, position, org, location, assignment));
        var result = await session.Service.ValidateAsync(batch.Id, default);
        Assert.Equal("ValidationFailed", result.Status);
        Assert.NotEmpty(result.Rows[0].Errors);
        Assert.False(await session.Db.HrEmployees.AnyAsync(e => e.EmployeeCode == code), scenario);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Service.ExecuteAsync(batch.Id, default));
    }

    [Fact]
    public async Task DuplicateCodesWithinFileAndDatabaseAreRejectedAtomically()
    {
        await using var session = fixture.Session();
        var code = Code();
        var csv = EmployeeCsv(code);
        var duplicate = await session.Upload(csv + "\n" + csv.Split('\n')[1]);
        Assert.Equal(2, (await session.Service.ValidateAsync(duplicate.Id, default)).InvalidRows);
        var first = await session.Upload(csv);
        await session.Service.ValidateAsync(first.Id, default);
        await session.Service.ExecuteAsync(first.Id, default);
        var second = await session.Upload(csv);
        Assert.Equal("ValidationFailed", (await session.Service.ValidateAsync(second.Id, default)).Status);
    }

    [Fact]
    public async Task SameKeyReusesOutcomeAndDifferentContentConflicts()
    {
        await using var session = fixture.Session();
        var key = Guid.NewGuid().ToString("N");
        var csv = EmployeeCsv(Code());
        var first = await session.Upload(csv, key);
        var blobCount = fixture.Blobs.Count;
        Assert.Equal(first.Id, (await session.Upload(csv, key)).Id);
        Assert.Equal(blobCount, fixture.Blobs.Count);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Upload(EmployeeCsv(Code()), key));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Upload(
            new HrOpeningImportCsvParser().Template("Assignment", "1") + "key,EMP,77,Primary,2026-10-01",
            key, "Assignment"));
    }

    [Fact]
    public async Task ConcurrentUploadAndExecuteCreateOneDeterministicOutcome()
    {
        var code = Code();
        var key = Guid.NewGuid().ToString("N");
        await using var one = fixture.Session();
        await using var two = fixture.Session();
        var uploads = await Task.WhenAll(one.Upload(EmployeeCsv(code), key), two.Upload(EmployeeCsv(code), key));
        Assert.Equal(uploads[0].Id, uploads[1].Id);
        await one.Service.ValidateAsync(uploads[0].Id, default);
        await using var executionOne = fixture.Session();
        await using var executionTwo = fixture.Session();
        var results = await Task.WhenAll(executionOne.Service.ExecuteAsync(uploads[0].Id, default),
            executionTwo.Service.ExecuteAsync(uploads[0].Id, default));
        Assert.All(results, result => Assert.Equal("Completed", result.Status));
        Assert.Equal(results[0].Rows[0].CreatedEntityId, results[1].Rows[0].CreatedEntityId);
        Assert.Equal(1, await one.Db.HrEmployees.CountAsync(e => e.EmployeeCode == code));
    }

    [Fact]
    public async Task ConcurrentNormalEmployeeCreationIsRevalidatedBeforeExecute()
    {
        var code = Code();
        await using var import = fixture.Session();
        var batch = await import.Upload(EmployeeCsv(code));
        await import.Service.ValidateAsync(batch.Id, default);
        await using var normal = fixture.Session();
        var parsed = new HrOpeningImportCsvParser().Parse(Encoding.UTF8.GetBytes(EmployeeCsv(code)), "Employee", "1", 10)[0];
        await normal.Employees.CreateAsync(1, 101, parsed.Employee!, new(false, false), default);
        var result = await import.Service.ExecuteAsync(batch.Id, default);
        Assert.Equal("ValidationFailed", result.Status);
        Assert.Equal(1, await normal.Db.HrEmployees.CountAsync(e => e.EmployeeCode == code));
    }

    [Fact]
    public async Task AdditionalAssignmentDoesNotUpdatePrimaryPointerOrGrantLocationAccess()
    {
        await using var session = fixture.Session();
        var code = Code();
        var batch = await session.Upload(EmployeeCsv(code, location: "77", assignment: "Primary"));
        await session.Service.ValidateAsync(batch.Id, default);
        var completed = await session.Service.ExecuteAsync(batch.Id, default);
        var csv = new HrOpeningImportCsvParser().Template("Assignment", "1")
            + $"additional,{code},78,Additional,{DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}";
        var assignmentBatch = await session.Upload(csv, importType: "Assignment");
        await session.Service.ValidateAsync(assignmentBatch.Id, default);
        var assignmentResult = await session.Service.ExecuteAsync(assignmentBatch.Id, default);
        Assert.Equal("Completed", assignmentResult.Status);
        var employeeId = completed.Rows[0].CreatedEntityId!.Value;
        Assert.Equal(77, (await session.Db.HrEmployees.SingleAsync(e => e.Id == employeeId)).CurrentPrimaryLocationProjectionId);
        Assert.False(await session.Locations.IsEmployeeAccessibleAsync(1, employeeId,
            [new("Outlet", "712")], default));
    }

    [Fact]
    public async Task WholeBatchFailureRollsBackSuccessfulRowsAndRetryDoesNotDuplicate()
    {
        await using var session = fixture.Session();
        var good = Code(); var bad = Code();
        var csv = EmployeeCsv(good) + "\n" + EmployeeCsv(bad).Split('\n')[1];
        var batch = await session.Upload(csv);
        await session.Service.ValidateAsync(batch.Id, default);
        session.Failure.EmployeeCode = bad;
        await Assert.ThrowsAsync<HrImportExecutionException>(() => session.Service.ExecuteAsync(batch.Id, default));
        Assert.False(await session.Db.HrEmployees.AnyAsync(e => e.EmployeeCode == good || e.EmployeeCode == bad));
        var failure = await session.Service.GetAsync(batch.Id, default);
        Assert.Equal("Failed", failure.Status);
        Assert.All(failure.Rows, row => Assert.NotEmpty(row.Errors));
        session.Failure.EmployeeCode = null;
        await session.Service.ValidateAsync(batch.Id, default);
        await session.Service.ExecuteAsync(batch.Id, default);
        Assert.Equal(2, await session.Db.HrEmployees.CountAsync(e => e.EmployeeCode == good || e.EmployeeCode == bad));
    }

    [Fact]
    public async Task AuditFailureRollsBackUploadAndExecutionAndCleansOwnedStage()
    {
        await using var session = fixture.Session();
        var count = fixture.Blobs.Count;
        session.Failure.Audit = true;
        await Assert.ThrowsAsync<IOException>(() => session.Upload(EmployeeCsv(Code())));
        Assert.Equal(count, fixture.Blobs.Count);
        session.Failure.Audit = false;
        var code = Code();
        var batch = await session.Upload(EmployeeCsv(code));
        await session.Service.ValidateAsync(batch.Id, default);
        session.Failure.Audit = true;
        await Assert.ThrowsAsync<IOException>(() => session.Service.ExecuteAsync(batch.Id, default));
        Assert.False(await session.Db.HrEmployees.AnyAsync(e => e.EmployeeCode == code));
        session.Failure.Audit = false;
        Assert.Equal("Ready", (await session.Service.GetAsync(batch.Id, default)).Status);
    }

    [Fact]
    public async Task SensitiveFieldsAndActionPermissionsAreNotBypassed()
    {
        await using var session = fixture.Session();
        session.EmployeeAccess.Setup(a => a.GetFieldAccess()).Returns(new HrEmployeeFieldAccess(false, false));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.Upload(EmployeeCsv(Code(), identity: "private")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.Upload(EmployeeCsv(Code(), mobile: "private")));
        session.EmployeeAccess.Setup(a => a.EnsureAsync(1, HrEmployeeOperation.Create, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("No create permission."));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.Upload(EmployeeCsv(Code())));
    }

    [Fact]
    public async Task BusinessLocationAndAssignmentPermissionChecksAreReevaluated()
    {
        await using var session = fixture.Session();
        var batch = await session.Upload(EmployeeCsv(Code(), location: "77", assignment: "Primary"));
        session.Access.Setup(a => a.EnsureAsync(1, HrOrganisationOperation.ManageEmployeeLocationAssignments,
            77, It.IsAny<CancellationToken>())).ThrowsAsync(new UnauthorizedAccessException("Location denied."));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.Service.ValidateAsync(batch.Id, default));
        await using var other = fixture.Session(2);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => other.Service.GetAsync(batch.Id, default));
    }

    [Theory]
    [InlineData(ConfidentialFileScanStatus.Rejected)]
    [InlineData(ConfidentialFileScanStatus.Error)]
    [InlineData(ConfidentialFileScanStatus.Pending)]
    public async Task NonCleanSourceNeverCreatesBatch(ConfidentialFileScanStatus status)
    {
        await using var session = fixture.Session();
        session.Inspector.Setup(i => i.ScanAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);
        var count = await session.Db.HrImportBatches.CountAsync();
        await Assert.ThrowsAsync<ConfidentialFileScanException>(() => session.Upload(EmployeeCsv(Code())));
        Assert.Equal(count, await session.Db.HrImportBatches.CountAsync());
    }

    [Theory]
    [InlineData("../private.csv", "text/csv")]
    [InlineData("file.csv", "application/pdf")]
    [InlineData("file.xlsx", "text/csv")]
    public async Task UnsafeFilenameAndMimeAreDenied(string name, string mime)
    {
        await using var session = fixture.Session();
        await Assert.ThrowsAsync<ArgumentException>(() => session.Service.UploadAsync("Employee", "1",
            Guid.NewGuid().ToString("N"), name, mime, new MemoryStream(Encoding.UTF8.GetBytes(EmployeeCsv(Code()))), default));
    }

    [Fact]
    public async Task PreviouslyTrackedBatchCannotRegressACompletedConcurrentExecution()
    {
        await using var first = fixture.Session();
        var code = Code();
        var batch = await first.Upload(EmployeeCsv(code));
        await first.Service.ValidateAsync(batch.Id, default);
        await using var second = fixture.Session();
        await second.Service.ExecuteAsync(batch.Id, default);
        Assert.Equal("Completed", (await first.Service.ExecuteAsync(batch.Id, default)).Status);
        Assert.Equal(1, await first.Db.HrEmployees.CountAsync(e => e.EmployeeCode == code));
    }

    [Fact]
    public async Task ScopedOwnerReadRechecksCurrentEmployeeLocationAccess()
    {
        await using var session = fixture.Session();
        var batch = await session.Upload(EmployeeCsv(Code(), location: "77", assignment: "Primary"));
        await session.Service.ValidateAsync(batch.Id, default);
        var completed = await session.Service.ExecuteAsync(batch.Id, default);
        session.Access.Setup(a => a.GetLocationReadScopeAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HrOperationalLocationReadScope(1, [new("Outlet", "711")]));
        session.EmployeeAccess.Setup(a => a.EnsureEmployeeLocationAccessAsync(1,
            completed.Rows[0].CreatedEntityId!.Value, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Location revoked."));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.Service.GetAsync(batch.Id, default));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Additional")]
    public async Task ScopedOpeningCannotUseUnassignedOrAdditionalLocationForAccess(string assignment)
    {
        await using var session = fixture.Session();
        session.Access.Setup(a => a.GetLocationReadScopeAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HrOperationalLocationReadScope(1, [new("Outlet", "711")]));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.Upload(
            EmployeeCsv(Code(), location: assignment.Length == 0 ? "" : "77", assignment: assignment)));
    }

    [Fact]
    public async Task OversizeStagingFailureAndTamperedSourceNeverPublishBusinessState()
    {
        await using var session = fixture.Session();
        await Assert.ThrowsAsync<ArgumentException>(() => session.Upload(new string('x', 1_000_001)));
        fixture.Blobs.FailStaging = true;
        try { await Assert.ThrowsAsync<IOException>(() => session.Upload(EmployeeCsv(Code()))); }
        finally { fixture.Blobs.FailStaging = false; }
        var code = Code();
        var batch = await session.Upload(EmployeeCsv(code));
        var source = await new HrOpeningImportStore(session.Db, session.Employees, session.Locations,
            TimeProvider.System).GetSourceAsync(1, batch.Id, default);
        fixture.Blobs.Tamper(1, source.File.StorageKey);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Service.ValidateAsync(batch.Id, default));
        Assert.False(await session.Db.HrEmployees.AnyAsync(e => e.EmployeeCode == code));
    }

    [Fact]
    public async Task ScannerExceptionIsAuditedAsRejectedWithoutCreatingBatch()
    {
        await using var session = fixture.Session();
        session.Inspector.Setup(i => i.ScanAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Test scanner unavailable."));
        var exception = await Assert.ThrowsAsync<ConfidentialFileScanException>(() => session.Upload(EmployeeCsv(Code())));
        Assert.Equal(ConfidentialFileScanStatus.Error, exception.Status);
        Assert.True(await session.Db.AuditEvents.AnyAsync(a => a.ActionType == "hr.import.rejected"));
    }

    [Fact]
    public async Task ConcurrentBatchesWithSameEmployeeCodeCannotDuplicateOrPartiallyComplete()
    {
        await using var first = fixture.Session();
        await using var second = fixture.Session();
        var code = Code();
        var one = await first.Upload(EmployeeCsv(code));
        var two = await second.Upload(EmployeeCsv(code));
        await first.Service.ValidateAsync(one.Id, default);
        await second.Service.ValidateAsync(two.Id, default);
        var outcomes = await Task.WhenAll(first.Service.ExecuteAsync(one.Id, default),
            second.Service.ExecuteAsync(two.Id, default));
        Assert.Single(outcomes, b => b.Status == "Completed");
        Assert.Single(outcomes, b => b.Status == "ValidationFailed");
        Assert.Equal(1, await first.Db.HrEmployees.CountAsync(e => e.EmployeeCode == code));
    }

    [Fact]
    public async Task FailedOrphanCleanupIsExplicitAndLimitedToOwnedStage()
    {
        await using var session = fixture.Session();
        var before = fixture.Blobs.Count;
        session.Failure.Audit = true;
        fixture.Blobs.FailCleanup = true;
        try
        {
            await Assert.ThrowsAsync<AggregateException>(() => session.Upload(EmployeeCsv(Code())));
            Assert.Equal(before + 1, fixture.Blobs.Count);
        }
        finally { fixture.Blobs.FailCleanup = false; }
        await fixture.Blobs.DeleteOwnedStageAsync(1, fixture.Blobs.LastStageKey!, default);
        Assert.Equal(before, fixture.Blobs.Count);
    }

    [Fact]
    public async Task ExpiredRetainedSourceCannotExecuteButKeepsMetadataEvidence()
    {
        var clock = new ImportTestClock();
        await using var session = fixture.Session(clock: clock);
        var code = Code();
        var batch = await session.Upload(EmployeeCsv(code));
        await session.Service.ValidateAsync(batch.Id, default);
        clock.Now = clock.Now.AddDays(366);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Service.ExecuteAsync(batch.Id, default));
        Assert.False(await session.Db.HrEmployees.AnyAsync(e => e.EmployeeCode == code));
        Assert.Equal("Ready", (await session.Service.GetAsync(batch.Id, default)).Status);
    }

    [Fact]
    public async Task ExistingPrimaryAssignmentConflictIsReportedInDryRun()
    {
        await using var session = fixture.Session();
        var code = Code();
        var employeeBatch = await session.Upload(EmployeeCsv(code, location: "77", assignment: "Primary"));
        await session.Service.ValidateAsync(employeeBatch.Id, default);
        var employee = await session.Service.ExecuteAsync(employeeBatch.Id, default);
        var csv = new HrOpeningImportCsvParser().Template("Assignment", "1")
            + $"opening,{code},78,Primary,{DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}";
        var assignmentBatch = await session.Upload(csv, importType: "Assignment");
        var invalid = await session.Service.ValidateAsync(assignmentBatch.Id, default);
        Assert.Equal("ValidationFailed", invalid.Status);
        Assert.Contains(invalid.Rows[0].Errors, e => e.Contains("transfer workflow", StringComparison.Ordinal));
        Assert.Equal(1, await session.Db.HrEmployeeLocationAssignments.CountAsync(
            a => a.EmployeeId == employee.Rows[0].CreatedEntityId));
    }
}

public sealed class HrOpeningImportDatabase : IAsyncLifetime
{
    private PostgresTestDatabase? database;
    public readonly ImportTestBlobs Blobs = new();
    public string ConnectionString => database!.ConnectionString;

    public async Task InitializeAsync()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var connection = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connection))
        {
            var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "RetailPOS.API", "appsettings.json"));
            connection = new ConfigurationBuilder().AddJsonFile(path).Build().GetConnectionString("DefaultConnection");
        }
        database = await PostgresTestDatabase.CreateAsync(connection!, PostgresTestDatabasePrefix.MigrationBaseline);
        await using var db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>().UseNpgsql(ConnectionString).Options);
        await db.Database.MigrateAsync();
        await db.Database.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        db.Businesses.AddRange(new Business { Id = 1, Name = "Import" }, new Business { Id = 2, Name = "Other" });
        await db.SaveChangesAsync();
        db.Outlets.AddRange(new Outlet { Id = 711, BusinessId = 1, Name = "Primary", Address = "Test" },
            new Outlet { Id = 712, BusinessId = 1, Name = "Additional", Address = "Test" });
        db.HrEmploymentTypes.Add(new HrEmploymentType { Id = 123, BusinessId = 1, Code = "PERM", Name = "Permanent", IsActive = true });
        db.OperationalLocationProjections.AddRange(Location(77, 1, "711"), Location(78, 1, "712"),
            Location(88, 2, "800"), Location(79, 1, "714", false), Location(80, 1, "9999"));
        db.HrOrgUnits.Add(new HrOrgUnit { Id = 502, BusinessId = 1, Code = "ORG", Name = "Org", UnitType = "Department",
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1) });
        await db.SaveChangesAsync();
        db.HrPositions.Add(new HrPosition { Id = 501, BusinessId = 1, Code = "POS", Name = "Position", OrgUnitId = 502,
            Status = HrPositionStatus.Vacant, EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1) });
        await db.SaveChangesAsync();
    }

    private static OperationalLocationProjection Location(long id, long business, string source, bool active = true, string type = "Outlet") =>
        new() { Id = id, BusinessId = business, SourceSystem = "RetailPOS", SourceEntityType = type,
            SourceEntityId = source, SourceVersion = 1, SourcePayloadHash = new string('A', 64),
            Name = "Import location", IsSourceActive = active, SyncStatus = "Current" };

    public ImportSession Session(long businessId = 1, TimeProvider? clock = null) => new(this, businessId, clock);
    public async Task DisposeAsync() { if (database is not null) await database.DisposeAsync(); }
}

public sealed class ImportSession : IAsyncDisposable
{
    public RetailPOSDbContext Db { get; }
    public HrOpeningImportService Service { get; }
    public HrEmployeeStore Employees { get; }
    public HrEmployeeLocationAssignmentStore Locations { get; }
    public readonly ImportFailure Failure = new();
    public readonly Mock<IHrOrganisationAccess> Access = new();
    public readonly Mock<IHrEmployeeAccess> EmployeeAccess = new();
    public readonly Mock<IConfidentialContentInspector> Inspector = new();

    public ImportSession(HrOpeningImportDatabase fixture, long businessId, TimeProvider? clock = null)
    {
        var context = new AuditContext();
        context.Initialize(AuditContext.ForBusinessSystemJob("opening-import-tests", businessId));
        Db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .AddInterceptors(new AuditChangeTrackerInterceptor(context, NullLogger<AuditChangeTrackerInterceptor>.Instance), Failure)
            .Options);
        Access.Setup(a => a.RequireBusinessId()).Returns(businessId);
        Access.Setup(a => a.RequireRealUserId()).Returns(101);
        Access.Setup(a => a.EnsureAsync(It.IsAny<long>(), It.IsAny<HrOrganisationOperation>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        Access.Setup(a => a.GetLocationReadScopeAsync(businessId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HrOperationalLocationReadScope(businessId, null));
        Access.Setup(a => a.EnsureLocationAccessibleAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        EmployeeAccess.Setup(a => a.EnsureAsync(It.IsAny<long>(), It.IsAny<HrEmployeeOperation>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        EmployeeAccess.Setup(a => a.GetFieldAccess()).Returns(new HrEmployeeFieldAccess(true, true));
        EmployeeAccess.Setup(a => a.EnsureEmployeeLocationAccessAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        Inspector.Setup(i => i.ValidateAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConfidentialContentValidation("text/csv", ".csv"));
        Inspector.Setup(i => i.ScanAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConfidentialFileScanStatus.Clean);
        var auditService = new AuditService(Db, context, Mock.Of<IServiceProvider>(), NullLogger<AuditService>.Instance);
        var audit = new RequiredAuditTransaction(Db, context, auditService, NullLogger<RequiredAuditTransaction>.Instance);
        Employees = new HrEmployeeStore(Db); Locations = new HrEmployeeLocationAssignmentStore(Db);
        clock ??= TimeProvider.System;
        var salaryStore = new PayrollSalarySetupStore(Db, clock);
        var loanStore = new PayrollEmployeeLoanStore(Db, salaryStore, clock);
        var payrollAccess = new Mock<IPayrollSalarySetupAccess>();
        payrollAccess.Setup(a => a.RequireBusinessId()).Returns(businessId);
        payrollAccess.Setup(a => a.RequireRealUserId()).Returns(101);
        payrollAccess.Setup(a => a.EnsureBusinessPermissionAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        payrollAccess.Setup(a => a.EnsureAsync(
                It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        Service = new HrOpeningImportService(Access.Object, EmployeeAccess.Object,
            new HrOpeningImportStore(Db, Employees, Locations, clock, salaryStore, loanStore), new HrOpeningImportCsvParser(),
            audit, clock, fixture.Blobs, new ImportTestEncryption(), Inspector.Object,
            new ConfidentialFileLimits(1_000_000, 1_000_000), new HrImportPolicy(100, 365),
            payrollAccess.Object, salaryStore);
    }

    public async Task<HrImportBatchResult> Upload(string csv, string? key = null, string importType = "Employee")
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        return await Service.UploadAsync(importType, "1", key ?? Guid.NewGuid().ToString("N"), "opening.csv", "text/csv", stream, default);
    }

    public ValueTask DisposeAsync() => Db.DisposeAsync();
}

public sealed class ImportFailure : SaveChangesInterceptor
{
    public bool Audit { get; set; }
    public string? EmployeeCode { get; set; }
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Audit && eventData.Context!.ChangeTracker.Entries<AuditEvent>().Any(e => e.State == EntityState.Added))
            throw new IOException("Injected import audit failure.");
        if (EmployeeCode is not null && eventData.Context!.ChangeTracker.Entries<HrEmployee>()
            .Any(e => e.State == EntityState.Added && e.Entity.EmployeeCode == EmployeeCode))
            throw new DbUpdateException("Injected second-row conflict.");
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

public sealed class ImportTestBlobs : IPrivateBlobStore
{
    private readonly ConcurrentDictionary<(long, string), byte[]> content = new();
    public int Count => content.Count;
    public bool FailStaging { get; set; }
    public bool FailCleanup { get; set; }
    public string? LastStageKey { get; private set; }
    public void Tamper(long businessId, string key) => content[(businessId, key)][0] ^= 1;
    public Task<string> StageAsync(long businessId, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        if (FailStaging) throw new IOException("Injected owned stage failure.");
        var key = Guid.NewGuid().ToString("N");
        LastStageKey = key;
        content[(businessId, key)] = bytes.ToArray();
        return Task.FromResult(key);
    }
    public Task<Stream> OpenAsync(long businessId, string key, CancellationToken cancellationToken) =>
        Task.FromResult<Stream>(new MemoryStream(content[(businessId, key)], writable: false));
    public Task DeleteOwnedStageAsync(long businessId, string key, CancellationToken cancellationToken)
    {
        if (FailCleanup) throw new IOException("Injected owned orphan cleanup failure.");
        content.TryRemove((businessId, key), out _);
        return Task.CompletedTask;
    }

}

public sealed class ImportTestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => Now;
}

public sealed class ImportTestEncryption : IConfidentialFileEncryption
{
    public Task<EncryptedConfidentialContent> EncryptAsync(ReadOnlyMemory<byte> plaintext, CancellationToken cancellationToken) =>
        Task.FromResult(new EncryptedConfidentialContent(Transform(plaintext), "test-only-import-key"));
    public Task<byte[]> DecryptAsync(ReadOnlyMemory<byte> ciphertext, string keyReference, CancellationToken cancellationToken) =>
        Task.FromResult(Transform(ciphertext));
    private static byte[] Transform(ReadOnlyMemory<byte> bytes) => bytes.ToArray().Select(b => (byte)(b ^ 0x5a)).ToArray();
}
