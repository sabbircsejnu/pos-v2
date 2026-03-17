namespace RetailPOS.API.DTOs.Reports;

public class PurchaseSummaryDto
{
    public int TotalOrders { get; set; }
    public decimal TotalAmount { get; set; }
    public int PendingApprovals { get; set; }
    public int ReceivedOrders { get; set; }
}

public class PurchaseBySupplierDto
{
    public long SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public int OrderCount { get; set; }
    public decimal TotalAmount { get; set; }
}
