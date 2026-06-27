namespace GeFeSLE.DTOs
{
    /// <summary>
    /// Data transfer object for list responses. Includes owner and permission info.
    /// </summary>
    public class GeListResponseDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Comment { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
        public string? CreatorId { get; set; }
        public string? CreatorName { get; set; }
        public string? ActivityPubId { get; set; }
        public GeListVisibility Visibility { get; set; } = GeListVisibility.Public;
        public bool IsOrdered { get; set; } = false;
        public int VisibleItemCount { get; set; }
        public List<UserSummaryDto> Owners { get; set; } = new List<UserSummaryDto>();
        public List<UserSummaryDto> Contributors { get; set; } = new List<UserSummaryDto>();
    }
}
