using System.ComponentModel.DataAnnotations;

namespace GeFeSLE.DTOs
{
    /// <summary>
    /// Data transfer object for creating or updating a user.
    /// </summary>
    public class UserCreateUpdateDto
    {
        [Required]
        public string? UserName { get; set; }

        [EmailAddress]
        public string? Email { get; set; }

        public string? PhoneNumber { get; set; }
    }
}
