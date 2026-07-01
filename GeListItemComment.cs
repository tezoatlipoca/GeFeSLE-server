public class GeListItemComment
{
    public int Id { get; set; }

    public int ListId { get; set; }
    public int ItemId { get; set; }
    public int? ParentCommentId { get; set; }

    // Remote ActivityPub object identifiers.
    public string RemoteObjectIri { get; set; } = string.Empty;
    public string? InReplyToIri { get; set; }
    public string? ActorIri { get; set; }
    public string? AttributedToIri { get; set; }

    // Cached remote Note fields for local rendering.
    public string? Name { get; set; }
    public string? ContentHtml { get; set; }
    public string? Summary { get; set; }
    public string? RawNoteJson { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public int? RemoteLikesCount { get; set; }
    public string? RemoteLikeActorsJson { get; set; }
    public DateTimeOffset? RemoteLikesLastCheckedAt { get; set; }
    public DateTime LastReceivedAt { get; set; } = DateTime.UtcNow;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
}
