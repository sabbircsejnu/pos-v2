namespace RetailPOS.API.DTOs.Product
{
    public class ProductVariationDto
    {
        public long VariationId { get; set; }
        public string VariationName { get; set; } = string.Empty;
        public bool IsRequired { get; set; }
        public List<VariationOptionInfo> Options { get; set; } = new();
    }

    public class VariationOptionInfo
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal PriceAdjustment { get; set; }
    }

    public class AssignVariationsRequest
    {
        public long ProductId { get; set; }
        public List<long> VariationIds { get; set; } = new();
    }

    public class CombinationDto
    {
        public long? Id { get; set; }
        public string Sku { get; set; } = string.Empty;
        public string? Barcode { get; set; }
        public decimal PriceAdjustment { get; set; }
        public decimal CostAdjustment { get; set; }
        public List<long> OptionIds { get; set; } = new();
        public List<CombinationOptionInfo> Options { get; set; } = new();
        public string CombinationName { get; set; } = string.Empty;
        public decimal FinalPrice { get; set; }
    }

    public class CombinationOptionInfo
    {
        public long Id { get; set; }
        public string VariationName { get; set; } = string.Empty;
        public string OptionName { get; set; } = string.Empty;
        public decimal PriceAdjustment { get; set; }
    }

    public class GenerateCombinationsRequest
    {
        public long ProductId { get; set; }
        public List<long>? VariationIds { get; set; } // null = all assigned variations
    }

    public class CreateCombinationRequest
    {
        public long ProductId { get; set; }
        public string? Sku { get; set; }
        public string? Barcode { get; set; }
        public decimal PriceAdjustment { get; set; } = 0;
        public decimal CostAdjustment { get; set; } = 0;
        public List<long> OptionIds { get; set; } = new();
    }

    public class UpdateCombinationRequest
    {
        public string? Sku { get; set; }
        public string? Barcode { get; set; }
        public decimal PriceAdjustment { get; set; }
        public decimal CostAdjustment { get; set; }
    }
}
