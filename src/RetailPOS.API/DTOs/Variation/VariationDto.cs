namespace RetailPOS.API.DTOs.Variation
{
    public class VariationDto
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; }
        public List<VariationOptionDto> Options { get; set; } = new();
    }

    public class CreateVariationDto
    {
        public string Name { get; set; } = string.Empty;
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; } = true;
        public List<CreateVariationOptionDto> Options { get; set; } = new();
    }

    public class UpdateVariationDto
    {
        public string Name { get; set; } = string.Empty;
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; }
    }

    public class VariationOptionDto
    {
        public long Id { get; set; }
        public long VariationId { get; set; }
        public string VariationName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal PriceAdjustment { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; }
    }

    public class CreateVariationOptionDto
    {
        public string Name { get; set; } = string.Empty;
        public decimal PriceAdjustment { get; set; } = 0;
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class UpdateVariationOptionDto
    {
        public string Name { get; set; } = string.Empty;
        public decimal PriceAdjustment { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; }
    }
}
