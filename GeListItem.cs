using System.Text;
using System.Text.RegularExpressions;
using Mastonet.Entities;

public class GeListItem
{
    public int Id { get; set; }

    public int ListId { get; set; }
    public string? Name { get; set; }
    public string? Comment { get; set; }
    public bool IsComplete { get; set; }

    public bool Visible { get; set; } = true;

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
        if (poster is not null)
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

    // looks in the Comment field for reference to files that are local and returns those paths
    // relative to wwwroot, in a List. 
    public List<string> LocalFiles()
    {
        string fn = "LocalFiles"; DBg.d(LogLevel.Trace, fn);

        // search the Comment field for any occurances of an image or link markdown reference
        // where the first part of the URL is GlobalConfig.Hostname
        // e.g. [receipt](https://lists.awadwatt.com/uploads/backadmin/screenshot-2024-05-11T02-29-53.983Z.png)
        // or
        // [<something>](<GlobalConfig.Hostname>/<relative path>)
        // then capture that relative path

        List<string> returnMatches = new List<string>{};

        // Assuming item.Comment contains the comment
        string comment = Comment;

        // Define the regex pattern
        string pattern = @"\[.*?\]\(" + GlobalConfig.Hostname + @"[[/\\](.*?)\)";

        // Create a regex object
        Regex regex = new Regex(pattern);

        // Search the comment for matches
        MatchCollection matches = regex.Matches(comment);

        // Loop through the matches and print the relative paths
        foreach (Match match in matches)
        {
            DBg.d(LogLevel.Trace, $"{fn} -- item {Id} - found FILE REFERENCE: {match})");
            // so the match was for the markdown file reference
            // e.g. [receipt](http://localhost:7036/uploads/backadmin/screenshot-2024-05-11T14-08-36.727Z.png
            // but we just want the relative path name part after the hostname
            // this is match.Groups[1].Value

            if (match.Groups.Count > 1)
            {
                string relativePath = match.Groups[1].Value;
                returnMatches.Add($"/{relativePath}");
            }
        }
        return returnMatches;

    }

    

}