using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

public static class ActivityPubInboxService
{
    public static async Task<bool> SendActivityPubFollowAckAsync(
        string inboxUrl,
        string localActorUrl,
        string sourceActivityId,
        string sourceActivityType,
        string sourceActorIri,
        string? sourceObjectIri,
        bool accepted,
        string statusMessage,
        Func<string, string, object, string, Task<bool>> sendSignedActivityPubMessageAsync)
    {
        var ackActivity = new Dictionary<string, object?>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = $"{GlobalConfig.Hostname}/apv1/activities/{Guid.NewGuid()}",
            ["type"] = accepted ? "Accept" : "Reject",
            ["actor"] = localActorUrl,
            ["object"] = new Dictionary<string, object?>
            {
                ["id"] = sourceActivityId,
                ["type"] = sourceActivityType,
                ["actor"] = sourceActorIri,
                ["object"] = sourceObjectIri
            },
            ["summary"] = statusMessage,
            ["published"] = DateTimeOffset.UtcNow.ToString("o")
        };

        return await sendSignedActivityPubMessageAsync(
            inboxUrl,
            localActorUrl,
            ackActivity,
            $"AP {(accepted ? "Accept" : "Reject")} sent to {inboxUrl} for activity {sourceActivityId}");
    }

    public static async Task<IResult> HandleListInboxAsync(
        int listId,
        JsonElement activityJson,
        GeFeSLEDb db,
        Func<JsonElement, string?> readIriFromActivityPubNode,
        Func<string, GeListFollower?, Task<string?>> resolveActorInboxAsync,
        Func<string, string, string, string, string, string?, bool, string, Task<bool>> sendActivityPubFollowAckAsync,
        Func<GeList, GeFeSLEDb, GeListItem, string, GeListFollower?, Task> broadcastActivityPubItemToFollowersAsync)
    {
        string fn = "/apv1/lists/{listId}/inbox (POST)";
        DBg.d(LogLevel.Trace, fn);

        GeList? list = await db.Lists.FirstOrDefaultAsync(l => l.Id == listId);
        string expectedActorUrl = $"{GlobalConfig.Hostname}/apv1/lists/{listId}";
        if (list == null)
        {
            return Results.NotFound($"List with id {listId} not found");
        }

        DBg.d(LogLevel.Trace, $"{fn} <-- {activityJson.GetRawText()}");

        string incomingType = activityJson.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String
            ? typeProp.GetString() ?? string.Empty
            : string.Empty;
        string incomingId = activityJson.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String
            ? idProp.GetString() ?? $"{GlobalConfig.Hostname}/apv1/activities/unknown-{Guid.NewGuid()}"
            : $"{GlobalConfig.Hostname}/apv1/activities/unknown-{Guid.NewGuid()}";
        string? actorIri = activityJson.TryGetProperty("actor", out var actorProp)
            ? readIriFromActivityPubNode(actorProp)
            : null;

        string? targetObject = null;
        string? topLevelObjectIri = null;
        string? nestedObjectType = null;
        if (activityJson.TryGetProperty("object", out var objectProp))
        {
            topLevelObjectIri = readIriFromActivityPubNode(objectProp);
            targetObject = topLevelObjectIri;

            if (objectProp.ValueKind == JsonValueKind.Object)
            {
                if (objectProp.TryGetProperty("type", out var nestedTypeProp)
                    && nestedTypeProp.ValueKind == JsonValueKind.String)
                {
                    nestedObjectType = nestedTypeProp.GetString();
                }

                if (objectProp.TryGetProperty("object", out var followTargetProp))
                {
                    targetObject = readIriFromActivityPubNode(followTargetProp) ?? targetObject;
                }

                if (string.IsNullOrWhiteSpace(actorIri)
                    && objectProp.TryGetProperty("actor", out var nestedActorProp))
                {
                    actorIri = readIriFromActivityPubNode(nestedActorProp);
                }
            }
        }

        expectedActorUrl = $"{GlobalConfig.Hostname}/apv1/lists/{list.Id}";
        bool isDirectFollow = string.Equals(incomingType, "Follow", StringComparison.OrdinalIgnoreCase);
        bool isDirectUnfollow = string.Equals(incomingType, "Unfollow", StringComparison.OrdinalIgnoreCase);
        bool isCreateFollow = string.Equals(incomingType, "Create", StringComparison.OrdinalIgnoreCase)
            && string.Equals(nestedObjectType, "Follow", StringComparison.OrdinalIgnoreCase);
        bool isUndoFollow = string.Equals(incomingType, "Undo", StringComparison.OrdinalIgnoreCase)
            && string.Equals(nestedObjectType, "Follow", StringComparison.OrdinalIgnoreCase);
        bool isDeleteFollow = string.Equals(incomingType, "Delete", StringComparison.OrdinalIgnoreCase)
            && string.Equals(nestedObjectType, "Follow", StringComparison.OrdinalIgnoreCase);
        bool isFollow = isDirectFollow || isCreateFollow;
        bool isUnfollow = isDirectUnfollow || isUndoFollow || isDeleteFollow;
        bool isDeleteActor = string.Equals(incomingType, "Delete", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(actorIri)
            && !string.IsNullOrWhiteSpace(topLevelObjectIri)
            && string.Equals(topLevelObjectIri, actorIri, StringComparison.OrdinalIgnoreCase);

        if (string.Equals(incomingType, "Delete", StringComparison.OrdinalIgnoreCase))
        {
            DBg.d(LogLevel.Debug,
                $"{fn} -- Delete diagnostics: nestedObjectType={nestedObjectType ?? "(null)"}, targetObject={targetObject ?? "(null)"}, actorIri={actorIri ?? "(null)"}, topLevelObjectIri={topLevelObjectIri ?? "(null)"}, expectedActorUrl={expectedActorUrl}");
        }

        if (string.IsNullOrWhiteSpace(actorIri))
        {
            DBg.d(LogLevel.Warning, $"{fn} -- activity.actor is null or empty");
            return Results.BadRequest("activity.actor is null or empty");
        }

        if (isDeleteActor)
        {
            GeListFollower? deletedFollower = await db.ListFollowers.FirstOrDefaultAsync(f => f.Id == actorIri);
            if (deletedFollower is not null)
            {
                bool removed = deletedFollower.FollowingLists.RemoveAll(id => id == list.Id) > 0;
                if (deletedFollower.FollowingLists.Count == 0)
                {
                    db.ListFollowers.Remove(deletedFollower);
                }
                await db.SaveChangesAsync();
                if (removed)
                {
                    await list.RegenerateAllFiles(db);
                }
                DBg.d(LogLevel.Information, $"{fn} -- processed Delete actor cleanup for follower {actorIri} on list {list.Id}");
            }
            else
            {
                DBg.d(LogLevel.Information, $"{fn} -- Delete actor received for unknown follower {actorIri}; ignoring");
            }

            return Results.Ok($"Delete actor processed for {actorIri}");
        }

        if (!isFollow && !isUnfollow)
        {
            DBg.d(LogLevel.Information, $"{fn} -- ignoring unsupported activity type '{incomingType}' for list inbox");
            return Results.Ok($"Ignored unsupported ActivityPub activity type: {incomingType}");
        }

        if (string.IsNullOrWhiteSpace(targetObject) || !string.Equals(targetObject, expectedActorUrl, StringComparison.OrdinalIgnoreCase))
        {
            DBg.d(LogLevel.Warning, $"{fn} -- activity.object is null or does not match expected actor URL. Expected: {expectedActorUrl}, Actual: {targetObject ?? "(null)"}");
            string? rejectInbox = await resolveActorInboxAsync(actorIri, null);
            if (!string.IsNullOrWhiteSpace(rejectInbox))
            {
                await sendActivityPubFollowAckAsync(rejectInbox, expectedActorUrl, incomingId, incomingType, actorIri, targetObject, false,
                    $"Rejected: activity.object must match {expectedActorUrl}");
            }
            return Results.BadRequest($"activity.object is null or does not match expected actor URL. Expected: {expectedActorUrl}, Actual: {targetObject ?? "(null)"}");
        }

        GeListFollower? follower = await db.ListFollowers.FirstOrDefaultAsync(f => f.Id == actorIri);

        if (isUnfollow)
        {
            if (follower == null)
            {
                DBg.d(LogLevel.Information, $"{fn} -- ignoring unfollow for unknown follower {actorIri}");
                string? rejectInbox = await resolveActorInboxAsync(actorIri, null);
                if (!string.IsNullOrWhiteSpace(rejectInbox))
                {
                    await sendActivityPubFollowAckAsync(rejectInbox, expectedActorUrl, incomingId, incomingType, actorIri, targetObject, false,
                        $"Unfollow rejected: follower {actorIri} is unknown for this list");
                }
                return Results.BadRequest($"Unfollow rejected: follower {actorIri} is unknown for this list");
            }

            bool removedFromList = follower.FollowingLists.RemoveAll(id => id == list.Id) > 0;
            if (follower.FollowingLists.Count == 0)
            {
                db.ListFollowers.Remove(follower);
            }

            await db.SaveChangesAsync();
            string? acceptInbox = await resolveActorInboxAsync(actorIri, follower);
            if (string.IsNullOrWhiteSpace(acceptInbox)
                || !await sendActivityPubFollowAckAsync(acceptInbox, expectedActorUrl, incomingId, incomingType, actorIri, targetObject, true,
                    $"Unfollow accepted for list {list.Name} (id: {list.Id})"))
            {
                return Results.Problem($"Unfollow processed locally but failed to send ActivityPub Accept to {actorIri}", statusCode: 502);
            }

            if (removedFromList)
            {
                await list.RegenerateAllFiles(db);
            }

            DBg.d(LogLevel.Information, $"{fn} -- unfollow processed for list {list.Name} (id: {list.Id}) by follower {follower.Id}");
            return Results.Ok($"Unfollow processed for list {list.Name} (id: {list.Id})");
        }

        if (follower == null)
        {
            follower = new GeListFollower
            {
                Id = actorIri,
                Type = "Person"
            };
            db.ListFollowers.Add(follower);
            DBg.d(LogLevel.Information, $"{fn} -- added new follower to DB {follower.Id}");
        }
        else
        {
            DBg.d(LogLevel.Information, $"{fn} -- found existing follower in DB {follower.Id}");
        }

        await follower.FetchActorInfoFromIriAsync();

        bool addedToList = false;
        if (!follower.FollowingLists.Contains(list.Id))
        {
            follower.FollowingLists.Add(list.Id);
            addedToList = true;
            DBg.d(LogLevel.Information, $"{fn} -- added follower {follower.Id} to list {list.Name} (id: {list.Id})");
        }

        await db.SaveChangesAsync();

        string? acceptInboxForFollow = await resolveActorInboxAsync(actorIri, follower);
        if (string.IsNullOrWhiteSpace(acceptInboxForFollow)
            || !await sendActivityPubFollowAckAsync(acceptInboxForFollow, expectedActorUrl, incomingId, incomingType, actorIri, targetObject, true,
                $"Follow accepted for list {list.Name} (id: {list.Id})"))
        {
            return Results.Problem($"Follow processed locally but failed to send ActivityPub Accept to {actorIri}", statusCode: 502);
        }

        var currentItems = await list.GetItems(db);
        foreach (var currentItem in currentItems.Where(i => i.Visible && !i.IsDeleted))
        {
            await broadcastActivityPubItemToFollowersAsync(list, db, currentItem, "Create", follower);
        }

        if (addedToList)
        {
            await list.RegenerateAllFiles(db);
        }

        return Results.Ok($"Activity received and processed for list {list.Name} (id: {list.Id})");
    }
}
