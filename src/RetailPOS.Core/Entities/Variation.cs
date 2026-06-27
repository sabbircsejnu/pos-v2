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

        /// <summary>
        /// When true, all options of this variation are pre-selected by default
        /// when a user assigns this variation type to a product for the first time.
        /// Useful for variation types where most products use every option (e.g. Size).
        /// Defaults to false (user must manually choose options).
        /// </summary>
        [Column("default_select_all_options")]
        public bool AutoSelectAllOptions { get; set; } = false;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual ICollection<VariationOption> Options { get; set; } = new List<VariationOption>();
        public virtual ICollection<ProductVariation> ProductVariations { get; set; } = new List<ProductVariation>();
    }
}
