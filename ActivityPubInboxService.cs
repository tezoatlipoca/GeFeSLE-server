using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Net;
using System.Text.RegularExpressions;
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
        Func<GeList, GeFeSLEDb, GeListItem, string, GeListFollower?, Task> broadcastActivityPubItemToFollowersAsync,
        Func<GeList, GeFeSLEDb, string, string, GeListFollower?, Task> broadcastActivityPubCommentAnnounceToFollowersAsync)
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
        bool hasObject = activityJson.TryGetProperty("object", out var objectProp);
        if (hasObject)
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
        bool isCreateNote = string.Equals(incomingType, "Create", StringComparison.OrdinalIgnoreCase)
            && hasObject
            && string.Equals(nestedObjectType, "Note", StringComparison.OrdinalIgnoreCase);
        bool isUpdateNote = string.Equals(incomingType, "Update", StringComparison.OrdinalIgnoreCase)
            && hasObject
            && string.Equals(nestedObjectType, "Note", StringComparison.OrdinalIgnoreCase);
        bool isLike = string.Equals(incomingType, "Like", StringComparison.OrdinalIgnoreCase);
        bool isUndo = string.Equals(incomingType, "Undo", StringComparison.OrdinalIgnoreCase);
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
                    // this was easy - we can improve by restricting to just the lists they actuall used to follow. 
                    
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

        if (isLike)
        {
            DBg.d(LogLevel.Information, $"{fn} -- received ActivityPub Like from {actorIri} (activityId={incomingId ?? "<none>"})");
            string? likeTargetIri = topLevelObjectIri;
            if (hasObject && objectProp.ValueKind == JsonValueKind.Object)
            {
                likeTargetIri ??= ReadIriProperty(objectProp, "id", readIriFromActivityPubNode);
                if (string.IsNullOrWhiteSpace(likeTargetIri)
                    && objectProp.TryGetProperty("object", out var nestedLikeObjectProp))
                {
                    likeTargetIri = readIriFromActivityPubNode(nestedLikeObjectProp);
                }
            }

            var resolvedLikeTarget = await ResolveIncomingLikeTargetAsync(list.Id, likeTargetIri, db);
            if (resolvedLikeTarget is null)
            {
                string ignoredMsg = $"Ignored ActivityPub Like for unknown/non-local object {(likeTargetIri ?? "(null)")}";
                DBg.d(LogLevel.Information, $"{fn} -- {ignoredMsg}");
                return OkWithTrace(fn, ignoredMsg);
            }

            DBg.d(LogLevel.Trace,
                $"{fn} -- ActivityPub Like target resolved to object={resolvedLikeTarget.ObjectIri}, itemId={resolvedLikeTarget.ItemId?.ToString() ?? "<null>"}, commentId={resolvedLikeTarget.CommentId?.ToString() ?? "<null>"}");

            ActivityPubObjectLike? existingLike = null;
            if (!string.IsNullOrWhiteSpace(incomingId))
            {
                existingLike = await db.ActivityPubObjectLikes.FirstOrDefaultAsync(l =>
                    l.ListId == list.Id
                    && l.LikeActivityIri == incomingId);
            }

            existingLike ??= await db.ActivityPubObjectLikes.FirstOrDefaultAsync(l =>
                l.ListId == list.Id
                && l.ObjectIri == resolvedLikeTarget.ObjectIri
                && l.ActorIri == actorIri);

            bool created = false;
            if (existingLike is null)
            {
                existingLike = new ActivityPubObjectLike
                {
                    ListId = list.Id,
                    ItemId = resolvedLikeTarget.ItemId,
                    CommentId = resolvedLikeTarget.CommentId,
                    ObjectIri = resolvedLikeTarget.ObjectIri,
                    ActorIri = actorIri,
                    LikeActivityIri = incomingId,
                    IsActive = true,
                    CreatedDate = DateTime.UtcNow,
                    ModifiedDate = DateTime.UtcNow
                };
                db.ActivityPubObjectLikes.Add(existingLike);
                created = true;
            }
            else
            {
                existingLike.ItemId = resolvedLikeTarget.ItemId;
                existingLike.CommentId = resolvedLikeTarget.CommentId;
                existingLike.IsActive = true;
                existingLike.ModifiedDate = DateTime.UtcNow;
                existingLike.LikeActivityIri = incomingId;
            }

            await db.SaveChangesAsync();
            string msg = $"ActivityPub Like applied for {resolvedLikeTarget.ObjectIri} by {actorIri}";
            DBg.d(LogLevel.Information, $"{fn} -- {msg}");
            return OkPayloadWithTrace(fn, new
            {
                objectIri = resolvedLikeTarget.ObjectIri,
                itemId = resolvedLikeTarget.ItemId,
                commentId = resolvedLikeTarget.CommentId,
                actor = actorIri,
                likeActivity = incomingId,
                created,
                active = true,
                persistedActivityPath
            }, msg);
        }

        if (isUndo && !isUndoFollow)
        {
            DBg.d(LogLevel.Information, $"{fn} -- received ActivityPub Undo from {actorIri}");
            string? undoLikeActivityIri = null;
            string? undoLikeTargetIri = null;
            if (hasObject)
            {
                undoLikeActivityIri = topLevelObjectIri;
                if (objectProp.ValueKind == JsonValueKind.Object)
                {
                    string? undoNestedType = ReadStringProperty(objectProp, "type");
                    if (string.Equals(undoNestedType, "Like", StringComparison.OrdinalIgnoreCase))
                    {
                        undoLikeActivityIri = readIriFromActivityPubNode(objectProp) ?? undoLikeActivityIri;
                        if (objectProp.TryGetProperty("object", out var undoNestedTargetProp))
                        {
                            undoLikeTargetIri = readIriFromActivityPubNode(undoNestedTargetProp);
                        }
                    }
                    else if (objectProp.TryGetProperty("object", out var undoObjectProp))
                    {
                        undoLikeTargetIri = readIriFromActivityPubNode(undoObjectProp);
                    }
                }
            }

            ActivityPubObjectLike? existingLike = null;
            if (!string.IsNullOrWhiteSpace(undoLikeActivityIri))
            {
                existingLike = await db.ActivityPubObjectLikes.FirstOrDefaultAsync(l =>
                    l.ListId == list.Id
                    && l.IsActive
                    && l.LikeActivityIri == undoLikeActivityIri);
            }

            if (existingLike is null)
            {
                string? candidateTarget = undoLikeTargetIri;
                if (string.IsNullOrWhiteSpace(candidateTarget)
                    && !string.IsNullOrWhiteSpace(undoLikeActivityIri)
                    && Uri.TryCreate(undoLikeActivityIri, UriKind.Absolute, out _))
                {
                    candidateTarget = undoLikeActivityIri;
                }

                var resolvedUndoTarget = await ResolveIncomingLikeTargetAsync(list.Id, candidateTarget, db);
                if (resolvedUndoTarget is not null)
                {
                    existingLike = await db.ActivityPubObjectLikes.FirstOrDefaultAsync(l =>
                        l.ListId == list.Id
                        && l.ObjectIri == resolvedUndoTarget.ObjectIri
                        && l.ActorIri == actorIri
                        && l.IsActive);
                }
            }

            if (existingLike is null)
            {
                string ignoredMsg = $"Ignored ActivityPub Undo with no matching active Like for actor {actorIri}";
                DBg.d(LogLevel.Information, $"{fn} -- {ignoredMsg}");
                return OkWithTrace(fn, ignoredMsg);
            }

            existingLike.IsActive = false;
            existingLike.ModifiedDate = DateTime.UtcNow;
            await db.SaveChangesAsync();

            string msg = $"ActivityPub Undo(Like) applied for {existingLike.ObjectIri} by {actorIri}";
            DBg.d(LogLevel.Information, $"{fn} -- {msg}");
            return OkPayloadWithTrace(fn, new
            {
                objectIri = existingLike.ObjectIri,
                itemId = existingLike.ItemId,
                commentId = existingLike.CommentId,
                actor = actorIri,
                likeActivity = existingLike.LikeActivityIri,
                active = false,
                persistedActivityPath
            }, msg);
        }

        if (string.Equals(incomingType, "Delete", StringComparison.OrdinalIgnoreCase) && !isDeleteFollow)
        {
            string? deleteObjectIri = topLevelObjectIri;
            if (string.IsNullOrWhiteSpace(deleteObjectIri) && hasObject && objectProp.ValueKind == JsonValueKind.Object)
            {
                deleteObjectIri = ReadIriProperty(objectProp, "atomUri", readIriFromActivityPubNode)
                    ?? ReadStringProperty(objectProp, "atomUri");
            }

            if (string.IsNullOrWhiteSpace(deleteObjectIri))
            {
                string ignoredMsg = "Ignored ActivityPub Delete without a resolvable object IRI";
                DBg.d(LogLevel.Information, $"{fn} -- {ignoredMsg}");
                return OkWithTrace(fn, ignoredMsg);
            }

            GeListItemComment? deletedComment = await db.ItemComments.FirstOrDefaultAsync(c =>
                c.ListId == list.Id
                && c.RemoteObjectIri == deleteObjectIri);
            if (deletedComment is null)
            {
                string ignoredMsg = $"Ignored ActivityPub Delete for unknown comment object {deleteObjectIri}";
                DBg.d(LogLevel.Information, $"{fn} -- {ignoredMsg}");
                return OkWithTrace(fn, ignoredMsg);
            }

            if (!IsCommentDeleteAuthorized(actorIri, deletedComment))
            {
                string ignoredMsg = $"Ignored ActivityPub Delete for comment {deletedComment.Id} due to actor mismatch. Actor: {actorIri}";
                DBg.d(LogLevel.Warning, $"{fn} -- {ignoredMsg}");
                return OkWithTrace(fn, ignoredMsg);
            }

            // Keep the comment row as a tombstone so existing child threading stays intact.
            deletedComment.ActorIri = null;
            deletedComment.AttributedToIri = null;
            deletedComment.Name = null;
            deletedComment.ContentHtml = null;
            deletedComment.Summary = "<comment deleted>";
            deletedComment.RawNoteJson = null;
            deletedComment.UpdatedAt = DateTimeOffset.UtcNow;
            deletedComment.ModifiedDate = DateTime.UtcNow;
            deletedComment.LastReceivedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();
            await list.GenerateHTMLListPage(db);

            await broadcastActivityPubCommentAnnounceToFollowersAsync(
                list,
                db,
                deleteObjectIri,
                $"Deleted remote comment for list item {deletedComment.ItemId}",
                null);

            string deletedMsg = $"ActivityPub comment delete applied for item {deletedComment.ItemId}: comment {deletedComment.Id}";
            return OkPayloadWithTrace(fn, new
            {
                itemId = deletedComment.ItemId,
                commentId = deletedComment.Id,
                remoteObject = deleteObjectIri,
                tombstoned = true,
                persistedActivityPath
            }, deletedMsg);
        }

        if (isCreateNote && hasObject)
        {
            string followersUrl = $"{GlobalConfig.Hostname}/apv1/lists/{list.Id}/followers";
            bool addressedToList = IsActivityAddressedToListActor(activityJson, expectedActorUrl, followersUrl)
                || IsNoteMentioningListActor(objectProp, expectedActorUrl, list.ActivityPubId, GlobalConfig.APDomain);
            if (!addressedToList)
            {
                string ignoredMsg = "Ignored ActivityPub Create/Note not addressed to this list actor";
                DBg.d(LogLevel.Information, $"{fn} -- {ignoredMsg}");
                return OkWithTrace(fn, ignoredMsg);
            }

            string? noteObjectIri = readIriFromActivityPubNode(objectProp) ?? topLevelObjectIri;
            string? noteInReplyToIri = ReadIriProperty(objectProp, "inReplyTo", readIriFromActivityPubNode)
                ?? ReadIriProperty(objectProp, "inreplyto", readIriFromActivityPubNode);
            if (!string.IsNullOrWhiteSpace(noteInReplyToIri) && !string.IsNullOrWhiteSpace(noteObjectIri))
            {
                var threadTarget = await ResolveCommentThreadTargetAsync(list.Id, noteInReplyToIri, db);
                if (threadTarget is not null)
                {
                    GeListItemComment? existingComment = await db.ItemComments.FirstOrDefaultAsync(c =>
                        c.ListId == list.Id
                        && c.RemoteObjectIri == noteObjectIri);

                    if (existingComment is null)
                    {
                        existingComment = new GeListItemComment
                        {
                            ListId = list.Id,
                            ItemId = threadTarget.ItemId,
                            ParentCommentId = threadTarget.ParentCommentId,
                            RemoteObjectIri = noteObjectIri,
                            InReplyToIri = noteInReplyToIri,
                            CreatedDate = DateTime.UtcNow,
                            ModifiedDate = DateTime.UtcNow,
                            LastReceivedAt = DateTime.UtcNow
                        };
                        db.ItemComments.Add(existingComment);
                    }
                    else if (IsCommentTombstoned(existingComment))
                    {
                        string ignoredMsg = $"Ignored ActivityPub Create/Note for tombstoned comment object {noteObjectIri}";
                        DBg.d(LogLevel.Information, $"{fn} -- {ignoredMsg}");
                        return OkWithTrace(fn, ignoredMsg);
                    }

                    ApplyIncomingCommentNote(existingComment, objectProp, actorIri, readIriFromActivityPubNode);
                    existingComment.ItemId = threadTarget.ItemId;
                    existingComment.ParentCommentId = threadTarget.ParentCommentId;
                    existingComment.InReplyToIri = noteInReplyToIri;
                    existingComment.ModifiedDate = DateTime.UtcNow;
                    existingComment.LastReceivedAt = DateTime.UtcNow;

                    await db.SaveChangesAsync();
                    await list.RefreshRemoteCommentLikesForItemAsync(existingComment.ItemId, db);
                    await list.GenerateHTMLListPage(db);

                    await broadcastActivityPubCommentAnnounceToFollowersAsync(
                        list,
                        db,
                        existingComment.RemoteObjectIri,
                        $"New remote comment for list item {existingComment.ItemId}",
                        null);

                    string commentCreateMsg = $"ActivityPub threaded comment accepted for item {existingComment.ItemId}: comment {existingComment.Id}";
                    return OkPayloadWithTrace(fn, new
                    {
                        itemId = existingComment.ItemId,
                        commentId = existingComment.Id,
                        remoteObject = existingComment.RemoteObjectIri,
                        inReplyTo = existingComment.InReplyToIri,
                        parentCommentId = existingComment.ParentCommentId,
                        persistedActivityPath
                    }, commentCreateMsg);
                }

                string ignoredThreadMsg = $"Ignored ActivityPub Create/Note reply with unknown inReplyTo target {noteInReplyToIri}";
                DBg.d(LogLevel.Information, $"{fn} -- {ignoredThreadMsg}");
                return OkWithTrace(fn, ignoredThreadMsg);
            }

            string? noteContent = ReadStringProperty(objectProp, "content")
                ?? ReadStringProperty(objectProp, "summary");
            string attachmentsHtml = BuildIncomingAttachmentMarkup(objectProp, readIriFromActivityPubNode);
            string combinedComment = BuildSuggestedItemComment(noteContent, attachmentsHtml);
            string? noteName = ReadStringProperty(objectProp, "name");
            if (string.IsNullOrWhiteSpace(noteName))
            {
                string submitterLabel = BuildActorMentionLabel(activityJson, actorIri);
                noteName = $"Suggestion from {submitterLabel}";
            }

            string? attributedToIri = ReadIriProperty(objectProp, "attributedTo", readIriFromActivityPubNode)
                ?? ReadIriProperty(objectProp, "attributedto", readIriFromActivityPubNode);

            var suggestedItem = new GeListItem
            {
                ListId = list.Id,
                Name = noteName,
                Comment = combinedComment,
                IsComplete = false,
                Visible = false,
                OriginatorActorIri = actorIri,
                SourceObjectIri = noteObjectIri,
                SourceAttributedToIri = attributedToIri
            };
            foreach (string tag in ExtractHashtagsFromNoteTag(objectProp))
            {
                suggestedItem.Tags.Add(tag);
            }

            GeList? modlist = await db.Lists.FirstOrDefaultAsync(l => l.Name == GlobalConfig.modListName);
            if (modlist is null)
            {
                modlist = new GeList
                {
                    Name = GlobalConfig.modListName,
                    Visibility = GeListVisibility.ListOwners,
                    CreatorId = list.CreatorId
                };
                db.Lists.Add(modlist);
            }

            db.Items.Add(suggestedItem);
            await db.SaveChangesAsync();

            var modItem = new GeListItem
            {
                Name = $"{list.Name}#{suggestedItem.Id} <= by {actorIri}",
                ListId = modlist.Id,
                Visible = true,
                Tags = new List<string> { "SUGGESTED", "ACTIVITYPUB", list.Name ?? $"list-{list.Id}" }
            };

            var modComment = new StringBuilder();
            modComment.AppendLine($"SUGGESTED via ActivityPub by {actorIri}  ");
            if (!string.IsNullOrWhiteSpace(noteObjectIri))
            {
                modComment.AppendLine($"Source object: <a href=\"{WebUtility.HtmlEncode(noteObjectIri)}\">{WebUtility.HtmlEncode(noteObjectIri)}</a>  ");
            }
            if (!string.IsNullOrWhiteSpace(attributedToIri))
            {
                modComment.AppendLine($"Attributed to: <a href=\"{WebUtility.HtmlEncode(attributedToIri)}\">{WebUtility.HtmlEncode(attributedToIri)}</a>  ");
            }
            modComment.AppendLine("Item has been preemptively marked as invisible pending approval  ");
            modComment.AppendLine("---------  ");
            modComment.AppendLine($"<a href=\"_edit.item.html?listid={list.Id}&itemid={suggestedItem.Id}\">LINK TO APPROVE</a>");
            modItem.Comment = modComment.ToString();

            db.Items.Add(modItem);
            await db.SaveChangesAsync();

            await list.RegenerateAllFiles(db);
            await modlist.RegenerateAllFiles(db);

            string msg = $"ActivityPub suggestion accepted for moderation: item {suggestedItem.Id}, moderation ticket {modItem.Id}";
            return OkPayloadWithTrace(fn, new
            {
                suggestedItemId = suggestedItem.Id,
                moderationItemId = modItem.Id,
                sourceActor = actorIri,
                sourceObject = noteObjectIri,
                sourceAttributedTo = attributedToIri,
                persistedActivityPath
            }, msg);
        }

        if (isUpdateNote && hasObject)
        {
            string followersUrl = $"{GlobalConfig.Hostname}/apv1/lists/{list.Id}/followers";
            bool addressedToList = IsActivityAddressedToListActor(activityJson, expectedActorUrl, followersUrl)
                || IsNoteMentioningListActor(objectProp, expectedActorUrl, list.ActivityPubId, GlobalConfig.APDomain);
            if (!addressedToList)
            {
                string ignoredMsg = "Ignored ActivityPub Update/Note not addressed to this list actor";
                DBg.d(LogLevel.Information, $"{fn} -- {ignoredMsg}");
                return OkWithTrace(fn, ignoredMsg);
            }

            string? noteObjectIri = readIriFromActivityPubNode(objectProp) ?? topLevelObjectIri;
            if (string.IsNullOrWhiteSpace(noteObjectIri))
            {
                string ignoredMsg = "Ignored ActivityPub Update/Note without a resolvable object IRI";
                DBg.d(LogLevel.Information, $"{fn} -- {ignoredMsg}");
                return OkWithTrace(fn, ignoredMsg);
            }

            GeListItemComment? existingComment = await db.ItemComments.FirstOrDefaultAsync(c =>
                c.ListId == list.Id
                && c.RemoteObjectIri == noteObjectIri);
            if (existingComment is not null)
            {
                if (IsCommentTombstoned(existingComment))
                {
                    string ignoredMsg = $"Ignored ActivityPub Update/Note for tombstoned comment object {noteObjectIri}";
                    DBg.d(LogLevel.Information, $"{fn} -- {ignoredMsg}");
                    return OkWithTrace(fn, ignoredMsg);
                }

                string? noteInReplyToIri = ReadIriProperty(objectProp, "inReplyTo", readIriFromActivityPubNode)
                    ?? ReadIriProperty(objectProp, "inreplyto", readIriFromActivityPubNode);
                if (!string.IsNullOrWhiteSpace(noteInReplyToIri))
                {
                    var threadTarget = await ResolveCommentThreadTargetAsync(list.Id, noteInReplyToIri, db);
                    if (threadTarget is not null)
                    {
                        existingComment.ItemId = threadTarget.ItemId;
                        existingComment.ParentCommentId = threadTarget.ParentCommentId;
                        existingComment.InReplyToIri = noteInReplyToIri;
                    }
                }

                ApplyIncomingCommentNote(existingComment, objectProp, actorIri, readIriFromActivityPubNode);
                existingComment.ModifiedDate = DateTime.UtcNow;
                existingComment.LastReceivedAt = DateTime.UtcNow;

                await db.SaveChangesAsync();
                await list.RefreshRemoteCommentLikesForItemAsync(existingComment.ItemId, db);
                await list.GenerateHTMLListPage(db);

                await broadcastActivityPubCommentAnnounceToFollowersAsync(
                    list,
                    db,
                    existingComment.RemoteObjectIri,
                    $"Updated remote comment for list item {existingComment.ItemId}",
                    null);

                string updateMsg = $"ActivityPub comment update applied for item {existingComment.ItemId}: comment {existingComment.Id}";
                return OkPayloadWithTrace(fn, new
                {
                    itemId = existingComment.ItemId,
                    commentId = existingComment.Id,
                    remoteObject = existingComment.RemoteObjectIri,
                    inReplyTo = existingComment.InReplyToIri,
                    parentCommentId = existingComment.ParentCommentId,
                    persistedActivityPath
                }, updateMsg);
            }
            // This next bit handles the case where we were not originally addressed in a post, but then 
            // an update to a comment/item that we know about subsequently gets addressed properly to us. The thing we care about is in the inReplyTo block. 
            
            string? updateInReplyToIri = ReadIriProperty(objectProp, "inReplyTo", readIriFromActivityPubNode)
                ?? ReadIriProperty(objectProp, "inreplyto", readIriFromActivityPubNode);
            if (!string.IsNullOrWhiteSpace(updateInReplyToIri))
            {
                if (!IsIncomingNoteActorConsistent(actorIri, objectProp, readIriFromActivityPubNode))
                {
                    string ignoredMsg = $"Ignored ActivityPub Update/Note for unknown comment object {noteObjectIri} due to actor/attributedTo mismatch";
                    DBg.d(LogLevel.Warning, $"{fn} -- {ignoredMsg}");
                    return OkWithTrace(fn, ignoredMsg);
                }

                var threadTarget = await ResolveCommentThreadTargetAsync(list.Id, updateInReplyToIri, db);
                if (threadTarget is not null)
                {
                    var createdFromUpdate = new GeListItemComment
                    {
                        ListId = list.Id,
                        ItemId = threadTarget.ItemId,
                        ParentCommentId = threadTarget.ParentCommentId,
                        RemoteObjectIri = noteObjectIri,
                        InReplyToIri = updateInReplyToIri,
                        CreatedDate = DateTime.UtcNow,
                        ModifiedDate = DateTime.UtcNow,
                        LastReceivedAt = DateTime.UtcNow
                    };

                    ApplyIncomingCommentNote(createdFromUpdate, objectProp, actorIri, readIriFromActivityPubNode);
                    db.ItemComments.Add(createdFromUpdate);

                    await db.SaveChangesAsync();
                    await list.RefreshRemoteCommentLikesForItemAsync(createdFromUpdate.ItemId, db);
                    await list.GenerateHTMLListPage(db);

                    await broadcastActivityPubCommentAnnounceToFollowersAsync(
                        list,
                        db,
                        createdFromUpdate.RemoteObjectIri,
                        $"Accepted first-seen remote comment via Update for list item {createdFromUpdate.ItemId}",
                        null);

                    string createdFromUpdateMsg = $"ActivityPub threaded comment accepted via Update for item {createdFromUpdate.ItemId}: comment {createdFromUpdate.Id}";
                    return OkPayloadWithTrace(fn, new
                    {
                        itemId = createdFromUpdate.ItemId,
                        commentId = createdFromUpdate.Id,
                        remoteObject = createdFromUpdate.RemoteObjectIri,
                        inReplyTo = createdFromUpdate.InReplyToIri,
                        parentCommentId = createdFromUpdate.ParentCommentId,
                        acceptedViaUpdate = true,
                        persistedActivityPath
                    }, createdFromUpdateMsg);
                }
            }

            GeListItem? existingItem = await db.Items.FirstOrDefaultAsync(item =>
                item.ListId == list.Id
                && !string.IsNullOrWhiteSpace(item.SourceObjectIri)
                && item.SourceObjectIri == noteObjectIri);

            if (existingItem is null)
            {
                string ignoredMsg = $"Ignored ActivityPub Update/Note for unknown source object {noteObjectIri}";
                DBg.d(LogLevel.Information, $"{fn} -- {ignoredMsg}");
                return OkWithTrace(fn, ignoredMsg);
            }

            string? oldName = existingItem.Name;
            string? oldComment = existingItem.Comment;
            List<string> oldTags = existingItem.Tags?.ToList() ?? new List<string>();
            string? oldSourceAttributedTo = existingItem.SourceAttributedToIri;
            string? oldOriginatorActor = existingItem.OriginatorActorIri;

            string? noteContent = ReadStringProperty(objectProp, "content")
                ?? ReadStringProperty(objectProp, "summary");
            string attachmentsHtml = BuildIncomingAttachmentMarkup(objectProp, readIriFromActivityPubNode);
            string combinedComment = BuildSuggestedItemComment(noteContent, attachmentsHtml);
            string? noteName = ReadStringProperty(objectProp, "name");
            if (!string.IsNullOrWhiteSpace(noteName))
            {
                existingItem.Name = noteName;
            }

            if (!string.IsNullOrWhiteSpace(combinedComment))
            {
                existingItem.Comment = combinedComment;
            }

            existingItem.IsComplete = false;
            existingItem.OriginatorActorIri = actorIri;
            existingItem.SourceObjectIri = noteObjectIri;

            string? attributedToIri = ReadIriProperty(objectProp, "attributedTo", readIriFromActivityPubNode)
                ?? ReadIriProperty(objectProp, "attributedto", readIriFromActivityPubNode);
            if (!string.IsNullOrWhiteSpace(attributedToIri))
            {
                existingItem.SourceAttributedToIri = attributedToIri;
            }

            existingItem.Tags = ExtractHashtagsFromNoteTag(objectProp).ToList();
            existingItem.ModifiedDate = DateTime.Now;

            GeList? modlist = await db.Lists.FirstOrDefaultAsync(l => l.Name == GlobalConfig.modListName);
            if (modlist is null)
            {
                modlist = new GeList
                {
                    Name = GlobalConfig.modListName,
                    Visibility = GeListVisibility.ListOwners,
                    CreatorId = list.CreatorId
                };
                db.Lists.Add(modlist);
                await db.SaveChangesAsync();
            }

            await db.SaveChangesAsync();

            var modItem = new GeListItem
            {
                Name = $"{list.Name}#{existingItem.Id} <= UPDATE by {actorIri}",
                ListId = modlist.Id,
                Visible = true,
                Tags = new List<string> { "SUGGESTED", "ACTIVITYPUB", "UPDATED", list.Name ?? $"list-{list.Id}" }
            };

            string oldTagsJoined = string.Join(", ", oldTags);
            string newTagsJoined = string.Join(", ", existingItem.Tags ?? new List<string>());
            var modComment = new StringBuilder();
            modComment.AppendLine($"UPDATE received via ActivityPub for existing suggestion by {actorIri}  ");
            modComment.AppendLine($"Target item: <a href=\"_edit.item.html?listid={list.Id}&itemid={existingItem.Id}\">{list.Name}#{existingItem.Id}</a>  ");
            modComment.AppendLine($"Source object: <a href=\"{WebUtility.HtmlEncode(noteObjectIri)}\">{WebUtility.HtmlEncode(noteObjectIri)}</a>  ");
            modComment.AppendLine("---------  ");
            modComment.AppendLine("**Diff (old -> new):**  ");
            bool anyChanged = false;

            string? diffName = BuildDiffBlockIfChanged("Name", oldName, existingItem.Name);
            if (!string.IsNullOrWhiteSpace(diffName))
            {
                anyChanged = true;
                modComment.AppendLine(diffName);
            }

            string? diffComment = BuildDiffBlockIfChanged("Comment", oldComment, existingItem.Comment);
            if (!string.IsNullOrWhiteSpace(diffComment))
            {
                anyChanged = true;
                modComment.AppendLine(diffComment);
            }

            string? diffTags = BuildDiffBlockIfChanged("Tags", oldTagsJoined, newTagsJoined);
            if (!string.IsNullOrWhiteSpace(diffTags))
            {
                anyChanged = true;
                modComment.AppendLine(diffTags);
            }

            string? diffAttributedTo = BuildDiffBlockIfChanged("Source AttributedTo", oldSourceAttributedTo, existingItem.SourceAttributedToIri);
            if (!string.IsNullOrWhiteSpace(diffAttributedTo))
            {
                anyChanged = true;
                modComment.AppendLine(diffAttributedTo);
            }

            string? diffSourceActor = BuildDiffBlockIfChanged("Source Actor", oldOriginatorActor, existingItem.OriginatorActorIri);
            if (!string.IsNullOrWhiteSpace(diffSourceActor))
            {
                anyChanged = true;
                modComment.AppendLine(diffSourceActor);
            }

            if (!anyChanged)
            {
                modComment.AppendLine("No material field changes detected after normalization.  ");
            }
            modComment.AppendLine("---------  ");
            modComment.AppendLine("**Previous snapshot:**  ");
            modComment.AppendLine($"- Name: {FormatValueInline(oldName)}  ");
            modComment.AppendLine($"- Tags: {FormatValueInline(oldTagsJoined)}  ");
            modComment.AppendLine("- Comment:  ");
            modComment.AppendLine("```text");
            modComment.AppendLine(SanitizeForCodeFence(oldComment));
            modComment.AppendLine("```  ");
            modComment.AppendLine("---------  ");
            modComment.AppendLine($"<a href=\"_edit.item.html?listid={list.Id}&itemid={existingItem.Id}\">LINK TO REVIEW UPDATED ITEM</a>");
            modItem.Comment = modComment.ToString();

            db.Items.Add(modItem);
            await db.SaveChangesAsync();

            await list.RegenerateAllFiles(db);
            await modlist.RegenerateAllFiles(db);

            string msg = $"ActivityPub update applied to item {existingItem.Id}; moderation ticket {modItem.Id} created";
            return OkPayloadWithTrace(fn, new
            {
                itemId = existingItem.Id,
                moderationItemId = modItem.Id,
                sourceObject = noteObjectIri,
                sourceActor = actorIri,
                sourceAttributedTo = existingItem.SourceAttributedToIri,
                persistedActivityPath
            }, msg);
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

    private static bool IsActivityAddressedToListActor(JsonElement activityJson, string expectedActorUrl, string followersUrl)
    {
        static bool HasAddressedTarget(JsonElement node, string expectedActorUrl, string followersUrl)
        {
            if (node.ValueKind == JsonValueKind.String)
            {
                string? value = node.GetString();
                return !string.IsNullOrWhiteSpace(value)
                    && (string.Equals(value, expectedActorUrl, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(value, followersUrl, StringComparison.OrdinalIgnoreCase));
            }

            if (node.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in node.EnumerateArray())
                {
                    if (HasAddressedTarget(entry, expectedActorUrl, followersUrl))
                    {
                        return true;
                    }
                }
                return false;
            }

            if (node.ValueKind == JsonValueKind.Object)
            {
                if (node.TryGetProperty("id", out var idProp) && HasAddressedTarget(idProp, expectedActorUrl, followersUrl))
                {
                    return true;
                }
                if (node.TryGetProperty("href", out var hrefProp) && HasAddressedTarget(hrefProp, expectedActorUrl, followersUrl))
                {
                    return true;
                }
            }

            return false;
        }

        foreach (string propName in new[] { "to", "cc", "audience" })
        {
            if (activityJson.TryGetProperty(propName, out var prop) && HasAddressedTarget(prop, expectedActorUrl, followersUrl))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNoteMentioningListActor(JsonElement noteObject, string expectedActorUrl, string? listActivityPubId, string? apDomain)
    {
        if (!noteObject.TryGetProperty("tag", out var tagProp))
        {
            return false;
        }

        string? expectedHandle = !string.IsNullOrWhiteSpace(listActivityPubId) && !string.IsNullOrWhiteSpace(apDomain)
            ? $"@{listActivityPubId}@{apDomain}"
            : null;

        IEnumerable<JsonElement> tags = tagProp.ValueKind == JsonValueKind.Array
            ? tagProp.EnumerateArray()
            : new[] { tagProp };

        foreach (var tag in tags)
        {
            if (tag.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string? type = ReadStringProperty(tag, "type");
            if (!string.Equals(type, "Mention", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? href = ReadStringProperty(tag, "href") ?? ReadIriProperty(tag, "id", ActivityPubPayloadFactory.ReadIriFromActivityPubNode);
            if (!string.IsNullOrWhiteSpace(href)
                && string.Equals(href, expectedActorUrl, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string? name = ReadStringProperty(tag, "name");
            if (!string.IsNullOrWhiteSpace(expectedHandle)
                && !string.IsNullOrWhiteSpace(name)
                && string.Equals(name, expectedHandle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? ReadStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static string? ReadIriProperty(JsonElement element, string propertyName, Func<JsonElement, string?> readIriFromActivityPubNode)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in property.EnumerateArray())
            {
                string? iri = readIriFromActivityPubNode(entry);
                if (!string.IsNullOrWhiteSpace(iri))
                {
                    return iri;
                }
            }
            return null;
        }

        return readIriFromActivityPubNode(property);
    }

    private static string BuildSuggestedItemComment(string? noteContent, string attachmentMarkup)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(noteContent))
        {
            parts.Add(noteContent.Trim());
        }
        if (!string.IsNullOrWhiteSpace(attachmentMarkup))
        {
            parts.Add(attachmentMarkup);
        }

        return string.Join("\n\n", parts);
    }

    private static string BuildIncomingAttachmentMarkup(JsonElement noteObject, Func<JsonElement, string?> readIriFromActivityPubNode)
    {
        if (!noteObject.TryGetProperty("attachment", out var attachmentProp))
        {
            return string.Empty;
        }

        IEnumerable<JsonElement> attachments = attachmentProp.ValueKind == JsonValueKind.Array
            ? attachmentProp.EnumerateArray()
            : new[] { attachmentProp };

        var sb = new StringBuilder();
        foreach (var attachment in attachments)
        {
            string? url = readIriFromActivityPubNode(attachment);
            string? name = null;
            string? mediaType = null;
            string? type = null;

            if (attachment.ValueKind == JsonValueKind.Object)
            {
                if (string.IsNullOrWhiteSpace(url) && attachment.TryGetProperty("url", out var urlProp))
                {
                    if (urlProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var urlEntry in urlProp.EnumerateArray())
                        {
                            url = readIriFromActivityPubNode(urlEntry);
                            if (!string.IsNullOrWhiteSpace(url))
                            {
                                break;
                            }
                        }
                    }
                    else
                    {
                        url = readIriFromActivityPubNode(urlProp);
                    }
                }

                name = ReadStringProperty(attachment, "name")
                    ?? ReadStringProperty(attachment, "summary");
                mediaType = ReadStringProperty(attachment, "mediaType")
                    ?? ReadStringProperty(attachment, "mediatype");
                type = ReadStringProperty(attachment, "type");
            }

            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            string safeUrl = WebUtility.HtmlEncode(url);
            string linkLabel = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(name) ? url : name);
            bool isImage = (!string.IsNullOrWhiteSpace(mediaType) && mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                || string.Equals(type, "Image", StringComparison.OrdinalIgnoreCase);

            if (isImage)
            {
                sb.Append($"<div class=\"ap-attachment ap-attachment-image\"><img src=\"{safeUrl}\" alt=\"{linkLabel}\"></div>");
            }
            else
            {
                sb.Append($"<div class=\"ap-attachment ap-attachment-link\"><a href=\"{safeUrl}\" rel=\"nofollow noopener noreferrer\" target=\"_blank\">{linkLabel}</a></div>");
            }
        }

        return sb.ToString();
    }

    private static IEnumerable<string> ExtractHashtagsFromNoteTag(JsonElement noteObject)
    {
        if (!noteObject.TryGetProperty("tag", out var tagProp))
        {
            return Enumerable.Empty<string>();
        }

        IEnumerable<JsonElement> tags = tagProp.ValueKind == JsonValueKind.Array
            ? tagProp.EnumerateArray()
            : new[] { tagProp };

        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tags)
        {
            if (tag.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string? type = ReadStringProperty(tag, "type");
            if (!string.Equals(type, "Hashtag", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? name = ReadStringProperty(tag, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            string normalized = name.Trim().TrimStart('#');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            // Match outbound payload normalization: keep non-space characters only.
            normalized = string.Concat(normalized.Where(c => !char.IsWhiteSpace(c)));
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            found.Add(normalized);
        }

        return found;
    }

    private static string BuildActorMentionLabel(JsonElement activityJson, string actorIri)
    {
        if (activityJson.TryGetProperty("actor", out var actorProp) && actorProp.ValueKind == JsonValueKind.Object)
        {
            string? preferredUsername = ReadStringProperty(actorProp, "preferredUsername")
                ?? ReadStringProperty(actorProp, "preferredusername");
            string? actorHost = TryGetHostFromIri(actorIri);
            if (!string.IsNullOrWhiteSpace(preferredUsername)
                && !string.IsNullOrWhiteSpace(actorHost)
                && Regex.IsMatch(preferredUsername, "^[A-Za-z0-9_]+$"))
            {
                return $"@{preferredUsername}@{actorHost}";
            }
        }

        if (TryBuildMentionFromActorIri(actorIri, out string? mentionFromIri))
        {
            return mentionFromIri!;
        }

        return actorIri;
    }

    private static bool TryBuildMentionFromActorIri(string actorIri, out string? mention)
    {
        mention = null;
        if (!Uri.TryCreate(actorIri, UriKind.Absolute, out var actorUri))
        {
            return false;
        }

        string[] pathParts = actorUri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (pathParts.Length == 0)
        {
            return false;
        }

        string? user = null;
        for (int i = pathParts.Length - 1; i >= 0; i--)
        {
            string candidate = pathParts[i];
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (candidate.StartsWith("@", StringComparison.Ordinal))
            {
                candidate = candidate[1..];
            }

            if (!Regex.IsMatch(candidate, "^[A-Za-z0-9_]+$"))
            {
                continue;
            }

            user = candidate;
            break;
        }

        if (string.IsNullOrWhiteSpace(user))
        {
            return false;
        }

        mention = $"@{user}@{actorUri.Host}";
        return true;
    }

    private static string? TryGetHostFromIri(string? iri)
    {
        if (string.IsNullOrWhiteSpace(iri))
        {
            return null;
        }

        return Uri.TryCreate(iri, UriKind.Absolute, out var uri)
            ? uri.Host
            : null;
    }

    private static string? BuildDiffBlockIfChanged(string label, string? oldValue, string? newValue)
    {
        string normalizedOld = NormalizeDiffValue(oldValue);
        string normalizedNew = NormalizeDiffValue(newValue);
        bool changed = !string.Equals(normalizedOld, normalizedNew, StringComparison.Ordinal);
        if (!changed)
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"*** {label} (changed) ***");
        sb.AppendLine("```diff");
        sb.AppendLine($"- {SanitizeForCodeFence(normalizedOld)}");
        sb.AppendLine($"+ {SanitizeForCodeFence(normalizedNew)}");
        sb.AppendLine("```  ");
        return sb.ToString();
    }

    private static string NormalizeDiffValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "(empty)" : value.Trim();
    }

    private static string FormatValueInline(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "(empty)" : value.Trim();
    }

    private static string SanitizeForCodeFence(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(empty)";
        }

        return value.Replace("```", "'''");
    }

    private static void ApplyIncomingCommentNote(
        GeListItemComment target,
        JsonElement noteObject,
        string actorIri,
        Func<JsonElement, string?> readIriFromActivityPubNode)
    {
        target.ActorIri = actorIri;
        target.AttributedToIri = ReadIriProperty(noteObject, "attributedTo", readIriFromActivityPubNode)
            ?? ReadIriProperty(noteObject, "attributedto", readIriFromActivityPubNode)
            ?? target.AttributedToIri;
        target.Name = ReadStringProperty(noteObject, "name") ?? target.Name;
        target.ContentHtml = ReadStringProperty(noteObject, "content")
            ?? ReadStringProperty(noteObject, "summary")
            ?? target.ContentHtml;
        target.Summary = ReadStringProperty(noteObject, "summary") ?? target.Summary;
        target.RawNoteJson = noteObject.GetRawText();
        target.PublishedAt = ReadDateTimeOffsetProperty(noteObject, "published") ?? target.PublishedAt;
        target.UpdatedAt = ReadDateTimeOffsetProperty(noteObject, "updated")
            ?? ReadDateTimeOffsetProperty(noteObject, "modified")
            ?? target.UpdatedAt;
    }

    private static bool IsCommentDeleteAuthorized(string actorIri, GeListItemComment comment)
    {
        if (string.IsNullOrWhiteSpace(actorIri))
        {
            return false;
        }

        bool hasStoredActor = !string.IsNullOrWhiteSpace(comment.ActorIri)
            || !string.IsNullOrWhiteSpace(comment.AttributedToIri);
        if (!hasStoredActor)
        {
            return true;
        }

        return string.Equals(actorIri, comment.ActorIri, StringComparison.OrdinalIgnoreCase)
            || string.Equals(actorIri, comment.AttributedToIri, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIncomingNoteActorConsistent(
        string actorIri,
        JsonElement noteObject,
        Func<JsonElement, string?> readIriFromActivityPubNode)
    {
        if (string.IsNullOrWhiteSpace(actorIri))
        {
            return false;
        }

        string? attributedTo = ReadIriProperty(noteObject, "attributedTo", readIriFromActivityPubNode)
            ?? ReadIriProperty(noteObject, "attributedto", readIriFromActivityPubNode);
        if (string.IsNullOrWhiteSpace(attributedTo))
        {
            return true;
        }

        return string.Equals(actorIri, attributedTo, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<LikeTargetResolution?> ResolveIncomingLikeTargetAsync(int listId, string? candidateObjectIri, GeFeSLEDb db)
    {
        if (string.IsNullOrWhiteSpace(candidateObjectIri))
        {
            return null;
        }

        if (TryResolveLocalCommentIdFromIri(candidateObjectIri, out int commentId))
        {
            GeListItemComment? comment = await db.ItemComments.FirstOrDefaultAsync(c => c.Id == commentId && c.ListId == listId);
            if (comment is not null)
            {
                return new LikeTargetResolution(
                    $"{GlobalConfig.Hostname}/apv1/comments/{comment.Id}",
                    comment.ItemId,
                    comment.Id);
            }
        }

        if (TryResolveLocalItemIdFromIri(candidateObjectIri, listId, out int itemId))
        {
            GeListItem? item = await db.Items.FirstOrDefaultAsync(i => i.Id == itemId && i.ListId == listId);
            if (item is not null)
            {
                return new LikeTargetResolution(
                    $"{GlobalConfig.Hostname}/apv1/items/{item.Id}",
                    item.Id,
                    null);
            }
        }

        return null;
    }

    private static bool TryResolveLocalCommentIdFromIri(string iri, out int commentId)
    {
        commentId = 0;
        if (string.IsNullOrWhiteSpace(iri))
        {
            return false;
        }

        string path = Uri.TryCreate(iri, UriKind.Absolute, out var absolute)
            ? absolute.AbsolutePath
            : iri;

        string[] parts = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 3
            && string.Equals(parts[0], "apv1", StringComparison.OrdinalIgnoreCase)
            && string.Equals(parts[1], "comments", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parts[2], out commentId))
        {
            return true;
        }

        if (parts.Length >= 5
            && string.Equals(parts[0], "apv1", StringComparison.OrdinalIgnoreCase)
            && string.Equals(parts[1], "lists", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parts[2], out _)
            && string.Equals(parts[3], "comments", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parts[4], out commentId))
        {
            return true;
        }

        commentId = 0;
        return false;
    }

    private static bool IsCommentTombstoned(GeListItemComment comment)
    {
        return string.Equals(comment.Summary?.Trim(), "<comment deleted>", StringComparison.Ordinal);
    }

    private static DateTimeOffset? ReadDateTimeOffsetProperty(JsonElement element, string propertyName)
    {
        string? raw = ReadStringProperty(element, propertyName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return DateTimeOffset.TryParse(raw, out var parsed)
            ? parsed
            : null;
    }

    private static async Task<CommentThreadTarget?> ResolveCommentThreadTargetAsync(int listId, string inReplyToIri, GeFeSLEDb db)
    {
        GeListItemComment? parentComment = await db.ItemComments.FirstOrDefaultAsync(c =>
            c.ListId == listId
            && c.RemoteObjectIri == inReplyToIri);
        if (parentComment is not null)
        {
            return new CommentThreadTarget(parentComment.ItemId, parentComment.Id);
        }

        if (TryResolveLocalItemIdFromIri(inReplyToIri, listId, out int localItemId))
        {
            bool itemExists = await db.Items.AnyAsync(i => i.ListId == listId && i.Id == localItemId);
            if (itemExists)
            {
                return new CommentThreadTarget(localItemId, null);
            }
        }

        GeListItem? sourceMatchedItem = await db.Items.FirstOrDefaultAsync(i =>
            i.ListId == listId
            && i.SourceObjectIri == inReplyToIri);
        if (sourceMatchedItem is not null)
        {
            return new CommentThreadTarget(sourceMatchedItem.Id, null);
        }

        return null;
    }

    private static bool TryResolveLocalItemIdFromIri(string iri, int expectedListId, out int itemId)
    {
        itemId = 0;
        if (string.IsNullOrWhiteSpace(iri))
        {
            return false;
        }

        string path;
        if (Uri.TryCreate(iri, UriKind.Absolute, out var absolute))
        {
            path = absolute.AbsolutePath;
        }
        else
        {
            path = iri;
        }

        string[] parts = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 3
            && string.Equals(parts[0], "apv1", StringComparison.OrdinalIgnoreCase)
            && string.Equals(parts[1], "items", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parts[2], out itemId))
        {
            return true;
        }

        if (parts.Length >= 5
            && string.Equals(parts[0], "apv1", StringComparison.OrdinalIgnoreCase)
            && string.Equals(parts[1], "lists", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parts[2], out int listId)
            && string.Equals(parts[3], "items", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parts[4], out itemId))
        {
            return listId == expectedListId;
        }

        itemId = 0;
        return false;
    }

    private sealed class CommentThreadTarget
    {
        public CommentThreadTarget(int itemId, int? parentCommentId)
        {
            ItemId = itemId;
            ParentCommentId = parentCommentId;
        }

        public int ItemId { get; }
        public int? ParentCommentId { get; }
    }

    private sealed class LikeTargetResolution
    {
        public LikeTargetResolution(string objectIri, int? itemId, int? commentId)
        {
            ObjectIri = objectIri;
            ItemId = itemId;
            CommentId = commentId;
        }

        public string ObjectIri { get; }
        public int? ItemId { get; }
        public int? CommentId { get; }
    }
}
