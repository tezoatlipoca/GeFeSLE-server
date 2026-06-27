using System.Net;
using System.Text.RegularExpressions;
using Markdig;
using GeFeSLE;

public static class ActivityPubActorFactory
{
    public static Dictionary<string, object?> BuildActivityPubListActor(GeList list, MarkdownPipeline markdownPipeline, string? activityPubPublicKeyPem)
    {
        static string NormalizeHandle(string rawHandle)
        {
            string trimmed = rawHandle.Trim();
            return trimmed.StartsWith("@", StringComparison.Ordinal) ? trimmed : $"@{trimmed}";
        }

        static string GuessMentionHref(string username, string domain)
        {
            return $"https://{domain}/@{username}";
        }

        static string GuessHashtagHref(string tag)
        {
            string baseUrl = (GlobalConfig.Hostname ?? string.Empty).TrimEnd('/');
            return $"{baseUrl}/tags/{Uri.EscapeDataString(tag)}";
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

        List<Dictionary<string, object?>> BuildActivityPubTagObjects(IEnumerable<string?> sourceTexts)
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
                    string tag = match.Groups["tag"].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(tag) && seenHashtags.Add(tag))
                    {
                        tags.Add(new Dictionary<string, object?>
                        {
                            ["type"] = "Hashtag",
                            ["name"] = $"#{tag}",
                            ["href"] = GuessHashtagHref(tag)
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

            return tags;
        }

        string LinkifyFediverseEntitiesInHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return html;
            }

            var mentionRegex = new Regex(@"(?<![\w""'=/])@(?<user>[A-Za-z0-9_]+)@(?<domain>[A-Za-z0-9.-]+\.[A-Za-z]{2,})(?![\w@-])", RegexOptions.Compiled);
            var withMentions = mentionRegex.Replace(html, match =>
            {
                string username = match.Groups["user"].Value;
                string domain = match.Groups["domain"].Value;
                string href = GuessMentionHref(username, domain);
                string handle = $"@{username}@{domain}";
                return $"<span class=\"h-card\"><a href=\"{WebUtility.HtmlEncode(href)}\" class=\"u-url mention\" rel=\"nofollow noopener noreferrer\">{WebUtility.HtmlEncode(handle)}</a></span>";
            });

            var bareHandleRegex = new Regex(@"(?<![\w""'=/@])(?<user>[A-Za-z0-9_]+)@(?<domain>[A-Za-z0-9.-]+\.[A-Za-z]{2,})(?![\w@-])", RegexOptions.Compiled);
            var withBareMentions = bareHandleRegex.Replace(withMentions, match =>
            {
                string username = match.Groups["user"].Value;
                string domain = match.Groups["domain"].Value;
                if (IsLikelyEmailDomain(domain))
                {
                    return match.Value;
                }

                string href = GuessMentionHref(username, domain);
                string handle = $"@{username}@{domain}";
                return $"<span class=\"h-card\"><a href=\"{WebUtility.HtmlEncode(href)}\" class=\"u-url mention\" rel=\"nofollow noopener noreferrer\">{WebUtility.HtmlEncode(handle)}</a></span>";
            });

            var hashtagRegex = new Regex(@"(?<![\w&/""'=])#(?<tag>[A-Za-z0-9_]+)(?![\w-])", RegexOptions.Compiled);
            return hashtagRegex.Replace(withBareMentions, match =>
            {
                string tag = match.Groups["tag"].Value;
                string href = GuessHashtagHref(tag);
                return $"<a href=\"{WebUtility.HtmlEncode(href)}\" class=\"mention hashtag\" rel=\"tag\">#<span>{WebUtility.HtmlEncode(tag)}</span></a>";
            });
        }

        string LinkifyEmailsInMarkdown(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return markdown;
            }

            var emailRegex = new Regex(@"(?<![\w/@\(:])(?<local>[A-Za-z0-9._%+-]+)@(?<domain>[A-Za-z0-9.-]+\.[A-Za-z]{2,})(?![\w@-])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            return emailRegex.Replace(markdown, match =>
            {
                string local = match.Groups["local"].Value;
                string domain = match.Groups["domain"].Value;
                if (!IsLikelyEmailDomain(domain))
                {
                    return match.Value;
                }

                string email = $"{local}@{domain}";
                return $"[{email}](mailto:{email})";
            });
        }

        string actorId = $"{GlobalConfig.Hostname}/apv1/lists/{list.Id}";
        string hostBase = (GlobalConfig.Hostname ?? string.Empty).TrimEnd('/');
        string listBaseName = list.Name ?? $"list-{list.Id}";
        string instanceUrl = $"{(GlobalConfig.Hostname ?? string.Empty).TrimEnd('/')}/";
        const string projectHomepageUrl = "https://github.com/tezoatlipoca/GeFeSLE-server";
        string iconUrl = string.IsNullOrWhiteSpace(hostBase) ? "/gefesleff.png" : $"{hostBase}/gefesleff.png";

        string? FormatHelpContact(GeFeSLEUser? user)
        {
            if (user is null)
            {
                return null;
            }

            string? username = string.IsNullOrWhiteSpace(user.UserName) ? null : user.UserName.Trim();
            string? email = string.IsNullOrWhiteSpace(user.Email) ? null : user.Email.Trim();
            string? primary = username ?? email;

            if (string.IsNullOrWhiteSpace(primary))
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(username) && !username.Contains('@'))
            {
                if (!string.IsNullOrWhiteSpace(email) && email.Contains('@'))
                {
                    return email;
                }

                return null;
            }

            if (!primary.Contains('@'))
            {
                return null;
            }

            if (primary.StartsWith("@", StringComparison.Ordinal))
            {
                return NormalizeHandle(primary);
            }

            if (!string.IsNullOrWhiteSpace(username) && username.Contains('@'))
            {
                return NormalizeHandle(username);
            }

            return email ?? primary;
        }

        var helpContacts = new List<string>();
        var seenHelpContacts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenUserIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var userContact in list.ListOwners.Append(list.Creator).Where(u => u is not null))
        {
            if (!string.IsNullOrWhiteSpace(userContact!.Id) && !seenUserIds.Add(userContact.Id))
            {
                continue;
            }

            var contact = FormatHelpContact(userContact);
            if (!string.IsNullOrWhiteSpace(contact) && seenHelpContacts.Add(contact))
            {
                helpContacts.Add(contact);
            }
        }

        if (!string.IsNullOrWhiteSpace(GlobalConfig.owner)
            && seenHelpContacts.Add(GlobalConfig.owner))
        {
            helpContacts.Add(GlobalConfig.owner);
        }

        string helpText = helpContacts.Count > 0
            ? string.Join(", ", helpContacts)
            : "the list creator or one of the list owners";
        string ownerContactMarkdown = LinkifyEmailsInMarkdown($"For help ask {helpText}");
        string visibilityStatusHtml = list.Visibility == GeListVisibility.Public
            ? "<span style=\"color: green;\"><b>PUBLIC</b></span>"
            : "<span style=\"color: red;\"><b>NOT PUBLIC</b> -- list items will not be visible.</span>";

        var summaryMarkdownParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(list.Comment))
        {
            summaryMarkdownParts.Add(list.Comment);
        }
        summaryMarkdownParts.Add(ownerContactMarkdown);
        summaryMarkdownParts.Add($"Status: {visibilityStatusHtml}");

        string combinedSummaryMarkdown = string.Join("\n\n", summaryMarkdownParts);
        string? actorSummary = string.IsNullOrWhiteSpace(combinedSummaryMarkdown)
            ? null
            : Markdown.ToHtml(combinedSummaryMarkdown, markdownPipeline);
        actorSummary = string.IsNullOrWhiteSpace(actorSummary)
            ? actorSummary
            : LinkifyFediverseEntitiesInHtml(actorSummary);
        var actorTags = BuildActivityPubTagObjects(new[] { list.Comment, ownerContactMarkdown });

        string htmlFileName = $"{listBaseName}.html";
        string rssFileName = $"rss-{listBaseName}.xml";
        string jsonFileName = $"{listBaseName}.json";

        string htmlUrl = $"{GlobalConfig.Hostname}/{Uri.EscapeDataString(htmlFileName)}";
        string rssUrl = $"{GlobalConfig.Hostname}/{Uri.EscapeDataString(rssFileName)}";
        string jsonUrl = $"{GlobalConfig.Hostname}/{Uri.EscapeDataString(jsonFileName)}";

        bool hasHtmlFile = !string.IsNullOrWhiteSpace(GlobalConfig.wwwroot)
            && File.Exists(Path.Combine(GlobalConfig.wwwroot, htmlFileName));
        bool hasRssFile = !string.IsNullOrWhiteSpace(GlobalConfig.wwwroot)
            && File.Exists(Path.Combine(GlobalConfig.wwwroot, rssFileName));
        bool hasJsonFile = !string.IsNullOrWhiteSpace(GlobalConfig.wwwroot)
            && File.Exists(Path.Combine(GlobalConfig.wwwroot, jsonFileName));

        var attachments = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["type"] = "PropertyValue",
                ["name"] = "List Server/Instance",
                ["value"] = $"<a href=\"{WebUtility.HtmlEncode(instanceUrl)}\" rel=\"nofollow noopener noreferrer\" target=\"_blank\">{WebUtility.HtmlEncode(instanceUrl)}</a>"
            },
            new()
            {
                ["type"] = "PropertyValue",
                ["name"] = "Project Homepage",
                ["value"] = $"<a href=\"{WebUtility.HtmlEncode(projectHomepageUrl)}\" rel=\"nofollow noopener noreferrer\" target=\"_blank\">{WebUtility.HtmlEncode(projectHomepageUrl)}</a>"
            }
        };

        if (hasHtmlFile)
        {
            attachments.Add(new Dictionary<string, object?>
            {
                ["type"] = "PropertyValue",
                ["name"] = "Static HTML List Page",
                ["value"] = $"<a href=\"{WebUtility.HtmlEncode(htmlUrl)}\" rel=\"nofollow noopener noreferrer\" target=\"_blank\">{WebUtility.HtmlEncode(htmlUrl)}</a>"
            });
        }
        if (hasRssFile)
        {
            attachments.Add(new Dictionary<string, object?>
            {
                ["type"] = "PropertyValue",
                ["name"] = "RSS Feed",
                ["value"] = $"<a href=\"{WebUtility.HtmlEncode(rssUrl)}\" rel=\"nofollow noopener noreferrer\" target=\"_blank\">{WebUtility.HtmlEncode(rssUrl)}</a>"
            });
        }
        if (hasJsonFile)
        {
            attachments.Add(new Dictionary<string, object?>
            {
                ["type"] = "PropertyValue",
                ["name"] = "JSON Export",
                ["value"] = $"<a href=\"{WebUtility.HtmlEncode(jsonUrl)}\" rel=\"nofollow noopener noreferrer\" target=\"_blank\">{WebUtility.HtmlEncode(jsonUrl)}</a>"
            });
        }

        var actor = new Dictionary<string, object?>
        {
            ["@context"] = new object[]
            {
                "https://www.w3.org/ns/activitystreams",
                "https://w3id.org/security/v1",
                new Dictionary<string, object?>
                {
                    ["schema"] = "http://schema.org#",
                    ["PropertyValue"] = "schema:PropertyValue",
                    ["value"] = "schema:value"
                }
            },
            ["id"] = actorId,
            ["type"] = "Group",
            ["name"] = list.Name,
            ["preferredUsername"] = list.ActivityPubId,
            ["summary"] = actorSummary,
            ["icon"] = new Dictionary<string, object?>
            {
                ["type"] = "Image",
                ["mediaType"] = "image/png",
                ["url"] = iconUrl
            },
            ["inbox"] = $"{GlobalConfig.Hostname}/apv1/lists/{list.Id}/inbox",
            ["outbox"] = $"{GlobalConfig.Hostname}/apv1/lists/{list.Id}/outbox",
            ["followers"] = $"{GlobalConfig.Hostname}/apv1/lists/{list.Id}/followers"
        };

        if (hasHtmlFile)
        {
            actor["url"] = htmlUrl;
        }

        if (attachments.Count > 0)
        {
            actor["attachment"] = attachments;
        }

        if (actorTags.Count > 0)
        {
            actor["tag"] = actorTags;
        }

        if (!string.IsNullOrWhiteSpace(activityPubPublicKeyPem))
        {
            actor["publicKey"] = new Dictionary<string, object?>
            {
                ["id"] = ActivityPubDeliveryUtils.ActivityPubKeyIdForActor(actorId),
                ["owner"] = actorId,
                ["publicKeyPem"] = activityPubPublicKeyPem
            };
        }

        DBg.d(LogLevel.Trace, $"ActivityPub actor for list {list.Id}:\n{System.Text.Json.JsonSerializer.Serialize(actor, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })}");

        return actor;
    }
}
