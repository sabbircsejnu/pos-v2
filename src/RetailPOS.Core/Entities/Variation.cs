using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailPOS.Core.Entities
{
    /// <summary>
    /// Global product variation (e.g., Color, Size, Material)
    /// Reusable across multiple products
    /// </summary>
    [Table("variations")]
    public class Variation
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("display_order")]
        public int DisplayOrder { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual ICollection<VariationOption> Options { get; set; } = new List<VariationOption>();
        public virtual ICollection<ProductVariation> ProductVariations { get; set; } = new List<ProductVariation>();
    }
}
