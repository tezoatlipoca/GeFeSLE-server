using System.ComponentModel.DataAnnotations;
using System.ComponentModel;

namespace GeFeSLE.DTOs
{
    /// <summary>
    /// Request model for removing a user from a list with a specific role
    /// </summary>
    public class DeleteListUserDto
    {
        /// <summary>
        /// The ID of the list from which to remove the user
        /// </summary>
        /// <example>123</example>
        [Required(ErrorMessage = "List ID is required")]
        [Range(1, int.MaxValue, ErrorMessage = "List ID must be a positive integer")]
        public string ListId { get; set; } = string.Empty;

        /// <summary>
        /// The username of the user to be removed from the list
        /// </summary>
        /// <example>john.doe</example>
        [Required(ErrorMessage = "Username is required")]
        [StringLength(50, MinimumLength = 1, ErrorMessage = "Username must be between 1 and 50 characters")]
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// The role to remove from the user for this list
        /// </summary>
        /// <example>contributor</example>
        [Required(ErrorMessage = "Role is required")]
        [RegularExpression("^(listowner|contributor)$", ErrorMessage = "Role must be either 'listowner' or 'contributor'")]
        public string Role { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response model for list user operations
    /// </summary>
    public class ListUserOperationResponse
    {
        /// <summary>
        /// Indicates whether the operation was successful
        /// </summary>
        /// <example>true</example>
        public bool Success { get; set; }

        /// <summary>
        /// Message describing the result of the operation
        /// </summary>
        /// <example>john.doe REMOVED FROM MyList as a contributor</example>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// The username that was affected by the operation
        /// </summary>
        /// <example>john.doe</example>
        public string? Username { get; set; }

        /// <summary>
        /// The list ID that was affected by the operation
        /// </summary>
        /// <example>123</example>
        public int? ListId { get; set; }

        /// <summary>
        /// The role that was affected by the operation
        /// </summary>
        /// <example>contributor</example>
        public string? Role { get; set; }
    }
}
