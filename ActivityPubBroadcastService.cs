using Microsoft.EntityFrameworkCore;

public static class ActivityPubBroadcastService
{
    public static async Task BroadcastActivityPubItemToFollowersAsync(
        GeList list,
        GeFeSLEDb db,
        GeListItem item,
        string activityType,
        Func<GeList, GeListItem, Dictionary<string, object?>> buildActivityPubItemNote,
        Func<string, GeListFollower?, Task<string?>> resolveActorInboxAsync,
        Func<string, string, object, string, Task<bool>> sendSignedActivityPubMessageAsync,
        GeListFollower? onlyFollower = null)
    {
        if (!string.Equals(activityType, "Delete", StringComparison.OrdinalIgnoreCase)
            && list.Visibility != GeListVisibility.Public)
        {
            return;
        }

        if (!string.Equals(activityType, "Delete", StringComparison.OrdinalIgnoreCase)
            && (item.IsDeleted || !item.Visible))
        {
            return;
        }

        var actorUrl = $"{GlobalConfig.Hostname}/apv1/lists/{list.Id}";
        var note = buildActivityPubItemNote(list, item);

        var followers = onlyFollower is not null
            ? new List<GeListFollower> { onlyFollower }
            : await db.ListFollowers.Where(f => f.FollowingLists.Contains(list.Id)).ToListAsync();

        foreach (var follower in followers.Where(f => !string.IsNullOrWhiteSpace(f.Id)))
        {
            string? followerInbox = await resolveActorInboxAsync(follower.Id, follower);
            if (string.IsNullOrWhiteSpace(followerInbox))
            {
                DBg.d(LogLevel.Warning, $"Skipping ActivityPub {activityType} for follower {follower.Id}: no inbox available.");
                continue;
            }

            bool isCreateActivity = string.Equals(activityType, "Create", StringComparison.OrdinalIgnoreCase);
            string[] activityTo = isCreateActivity
                ? new[] { "https://www.w3.org/ns/activitystreams#Public", follower.Id }
                : new[] { follower.Id };

            var activityPayload = new Dictionary<string, object?>
            {
                ["@context"] = "https://www.w3.org/ns/activitystreams",
                ["id"] = $"{GlobalConfig.Hostname}/apv1/activities/{Guid.NewGuid()}",
                ["type"] = activityType,
                ["actor"] = actorUrl,
                ["object"] = note,
                ["to"] = activityTo,
                ["published"] = DateTimeOffset.UtcNow.ToString("o")
            };

            if (isCreateActivity)
            {
                activityPayload["cc"] = new[] { $"{GlobalConfig.Hostname}/apv1/lists/{list.Id}/followers" };
            }

            await sendSignedActivityPubMessageAsync(
                followerInbox,
                actorUrl,
                activityPayload,
                $"AP {activityType} sent to {followerInbox} for item {item.Id} on list {list.Id}");
        }
    }

    public static async Task BroadcastAllActivityPubItemsToFollowersAsync(
        GeList list,
        GeFeSLEDb db,
        string activityType,
        Func<GeList, GeFeSLEDb, GeListItem, string, GeListFollower?, Task> broadcastActivityPubItemToFollowersAsync)
    {
        var items = await db.Items
            .Where(i => i.ListId == list.Id && i.Visible && !i.IsDeleted)
            .ToListAsync();

        foreach (var item in items)
        {
            await broadcastActivityPubItemToFollowersAsync(list, db, item, activityType, null);
        }
    }

    public static async Task BroadcastMovedItemToFollowersAsync(
        GeList oldList,
        GeList newList,
        GeFeSLEDb db,
        GeListItem item,
        Func<GeList, GeFeSLEDb, GeListItem, string, GeListFollower?, Task> broadcastActivityPubItemToFollowersAsync)
    {
        var oldFollowers = await db.ListFollowers
            .Where(f => f.FollowingLists.Contains(oldList.Id) && !string.IsNullOrWhiteSpace(f.Id))
            .ToListAsync();
        var newFollowers = await db.ListFollowers
            .Where(f => f.FollowingLists.Contains(newList.Id) && !string.IsNullOrWhiteSpace(f.Id))
            .ToListAsync();

        var oldFollowerById = oldFollowers
            .GroupBy(f => f.Id!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var newFollowerById = newFollowers
            .GroupBy(f => f.Id!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var followerId in oldFollowerById.Keys.Except(newFollowerById.Keys, StringComparer.OrdinalIgnoreCase))
        {
            await broadcastActivityPubItemToFollowersAsync(oldList, db, item, "Delete", oldFollowerById[followerId]);
        }

        foreach (var followerId in newFollowerById.Keys.Except(oldFollowerById.Keys, StringComparer.OrdinalIgnoreCase))
        {
            await broadcastActivityPubItemToFollowersAsync(newList, db, item, "Create", newFollowerById[followerId]);
        }

        foreach (var followerId in newFollowerById.Keys.Intersect(oldFollowerById.Keys, StringComparer.OrdinalIgnoreCase))
        {
            await broadcastActivityPubItemToFollowersAsync(newList, db, item, "Update", newFollowerById[followerId]);
        }
    }

    public static async Task RotateActivityPubItemIdsForListVisibilityDropAsync(
        GeList list,
        GeFeSLEDb db,
        Func<GeList, GeFeSLEDb, GeListItem, string, GeListFollower?, Task> broadcastActivityPubItemToFollowersAsync)
    {
        string fn = "RotateActivityPubItemIdsForListVisibilityDropAsync";
        var currentItems = await db.Items
            .Where(i => i.ListId == list.Id && i.Visible && !i.IsDeleted)
            .ToListAsync();

        foreach (var current in currentItems)
        {
            await RotateActivityPubItemIdForVisibilityDropAsync(
                list,
                db,
                current,
                successorVisible: true,
                broadcastActivityPubItemToFollowersAsync);
        }
    }

    public static async Task<GeListItem> RotateActivityPubItemIdForVisibilityDropAsync(
        GeList list,
        GeFeSLEDb db,
        GeListItem current,
        bool successorVisible,
        Func<GeList, GeFeSLEDb, GeListItem, string, GeListFollower?, Task> broadcastActivityPubItemToFollowersAsync)
    {
        string fn = "RotateActivityPubItemIdForVisibilityDropAsync";
        var cloned = new GeListItem
        {
            ListId = current.ListId,
            Name = current.Name,
            Comment = current.Comment,
            IsComplete = current.IsComplete,
            Visible = successorVisible,
            IsDeleted = false,
            Tags = current.Tags?.ToList() ?? new List<string>(),
            CreatedDate = current.CreatedDate,
            ModifiedDate = current.ModifiedDate,
            RedirectToItemId = null,
            OriginatorActorIri = current.OriginatorActorIri,
            SourceObjectIri = current.SourceObjectIri,
            SourceAttributedToIri = current.SourceAttributedToIri
        };

        db.Items.Add(cloned);
        await db.SaveChangesAsync();

        var predecessorIds = new HashSet<int> { current.Id };
        bool discovered;
        do
        {
            var moreIds = await db.Items
                .Where(i => i.ListId == list.Id
                    && i.RedirectToItemId.HasValue
                    && predecessorIds.Contains(i.RedirectToItemId.Value)
                    && !predecessorIds.Contains(i.Id))
                .Select(i => i.Id)
                .ToListAsync();

            discovered = moreIds.Count > 0;
            foreach (var id in moreIds)
            {
                predecessorIds.Add(id);
            }
        }
        while (discovered);

        var predecessors = await db.Items
            .Where(i => i.ListId == list.Id && predecessorIds.Contains(i.Id))
            .ToListAsync();

        string replacementPath = $"{GlobalConfig.Hostname}/apv1/items/{cloned.Id}";
        foreach (var predecessor in predecessors)
        {
            predecessor.Visible = false;
            predecessor.IsDeleted = true;
            predecessor.Comment = replacementPath;
            predecessor.RedirectToItemId = cloned.Id;
        }

        await db.SaveChangesAsync();

        DBg.d(LogLevel.Trace, $"{fn}: rotated item {current.Id} -> {cloned.Id} for list {list.Id} successorVisible={successorVisible}");
        await broadcastActivityPubItemToFollowersAsync(list, db, current, "Delete", null);
        return cloned;
    }

    public static async Task BroadcastActivityPubActorUpdateToFollowersAsync(
        GeList list,
        GeFeSLEDb db,
        Func<GeList, Dictionary<string, object?>> buildActivityPubListActor,
        Func<string, GeListFollower?, Task<string?>> resolveActorInboxAsync,
        Func<string, string, object, string, Task<bool>> sendSignedActivityPubMessageAsync)
    {
        if (list.Creator is null || list.ListOwners.Count == 0)
        {
            list = await db.Lists
                .Include(l => l.Creator)
                .Include(l => l.ListOwners)
                .FirstOrDefaultAsync(l => l.Id == list.Id) ?? list;
        }

        var actorUrl = $"{GlobalConfig.Hostname}/apv1/lists/{list.Id}";
        var actorObject = buildActivityPubListActor(list);
        var followers = await db.ListFollowers.Where(f => f.FollowingLists.Contains(list.Id)).ToListAsync();

        foreach (var follower in followers.Where(f => !string.IsNullOrWhiteSpace(f.Id)))
        {
            string? followerInbox = await resolveActorInboxAsync(follower.Id, follower);
            if (string.IsNullOrWhiteSpace(followerInbox))
            {
                DBg.d(LogLevel.Warning, $"Skipping ActivityPub actor Update for follower {follower.Id}: no inbox available.");
                continue;
            }

            var activityPayload = new Dictionary<string, object?>
            {
                ["@context"] = "https://www.w3.org/ns/activitystreams",
                ["id"] = $"{GlobalConfig.Hostname}/apv1/activities/{Guid.NewGuid()}",
                ["type"] = "Update",
                ["actor"] = actorUrl,
                ["object"] = actorObject,
                ["to"] = new[] { follower.Id },
                ["published"] = DateTimeOffset.UtcNow.ToString("o")
            };

            await sendSignedActivityPubMessageAsync(
                followerInbox,
                actorUrl,
                activityPayload,
                $"AP Update sent to {followerInbox} for actor/list {list.Id}");
        }
    }

    public static async Task BroadcastActivityPubCommentAnnounceToFollowersAsync(
        GeList list,
        GeFeSLEDb db,
        string remoteCommentObjectIri,
        string reason,
        Func<string, GeListFollower?, Task<string?>> resolveActorInboxAsync,
        Func<string, string, object, string, Task<bool>> sendSignedActivityPubMessageAsync,
        GeListFollower? onlyFollower = null)
    {
        if (string.IsNullOrWhiteSpace(remoteCommentObjectIri))
        {
            return;
        }

        if (list.Visibility != GeListVisibility.Public)
        {
            return;
        }

        var actorUrl = $"{GlobalConfig.Hostname}/apv1/lists/{list.Id}";
        var followers = onlyFollower is not null
            ? new List<GeListFollower> { onlyFollower }
            : await db.ListFollowers.Where(f => f.FollowingLists.Contains(list.Id)).ToListAsync();

        foreach (var follower in followers.Where(f => !string.IsNullOrWhiteSpace(f.Id)))
        {
            string? followerInbox = await resolveActorInboxAsync(follower.Id, follower);
            if (string.IsNullOrWhiteSpace(followerInbox))
            {
                DBg.d(LogLevel.Warning, $"Skipping ActivityPub Announce for follower {follower.Id}: no inbox available.");
                continue;
            }

            var activityPayload = new Dictionary<string, object?>
            {
                ["@context"] = "https://www.w3.org/ns/activitystreams",
                ["id"] = $"{GlobalConfig.Hostname}/apv1/activities/{Guid.NewGuid()}",
                ["type"] = "Announce",
                ["actor"] = actorUrl,
                ["object"] = remoteCommentObjectIri,
                ["to"] = new[] { "https://www.w3.org/ns/activitystreams#Public", follower.Id },
                ["cc"] = new[] { $"{GlobalConfig.Hostname}/apv1/lists/{list.Id}/followers" },
                ["summary"] = reason,
                ["published"] = DateTimeOffset.UtcNow.ToString("o")
            };

            await sendSignedActivityPubMessageAsync(
                followerInbox,
                actorUrl,
                activityPayload,
                $"AP Announce sent to {followerInbox} for comment object {remoteCommentObjectIri} on list {list.Id}");
        }
    }
}
