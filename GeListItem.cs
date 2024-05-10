using System.Text;
using Mastonet.Entities;

public class GeListItem
{
    public int Id { get; set; }

    public int ListId { get; set; }
    public string? Name { get; set; }
    public string? Comment { get; set; }
    public bool IsComplete { get; set; }

    public bool Visible {get; set;} = true;

    // add a member that is a collection of tags (which are strings)
    public List<string> Tags { get; set; } = new List<string>();

    // add a member that is this item's created date
    public DateTime CreatedDate { get; set; } = DateTime.Now;

    // add a member that is this item's modified date
    public DateTime ModifiedDate { get; set; } = DateTime.Now;

    public bool ParseMastoStatus(Status status, int listid)
    {
        ListId = listid;
        Name = status.Url;
        IsComplete = false;
        CreatedDate = status.CreatedAt;
        ModifiedDate = status.CreatedAt;

        Account poster = status.Account;
        StringBuilder sb = new StringBuilder();
        if(poster is not null)
        {
            
            sb.AppendLine($"<div class=\"oposter\">");
            sb.AppendLine($"<img src=\"{poster.StaticAvatarUrl}\" alt=\"{poster.DisplayName}\" class=\"opavatar\">");
            sb.AppendLine($"<a href=\"{poster.ProfileUrl}\">{poster.DisplayName}({poster.AccountName})</a></div>");
            
            }
        sb.AppendLine($"<span class=\"status_Content\">{status.Content}</span>");


        int numMedias = status.MediaAttachments.Count();

        if (numMedias > 0)
        {
            DBg.d(LogLevel.Trace, $"medias: {numMedias}");
            foreach (Attachment media in status.MediaAttachments)
            {
                DBg.d(LogLevel.Trace, $"media: {media.Url}");
                sb.AppendLine($"<span class=\"media_attachment\"><img src=\"{media.Url}\" alt=\"{media.Description}\"></span>");
            }
        }
        else
        {
            DBg.d(LogLevel.Trace, "no medias");
        }

        // get the tags from the status.Tags list; cast to a List<string>
        Tags = status.Tags.Select(tag => tag.Name).ToList();
        var fooya = status.MediaAttachments.Select(attachment => attachment.Url);
        // does the status have a .card? a card is where the Masto server
        // pulled in a link from the status content and rendered a preview
        // in a nice looking box.
        // if so, add the card url to the comment
        if (status.Card is not null)
        {
            
            sb.AppendLine($"<span class=\"status_card\">{status.Card.Description}");
            sb.AppendLine($"<img src=\"{status.Card.Image}\" alt=\"{status.Card.Title}\"></span>");
        }
        Comment = sb.ToString();
        return true;
    }

}