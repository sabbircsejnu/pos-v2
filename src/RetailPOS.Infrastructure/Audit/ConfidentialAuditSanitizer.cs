using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities.Audit;

namespace RetailPOS.Infrastructure.Audit;

internal static class ConfidentialAuditSanitizer
{
    public static void Sanitize(AuditEvent header)
    {
        var confidential = ConfidentialAuditPolicy.IsHr(header.Module, header.PrimaryEntityType)
            || ConfidentialAuditPolicy.IsPayroll(header.Module, header.PrimaryEntityType)
            || header.Entities.Any(entity => ConfidentialAuditPolicy.IsHrEntity(entity.EntityType)
                || ConfidentialAuditPolicy.IsPayrollEntity(entity.EntityType));
        if (!confidential) return;

        header.ActionSummary = "Confidential HR/Payroll audit event";
        header.PrimaryEntityId = ConfidentialAuditPolicy.SafeReference(header.PrimaryEntityId);
        header.RequestPath = header.Source == AuditSource.SystemJob
            && header.RequestPath?.StartsWith("job:", StringComparison.Ordinal) == true
            ? header.RequestPath : null;
        header.ErrorMessage = header.ErrorMessage is null ? null : ConfidentialAuditPolicy.RedactedToken;
        header.Metadata = null;
        foreach (var entity in header.Entities)
        {
            entity.EntityId = ConfidentialAuditPolicy.SafeReference(entity.EntityId) ?? string.Empty;
            entity.Metadata = null;
            entity.InternalOperationName = null;
            foreach (var field in entity.FieldChanges)
            {
                field.OldDisplayValue = null;
                field.NewDisplayValue = null;
            }
            foreach (var field in entity.FieldChanges.Where(field => !ConfidentialAuditPolicy.IsSafeField(field.FieldName)))
            {
                field.OldValue = System.Text.Json.JsonSerializer.Serialize(ConfidentialAuditPolicy.RedactedToken);
                field.NewValue = System.Text.Json.JsonSerializer.Serialize(ConfidentialAuditPolicy.RedactedToken);
                field.OldDisplayValue = null;
                field.NewDisplayValue = null;
                field.ReferenceEntityType = null;
                field.IsReferenceField = false;
            }
        }
    }
}
