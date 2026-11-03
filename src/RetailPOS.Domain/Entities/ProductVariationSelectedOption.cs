using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailPOS.Core.Entities
{
    /// <summary>
    /// Stores the specific variation options selected for a product.
    /// For example: Product X uses Color = Black, Blue (not all 4 global colours)
    /// and Size = S, M, L (not all 5 global sizes).
    /// This allows combination generation to use only the product-selected options
    /// rather than all global options under each variation type.
    /// </summary>
    [Table("product_variation_selected_options")]
    public class ProductVariationSelectedOption
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

        [Required]
        [Column("option_id")]
        public long OptionId { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("ProductId")]
        public virtual Product Product { get; set; } = null!;

        [ForeignKey("VariationId")]
        public virtual Variation Variation { get; set; } = null!;

        [ForeignKey("OptionId")]
        public virtual VariationOption Option { get; set; } = null!;
    }
}
