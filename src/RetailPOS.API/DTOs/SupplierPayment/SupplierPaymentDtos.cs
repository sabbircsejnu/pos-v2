namespace RetailPOS.API.DTOs.SupplierPayment;

public class SupplierPaymentDto
{
    public long Id { get; set; }
    public long SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public long? OutletId { get; set; }
    public DateTime PaymentDate { get; set; }
    public DateTime? PostingDate { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public long PaymentAccountId { get; set; }
    public decimal Amount { get; set; }
    public decimal AllocatedAmount { get; set; }
    public decimal AdvanceAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    public long? JournalEntryId { get; set; }
}

public class CreateSupplierPaymentDto
{
    public long SupplierId { get; set; }
    public long? OutletId { get; set; }
    public DateTime PaymentDate { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public long PaymentAccountId { get; set; }
    public decimal Amount { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    public List<SupplierPaymentAllocationLineDto> Allocations { get; set; } = new();
}

public class SupplierPaymentAllocationLineDto
{
    public long BillId { get; set; }
    public long? SupplierPayableDocumentId { get; set; }
    public decimal Amount { get; set; }
}

public class ApplySupplierAdvanceDto
{
    public long SupplierPayableDocumentId { get; set; }
    public decimal Amount { get; set; }
    public DateTime EffectiveDate { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
}

public class ReverseSupplierAdvanceUtilizationDto
{
    public DateTime PostingDate { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class ReverseSupplierPaymentDto
{
    public DateTime PostingDate { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class SupplierAdvanceDto
{
    public long Id { get; set; }
    public long SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public long? OutletId { get; set; }
    public DateTime PaymentDate { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public long PaymentAccountId { get; set; }
    public decimal Amount { get; set; }
    public decimal RemainingAmount { get; set; }
    public decimal AppliedAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    public long? JournalEntryId { get; set; }
}

public class CreateSupplierAdvanceDto
{
    public long SupplierId { get; set; }
    public long? OutletId { get; set; }
    public DateTime PaymentDate { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public long PaymentAccountId { get; set; }
    public decimal Amount { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
}
