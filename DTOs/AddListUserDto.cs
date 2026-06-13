using System.ComponentModel.DataAnnotations;

namespace GeFeSLE.DTOs
{
    /// <summary>
    /// Request model for adding a user to a list as an owner or contributor.
    /// </summary>
    public class AddListUserDto
    {
        /// <summary>
        /// The username of the user to add to the list.
        /// </summary>
        /// <example>john.doe</example>
        [Required(ErrorMessage = "Username is required")]
        [StringLength(50, MinimumLength = 1, ErrorMessage = "Username must be between 1 and 50 characters")]
        public string Username { get; set; } = string.Empty;
    }
}