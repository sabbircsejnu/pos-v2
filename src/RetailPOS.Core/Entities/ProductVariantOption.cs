using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailPOS.Core.Entities
{
    /// <summary>
    /// Junction table - which variation options are selected for a product variant
    /// </summary>
    [Table("product_variant_options")]
    public class ProductVariantOption
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Required]
        [Column("variant_id")]
        public long VariantId { get; set; }

        [Required]
        [Column("option_id")]
        public long OptionId { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("VariantId")]
        public virtual ProductVariant Variant { get; set; } = null!;

        [ForeignKey("OptionId")]
        public virtual VariationOption Option { get; set; } = null!;
    }
}
