// class to mirror the ApActorDto
using GeFeSLE.DTOs;

public class GeAPActor
{
    public string Id { get; set; } = string.Empty; // the IRI of this actor. 
    public string Type { get; set; } = "Person";
    public GeAPActor? Context { get; set; } = null;
    public string? PreferredUsername { get; set; } = null;
    public string? Name { get; set; } = null;
    public string? Summary { get; set; } = null;
    public string? Inbox { get; set; } = null;
    public string? Outbox { get; set; } = null;
    public string? Followers { get; set; } = null;
    public GeAPAttachment? Icon { get; set; } = null;
    public GeAPAttachment? Image { get; set; } = null;
    public string? Url { get; set; } = null;
}

