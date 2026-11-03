using System.ComponentModel.DataAnnotations;

namespace RetailPOS.API.DTOs.Sale;

public class VoidSaleDto
{
    [Required]
    [MinLength(3, ErrorMessage = "Void reason must be at least 3 characters")]
    public string? Reason { get; set; }

    /// <summary>The terminal whose open cash session pays back the cash (D1); optional when it can be inferred.</summary>
    public long? TerminalId { get; set; }
}
