using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using static EndpointLoggingHelpers;

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
        string fn = $"/apv1/lists/{listId}/inbox (POST)";
        DBg.d(LogLevel.Trace, fn);
        LogDtoIn(fn, nameof(JsonElement), activityJson);

        GeList? list = await db.Lists.FirstOrDefaultAsync(l => l.Id == listId);
        string expectedActorUrl = $"{GlobalConfig.Hostname}/apv1/lists/{listId}";
        if (list == null)
        {
            string msg = $"List with id {listId} not found";
            return NotFoundWithTrace(fn, msg);
        }

        //DBg.d(LogLevel.Trace, $"{fn} <-- {activityJson.GetRawText()}");

        string incomingType = activityJson.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String
            ? typeProp.GetString() ?? string.Empty
            : string.Empty;
        string incomingId = activityJson.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String
            ? idProp.GetString() ?? $"{GlobalConfig.Hostname}/apv1/activities/unknown-{Guid.NewGuid()}"
            : $"{GlobalConfig.Hostname}/apv1/activities/unknown-{Guid.NewGuid()}";
        var persistedActivity = await TryWriteIncomingActivityAsync(incomingId, activityJson);
        string persistedActivityPath = persistedActivity.FilePath ?? "(not written)";
        if (persistedActivity.Wrote)
        {
            DBg.d(LogLevel.Debug, $"{fn} -- incoming ActivityPub activity saved to {persistedActivityPath}");
        }
        else if (string.IsNullOrWhiteSpace(persistedActivity.Error))
        {
            DBg.d(LogLevel.Trace, $"{fn} -- incoming ActivityPub activity persistence skipped (full logging disabled)");
        }
        else
        {
            DBg.d(LogLevel.Warning, $"{fn} -- incoming ActivityPub activity not saved: {persistedActivity.Error ?? "unknown error"}");
        }

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
                $"{fn} -- Delete diagnostics from activity file: {persistedActivityPath}");
        }

        if (string.IsNullOrWhiteSpace(actorIri))
        {
            string msg = "activity.actor is null or empty";
            return BadRequestWithTrace(fn, msg);
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

            string msg = $"Delete actor processed for {actorIri}";
            return OkWithTrace(fn, msg);
        }

        if (!isFollow && !isUnfollow)
        {
            string msg = $"Ignored unsupported ActivityPub activity type: {incomingType}";
            return OkWithTrace(fn, msg);
        }

        if (string.IsNullOrWhiteSpace(targetObject) || !string.Equals(targetObject, expectedActorUrl, StringComparison.OrdinalIgnoreCase))
        {
            string msg = $"activity.object is null or does not match expected actor URL. Expected: {expectedActorUrl}, Actual: {targetObject ?? "(null)"}";
            string? rejectInbox = await resolveActorInboxAsync(actorIri, null);
            if (!string.IsNullOrWhiteSpace(rejectInbox))
            {
                await sendActivityPubFollowAckAsync(rejectInbox, expectedActorUrl, incomingId, incomingType, actorIri, targetObject, false,
                    $"Rejected: {msg}");
            }
            return BadRequestWithTrace(fn, msg);
        }

        GeListFollower? follower = await db.ListFollowers.FirstOrDefaultAsync(f => f.Id == actorIri);

        if (isUnfollow)
        {
            if (follower == null)
            {
                string rejectMsg = $"Unfollow rejected: follower {actorIri} is unknown for this list";
                string? rejectInbox = await resolveActorInboxAsync(actorIri, null);
                if (!string.IsNullOrWhiteSpace(rejectInbox))
                {
                    await sendActivityPubFollowAckAsync(rejectInbox, expectedActorUrl, incomingId, incomingType, actorIri, targetObject, false,
                        rejectMsg);
                }
                return BadRequestWithTrace(fn, rejectMsg);
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
                string acceptFailureMsg = $"Unfollow processed locally but failed to send ActivityPub Accept to {actorIri}";
                return ProblemWithTrace(fn, acceptFailureMsg, 502);
            }

            if (removedFromList)
            {
                await list.RegenerateAllFiles(db);
            }

            string unfollowSuccessMsg = $"Unfollow processed for list {list.Name} (id: {list.Id})";
            return OkWithTrace(fn, unfollowSuccessMsg);
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
            string msg = $"Follow processed locally but failed to send ActivityPub Accept to {actorIri}";
            return ProblemWithTrace(fn, msg, 502);
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

        string successMsg = $"Activity received and processed for list {list.Name} (id: {list.Id})";
        return OkWithTrace(fn, successMsg);
    }

    private static async Task<(bool Wrote, string? FilePath, string? Error)> TryWriteIncomingActivityAsync(string activityId, JsonElement activityJson)
    {
        if (!ActivityPubActivityLogStore.IsFullLoggingEnabled())
        {
            return (false, null, null);
        }

        if (string.IsNullOrWhiteSpace(GlobalConfig.ActivityPubActivitiesFolder))
        {
            return (false, null, "ActivityPub activities folder is not configured.");
        }

        try
        {
            Directory.CreateDirectory(GlobalConfig.ActivityPubActivitiesFolder);
            string filePath = BuildIncomingActivityFilePath(activityId);
            await File.WriteAllTextAsync(filePath, activityJson.GetRawText());
            return (true, filePath, null);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    private static string BuildIncomingActivityFilePath(string activityId)
    {
        string encodedId = Uri.EscapeDataString(activityId);
        if (string.IsNullOrWhiteSpace(encodedId))
        {
            encodedId = $"unknown-{Guid.NewGuid()}";
        }

        const int maxFileNameLength = 180;
        if (encodedId.Length > maxFileNameLength)
        {
            string hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(activityId))).ToLowerInvariant()[..16];
            encodedId = $"{encodedId[..(maxFileNameLength - 17)]}_{hash}";
        }

        return Path.Combine(GlobalConfig.ActivityPubActivitiesFolder!, $"{encodedId}.json");
    }
}
