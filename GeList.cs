using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Xml.Linq;
//using Newtonsoft.Json;
using System.Text.Json;
using Markdig;
using Microsoft.AspNetCore.Identity;
using System.Text.Json.Serialization;

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
    public string Service { get; set; } = null;
    public string? Data { get; set; } = null;


}

public class GeList
{
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


        // create a new file in wwwroot with the name of the list
        var filename = $"{Name}.html";
        var dest = Path.Combine(GlobalConfig.wwwroot!, filename);


        
        var items = await db.Items.Where(item => item.ListId == Id && item.Visible).ToListAsync();
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
        sb.AppendLine($"<h1 class=\"listtitle\"><a class=\"indexlink\" href=\"index.html\">&lt;-</a> {Name}</h1>");
        sb.AppendLine($"<p class=\"listcreated\">Created: {CreatedDate.ToString("yyyy-MM-dd HH:mm:ss")}");
        sb.AppendLine($"Modified: {ModifiedDate.ToString("yyyy-MM-dd HH:mm:ss")}</p>");
        if (Comment != null)
        {
            var md = Markdown.ToHtml(Comment);
            sb.AppendLine($"<p class=\"listcomment\">{md}</p>");
        }

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
    sb.AppendLine($"<div class=\"button rsslink\">No RSS (No Items)</div>");
    sb.AppendLine($"<div class=\"button exportlink\" id=\"exportlink\">No JSON (No Items)</div>");
}
sb.AppendLine($"<div class=\"button suggestlink\" onclick=\"window.location.href='_edit.item.html?listid={Id}&suggestion=true'\">Suggest Item</div>");

        // display a form with a text box for the text and tags search parameters
        sb.AppendLine($"<span class=\"result\" id=\"result\"></span>");
        sb.AppendLine($"<div class=\"searchbox\">");
        sb.AppendLine($"<div class=\"textsearch\"><form>Search Text (space separated)");
        sb.AppendLine($"<input type=\"text\" id=\"textsearchbox\" onInput=\"filterUpdate(); return false;\" placeholder=\"Search text..\">");
        sb.AppendLine($"</form></div>");
        sb.AppendLine("<div class=\"tagsearch\"><form>Search Tags (space separated)");
        sb.AppendLine("<input type=\"text\" id=\"tagsearchbox\" onInput=\"filterUpdate(); return false;\" placeholder=\"Search tags..\">");
        sb.AppendLine("</form></div></div>");
        
        sb.AppendLine("<hr>");

        sb.AppendLine("<div class=\"itemtable\" id=\"itemtable\">");


        foreach (var item in items)
        {
            sb.AppendLine($"<div class=\"namecell\">{item.Name}<img src=\"{GlobalConfig.Hostname}/gefesleff.png\" width=\"15px\" height=\"15px\" onclick=\"copyToClipboard({item.Id});\"></div>");
            sb.AppendLine($"<div class=\"itemrow\" id=\"{item.Id}\">");
            

            if (item.Comment != null)
            {
                var itemmd = Markdown.ToHtml(item.Comment);
                sb.AppendLine($"<div class=\"commentcell\">{itemmd}</div>");
            }
            else
            {
                sb.AppendLine($"<div class=\"commentcell\"></div>");
            }

            sb.AppendLine($"<div class=\"tagscell\">");
            // wrap each tag in a span with a class of tag
            foreach (var tag in item.Tags)
            {
                // Only render non-empty, non-whitespace tags
                if (!string.IsNullOrWhiteSpace(tag))
                {
                    sb.AppendLine($"<span class=\"tag\">{tag.Trim()}</span>");
                }
            }
            sb.AppendLine("</div>");
            sb.AppendLine("<div class=\"utilitybox\">");
            sb.AppendLine($"<span class=\"itemmoddate\">{item.ModifiedDate.ToString("yyyy-MM-dd HH:mm:ss")}</span>");
            sb.AppendLine($"<span class=\"moveitemlink\" style=\"display: none;\"><a href=\"#\" oncontextmenu=\"showContextMenu(event)\">Move</a></span>");
            sb.AppendLine($"<span class=\"itemeditlink\" style=\"display: none;\"><a href=\"_edit.item.html?listid={item.ListId}&itemid={item.Id}\" >Edit</a></span>");
            // call the deleteitem endpoint but then refresh the page as well
            sb.AppendLine($"<span class=\"itemdeletelink\" style=\"display: none;\"><a href=\"#\" onclick=\"deleteItem({item.ListId},{item.Id}); return;\" >Delete</a></span>");
            sb.AppendLine($"<span class=\"itemreportlink\"><a href=\"#\" onclick=\"reportItem({item.ListId},{item.Id}); return;\" >Report</a></span>");
            sb.AppendLine("</div>");

            sb.AppendLine("</div>");
        }
        sb.AppendLine("</div>");
        // add a reference to the javascript files
        sb.AppendLine("<script src=\"_utils.js\"></script>");
        sb.AppendLine("<script src=\"_list_view.js\"></script>");
        sb.AppendLine("<script src=\"_modal.mastodon.js\"></script>");
        sb.AppendLine("<script src=\"_modal.google.js\"></script>");
        sb.AppendLine("<script src=\"_modal.report.item.js\"></script>");


        await GlobalStatic.GeneratePageFooter(sb);
        DBg.d(LogLevel.Trace, $"Writing to {dest}");
        await File.WriteAllTextAsync(dest, sb.ToString());

    }
    public async Task GenerateRSSFeed(GeFeSLEDb db)
    {
        DBg.d(LogLevel.Trace, $"GenerateRssFeed {Id}");
        // create new database context

        var items = await db.Items.Where(item => item.ListId == Id && item.Visible).ToListAsync();
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
        var items = await db.Items.Where(item => item.ListId == Id && item.Visible).ToListAsync();
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
        
        // Generate all list files
        await GenerateHTMLListPage(db);
        await GenerateRSSFeed(db);
        await GenerateJSON(db);
        
        // Regenerate the index since list content has changed
        await GlobalStatic.GenerateHTMLListIndex(db);
        
        DBg.d(LogLevel.Trace, $"RegenerateAllFiles completed for: {Id} - {Name}");
    }
}
