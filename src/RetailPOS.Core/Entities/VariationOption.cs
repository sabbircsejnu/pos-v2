using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailPOS.Core.Entities
{
    /// <summary>
    /// Option/value for a variation (e.g., Red, Blue for Color variation)
    /// </summary>
    [Table("variation_options")]
    public class VariationOption
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Required]
        [Column("variation_id")]
        public long VariationId { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("price_adjustment", TypeName = "decimal(18,2)")]
        public decimal PriceAdjustment { get; set; } = 0;

        [Column("display_order")]
        public int DisplayOrder { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("VariationId")]
        public virtual Variation Variation { get; set; } = null!;

        public virtual ICollection<ProductVariantOption> ProductVariantOptions { get; set; } = new List<ProductVariantOption>();
    }
}
