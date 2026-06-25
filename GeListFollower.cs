using GeFeSLE.DTOs;

// extends GeAPActor to add reference to the lists that this fedi-fellow is following.
// why not just have a single class? we may want to store just fedi-actors aka 
// list item commentators seperately from people who follow lists. 

public class GeListFollower : GeAPActor
{
    public List<int> FollowingLists { get; set; } = new List<int>(); // the list of listIds that this fedi-fellow is following. This is used to generate the /apv1/lists/{listId}/followers endpoint.

    public static string? GuessInboxFromActorIri(string? actorIri)
    {
        if (string.IsNullOrWhiteSpace(actorIri))
        {
            return null;
        }

        if (!Uri.TryCreate(actorIri, UriKind.Absolute, out var actorUri))
        {
            return null;
        }

        var path = actorUri.AbsolutePath.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (path.EndsWith("/inbox", StringComparison.OrdinalIgnoreCase))
        {
            return actorUri.GetLeftPart(UriPartial.Path);
        }

        return $"{actorUri.GetLeftPart(UriPartial.Authority)}{path}/inbox";
    }

    // a constructor that takes an ApActorDto and initializes the GeListFollower object with its properties.
 

    public bool IsListFollower(int listId)
    {
        // return true if this follower follows the list by listid.
        return FollowingLists.Contains(listId);
    }

    public ApActorDto ToApActorDto()
    {
        return new ApActorDto(
            id: Id,
            type: Type,
            context: null,
            preferredUsername: PreferredUsername,
            name: Name,
            summary: Summary,
            inbox: Inbox,
            outbox: Outbox,
            followers: Followers,
            icon: MapAttachment(Icon),
            image: MapAttachment(Image),
            url: Url
        );
    }

    private static ApAttachmentDto? MapAttachment(GeAPAttachment? attachment)
    {
        if (attachment == null)
        {
            return null;
        }

        return new ApAttachmentDto(
            type: attachment.Type,
            url: attachment.Url,
            mediaType: attachment.MediaType,
            width: attachment.Width,
            height: attachment.Height
        );
    }

    private static GeAPAttachment? MapAttachmentBack(ApAttachmentDto? attachment)
    {
        if (attachment == null)
        {
            return null;
        }

        return new GeAPAttachment
        {
            Type = attachment.type,
            Url = attachment.url,
            MediaType = attachment.mediaType,
            Width = attachment.width,
            Height = attachment.height
        };
    }

    // method that checks the IRI; if not null then queries it remotely. 
    // should get something castable to an ApActorDto. If so, store the info in this GeListFollower object.
    public async Task<bool> FetchActorInfoFromIriAsync()
    {
        var fn = $"FetchActorInfoFromIriAsync (id={Id})"; DBg.d(LogLevel.Trace, fn);
        if (string.IsNullOrWhiteSpace(Id))
        {
            DBg.d(LogLevel.Warning, $"{fn} -- Id is null or empty");
            return false;   
        }
        // create new httpclient
        using (var httpClient = new HttpClient())
        {
            try
            {
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"{GlobalStatic.applicationName}/{GlobalConfig.bldVersion}");
                httpClient.DefaultRequestHeaders.Accept.Clear();
                httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/activity+json");
                httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\"");
                httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");

                // send GET request to the IRI
                var response = await httpClient.GetAsync(Id);
                if (!response.IsSuccessStatusCode)
                {
                    DBg.d(LogLevel.Warning, $"{fn} -- Failed to fetch actor info from IRI: {Id}, StatusCode: {response.StatusCode}");
                    if (string.IsNullOrWhiteSpace(Inbox))
                    {
                        Inbox = GuessInboxFromActorIri(Id);
                    }
                    return false;
                }

                // read response content as string
                var content = await response.Content.ReadAsStringAsync();

                if (string.IsNullOrWhiteSpace(content))
                {
                    DBg.d(LogLevel.Warning, $"{fn} -- Empty actor payload from IRI: {Id}");
                    if (string.IsNullOrWhiteSpace(Inbox))
                    {
                        Inbox = GuessInboxFromActorIri(Id);
                    }
                    return false;
                }

                var contentType = response.Content.Headers.ContentType?.MediaType ?? "(none)";
                var trimmed = content.TrimStart();
                if (!(trimmed.StartsWith("{") || trimmed.StartsWith("[")))
                {
                    var preview = content.Length > 140 ? content.Substring(0, 140) + "..." : content;
                    DBg.d(LogLevel.Warning, $"{fn} -- Non-JSON actor payload from IRI: {Id}, Content-Type: {contentType}, Preview: {preview}");
                    if (string.IsNullOrWhiteSpace(Inbox))
                    {
                        Inbox = GuessInboxFromActorIri(Id);
                    }
                    return false;
                }

                // deserialize JSON to ApActorDto
                var actorDto = System.Text.Json.JsonSerializer.Deserialize<ApActorDto>(content);
                if (actorDto == null)
                {
                    DBg.d(LogLevel.Warning, $"{fn} -- Failed to deserialize actor info from IRI: {Id}, Content-Type: {contentType}");
                    if (string.IsNullOrWhiteSpace(Inbox))
                    {
                        Inbox = GuessInboxFromActorIri(Id);
                    }
                    return false;
                }

                // update this GeListFollower object with the fetched info
                Type = actorDto.type;
                PreferredUsername = actorDto.preferredUsername;
                Name = actorDto.name;
                Summary = actorDto.summary;
                Inbox = actorDto.inbox;
                Outbox = actorDto.outbox;
                Followers = actorDto.followers;
                Icon = MapAttachmentBack(actorDto.icon);
                Image = MapAttachmentBack(actorDto.image);
                Url = actorDto.url;

                return true;
            }
            catch (Exception ex)
            {
                DBg.d(LogLevel.Warning, $"{fn} -- Exception occurred while fetching actor info from IRI: {Id}, Exception: {ex.Message}");
                if (string.IsNullOrWhiteSpace(Inbox))
                {
                    Inbox = GuessInboxFromActorIri(Id);
                }
                return false;
            }
        }
    }
}
