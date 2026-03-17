namespace RetailPOS.API.DTOs.Customer;

public class CustomerListDto
{
    public List<CustomerDto> Customers { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
}
