using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Xml.Linq;
//using Newtonsoft.Json;
using System.Text.Json;
using Markdig;
using Microsoft.AspNetCore.Identity;
using System.Text.Json.Serialization;
using System.Net;
using System.Text.RegularExpressions;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Collections.Concurrent;

using GeFeSLE;


[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GeListVisibility
{
    Public,          // anyone can view the list's html page, json, rss etc. 
    Contributors,    // restricted to contributors and list owners (and list creator and SU)
    ListOwners,      // restricted to only list owners (and creator and SU)
    Private          // restricted to only creator and SU
}

public class GeListDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? ActivityPubId { get; set; }
    public string? Comment { get; set; }
    public GeListVisibility Visibility { get; set; } = GeListVisibility.Public;
}

public class ImportService
{
    public static readonly string Microsoft = "Microsoft";
    public static readonly string Google = "Google";
    public static readonly string Mastodon = "Mastodon";
    public static readonly Dictionary<string, List<string>> Services = new Dictionary<string, List<string>>
    {
        { Microsoft, new List<string> { "StickyNotes" } }, // , "OneNote", "MicrosoftLists" -- NOT SUPPORTED YET
        { Google, new List<string> { "Tasks"  } },        //"Keep","Saved" -- NOT SUPPORTED YET 
        { Mastodon, new List<string> { "Bookmarks" } }     // ?? TBD
    };

    // provided a platform and a service, return a boolean if its in our support list
    public static bool IsSupported(string platform, string service)
    {
        if (Services.ContainsKey(platform))
        {
            return Services[platform].Contains(service);
        }
        return false;
    }
    public static bool IsSupported(string platformService)
    {
        string[] parts = platformService.Split(':');
        if (parts.Length != 2)
        {
            throw new ArgumentException("platformService must be in the format 'platform:service'");
        }

        string platform = parts[0];
        string service = parts[1];

        return IsSupported(platform, service);
    }
}

public class GeListImportDto
{
    public string Service { get; set; } = string.Empty;
    public string? Data { get; set; } = null;


}

public class GeList
{
    private static readonly ConcurrentDictionary<string, string> ActorHandleCacheByIri =
        new(StringComparer.OrdinalIgnoreCase);

    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Comment { get; set; }

    // add a member that is this list's created date
    public DateTime CreatedDate { get; set; } = DateTime.Now;

    // add a member that is this list's modified date
    public DateTime ModifiedDate { get; set; } = DateTime.Now;

    // list owner
    public string CreatorId { get; set; } = null!;
    public GeFeSLEUser? Creator { get; set; }

    // Navigation property for the list owners
    public ICollection<GeFeSLEUser> ListOwners { get; set; } = new List<GeFeSLEUser>();

    // Navigation property for the contributors
    public ICollection<GeFeSLEUser> Contributors { get; set; } = new List<GeFeSLEUser>();

    // set list visibility
    public GeListVisibility Visibility { get; set; } = GeListVisibility.Public;

    // ACTIVITYPUB FIELDS
    public string? ActivityPubId { get; set; } = null; 
    // could be same as Name but with more compatible characters

    // ActivityPub convenience - just so we know the items returned by GetItems are 
    // OrderedCollection (isOrdered = true) or Collection (isOrdered = false).
    public bool isOrdered { get; set; } = false; // for future use when we implement ordered lists. For now, all lists are unordered.

    [NotMapped]
    public int VisibleItemCount { get; set; }




    public void SetVisibility(GeListVisibility newvisibility)
    {
        var oldVisibility = Visibility;
        Visibility = newvisibility;
        // if the visibility has changed FROM public to something else,
        // then remove the list from the list of public lists
        if (oldVisibility == GeListVisibility.Public && newvisibility > GeListVisibility.Public)
        {
            ProtectedFiles.AddList(this);
        }
        else if (oldVisibility > GeListVisibility.Public && newvisibility == GeListVisibility.Public)
        {
            ProtectedFiles.RemoveList(this);

        }


    }


    async public Task GenerateHTMLListPage(GeFeSLEDb db)
    {
        DBg.d(LogLevel.Trace, $"GenerateHTMLListPage: {Id}");
        var renderStopwatch = System.Diagnostics.Stopwatch.StartNew();

        await db.Entry(this).Reference(list => list.Creator).LoadAsync();
        await db.Entry(this).Collection(list => list.ListOwners).LoadAsync();
        await db.Entry(this).Collection(list => list.Contributors).LoadAsync();

        var listMarkdownPipeline = new MarkdownPipelineBuilder()
            .UseSoftlineBreakAsHardlineBreak()
            .UseAutoLinks()
            .Build();

        static string LinkifyObviousUrls(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            if (Regex.IsMatch(raw, @"<a\b", RegexOptions.IgnoreCase))
            {
                return raw;
            }

            string encoded = WebUtility.HtmlEncode(raw);
            var urlRegex = new Regex(@"(?<url>https?://[^\s<""']+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            return urlRegex.Replace(encoded, match =>
            {
                string url = match.Groups["url"].Value;
                string trailing = string.Empty;
                while (url.Length > 0 && ".,!?;:)".Contains(url[^1]))
                {
                    trailing = url[^1] + trailing;
                    url = url[..^1];
                }

                if (string.IsNullOrWhiteSpace(url))
                {
                    return match.Value;
                }

                return $"<a href=\"{url}\" rel=\"nofollow noopener noreferrer\" target=\"_blank\">{url}</a>{trailing}";
            });
        }

        static string FriendlyActorHandleFromIri(string? iri)
        {
            if (string.IsNullOrWhiteSpace(iri))
            {
                return string.Empty;
            }

            string trimmed = iri.Trim();
            if (trimmed.StartsWith("acct:", StringComparison.OrdinalIgnoreCase))
            {
                string acct = trimmed.Substring("acct:".Length).Trim().TrimStart('@');
                return string.IsNullOrWhiteSpace(acct) ? trimmed : $"@{acct}";
            }

            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            {
                return trimmed;
            }

            var segments = uri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string user = string.Empty;
            if (segments.Length >= 2 && (segments[^2].Equals("users", StringComparison.OrdinalIgnoreCase)
                    || segments[^2].Equals("u", StringComparison.OrdinalIgnoreCase)
                    || segments[^2].Equals("profile", StringComparison.OrdinalIgnoreCase)
                    || segments[^2].Equals("@", StringComparison.OrdinalIgnoreCase)))
            {
                user = segments[^1];
            }
            else if (segments.Length > 0)
            {
                user = segments[^1];
            }

            user = user.TrimStart('@');
            if (string.IsNullOrWhiteSpace(user))
            {
                return trimmed;
            }

            return $"@{user}@{uri.Host}";
        }

        static string FormatAcctLikeHandle(string? acct, string hostFallback)
        {
            if (string.IsNullOrWhiteSpace(acct))
            {
                return string.Empty;
            }

            string normalized = acct.Trim().TrimStart('@');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            if (!normalized.Contains('@'))
            {
                normalized = $"{normalized}@{hostFallback}";
            }

            return $"@{normalized}";
        }

        static bool TryGetMastodonNumericAccountId(Uri actorUri, out string accountId)
        {
            accountId = string.Empty;
            var segments = actorUri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length < 3)
            {
                return false;
            }

            if (!segments[^3].Equals("ap", StringComparison.OrdinalIgnoreCase)
                || !segments[^2].Equals("users", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!long.TryParse(segments[^1], out _))
            {
                return false;
            }

            accountId = segments[^1];
            return true;
        }

        static async Task<string?> TryResolveFriendlyActorHandleAsync(HttpClient client, string actorIri)
        {
            if (string.IsNullOrWhiteSpace(actorIri)
                || !Uri.TryCreate(actorIri, UriKind.Absolute, out var actorUri))
            {
                return null;
            }

            if (TryGetMastodonNumericAccountId(actorUri, out string accountId))
            {
                string accountEndpoint = $"{actorUri.Scheme}://{actorUri.Authority}/api/v1/accounts/{accountId}";
                try
                {
                    using var accountResp = await client.GetAsync(accountEndpoint);
                    if (accountResp.IsSuccessStatusCode)
                    {
                        await using var accountStream = await accountResp.Content.ReadAsStreamAsync();
                        using var accountDoc = await JsonDocument.ParseAsync(accountStream);
                        if (accountDoc.RootElement.ValueKind == JsonValueKind.Object)
                        {
                            string? acct = accountDoc.RootElement.TryGetProperty("acct", out var acctProp)
                                && acctProp.ValueKind == JsonValueKind.String
                                ? acctProp.GetString()
                                : null;
                            string formattedAcct = FormatAcctLikeHandle(acct, actorUri.Host);
                            if (!string.IsNullOrWhiteSpace(formattedAcct))
                            {
                                return formattedAcct;
                            }

                            string? username = accountDoc.RootElement.TryGetProperty("username", out var usernameProp)
                                && usernameProp.ValueKind == JsonValueKind.String
                                ? usernameProp.GetString()
                                : null;
                            string fallback = FormatAcctLikeHandle(username, actorUri.Host);
                            if (!string.IsNullOrWhiteSpace(fallback))
                            {
                                return fallback;
                            }
                        }
                    }
                }
                catch
                {
                    // Fall through to actor JSON fetch.
                }
            }

            try
            {
                using var actorResp = await client.GetAsync(actorUri);
                if (!actorResp.IsSuccessStatusCode)
                {
                    return null;
                }

                await using var actorStream = await actorResp.Content.ReadAsStreamAsync();
                using var actorDoc = await JsonDocument.ParseAsync(actorStream);
                if (actorDoc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                string? acct = actorDoc.RootElement.TryGetProperty("acct", out var acctProp)
                    && acctProp.ValueKind == JsonValueKind.String
                    ? acctProp.GetString()
                    : null;
                string formattedAcct = FormatAcctLikeHandle(acct, actorUri.Host);
                if (!string.IsNullOrWhiteSpace(formattedAcct))
                {
                    return formattedAcct;
                }

                string? preferredUsername = actorDoc.RootElement.TryGetProperty("preferredUsername", out var preferredProp)
                    && preferredProp.ValueKind == JsonValueKind.String
                    ? preferredProp.GetString()
                    : null;
                string preferredHandle = FormatAcctLikeHandle(preferredUsername, actorUri.Host);
                if (!string.IsNullOrWhiteSpace(preferredHandle))
                {
                    return preferredHandle;
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        static string RenderExternalLink(string? iri, string? displayLabel = null)
        {
            if (string.IsNullOrWhiteSpace(iri))
            {
                return string.Empty;
            }

            string trimmedIri = iri.Trim();
            string encodedIri = WebUtility.HtmlEncode(trimmedIri);
            string linkText = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(displayLabel)
                ? trimmedIri
                : displayLabel.Trim());
            return $"<a href=\"{encodedIri}\" rel=\"nofollow noopener noreferrer\" target=\"_blank\">{linkText}</a>";
        }

        static string RenderRemoteCommentBody(GeListItemComment comment)
        {
            if (!string.IsNullOrWhiteSpace(comment.ContentHtml))
            {
                return comment.ContentHtml;
            }

            if (!string.IsNullOrWhiteSpace(comment.Summary))
            {
                return $"<p>{WebUtility.HtmlEncode(comment.Summary)}</p>";
            }

            return "<p>(no content)</p>";
        }

        static string FormatUserDisplay(GeFeSLEUser? user)
        {
            string displayName = user?.UserName
                ?? user?.Email
                ?? user?.Id
                ?? "unknown";
            return WebUtility.HtmlEncode(displayName);
        }

        static List<string> FormatDistinctUsers(IEnumerable<GeFeSLEUser> users, params string?[] excludedIds)
        {
            var excluded = new HashSet<string>(excludedIds.Where(id => !string.IsNullOrWhiteSpace(id))!, StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var formatted = new List<string>();

            foreach (var user in users)
            {
                if (user == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(user.Id) && excluded.Contains(user.Id))
                {
                    continue;
                }

                string displayName = user.UserName
                    ?? user.Email
                    ?? user.Id
                    ?? string.Empty;

                if (string.IsNullOrWhiteSpace(displayName))
                {
                    continue;
                }

                if (seen.Add(displayName))
                {
                    formatted.Add(WebUtility.HtmlEncode(displayName));
                }
            }

            return formatted;
        }

        static List<string> ParseCachedRemoteLikeActors(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<string>();
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return new List<string>();
                }

                return doc.RootElement
                    .EnumerateArray()
                    .Where(el => el.ValueKind == JsonValueKind.String)
                    .Select(el => el.GetString())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        // create a new file in wwwroot with the name of the list
        var filename = $"{Name}.html";
        var dest = Path.Combine(GlobalConfig.wwwroot!, filename);

        var items = await db.Items.Where(item => item.ListId == Id && item.Visible && !item.IsDeleted).ToListAsync();
        var movedItems = await db.Items
            .Where(item => item.ListId == Id
                && item.IsDeleted
                && !item.Visible
                && item.RedirectToItemId.HasValue)
            .Select(item => new { OldId = item.Id, NewId = item.RedirectToItemId!.Value })
            .ToListAsync();
        var itemComments = (await db.ItemComments
            .Where(comment => comment.ListId == Id)
            .ToListAsync())
            .OrderBy(comment => comment.PublishedAt ?? new DateTimeOffset(comment.CreatedDate))
            .ThenBy(comment => comment.Id)
            .ToList();
        var topLevelCommentsByItem = itemComments
            .Where(comment => !comment.ParentCommentId.HasValue)
            .GroupBy(comment => comment.ItemId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var childCommentsByParent = itemComments
            .Where(comment => comment.ParentCommentId.HasValue)
            .GroupBy(comment => comment.ParentCommentId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());
        var commentCountByItem = itemComments
            .GroupBy(comment => comment.ItemId)
            .ToDictionary(group => group.Key, group => group.Count());
        var activeLocalLikes = await db.ActivityPubObjectLikes
            .Where(l => l.ListId == Id && l.IsActive)
            .ToListAsync();
        var localLikeCountByItem = activeLocalLikes
            .Where(l => l.ItemId.HasValue)
            .GroupBy(l => l.ItemId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());
        var localLikeActorsByItem = activeLocalLikes
            .Where(l => l.ItemId.HasValue)
            .GroupBy(l => l.ItemId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(l => l.ActorIri)
                    .Where(actor => !string.IsNullOrWhiteSpace(actor))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(actor => actor, StringComparer.OrdinalIgnoreCase)
                    .ToList());
        var localLikeActorsByComment = activeLocalLikes
            .Where(l => l.CommentId.HasValue)
            .GroupBy(l => l.CommentId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(l => l.ActorIri)
                    .Where(actor => !string.IsNullOrWhiteSpace(actor))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(actor => actor, StringComparer.OrdinalIgnoreCase)
                    .ToList());

        var actorDisplayByIri = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using (var actorLookupClient = new HttpClient())
        {
            actorLookupClient.Timeout = TimeSpan.FromSeconds(5);
            actorLookupClient.DefaultRequestHeaders.Accept.Clear();
            actorLookupClient.DefaultRequestHeaders.Accept.ParseAdd("application/activity+json");
            actorLookupClient.DefaultRequestHeaders.Accept.ParseAdd("application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\"");
            actorLookupClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            actorLookupClient.DefaultRequestHeaders.UserAgent.ParseAdd($"{GlobalStatic.applicationName}/{GlobalConfig.bldVersion}");

            IEnumerable<string> actorCandidates = itemComments.SelectMany(c => new[] { c.ActorIri, c.AttributedToIri })
                .Concat(items.SelectMany(i => new[] { i.OriginatorActorIri, i.SourceAttributedToIri }))
                .Concat(localLikeActorsByItem.SelectMany(pair => pair.Value))
                .Concat(localLikeActorsByComment.SelectMany(pair => pair.Value))
                .Concat(itemComments.SelectMany(c => ParseCachedRemoteLikeActors(c.RemoteLikeActorsJson)))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var actor in actorCandidates)
            {
                if (ActorHandleCacheByIri.TryGetValue(actor, out string? cachedLabel)
                    && !string.IsNullOrWhiteSpace(cachedLabel))
                {
                    actorDisplayByIri[actor] = cachedLabel;
                    continue;
                }

                string? resolved = await TryResolveFriendlyActorHandleAsync(actorLookupClient, actor);
                string finalLabel = !string.IsNullOrWhiteSpace(resolved)
                    ? resolved
                    : FriendlyActorHandleFromIri(actor);

                actorDisplayByIri[actor] = finalLabel;
                ActorHandleCacheByIri[actor] = finalLabel;
            }
        }

        string ResolveActorDisplay(string? actorIri)
        {
            if (string.IsNullOrWhiteSpace(actorIri))
            {
                return string.Empty;
            }

            string iri = actorIri.Trim();
            if (actorDisplayByIri.TryGetValue(iri, out string? label) && !string.IsNullOrWhiteSpace(label))
            {
                return label;
            }

            return FriendlyActorHandleFromIri(iri);
        }

        string RenderLikeDisclosureInline(int displayCount, List<string>? knownActors)
        {
            int safeCount = Math.Max(displayCount, 0);
            List<string> actors = (knownActors ?? new List<string>())
                .Where(actor => !string.IsNullOrWhiteSpace(actor))
                .Select(actor => actor.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(actor => actor, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var likeSb = new StringBuilder();
            likeSb.Append("<details class=\"ap-like-disclosure\">");
            likeSb.Append($"<summary class=\"ap-like-summary\">&hearts; {safeCount}</summary>");
            likeSb.Append("<div class=\"ap-like-details\">");
            likeSb.Append($"<div class=\"ap-like-local-count\">Known actors: {actors.Count}</div>");
            if (actors.Count == 0)
            {
                likeSb.Append("<div class=\"ap-like-empty\">No known likers.</div>");
            }
            else
            {
                likeSb.Append("<ul class=\"ap-like-actors\">");
                foreach (string actorIri in actors)
                {
                    likeSb.Append($"<li>{RenderExternalLink(actorIri, ResolveActorDisplay(actorIri))}</li>");
                }

                likeSb.Append("</ul>");
            }

            likeSb.Append("</div>");
            likeSb.Append("</details>");
            return likeSb.ToString();
        }

        string RenderSourceLinkInline(string? sourceIri)
        {
            if (string.IsNullOrWhiteSpace(sourceIri))
            {
                return string.Empty;
            }

            return $"({RenderExternalLink(sourceIri, "source")})";
        }

        static bool IsCommentTombstoned(GeListItemComment comment)
        {
            return string.Equals(comment.Summary?.Trim(), "<comment deleted>", StringComparison.Ordinal);
        }

        void RenderCommentTree(StringBuilder output, GeListItemComment comment, int depth)
        {
            int clampedDepth = Math.Min(depth, 8);
            bool isTombstoned = IsCommentTombstoned(comment);
            List<GeListItemComment> children = childCommentsByParent.TryGetValue(comment.Id, out var childNodes)
                ? childNodes
                : new List<GeListItemComment>();
            string tombstoneClass = isTombstoned ? " ap-item-comment-tombstone" : string.Empty;
            output.AppendLine($"<div class=\"ap-item-comment depth-{clampedDepth}{tombstoneClass}\">");

            if (!isTombstoned)
            {
                output.AppendLine("<div class=\"ap-item-comment-meta\">");

                string? actorIri = string.IsNullOrWhiteSpace(comment.ActorIri) ? null : comment.ActorIri.Trim();
                string? attributedIri = string.IsNullOrWhiteSpace(comment.AttributedToIri) ? null : comment.AttributedToIri.Trim();
                bool hasActor = !string.IsNullOrWhiteSpace(actorIri);
                bool hasAttributed = !string.IsNullOrWhiteSpace(attributedIri);
                bool sameActorAndAttributed = hasActor
                    && hasAttributed
                    && string.Equals(actorIri, attributedIri, StringComparison.OrdinalIgnoreCase);

                string? fromIri = hasActor ? actorIri : attributedIri;
                if (!string.IsNullOrWhiteSpace(fromIri))
                {
                    output.AppendLine($"<span class=\"ap-item-comment-from\"><strong>From:</strong> {RenderExternalLink(fromIri, ResolveActorDisplay(fromIri))}</span>");
                }

                if (hasActor && hasAttributed && !sameActorAndAttributed)
                {
                    output.AppendLine($"<span class=\"ap-item-comment-attributed\"><strong>Attributed:</strong> {RenderExternalLink(attributedIri, ResolveActorDisplay(attributedIri))}</span>");
                }

                if (!string.IsNullOrWhiteSpace(comment.RemoteObjectIri))
                {
                    output.AppendLine($"<span class=\"ap-item-comment-object\">{RenderSourceLinkInline(comment.RemoteObjectIri)} <span class=\"commentdeletelink\" style=\"display: none;\"><a href=\"#\" onclick=\"deleteItemComment({comment.ItemId},{comment.Id}); return false;\">DELETE</a></span></span>");
                }

                int localKnownLikes = localLikeActorsByComment.TryGetValue(comment.Id, out var localActors)
                    ? localActors.Count
                    : 0;
                List<string> remoteLikeActors = ParseCachedRemoteLikeActors(comment.RemoteLikeActorsJson);
                List<string> displayedCommentLikeActors = remoteLikeActors.Count > 0
                    ? remoteLikeActors
                    : (localActors ?? new List<string>());
                int actorDerivedLikeCount = displayedCommentLikeActors.Count;
                int displayedLikes = Math.Max(comment.RemoteLikesCount ?? 0, Math.Max(localKnownLikes, actorDerivedLikeCount));
                output.AppendLine($"<span class=\"ap-item-comment-likes\">{RenderLikeDisclosureInline(displayedLikes, displayedCommentLikeActors)}</span>");

                output.AppendLine("</div>");
                output.AppendLine($"<div class=\"ap-item-comment-body\">{RenderRemoteCommentBody(comment)}</div>");
            }
            else
            {
                output.AppendLine("<div class=\"ap-item-comment-body\"><p>&lt;comment deleted&gt;</p></div>");
            }

            output.AppendLine("</div>");

            if (children.Count == 0)
            {
                return;
            }

            output.AppendLine("<details class=\"ap-item-comment-children\" open>");
            output.AppendLine($"<summary class=\"ap-item-comment-children-toggle\">Replies ({children.Count})</summary>");
            output.AppendLine("<div class=\"ap-item-comment-children-content\">");
            foreach (var child in children)
            {
                RenderCommentTree(output, child, depth + 1);
            }

            output.AppendLine("</div>");
            output.AppendLine("</details>");
        }

        var followerCount = await db.ListFollowers
            .Where(f => f.FollowingLists.Contains(Id) && !string.IsNullOrWhiteSpace(f.Id))
            .Select(f => f.Id)
            .Distinct()
            .CountAsync();
        DBg.d(LogLevel.Trace, $"Found {items.Count} items for list {Name}");

        var sb = new StringBuilder();
        await GlobalStatic.GenerateHTMLHead(sb, $"{GlobalConfig.sitetitle}:{Name}");

        if (GlobalConfig.bodyHeader != null)
        {
            if (File.Exists(GlobalConfig.bodyHeader))
            {
                var bodyhead = await File.ReadAllTextAsync(GlobalConfig.bodyHeader);
                sb.AppendLine(bodyhead);
            }
            else
            {
                DBg.d(LogLevel.Error, $"Configured page header {GlobalConfig.bodyHeader} file does not exist");
                DBg.d(LogLevel.Error, "Double check your config file for \"SiteCustomize\" : { \"bodyheader\" : \"<filename>\"");
            }
        }

        sb.AppendLine("<div class=\"list-header-wrapper\">");
        sb.AppendLine("<div class=\"list-header-content\">");
        sb.AppendLine($"<h1 class=\"listtitle\"><a class=\"indexlink\" href=\"index.html\">&lt;-</a> {Name}</h1>");
        sb.AppendLine("<div class=\"list-header-stack\">");
        sb.AppendLine("<div class=\"list-federation-meta\">");
        if (!string.IsNullOrWhiteSpace(ActivityPubId))
        {
            sb.AppendLine($"<div class=\"listActivityPubId\"><strong>ActivityPub:</strong> @{ActivityPubId}@{GlobalConfig.APDomain}</div>");
        }
        else
        {
            sb.AppendLine("<div class=\"listActivityPubId\"><strong>ActivityPub:</strong> not configured</div>");
        }

        string visibilityLabel = Visibility switch
        {
            GeListVisibility.Public => "public",
            GeListVisibility.Contributors => "contributor",
            GeListVisibility.ListOwners => "owner",
            GeListVisibility.Private => "private",
            _ => Visibility.ToString()
        };

        string creatorDisplay = FormatUserDisplay(Creator);
        string ownerDisplay = string.Join(", ", FormatDistinctUsers(ListOwners, CreatorId));
        if (string.IsNullOrWhiteSpace(ownerDisplay))
        {
            ownerDisplay = "none";
        }

        string contributorDisplay = string.Join(", ", FormatDistinctUsers(Contributors));
        if (string.IsNullOrWhiteSpace(contributorDisplay))
        {
            contributorDisplay = "none";
        }

        sb.AppendLine($"<div class=\"listVisibility\"><strong>Visibility:</strong> {visibilityLabel}</div>");
        sb.AppendLine($"<div class=\"listFollowerCount\"><strong>Followers:</strong> {followerCount}</div>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class=\"list-date-box\">");
        sb.AppendLine($"<div class=\"list-date-item\"><strong>Created:</strong> {CreatedDate:yyyy-MM-dd HH:mm:ss}</div>");
        sb.AppendLine($"<div class=\"list-date-item\"><strong>Modified:</strong> {ModifiedDate:yyyy-MM-dd HH:mm:ss}</div>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class=\"list-people-box\">");
        sb.AppendLine($"<div class=\"list-people-item\"><strong>Creator:</strong> {creatorDisplay}</div>");
        sb.AppendLine($"<div class=\"list-people-item\"><strong>List Owners:</strong> {ownerDisplay}</div>");
        sb.AppendLine($"<div class=\"list-people-item\"><strong>Contributors:</strong> {contributorDisplay}</div>");
        sb.AppendLine("</div>");
        if (Comment != null)
        {
            var md = Markdown.ToHtml(Comment);
            sb.AppendLine($"<div class=\"list-comment-box\">{md}</div>");
        }
        sb.AppendLine("</div>");
        sb.AppendLine("<div class=\"list-options-panel\">");
        sb.AppendLine("<input type=\"checkbox\" id=\"list-options-toggle\" class=\"list-options-toggle\">");
        sb.AppendLine("<label for=\"list-options-toggle\" class=\"list-options-tab\">List Options</label>");
        sb.AppendLine("<div class=\"list-options-body\">");
        sb.AppendLine($"<div class=\"button editlink\" onclick=\"window.location.href='_edit.list.html?listid={Id}'\" style=\"display: none;\">Edit This List</div>");
        sb.AppendLine($"<div class=\"button edititemlink\" onclick=\"window.location.href='_edit.item.html?listid={Id}'\" style=\"display: none;\">Add New Item</div>");
        sb.AppendLine($"<div class=\"button mastoimportlink\" onclick=\"importItems('Mastodon:Bookmarks',{Id})\" style=\"display: none;\">Mastodon Bookmarks</div>");
        sb.AppendLine($"<div class=\"button stickynoteslink\" onclick=\"importItems('Microsoft:StickyNotes',{Id})\" style=\"display: none;\">Microsoft Sticky Notes</div>");
        sb.AppendLine($"<div class=\"button googletaskslink\" onclick=\"importItems('Google:Tasks',{Id})\" style=\"display: none;\">Google Tasks</div>");
        sb.AppendLine($"<div class=\"button regenlink\" onclick=\"window.location.href='/lists/{Id}/regen'\" style=\"display: none;\">Regenerate</div>");
        if (items.Count > 0)
        {
            sb.AppendLine($"<div class=\"button rsslink\" onclick=\"window.location.href='rss-{Name}.xml'\">RSS Feed</div>");
            sb.AppendLine($"<div class=\"button exportlink\" id=\"exportlink\" onclick=\"window.location.href='{Name}.json'\">JSON Export</div>");
        }
        else
        {
            sb.AppendLine("<div class=\"button rsslink\">No RSS (No Items)</div>");
            sb.AppendLine("<div class=\"button exportlink\" id=\"exportlink\">No JSON (No Items)</div>");
        }

        sb.AppendLine($"<div class=\"button suggestlink\" onclick=\"window.location.href='_edit.item.html?listid={Id}&suggestion=true'\">Suggest Item</div>");
        sb.AppendLine("</div>");
        sb.AppendLine("</div>");
        sb.AppendLine("</div>");

        sb.AppendLine("<span class=\"result\" id=\"result\"></span>");
        sb.AppendLine("<div class=\"searchbox\">");
        sb.AppendLine("<div class=\"textsearch\"><form>Search Text (space separated)");
        sb.AppendLine("<input type=\"text\" id=\"textsearchbox\" onInput=\"filterUpdate(); return false;\" placeholder=\"Search text..\">");
        sb.AppendLine("</form></div>");
        sb.AppendLine("<div class=\"tagsearch\"><form>Search Tags (space separated)");
        sb.AppendLine("<input type=\"text\" id=\"tagsearchbox\" onInput=\"filterUpdate(); return false;\" placeholder=\"Search tags..\">");
        sb.AppendLine("</form></div></div>");

        sb.AppendLine("<hr>");

        sb.AppendLine("<div class=\"itemtable\" id=\"itemtable\">");
        foreach (var item in items)
        {
            sb.AppendLine($"<div class=\"namecell\">{LinkifyObviousUrls(item.Name)}<img src=\"{GlobalConfig.Hostname}/gefesleff.png\" width=\"15px\" height=\"15px\" onclick=\"copyToClipboard({item.Id});\"></div>");
            sb.AppendLine($"<div class=\"itemrow\" id=\"{item.Id}\">");

            sb.AppendLine("<div class=\"commentcell\">");
            if (item.Comment != null)
            {
                var itemmd = Markdown.ToHtml(item.Comment, listMarkdownPipeline);
                sb.AppendLine("<div class=\"item-body-pane\">");
                sb.AppendLine(itemmd);
                sb.AppendLine("</div>");
            }
            else
            {
                sb.AppendLine("<div class=\"item-body-pane\"></div>");
            }

            int localItemLikes = localLikeCountByItem.TryGetValue(item.Id, out int itemLikeCount) ? itemLikeCount : 0;
            var itemLikeActors = localLikeActorsByItem.TryGetValue(item.Id, out var actors) ? actors : null;
            sb.AppendLine("<div class=\"itemsourcefooter\">");

            string? itemActorIri = string.IsNullOrWhiteSpace(item.OriginatorActorIri) ? null : item.OriginatorActorIri.Trim();
            string? itemAttributedIri = string.IsNullOrWhiteSpace(item.SourceAttributedToIri) ? null : item.SourceAttributedToIri.Trim();
            bool itemHasActor = !string.IsNullOrWhiteSpace(itemActorIri);
            bool itemHasAttributed = !string.IsNullOrWhiteSpace(itemAttributedIri);
            bool itemSameActorAndAttributed = itemHasActor
                && itemHasAttributed
                && string.Equals(itemActorIri, itemAttributedIri, StringComparison.OrdinalIgnoreCase);

            string? itemFromIri = itemHasActor ? itemActorIri : itemAttributedIri;
            if (!string.IsNullOrWhiteSpace(itemFromIri))
            {
                sb.AppendLine($"<div class=\"itemsourcefrom\"><strong>From:</strong> {RenderExternalLink(itemFromIri, ResolveActorDisplay(itemFromIri))}</div>");
            }

            if (itemHasActor && itemHasAttributed && !itemSameActorAndAttributed)
            {
                sb.AppendLine($"<div class=\"itemsourceattributed\"><strong>Attributed:</strong> {RenderExternalLink(itemAttributedIri, ResolveActorDisplay(itemAttributedIri))}</div>");
            }

            if (!string.IsNullOrWhiteSpace(item.SourceObjectIri))
            {
                sb.AppendLine($"<div class=\"itemsourceobject\">{RenderSourceLinkInline(item.SourceObjectIri)}</div>");
            }

            sb.AppendLine($"<div class=\"itemlikes\">{RenderLikeDisclosureInline(localItemLikes, itemLikeActors)}</div>");
            sb.AppendLine("</div>");

            if (topLevelCommentsByItem.TryGetValue(item.Id, out var topComments) && topComments.Count > 0)
            {
                int totalThreadComments = commentCountByItem.TryGetValue(item.Id, out int count)
                    ? count
                    : topComments.Count;
                sb.AppendLine("<details class=\"ap-item-comments\">");
                sb.AppendLine($"<summary class=\"ap-item-comments-title\">Comments ({totalThreadComments})</summary>");
                sb.AppendLine("<div class=\"ap-item-comments-content\">");
                foreach (var topComment in topComments)
                {
                    RenderCommentTree(sb, topComment, 0);
                }

                sb.AppendLine("</div>");
                sb.AppendLine("</details>");
            }

            sb.AppendLine("</div>");
            sb.AppendLine("<div class=\"tagscell\">");
            foreach (var tag in item.Tags)
            {
                if (!string.IsNullOrWhiteSpace(tag))
                {
                    sb.AppendLine($"<span class=\"tag\">{tag.Trim()}</span>");
                }
            }

            sb.AppendLine("</div>");
            sb.AppendLine("<div class=\"utilitybox\">");
            sb.AppendLine($"<span class=\"itemmoddate\">{item.ModifiedDate:yyyy-MM-dd HH:mm:ss}</span>");
            sb.AppendLine("<span class=\"moveitemlink\" style=\"display: none;\"><a href=\"#\" oncontextmenu=\"showContextMenu(event)\">Move</a></span>");
            sb.AppendLine($"<span class=\"itemeditlink\" style=\"display: none;\"><a href=\"_edit.item.html?listid={item.ListId}&itemid={item.Id}\" >Edit</a></span>");
            sb.AppendLine($"<span class=\"itemdeletelink\" style=\"display: none;\"><a href=\"#\" onclick=\"deleteItem({item.ListId},{item.Id}); return;\" >Delete</a></span>");
            sb.AppendLine($"<span class=\"itemreportlink\"><a href=\"#\" onclick=\"reportItem({item.ListId},{item.Id}); return;\" >Report</a></span>");
            sb.AppendLine("</div>");

            sb.AppendLine("</div>");
        }

        sb.AppendLine("</div>");
        sb.AppendLine("<script src=\"_utils.js\"></script>");
        sb.AppendLine("<script src=\"_list_view.js\"></script>");
        sb.AppendLine("<script src=\"_modal.mastodon.js\"></script>");
        sb.AppendLine("<script src=\"_modal.google.js\"></script>");
        sb.AppendLine("<script src=\"_modal.report.item.js\"></script>");

        if (movedItems.Count > 0)
        {
            string movedMapJson = System.Text.Json.JsonSerializer.Serialize(
                movedItems.ToDictionary(m => m.OldId.ToString(), m => m.NewId.ToString()));
            sb.AppendLine("<script>");
            sb.AppendLine($"const movedItemMap = {movedMapJson};");
            sb.AppendLine("if (window.location.hash) {");
            sb.AppendLine("  const hashId = window.location.hash.substring(1);");
            sb.AppendLine("  const redirectedId = movedItemMap[hashId];");
            sb.AppendLine("  if (redirectedId) {");
            sb.AppendLine("    window.location.replace(`${window.location.pathname}#${redirectedId}`);");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            sb.AppendLine("</script>");
        }

        renderStopwatch.Stop();
        string pageFileName = $"{Name}.html";
        string renderDurationLabel = $"{renderStopwatch.Elapsed.TotalMilliseconds:F2} ms";
        sb.AppendLine($"<div class=\"byline\">Render time for {WebUtility.HtmlEncode(pageFileName)}: {WebUtility.HtmlEncode(renderDurationLabel)}</div>");

        await GlobalStatic.GeneratePageFooter(sb);
        DBg.d(LogLevel.Trace, $"Writing to {dest}");
        await File.WriteAllTextAsync(dest, sb.ToString());
        DBg.d(LogLevel.Information, $"Rendered list HTML page {pageFileName} in {renderStopwatch.Elapsed.TotalMilliseconds:F2} ms");
    }

    public async Task RefreshRemoteCommentLikesForItemAsync(int itemId, GeFeSLEDb db)
    {
        string fn = $"RefreshRemoteCommentLikesForItemAsync(list:{Id}, item:{itemId})";
        var comments = await db.ItemComments
            .Where(c => c.ListId == Id && c.ItemId == itemId)
            .ToListAsync();
        if (comments.Count == 0)
        {
            DBg.d(LogLevel.Trace, $"{fn} -- no comments found to poll for remote likes/unlikes");
            return;
        }

        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(5);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/activity+json");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\"");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{GlobalStatic.applicationName}/{GlobalConfig.bldVersion}");

        foreach (var comment in comments)
        {
            string? likesEndpoint = ResolveCommentLikesEndpoint(comment);
            if (string.IsNullOrWhiteSpace(likesEndpoint))
            {
                DBg.d(LogLevel.Trace, $"{fn} -- comment {comment.Id}: no remote likes endpoint resolved; skipping");
                continue;
            }

            DBg.d(LogLevel.Trace, $"{fn} -- querying remote likes for comment {comment.Id} from {likesEndpoint}");
            ActivityPubLikeCollectionSnapshot? likesSnapshot = await TryFetchActivityPubLikesSnapshotAsync(client, likesEndpoint, $"comment:{comment.Id}");
            if (likesSnapshot is null)
            {
                DBg.d(LogLevel.Warning, $"{fn} -- comment {comment.Id}: failed remote likes query at {likesEndpoint}");
                continue;
            }

            var mergedActors = new HashSet<string>(likesSnapshot.ActorIris, StringComparer.OrdinalIgnoreCase);

            string? mastodonFavouritedByEndpoint = TryResolveMastodonFavouritedByEndpoint(comment);
            if (!string.IsNullOrWhiteSpace(mastodonFavouritedByEndpoint))
            {
                DBg.d(LogLevel.Trace,
                    $"{fn} -- comment {comment.Id}: querying Mastodon favourited_by endpoint {mastodonFavouritedByEndpoint}");
                List<string>? mastodonActors = await TryFetchMastodonFavouritedByActorsAsync(
                    client,
                    mastodonFavouritedByEndpoint,
                    $"comment:{comment.Id}");
                if (mastodonActors is not null)
                {
                    foreach (var actor in mastodonActors)
                    {
                        mergedActors.Add(actor);
                    }

                    DBg.d(LogLevel.Trace,
                        $"{fn} -- comment {comment.Id}: Mastodon favourited_by returned {mastodonActors.Count} actor(s)");
                }
            }

            int mergedActorCount = mergedActors.Count;
            int derivedCount = likesSnapshot.TotalItems ?? mergedActorCount;
            derivedCount = Math.Max(derivedCount, mergedActorCount);
            comment.RemoteLikesCount = Math.Max(derivedCount, 0);
            comment.RemoteLikeActorsJson = JsonSerializer.Serialize(
                mergedActors.OrderBy(actor => actor, StringComparer.OrdinalIgnoreCase).ToList());
            comment.RemoteLikesLastCheckedAt = DateTimeOffset.UtcNow;
            comment.ModifiedDate = DateTime.UtcNow;
            DBg.d(LogLevel.Information,
                $"{fn} -- comment {comment.Id}: remote likes sync complete count={comment.RemoteLikesCount}, actors={mergedActorCount}, endpoint={likesEndpoint}");
        }

        await db.SaveChangesAsync();
        DBg.d(LogLevel.Trace, $"{fn} -- persisted remote likes/unlikes polling results");
    }

    public async Task RefreshRemoteCommentLikesForAllItemsAsync(GeFeSLEDb db)
    {
        var itemIds = await db.ItemComments
            .Where(c => c.ListId == Id)
            .Select(c => c.ItemId)
            .Distinct()
            .ToListAsync();

        foreach (int itemId in itemIds)
        {
            await RefreshRemoteCommentLikesForItemAsync(itemId, db);
        }
    }

    private static string? ResolveCommentLikesEndpoint(GeListItemComment comment)
    {
        if (!string.IsNullOrWhiteSpace(comment.RawNoteJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(comment.RawNoteJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("likes", out var likesProp))
                {
                    string? fromObject = ActivityPubPayloadFactory.ReadIriFromActivityPubNode(likesProp);
                    if (!string.IsNullOrWhiteSpace(fromObject))
                    {
                        return fromObject;
                    }
                }
            }
            catch
            {
                // Ignore malformed cached JSON and fall back to conventional /likes endpoint.
            }
        }

        if (string.IsNullOrWhiteSpace(comment.RemoteObjectIri))
        {
            return null;
        }

        return $"{comment.RemoteObjectIri.TrimEnd('/')}/likes";
    }

    private static string? TryResolveMastodonFavouritedByEndpoint(GeListItemComment comment)
    {
        if (string.IsNullOrWhiteSpace(comment.RemoteObjectIri)
            || !Uri.TryCreate(comment.RemoteObjectIri, UriKind.Absolute, out var remoteObjectUri))
        {
            return null;
        }

        string[] segments = remoteObjectUri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int statusesIndex = Array.FindLastIndex(segments, s => string.Equals(s, "statuses", StringComparison.OrdinalIgnoreCase));
        if (statusesIndex < 0 || statusesIndex + 1 >= segments.Length)
        {
            return null;
        }

        string statusId = segments[statusesIndex + 1].Trim();
        if (string.IsNullOrWhiteSpace(statusId))
        {
            return null;
        }

        return $"{remoteObjectUri.Scheme}://{remoteObjectUri.Authority}/api/v1/statuses/{statusId}/favourited_by";
    }

    private static async Task<List<string>?> TryFetchMastodonFavouritedByActorsAsync(HttpClient client, string endpoint, string? contextLabel = null)
    {
        string context = string.IsNullOrWhiteSpace(contextLabel) ? "unknown" : contextLabel;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri))
        {
            DBg.d(LogLevel.Warning, $"TryFetchMastodonFavouritedByActorsAsync({context}) -- invalid endpoint URI: {endpoint}");
            return null;
        }

        try
        {
            using var response = await client.GetAsync(endpointUri);
            if (!response.IsSuccessStatusCode)
            {
                DBg.d(LogLevel.Warning,
                    $"TryFetchMastodonFavouritedByActorsAsync({context}) -- request failed {(int)response.StatusCode} {response.StatusCode} for {endpointUri}");
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var actors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var account in doc.RootElement.EnumerateArray())
            {
                if (account.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string? actorIri = null;
                if (account.TryGetProperty("url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String)
                {
                    actorIri = urlProp.GetString();
                }

                actorIri ??= ReadIriPropertyFromObject(account, "uri");

                if (string.IsNullOrWhiteSpace(actorIri)
                    && account.TryGetProperty("acct", out var acctProp)
                    && acctProp.ValueKind == JsonValueKind.String)
                {
                    string? acct = acctProp.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(acct))
                    {
                        string normalizedAcct = acct.Contains('@', StringComparison.Ordinal)
                            ? acct
                            : $"{acct}@{endpointUri.Host}";
                        actorIri = $"acct:{normalizedAcct}";
                    }
                }

                if (!string.IsNullOrWhiteSpace(actorIri))
                {
                    actors.Add(actorIri.Trim());
                }
            }

            return actors.OrderBy(actor => actor, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch
        {
            DBg.d(LogLevel.Warning,
                $"TryFetchMastodonFavouritedByActorsAsync({context}) -- exception while querying {endpoint}");
            return null;
        }
    }

    private static async Task<ActivityPubLikeCollectionSnapshot?> TryFetchActivityPubLikesSnapshotAsync(HttpClient client, string likesEndpoint, string? contextLabel = null)
    {
        string context = string.IsNullOrWhiteSpace(contextLabel) ? "unknown" : contextLabel;
        if (!Uri.TryCreate(likesEndpoint, UriKind.Absolute, out var likesUri))
        {
            DBg.d(LogLevel.Warning, $"TryFetchActivityPubLikesSnapshotAsync({context}) -- invalid likes endpoint URI: {likesEndpoint}");
            return null;
        }

        try
        {
            DBg.d(LogLevel.Trace, $"TryFetchActivityPubLikesSnapshotAsync({context}) -- GET {likesUri}");
            using var response = await client.GetAsync(likesUri);
            if (!response.IsSuccessStatusCode)
            {
                DBg.d(LogLevel.Warning,
                    $"TryFetchActivityPubLikesSnapshotAsync({context}) -- request failed {(int)response.StatusCode} {response.StatusCode} for {likesUri}");
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var actors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectActorIrisFromCollectionNode(doc.RootElement, actors);

            if (doc.RootElement.TryGetProperty("first", out var firstProp)
                && firstProp.ValueKind == JsonValueKind.Object)
            {
                CollectActorIrisFromCollectionNode(firstProp, actors);
            }

            string? firstPageIri = TryReadCollectionPageIri(doc.RootElement, "first");
            if (!string.IsNullOrWhiteSpace(firstPageIri)
                && Uri.TryCreate(firstPageIri, UriKind.Absolute, out var firstPageUri))
            {
                try
                {
                    DBg.d(LogLevel.Trace, $"TryFetchActivityPubLikesSnapshotAsync({context}) -- following collection page {firstPageUri}");
                    using var firstPageResponse = await client.GetAsync(firstPageUri);
                    if (firstPageResponse.IsSuccessStatusCode)
                    {
                        await using var firstPageStream = await firstPageResponse.Content.ReadAsStreamAsync();
                        using var firstPageDoc = await JsonDocument.ParseAsync(firstPageStream);
                        if (firstPageDoc.RootElement.ValueKind == JsonValueKind.Object)
                        {
                            CollectActorIrisFromCollectionNode(firstPageDoc.RootElement, actors);
                        }
                    }
                    else
                    {
                        DBg.d(LogLevel.Warning,
                            $"TryFetchActivityPubLikesSnapshotAsync({context}) -- first page request failed {(int)firstPageResponse.StatusCode} {firstPageResponse.StatusCode} for {firstPageUri}");
                    }
                }
                catch
                {
                    // Ignore first-page fetch errors and keep any actors we already captured.
                    DBg.d(LogLevel.Warning,
                        $"TryFetchActivityPubLikesSnapshotAsync({context}) -- exception while fetching first page {firstPageUri}");
                }
            }

            int? totalItems = null;
            if (TryGetTotalItems(doc.RootElement, out int parsedTotalItems))
            {
                totalItems = Math.Max(parsedTotalItems, 0);
            }

            if (!totalItems.HasValue
                && doc.RootElement.TryGetProperty("first", out var embeddedFirst)
                && embeddedFirst.ValueKind == JsonValueKind.Object
                && TryGetTotalItems(embeddedFirst, out int firstTotalItems))
            {
                totalItems = Math.Max(firstTotalItems, 0);
            }

            var snapshot = new ActivityPubLikeCollectionSnapshot(totalItems, actors.ToList());
            DBg.d(LogLevel.Trace,
                $"TryFetchActivityPubLikesSnapshotAsync({context}) -- extracted totalItems={(snapshot.TotalItems.HasValue ? snapshot.TotalItems.Value.ToString() : "<null>")}, actors={snapshot.ActorIris.Count}");
            return snapshot;
        }
        catch
        {
            DBg.d(LogLevel.Warning, $"TryFetchActivityPubLikesSnapshotAsync({context}) -- exception while querying {likesEndpoint}");
            return null;
        }
    }

    private static void CollectActorIrisFromCollectionNode(JsonElement collectionNode, HashSet<string> actorIris)
    {
        if (collectionNode.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (collectionNode.TryGetProperty("items", out var itemsProp)
            && itemsProp.ValueKind == JsonValueKind.Array)
        {
            CollectActorIrisFromItemsArray(itemsProp, actorIris);
        }

        if (collectionNode.TryGetProperty("orderedItems", out var orderedItemsProp)
            && orderedItemsProp.ValueKind == JsonValueKind.Array)
        {
            CollectActorIrisFromItemsArray(orderedItemsProp, actorIris);
        }
    }

    private static void CollectActorIrisFromItemsArray(JsonElement itemsArray, HashSet<string> actorIris)
    {
        foreach (var item in itemsArray.EnumerateArray())
        {
            switch (item.ValueKind)
            {
                case JsonValueKind.String:
                {
                    string? asIri = item.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(asIri))
                    {
                        actorIris.Add(asIri);
                    }

                    break;
                }
                case JsonValueKind.Object:
                {
                    string? actor = null;
                    if (item.TryGetProperty("actor", out var actorProp))
                    {
                        actor = ActivityPubPayloadFactory.ReadIriFromActivityPubNode(actorProp);
                    }

                    actor ??= ReadIriPropertyFromObject(item, "attributedTo");
                    actor ??= ReadIriPropertyFromObject(item, "attributedto");
                    actor ??= ReadIriPropertyFromObject(item, "id");

                    if (!string.IsNullOrWhiteSpace(actor))
                    {
                        actorIris.Add(actor.Trim());
                    }

                    break;
                }
            }
        }
    }

    private static string? ReadIriPropertyFromObject(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var prop))
        {
            return null;
        }

        return ActivityPubPayloadFactory.ReadIriFromActivityPubNode(prop);
    }

    private static string? TryReadCollectionPageIri(JsonElement collectionNode, string propertyName)
    {
        if (!collectionNode.TryGetProperty(propertyName, out var prop))
        {
            return null;
        }

        return ActivityPubPayloadFactory.ReadIriFromActivityPubNode(prop);
    }

    private static bool TryGetTotalItems(JsonElement root, out int totalItems)
    {
        totalItems = 0;
        if (!root.TryGetProperty("totalItems", out var totalItemsProp))
        {
            return false;
        }

        if (totalItemsProp.ValueKind == JsonValueKind.Number && totalItemsProp.TryGetInt32(out int numeric))
        {
            totalItems = numeric;
            return true;
        }

        if (totalItemsProp.ValueKind == JsonValueKind.String
            && int.TryParse(totalItemsProp.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            totalItems = parsed;
            return true;
        }

        return false;
    }

    private sealed class ActivityPubLikeCollectionSnapshot
    {
        public ActivityPubLikeCollectionSnapshot(int? totalItems, List<string> actorIris)
        {
            TotalItems = totalItems;
            ActorIris = actorIris;
        }

        public int? TotalItems { get; }
        public List<string> ActorIris { get; }
    }
    public async Task GenerateRSSFeed(GeFeSLEDb db)
    {
        DBg.d(LogLevel.Trace, $"GenerateRssFeed {Id}");
        // create new database context

        var items = await db.Items.Where(item => item.ListId == Id && item.Visible && !item.IsDeleted).ToListAsync();
        // if there are no items then return
        if (items.Count == 0) return;
        var list = await db.Lists.FindAsync(Id);
        if (list is null) return;
        else
        {
            var feed = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("rss",
                    new XAttribute("version", "2.0"),
                    new XElement("channel",
                        new XElement("title", $"{Name}"),
                        new XElement("link", $"http://{GlobalConfig.Hostname}/rss/{Id}"),
                        new XElement("description", $"{Comment}"),
                        items.Select(item =>
                            new XElement("item",
                                new XElement("title", item.Name),
                                new XElement("link", $"http://{GlobalConfig.Hostname}/showitems/{Id}/{item.Id}"),
                                new XElement("description", item.Comment),
                                new XElement("pubDate", item.CreatedDate.ToString("r"))
                            )
                        )
                    )
                )
            );
            var filename = $"rss-{list.Name}.xml";

            var dest = Path.Combine(GlobalConfig.wwwroot!, filename);
            await File.WriteAllTextAsync(dest, feed.ToString());

            var link = $"/{Path.GetFileName(dest)}";
            DBg.d(LogLevel.Trace, $"RSS feed generated. Link: {link}");

        }



    }

    public async Task GenerateJSON(GeFeSLEDb db)
    {
        DBg.d(LogLevel.Trace, $"GenerateJSON {Id}");
        var items = await db.Items.Where(item => item.ListId == Id && item.Visible && !item.IsDeleted).ToListAsync();
        if (items.Count == 0) return;
        var list = await db.Lists.FindAsync(Id);
        if (list is null) return;
        else
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true // Set WriteIndented to true for pretty formatting
            };
            var json = JsonSerializer.Serialize(items);
            var filename = $"{list.Name}.json";
            var dest = Path.Combine(GlobalConfig.wwwroot!, filename);
            await File.WriteAllTextAsync(dest, json);
            var link = $"/{Path.GetFileName(dest)}";
            DBg.d(LogLevel.Trace, $"JSON generated. Link: {link}");
        }
    }

    public (bool, string?) IsUserAllowedToView(GeFeSLEUser? user)
    {
        DBg.d(LogLevel.Trace, $"{(user?.UserName) ?? "anonymous"} ? {Name}");
        string? ynot = null;
        bool allowed = false;
        switch (Visibility)
        {
            case GeListVisibility.Contributors:
                if(user is null) { ynot = "Related list is CONTRIB access. User is null."; break; }
                if (Contributors.Contains(user) || ListOwners.Contains(user) || Creator == user)
                {
                    allowed = true;
                    ynot = $"Related list is CONTRIB access. User is a contributor/owner/creator.";
                }
                else
                {
                    ynot = $"User {user.UserName} is not a contributor/list owner/creator of list {Name}";
                }
                break;
            case GeListVisibility.ListOwners:
                if(user is null) { ynot = "Related list is OWNER access. User is null."; break; }
                if (ListOwners.Contains(user) || Creator == user)
                {
                    allowed = true;
                    ynot = $"Related list is OWNER access. User is a list owner/creator.";
                }
                else
                {
                    ynot = $"User {user.UserName} is not a list owner/creator of list {Name}";
                }
                break;
            case GeListVisibility.Private:
                if(user is null) { ynot = "Related list is PRIVATE access. User is null."; break; }
                if (Creator == user)
                {
                    ynot = $"Related list is PRIVATE access. User is the creator.";
                    allowed = true;
                }
                else
                {
                    ynot = $"User {user.UserName} is not the creator of list {Name}";
                }
                break;
            case GeListVisibility.Public:
                {
                    ynot = "List is public";
                    allowed = true;
                    break;
                }

        }
        DBg.d(LogLevel.Debug, $"{(user?.UserName) ?? "anonymous"} allowed: {allowed} - {ynot}");
        return (allowed, ynot);

    }

    // Who can MODIFY a LIST or ITEMS IN IT? 
    // 0. SuperUser - ALWAYS // though this fn does not check for that - CALLER MUST CHECK
    // 1. Creator of the list - ALWAYS
    // 2. List Owners - if they're in the ListOwners list (regardless of what role they have)
    // 3. Contributors - if they're in the Contributors list (regardless of what role they have)
    // 4. anonymous - never // this fn will never see that, only way to get that is OAuth login
    //    where OAuth user is not also in the db
    // note that this is independent of the visibility of the list

    public (bool, string?) IsUserAllowedToModify(GeFeSLEUser user)
    {
        DBg.d(LogLevel.Trace, $"{user.UserName} ? {Name}");
        string? ynot = null;
        bool allowed = false;
        DBg.d(LogLevel.Trace, $"ListOwners: {string.Join(", ", ListOwners.Select(u => u.UserName))}");
        DBg.d(LogLevel.Trace, $"Contributors: {string.Join(", ", Contributors.Select(u => u.UserName))}");
        DBg.d(LogLevel.Trace, $"Creator: {Creator?.UserName}");
        if (Contributors.Contains(user) || ListOwners.Contains(user) || Creator == user)
        {
            allowed = true;
            ynot = $"User {user.UserName} is a contributor/owner/creator of list {Name}.";
        }
        else
        {
            ynot = $"User {user.UserName} is not a contributor/list owner/creator of list {Name}";
        }

        DBg.d(LogLevel.Debug, $"{user.UserName} allowed: {allowed} - {ynot}");
        return (allowed, ynot);

    }

    // Method to regenerate all files for this list (HTML, RSS, JSON) and update the index
    public async Task RegenerateAllFiles(GeFeSLEDb db)
    {
        DBg.d(LogLevel.Trace, $"RegenerateAllFiles: {Id} - {Name}");

        await RefreshRemoteCommentLikesForAllItemsAsync(db);
        
        // Generate all list files
        await GenerateHTMLListPage(db);
        await GenerateRSSFeed(db);
        await GenerateJSON(db);
        
        // Regenerate the index since list content has changed
        await GlobalStatic.GenerateHTMLListIndex(db);
        
        DBg.d(LogLevel.Trace, $"RegenerateAllFiles completed for: {Id} - {Name}");
    }

    // in other end points where we query for items that beling in a list, we're essentially doing
    // db.Items.Where(item => item.ListId == listId).ToListAsync();
    // HOWEVER - this presumes we don't really care about the order in which items are returned. 
    // since we're going to introduce different list types soon, lets abstract that through
    // a GetItems function. 
    public async Task<List<GeListItem>> GetItems(GeFeSLEDb db)
    {
        DBg.d(LogLevel.Trace, $"GetItems for list {Id} - {Name}");
        // for now, we will just return items ordered by CreatedDate desc (newest first)
        // in the future, we can modify this to return items in a different order based on the list type
        var items = await db.Items.Where(item => item.ListId == Id && item.Visible && !item.IsDeleted).OrderByDescending(item => item.CreatedDate).ToListAsync();
        DBg.d(LogLevel.Trace, $"Found {items.Count} items for list {Id} - {Name}");
        return items;
    }


}
