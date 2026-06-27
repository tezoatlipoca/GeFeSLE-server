namespace GeFeSLE.DTOs
{
    /// <summary>
    /// Data transfer object for user responses. Used when returning user data from API endpoints.
    /// </summary>
    public class UserResponseDto
    {
        public string? Id { get; set; }
        public string? UserName { get; set; }
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public DateTime LastAccessTime { get; set; }
        public List<string> Roles { get; set; } = new List<string>();
    }
}
