namespace GeFeSLE.DTOs
{
    /// <summary>
    /// Data transfer object for list item responses.
    /// </summary>
    public class GeListItemResponseDto
    {
        public int Id { get; set; }
        public int ListId { get; set; }
        public string? Name { get; set; }
        public string? Comment { get; set; }
        public bool IsComplete { get; set; }
        public bool Visible { get; set; } = true;
        public bool IsDeleted { get; set; } = false;
        public int? RedirectToItemId { get; set; }
        public int? ModerationItemId { get; set; }
        public int? ModeratedItemId { get; set; }
        public IReadOnlyList<int> PreviousRedirectItemIds { get; set; } = Array.Empty<int>();
        public string? OriginatorActorIri { get; set; }
        public string? SourceObjectIri { get; set; }
        public string? SourceAttributedToIri { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
    }
}
