using GeFeSLE.DTOs;

// extends GeAPActor to add reference to the lists that this fedi-fellow is following.
// why not just have a single class? we may want to store just fedi-actors aka 
// list item commentators seperately from people who follow lists. 

public class GeListFollower : GeAPActor
{
    public List<int> FollowingLists { get; set; } = new List<int>(); // the list of listIds that this fedi-fellow is following. This is used to generate the /apv1/lists/{listId}/followers endpoint.

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
                // send GET request to the IRI
                var response = await httpClient.GetAsync(Id);
                if (!response.IsSuccessStatusCode)
                {
                    DBg.d(LogLevel.Warning, $"{fn} -- Failed to fetch actor info from IRI: {Id}, StatusCode: {response.StatusCode}");
                    return false;
                }

                // read response content as string
                var content = await response.Content.ReadAsStringAsync();

                // deserialize JSON to ApActorDto
                var actorDto = System.Text.Json.JsonSerializer.Deserialize<ApActorDto>(content);
                if (actorDto == null)
                {
                    DBg.d(LogLevel.Warning, $"{fn} -- Failed to deserialize actor info from IRI: {Id}");
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
                DBg.d(LogLevel.Error, $"{fn} -- Exception occurred while fetching actor info from IRI: {Id}, Exception: {ex.Message}");
                return false;
            }
        }
    }
}
