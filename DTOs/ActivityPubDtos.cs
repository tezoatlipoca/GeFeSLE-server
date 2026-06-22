using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;

namespace GeFeSLE.DTOs
{
    public sealed record ApActorDto(
    string id,
    string type,
    ApActorDto? context,                 // optional; can omit entirely
    string? preferredUsername,
    string? name,
    string? summary,
    string? inbox,
    string? outbox,
    string? followers,
    ApAttachmentDto? icon,
    ApAttachmentDto? image,
    string? url
  );

    public sealed record ApLinkDto(
    string type,
    string? href
  );

    public sealed record ApAttachmentDto(
      string? type,
      string? url,
      string? mediaType,
      int? width,
      int? height
    );

    // we need these for the /apv1/lists/{listId}/followers endpoint, which returns a Collection of actors.
    public sealed record ApCollectionDto(
        string id,
        string type,                        // "Collection" or "OrderedCollection"
        IReadOnlyList<string> items       // for "Collection"
    );

    public sealed record ApOrderedCollectionDto(
      string id,
      string type,                        // "OrderedCollection"
      IReadOnlyList<string> orderedItems // for "OrderedCollection"
    );

    // this junk is for INBOUND activities (follow, unfollow etc.)
    public sealed record ApActivityDto(
      string id,
      string type,                        // "Follow" or "Create" etc.
      ApActorLiteDto actor,
      ApActorOrIriDto @object,            // the target (often the list actor IRI)
      string? objectType,
      ApActorLiteDto? to,                // optional if you model audience fields
      string? published
    );

    public sealed record ApCreateFollowDto(
      string id,
      string type,                        // "Create"
      ApActorLiteDto actor,
      ApActivityFollowObjectDto @object
    );

    public sealed record ApActivityFollowObjectDto(
      string id,
      string type,                        // "Follow"
      ApActorLiteDto actor,              // the follower actor
      ApActorOrIriDto @object             // the followed object (your list actor IRI)
);

    public sealed record ApActorLiteDto(
      string id
    );

    public sealed record ApActorOrIriDto(
      string? id,
      string? iri
    );


}