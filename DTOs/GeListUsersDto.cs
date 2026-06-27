namespace GeFeSLE.DTOs
{
    /// <summary>
    /// Data transfer object for returning list users and permissions info.
    /// </summary>
    public class GeListUsersDto
    {
        public UserSummaryDto? Creator { get; set; }
        public List<UserSummaryDto> ListOwners { get; set; } = new List<UserSummaryDto>();
        public List<UserSummaryDto> Contributors { get; set; } = new List<UserSummaryDto>();
    }
}
