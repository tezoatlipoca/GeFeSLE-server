using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Markdig;

public static class ActivityPubPayloadFactory
{
    public static string? ReadIriFromActivityPubNode(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.String)
        {
            return node.GetString();
        }

        if (node.ValueKind == JsonValueKind.Object)
        {
            if (node.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
            {
                return idProp.GetString();
            }

            if (node.TryGetProperty("iri", out var iriProp) && iriProp.ValueKind == JsonValueKind.String)
            {
                return iriProp.GetString();
            }
        }

        return null;
    }

    public static Dictionary<string, object?> BuildActivityPubItemNote(GeList list, GeListItem item, MarkdownPipeline markdownPipeline)
    {
        static string NormalizeHashtag(string rawTag)
        {
            return string.Concat(rawTag.Trim().TrimStart('#').Where(c => !char.IsWhiteSpace(c)));
        }

        static string GuessMentionHref(string username, string domain)
        {
            return $"https://{domain}/@{username}";
        }

        static bool IsLikelyEmailDomain(string domain)
        {
            return domain.Equals("gmail.com", StringComparison.OrdinalIgnoreCase)
                || domain.Equals("outlook.com", StringComparison.OrdinalIgnoreCase)
                || domain.Equals("hotmail.com", StringComparison.OrdinalIgnoreCase)
                || domain.Equals("yahoo.com", StringComparison.OrdinalIgnoreCase)
                || domain.Equals("icloud.com", StringComparison.OrdinalIgnoreCase)
                || domain.Equals("proton.me", StringComparison.OrdinalIgnoreCase)
                || domain.Equals("protonmail.com", StringComparison.OrdinalIgnoreCase);
        }

        static Dictionary<string, object?>? BuildMentionTagFromActorIri(string? actorIri)
        {
            if (string.IsNullOrWhiteSpace(actorIri))
            {
                return null;
            }

            if (!Uri.TryCreate(actorIri.Trim(), UriKind.Absolute, out var actorUri))
            {
                return null;
            }

            string[] pathParts = actorUri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (pathParts.Length == 0)
            {
                return null;
            }

            string? username = null;
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

                username = candidate;
                break;
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                return null;
            }

            string handle = $"@{username}@{actorUri.Host}";
            return new Dictionary<string, object?>
            {
                ["type"] = "Mention",
                ["name"] = handle,
                ["href"] = actorIri.Trim()
            };
        }

        List<Dictionary<string, object?>> BuildActivityPubTagObjects(
            IEnumerable<string?> sourceTexts,
            IEnumerable<string>? extraHashtags = null,
            IEnumerable<string?>? extraActorMentions = null)
        {
            var tags = new List<Dictionary<string, object?>>();
            var seenMentions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenHashtags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var mentionRegex = new Regex(@"(?<![\w/])@(?<user>[A-Za-z0-9_]+)@(?<domain>[A-Za-z0-9.-]+\.[A-Za-z]{2,})(?![\w@-])", RegexOptions.Compiled);
            var bareHandleRegex = new Regex(@"(?<![\w/@])(?<user>[A-Za-z0-9_]+)@(?<domain>[A-Za-z0-9.-]+\.[A-Za-z]{2,})(?![\w@-])", RegexOptions.Compiled);
            var emailRegex = new Regex(@"(?<![\w/@])(?<email>[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,})(?![\w@-])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var hashtagRegex = new Regex(@"(?<![\w&])#(?<tag>[A-Za-z0-9_]+)", RegexOptions.Compiled);
            var linkRegex = new Regex(@"(?<url>https?://[^\s<""')]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            foreach (var text in sourceTexts.Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                foreach (Match match in mentionRegex.Matches(text!))
                {
                    string username = match.Groups["user"].Value;
                    string domain = match.Groups["domain"].Value;
                    string handle = $"@{username}@{domain}";
                    if (seenMentions.Add(handle))
                    {
                        tags.Add(new Dictionary<string, object?>
                        {
                            ["type"] = "Mention",
                            ["name"] = handle,
                            ["href"] = GuessMentionHref(username, domain)
                        });
                    }
                }

                foreach (Match match in bareHandleRegex.Matches(text!))
                {
                    string username = match.Groups["user"].Value;
                    string domain = match.Groups["domain"].Value;
                    if (IsLikelyEmailDomain(domain))
                    {
                        continue;
                    }

                    string handle = $"@{username}@{domain}";
                    if (seenMentions.Add(handle))
                    {
                        tags.Add(new Dictionary<string, object?>
                        {
                            ["type"] = "Mention",
                            ["name"] = handle,
                            ["href"] = GuessMentionHref(username, domain)
                        });
                    }
                }

                foreach (Match match in emailRegex.Matches(text!))
                {
                    string email = match.Groups["email"].Value;
                    if (!string.IsNullOrWhiteSpace(email) && seenLinks.Add($"mailto:{email}"))
                    {
                        tags.Add(new Dictionary<string, object?>
                        {
                            ["type"] = "Link",
                            ["name"] = email,
                            ["href"] = $"mailto:{email}"
                        });
                    }
                }

                foreach (Match match in hashtagRegex.Matches(text!))
                {
                    string tag = NormalizeHashtag(match.Groups["tag"].Value);
                    if (!string.IsNullOrWhiteSpace(tag) && seenHashtags.Add(tag))
                    {
                        tags.Add(new Dictionary<string, object?>
                        {
                            ["type"] = "Hashtag",
                            ["name"] = $"#{tag}"
                        });
                    }
                }

                foreach (Match match in linkRegex.Matches(text!))
                {
                    string url = match.Groups["url"].Value;
                    if (!string.IsNullOrWhiteSpace(url) && seenLinks.Add(url))
                    {
                        tags.Add(new Dictionary<string, object?>
                        {
                            ["type"] = "Link",
                            ["name"] = url,
                            ["href"] = url
                        });
                    }
                }
            }

            if (extraHashtags is not null)
            {
                foreach (var rawTag in extraHashtags.Where(t => !string.IsNullOrWhiteSpace(t)))
                {
                    string tag = NormalizeHashtag(rawTag!);
                    if (!string.IsNullOrWhiteSpace(tag) && seenHashtags.Add(tag))
                    {
                        tags.Add(new Dictionary<string, object?>
                        {
                            ["type"] = "Hashtag",
                            ["name"] = $"#{tag}"
                        });
                    }
                }
            }

            if (extraActorMentions is not null)
            {
                foreach (var actorIri in extraActorMentions)
                {
                    var mentionTag = BuildMentionTagFromActorIri(actorIri);
                    if (mentionTag is null)
                    {
                        continue;
                    }

                    string? handle = mentionTag.TryGetValue("name", out var nameObj) ? nameObj as string : null;
                    if (string.IsNullOrWhiteSpace(handle) || !seenMentions.Add(handle))
                    {
                        continue;
                    }

                    tags.Add(mentionTag);
                }
            }

            return tags;
        }

        string LinkifyFediverseMentionsInHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return html;
            }

            var mentionRegex = new Regex(@"(?<![\w""'=/])@(?<user>[A-Za-z0-9_]+)@(?<domain>[A-Za-z0-9.-]+\.[A-Za-z]{2,})(?![\w@-])", RegexOptions.Compiled);
            return mentionRegex.Replace(html, match =>
            {
                string username = match.Groups["user"].Value;
                string domain = match.Groups["domain"].Value;
                string href = GuessMentionHref(username, domain);
                return $"<span class=\"h-card\"><a href=\"{WebUtility.HtmlEncode(href)}\" class=\"u-url mention\" rel=\"nofollow noopener noreferrer\">@<span>{WebUtility.HtmlEncode(username)}</span></a></span>";
            });
        }

        static string ResolveImageUrl(string rawUrl, string hostBase)
        {
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                return string.Empty;
            }

            string trimmed = rawUrl.Trim();
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
            {
                return absoluteUri.ToString();
            }

            if (trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                return $"https:{trimmed}";
            }

            if (trimmed.StartsWith("/", StringComparison.Ordinal))
            {
                return $"{hostBase}{trimmed}";
            }

            return $"{hostBase}/{trimmed.TrimStart('/')}";
        }

        static string GuessImageMediaType(string imageUrl)
        {
            if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
            {
                return "image/*";
            }

            string ext = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
            return ext switch
            {
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".avif" => "image/avif",
                ".svg" => "image/svg+xml",
                _ => "image/*"
            };
        }

        static (string SanitizedComment, List<Dictionary<string, object?>> Attachments) ExtractActivityPubImageAttachments(string? comment, string hostBase)
        {
            string working = comment ?? string.Empty;
            var attachments = new List<Dictionary<string, object?>>();
            var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var htmlImgRegex = new Regex(@"<img\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            var htmlSrcRegex = new Regex(@"src\s*=\s*[\""'](?<url>[^\""']+)[\""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            var htmlAltRegex = new Regex(@"alt\s*=\s*[\""'](?<alt>[^\""']*)[\""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);

            working = htmlImgRegex.Replace(working, match =>
            {
                string tag = match.Value;
                var srcMatch = htmlSrcRegex.Match(tag);
                if (!srcMatch.Success)
                {
                    return string.Empty;
                }

                string resolvedUrl = ResolveImageUrl(srcMatch.Groups["url"].Value, hostBase);
                if (string.IsNullOrWhiteSpace(resolvedUrl) || !seenUrls.Add(resolvedUrl))
                {
                    return string.Empty;
                }

                string altText = string.Empty;
                var altMatch = htmlAltRegex.Match(tag);
                if (altMatch.Success)
                {
                    altText = altMatch.Groups["alt"].Value.Trim();
                }

                var attachment = new Dictionary<string, object?>
                {
                    ["type"] = "Document",
                    ["mediaType"] = GuessImageMediaType(resolvedUrl),
                    ["url"] = resolvedUrl
                };
                if (!string.IsNullOrWhiteSpace(altText))
                {
                    attachment["name"] = altText;
                }
                attachments.Add(attachment);
                return string.Empty;
            });

            var markdownImgRegex = new Regex(@"!\[(?<alt>[^\]]*)\]\((?<url>[^\)\s]+)(?:\s+\""[^\""\)]*\"")?\)", RegexOptions.Compiled);
            working = markdownImgRegex.Replace(working, match =>
            {
                string resolvedUrl = ResolveImageUrl(match.Groups["url"].Value, hostBase);
                if (string.IsNullOrWhiteSpace(resolvedUrl) || !seenUrls.Add(resolvedUrl))
                {
                    return string.Empty;
                }

                string altText = match.Groups["alt"].Value.Trim();
                var attachment = new Dictionary<string, object?>
                {
                    ["type"] = "Document",
                    ["mediaType"] = GuessImageMediaType(resolvedUrl),
                    ["url"] = resolvedUrl
                };
                if (!string.IsNullOrWhiteSpace(altText))
                {
                    attachment["name"] = altText;
                }
                attachments.Add(attachment);
                return string.Empty;
            });

            return (working.Trim(), attachments);
        }

        string hostBase = (GlobalConfig.Hostname ?? string.Empty).TrimEnd('/');
        var extractedMedia = ExtractActivityPubImageAttachments(item.Comment, hostBase);
        string sanitizedComment = extractedMedia.SanitizedComment;
        var noteAttachments = extractedMedia.Attachments;

        string? hashtagLine = null;
        var renderedHashtags = item.Tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => NormalizeHashtag(tag))
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => $"#{tag}")
            .ToList();
        if (renderedHashtags.Count > 0)
        {
            hashtagLine = string.Join(" ", renderedHashtags);
        }

        var contentMarkdownParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.Name))
        {
            contentMarkdownParts.Add(item.Name.Trim());
        }
        if (!string.IsNullOrWhiteSpace(sanitizedComment))
        {
            contentMarkdownParts.Add(sanitizedComment);
        }
        if (!string.IsNullOrWhiteSpace(hashtagLine))
        {
            contentMarkdownParts.Add(hashtagLine);
        }

        string listFileName = $"{list.Name}.html";
        string staticItemUrl = $"{hostBase}/{Uri.EscapeDataString(listFileName)}#{item.Id}";
        string activityPubItemUrl = $"{hostBase}/apv1/items/{item.Id}";

        contentMarkdownParts.Add($"[View in list]({staticItemUrl})");

        string renderedContent = string.Join("\n\n", contentMarkdownParts);
        var noteTags = new List<Dictionary<string, object?>>();
        noteTags.AddRange(BuildActivityPubTagObjects(
            new[] { item.Name },
            null,
            null));
        noteTags.AddRange(BuildActivityPubTagObjects(
            new[] { sanitizedComment, hashtagLine },
            item.Tags,
            new[] { item.SourceAttributedToIri, item.OriginatorActorIri }));

        noteTags = noteTags
            .GroupBy(tag =>
            {
                string type = tag.TryGetValue("type", out var typeObj) ? typeObj?.ToString() ?? string.Empty : string.Empty;
                string name = tag.TryGetValue("name", out var nameObj) ? nameObj?.ToString() ?? string.Empty : string.Empty;
                string href = tag.TryGetValue("href", out var hrefObj) ? hrefObj?.ToString() ?? string.Empty : string.Empty;
                return $"{type}|{name}|{href}";
            }, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        string renderedContentHtml = string.IsNullOrWhiteSpace(renderedContent)
            ? string.Empty
            : Markdown.ToHtml(renderedContent, markdownPipeline);
        renderedContentHtml = LinkifyFediverseMentionsInHtml(renderedContentHtml);

        var note = new Dictionary<string, object?>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = activityPubItemUrl,
            ["type"] = item.IsDeleted ? "Tombstone" : "Note",
            ["name"] = item.Name,
            ["content"] = renderedContentHtml,
            ["url"] = activityPubItemUrl,
            ["attributedTo"] = $"{GlobalConfig.Hostname}/apv1/lists/{list.Id}",
            ["to"] = new[] { "https://www.w3.org/ns/activitystreams#Public" },
            ["cc"] = new[] { "https://www.w3.org/ns/activitystreams#Public" },
            ["published"] = item.CreatedDate.ToUniversalTime().ToString("o"),
            ["updated"] = item.ModifiedDate.ToUniversalTime().ToString("o")
        };

        if (noteTags.Count > 0)
        {
            note["tag"] = noteTags;
        }

        if (noteAttachments.Count > 0)
        {
            note["attachment"] = noteAttachments;
        }

        return note;
    }
}
