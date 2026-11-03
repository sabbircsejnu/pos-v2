using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailPOS.Core.Entities
{
    /// <summary>
    /// Junction table - which variations apply to which products
    /// </summary>
    [Table("product_variations")]
    public class ProductVariation
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Required]
        [Column("product_id")]
        public long ProductId { get; set; }

        [Required]
        [Column("variation_id")]
        public long VariationId { get; set; }

        [Column("is_required")]
        public bool IsRequired { get; set; } = true;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("ProductId")]
        public virtual Product Product { get; set; } = null!;

        [ForeignKey("VariationId")]
        public virtual Variation Variation { get; set; } = null!;
    }
}
