using RetailPOS.Application.ConfidentialFiles;
using RetailPOS.Application.HR.Employees;
using RetailPOS.Application.HR.Leave;

namespace RetailPOS.Infrastructure.HR.Leave;

public sealed class HrConfidentialFileVersionStore(
    RetailPOS.Infrastructure.HR.Employees.HrEmployeeDocumentStore employeeDocuments,
    HrLeaveRequestStore leaveRequests) : IConfidentialFileVersionStore
{
    public Task AddAsync(ConfidentialFileVersion version, CancellationToken cancellationToken) =>
        Store(version).AddAsync(version, cancellationToken);

    public Task AddRejectedAsync(ConfidentialFileVersion version, CancellationToken cancellationToken) =>
        Store(version).AddRejectedAsync(version, cancellationToken);

    public async Task<ConfidentialFileVersion?> FindAsync(
        long businessId, Guid versionId, CancellationToken cancellationToken)
    {
        var employeeVersion = await employeeDocuments.FindAsync(businessId, versionId, cancellationToken);
        return employeeVersion
            ?? await leaveRequests.FindAsync(businessId, versionId, cancellationToken);
    }

    private IConfidentialFileVersionStore Store(ConfidentialFileVersion version) =>
        version.Classification switch
        {
            ConfidentialFileClassification.EmployeeDocument => employeeDocuments,
            ConfidentialFileClassification.LeaveRequestAttachment => leaveRequests,
            _ => throw new ArgumentOutOfRangeException(
                nameof(version), version.Classification, "HR file classification is not supported.")
        };
}
