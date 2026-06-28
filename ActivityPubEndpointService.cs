using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

public static class ActivityPubEndpointService
{
    public static async Task<IResult> GetWebfingerAsync(string resource, GeFeSLEDb db)
    {
        if (!resource.StartsWith("acct:"))
        {
            return Results.BadRequest("Invalid resource format - must start with acct:");
        }

        string[] parts = resource.Substring(5).Split('@');
        if (parts.Length != 2)
        {
            return Results.BadRequest("Invalid resource format - must be acct:username@hostname");
        }

        string listname = parts[0];
        string hostname = parts[1];

        if (hostname != GlobalConfig.APDomain)
        {
            return Results.BadRequest($"Invalid hostname - must be {GlobalConfig.APDomain ?? "undefined"}");
        }

        GeList? list = await db.Lists.FirstOrDefaultAsync(l => l.ActivityPubId == listname);
        if (list == null)
        {
            return Results.NotFound($"No list found with AP handle/name {listname}");
        }

        var response = new
        {
            subject = $"acct:{listname}@{GlobalConfig.APDomain}",
            links = new[]
            {
                new
                {
                    rel = "self",
                    type = "application/activity+json",
                    href = $"{GlobalConfig.Hostname}/apv1/lists/{list.Id}"
                }
            }
        };

        return Results.Content(System.Text.Json.JsonSerializer.Serialize(response), "application/jrd+json");
    }

    public static async Task<IResult> GetActorNameRedirectAsync(string fn, string actorName, GeFeSLEDb db)
    {
        if (string.IsNullOrWhiteSpace(actorName))
        {
            return EndpointLoggingHelpers.NotFoundNoMessageWithTrace(fn);
        }

        GeList? list = await db.Lists.FirstOrDefaultAsync(l => l.ActivityPubId == actorName);
        if (list is null)
        {
            return EndpointLoggingHelpers.NotFoundNoMessageWithTrace(fn);
        }

        string target = $"https://{GlobalConfig.Hostname}/apv1/lists/{list.Id}";
        return Results.Redirect(target);
    }

    public static async Task<IResult> GetListActorAsync(
        int listId,
        GeFeSLEDb db,
        Func<GeList, Dictionary<string, object?>> buildActivityPubListActor)
    {
        GeList? list = await db.Lists
            .Include(l => l.Creator)
            .Include(l => l.ListOwners)
            .FirstOrDefaultAsync(l => l.Id == listId);
        if (list == null)
        {
            return Results.NotFound($"List with id {listId} not found");
        }

        var actor = buildActivityPubListActor(list);
        return Results.Content(System.Text.Json.JsonSerializer.Serialize(actor), "application/activity+json");
    }

    public static async Task<IResult> GetListOutboxAsync(int listId, GeFeSLEDb db)
    {
        GeList? list = await db.Lists
            .FirstOrDefaultAsync(l => l.Id == listId);
        if (list == null)
        {
            return Results.NotFound($"List with id {listId} not found");
        }
        if (list.Visibility != GeListVisibility.Public)
        {
            return Results.StatusCode(403);
        }
        var items = await list.GetItems(db);

        var outbox = new Dictionary<string, object?>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = $"{GlobalConfig.Hostname}/apv1/lists/{list.Id}/outbox",
            ["type"] = list.isOrdered ? "OrderedCollection" : "Collection",
            ["totalItems"] = items.Count,
            ["orderedItems"] = items.Select(i => new
            {
                id = $"{GlobalConfig.Hostname}/apv1/lists/{list.Id}/items/{i.Id}",
                type = "Note",
                name = i.Name,
                content = i.Comment,
                attributedTo = $"{GlobalConfig.Hostname}/apv1/lists/{list.Id}"
            })
        };

        return Results.Content(System.Text.Json.JsonSerializer.Serialize(outbox), "application/activity+json");
    }

    public static async Task<IResult> GetListItemAsync(
        int listId,
        int itemId,
        GeFeSLEDb db,
        Func<GeList, GeListItem, Dictionary<string, object?>> buildActivityPubItemNote)
    {
        GeList? list = await db.Lists
            .FirstOrDefaultAsync(l => l.Id == listId);
        if (list == null)
        {
            return Results.NotFound($"List with id {listId} not found");
        }
        GeListItem? item = await db.Items
            .FirstOrDefaultAsync(i => i.Id == itemId);
        if (item == null)
        {
            return Results.NotFound($"Item with id {itemId} not found");
        }
        if (item.ListId != listId)
        {
            return Results.BadRequest($"Item with id {itemId} does not belong to list with id {listId}");
        }
        if (item.RedirectToItemId.HasValue)
        {
            return Results.Redirect($"/apv1/lists/{listId}/items/{item.RedirectToItemId.Value}", permanent: true);
        }
        if (list.Visibility != GeListVisibility.Public)
        {
            return Results.StatusCode(403);
        }
        if (item.IsDeleted)
        {
            return Results.StatusCode(410);
        }

        var note = buildActivityPubItemNote(list, item);
        return Results.Content(System.Text.Json.JsonSerializer.Serialize(note), "application/activity+json");
    }

    public static async Task<IResult> GetItemAsync(
        int itemId,
        GeFeSLEDb db,
        Func<GeList, GeListItem, Dictionary<string, object?>> buildActivityPubItemNote)
    {
        GeListItem? item = await db.Items.FirstOrDefaultAsync(i => i.Id == itemId);
        if (item == null)
        {
            return Results.NotFound($"Item with id {itemId} not found");
        }

        if (item.RedirectToItemId.HasValue)
        {
            return Results.Redirect($"/apv1/items/{item.RedirectToItemId.Value}", permanent: true);
        }

        if (item.IsDeleted)
        {
            return Results.StatusCode(410);
        }

        GeList? list = await db.Lists.FirstOrDefaultAsync(l => l.Id == item.ListId);
        if (list == null)
        {
            return Results.NotFound($"List with id {item.ListId} not found for item {itemId}");
        }
        if (list.Visibility != GeListVisibility.Public)
        {
            return Results.StatusCode(403);
        }

        var note = buildActivityPubItemNote(list, item);
        return Results.Content(System.Text.Json.JsonSerializer.Serialize(note), "application/activity+json");
    }

    public static async Task<IResult> GetFollowersAsync(int listId, GeFeSLEDb db)
    {
        GeList? list = await db.Lists
            .FirstOrDefaultAsync(l => l.Id == listId);
        if (list == null)
        {
            return Results.NotFound($"List with id {listId} not found");
        }
        if (list.Visibility != GeListVisibility.Public)
        {
            return Results.StatusCode(403);
        }

        var followers = await db.ListFollowers
            .Where(f => f.FollowingLists.Contains(listId)).ToListAsync();

        var followerDtos = followers
            .Where(f => !string.IsNullOrWhiteSpace(f.Id))
            .GroupBy(f => f.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Select(f => f.ToApActorDto())
            .ToList();

        var followerCollection = new Dictionary<string, object?>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = $"{GlobalConfig.Hostname}/apv1/lists/{list.Id}/followers",
            ["type"] = "Collection",
            ["totalItems"] = followerDtos.Count,
            ["items"] = followerDtos
        };

        return Results.Content(System.Text.Json.JsonSerializer.Serialize(followerCollection), "application/activity+json");
    }
}
