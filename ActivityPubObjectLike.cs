public class ActivityPubObjectLike
{
    public int Id { get; set; }

    public int ListId { get; set; }
    public int? ItemId { get; set; }
    public int? CommentId { get; set; }

    // Canonical local object IRI being liked (item or comment endpoint URL).
    public string ObjectIri { get; set; } = string.Empty;

    // Remote actor that liked the object.
    public string ActorIri { get; set; } = string.Empty;

    // Like activity id (if provided). Used to handle Undo when object points to Like activity id.
    public string? LikeActivityIri { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
}
