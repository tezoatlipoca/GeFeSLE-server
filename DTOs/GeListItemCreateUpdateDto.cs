using System.ComponentModel.DataAnnotations;

namespace GeFeSLE.DTOs
{
    /// <summary>
    /// Data transfer object for creating or updating a list item.
    /// </summary>
    public class GeListItemCreateUpdateDto
    {
        public int ListId { get; set; }

        [Required]
        public string? Name { get; set; }

        public string? Comment { get; set; }

        public bool IsComplete { get; set; }

        public bool Visible { get; set; } = true;

        public List<string> Tags { get; set; } = new List<string>();
    }
}
