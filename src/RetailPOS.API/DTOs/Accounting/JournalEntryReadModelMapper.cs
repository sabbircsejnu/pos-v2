using RetailPOS.Application.AccountingJournals.Abstractions;

namespace RetailPOS.API.DTOs.Accounting;

internal static class JournalEntryReadModelMapper
{
    public static JournalListDto ToDto(JournalListReadModel model) =>
        new()
        {
            Entries = model.Entries.Select(ToDto).ToList(),
            TotalCount = model.TotalCount,
            PageNumber = model.PageNumber,
            PageSize = model.PageSize
        };

    public static JournalEntryDto ToDto(JournalEntryReadModel model) =>
        new()
        {
            Id = model.Id,
            BusinessId = model.BusinessId,
            JournalNumber = model.JournalNumber,
            JournalType = model.JournalType,
            BusinessDate = model.BusinessDate,
            TransactionDate = model.TransactionDate,
            PostingDate = model.PostingDate,
            FinancialYearId = model.FinancialYearId,
            AccountingPeriodId = model.AccountingPeriodId,
            ReferenceType = model.ReferenceType,
            ReferenceId = model.ReferenceId,
            ReferenceNumber = model.ReferenceNumber,
            Description = model.Description,
            OutletId = model.OutletId,
            WarehouseId = model.WarehouseId,
            TerminalId = model.TerminalId,
            CashSessionId = model.CashSessionId,
            Status = model.Status,
            IsSystemGenerated = model.IsSystemGenerated,
            ReversalOfJournalEntryId = model.ReversalOfJournalEntryId,
            ReversedByJournalEntryId = model.ReversedByJournalEntryId,
            SubmittedAt = model.SubmittedAt,
            SubmittedBy = model.SubmittedBy,
            ApprovedAt = model.ApprovedAt,
            ApprovedBy = model.ApprovedBy,
            RejectedAt = model.RejectedAt,
            RejectedBy = model.RejectedBy,
            RejectionReason = model.RejectionReason,
            CreatedAt = model.CreatedAt,
            CreatedBy = model.CreatedBy,
            UpdatedAt = model.UpdatedAt,
            UpdatedBy = model.UpdatedBy,
            PostedAt = model.PostedAt,
            PostedBy = model.PostedBy,
            CancelledAt = model.CancelledAt,
            CancelledBy = model.CancelledBy,
            CancellationReason = model.CancellationReason,
            ReversedAt = model.ReversedAt,
            ReversedBy = model.ReversedBy,
            Lines = model.Lines.Select(line => new JournalEntryLineDto
            {
                Id = line.Id,
                AccountId = line.AccountId,
                Description = line.Description,
                DebitAmount = line.DebitAmount,
                CreditAmount = line.CreditAmount,
                OutletId = line.OutletId,
                WarehouseId = line.WarehouseId,
                SupplierId = line.SupplierId,
                CustomerId = line.CustomerId,
                PaymentMethodId = line.PaymentMethodId,
                CostCenterId = line.CostCenterId
            }).ToList(),
            Attachments = model.Attachments.Select(attachment => new JournalEntryAttachmentDto
            {
                Id = attachment.Id,
                FileName = attachment.FileName,
                ContentType = attachment.ContentType,
                FileSizeBytes = attachment.FileSizeBytes,
                CreatedAt = attachment.CreatedAt,
                CreatedBy = attachment.CreatedBy
            }).ToList()
        };
}
