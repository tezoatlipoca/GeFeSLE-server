using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Xml.Linq;
//using Newtonsoft.Json;
using System.Text.Json;
using Markdig;

public class GeList
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Comment { get; set; }

    // add a member that is this list's created date
    public DateTime CreatedDate { get; set; } = DateTime.Now;

    // add a member that is this list's modified date
    public DateTime ModifiedDate { get; set; } = DateTime.Now;

    async public Task GenerateHTMLListPage(GeFeSLEDb db)
    {
        DBg.d(LogLevel.Trace, $"GenerateHTMLListPage: {Id}");
        

        // create a new file in wwwroot with the name of the list
        var filename = $"{Name}.html";
        var dest = Path.Combine(GlobalConfig.wwwroot!, filename);


        // get all the items for the list
        var items = await db.Items.Where(item => item.ListId == Id).ToListAsync();


        var sb = new StringBuilder();
        await GlobalStatic.GenerateHTMLHead(sb, $"{GlobalConfig.sitetitle}:{Name}");
        
        if (GlobalConfig.bodyHeader != null)
        {
            var header = await File.ReadAllTextAsync(GlobalConfig.bodyHeader);
            sb.AppendLine(header);
        }
        sb.AppendLine($"<h1 class=\"listtitle\"><a class=\"indexlink\" href=\"index.html\">&lt;-</a> {Name}</h1>");
        sb.AppendLine($"<p class=\"listcreated\">Created: {CreatedDate.ToString("yyyy-MM-dd HH:mm:ss")}");
        sb.AppendLine($"Modified: {ModifiedDate.ToString("yyyy-MM-dd HH:mm:ss")}</p>");
        if(Comment != null) {
            var md = Markdown.ToHtml(Comment);
            sb.AppendLine($"<p class=\"listcomment\">{md}</p>");
        }
        sb.AppendLine($"<p> ");
        sb.AppendLine($" <a class=\"editlink\" href=\"_edit.list.html?listid={Id}\">Edit this list</a>");
        sb.AppendLine($" |  <a class=\"rsslink\" href=\"rss-{Name}.xml\">RSS Feed</a>");
        sb.AppendLine($" |  <a class=\"exportlink\" id=\"exportlink\" href=\"{Name}.json\">JSON</a>");
        sb.AppendLine($" |  <a class=\"edititemlink\" href=\"_edit.item.html?listid={Id}\">Add new item</a>");
        sb.AppendLine($" |  <a class=\"mastoimportlink\" href=\"_mastobookmark.html?listId={Id}\" >import Masto bookmarks 2 here</a></p>");
        sb.AppendLine("</p>");
        
        // display a form with a text box for the tags search parameters
        sb.AppendLine($"<span class=\"result\" id=\"result\"></span>");
        sb.AppendLine("<span class=\"tagsearch\"><form>Search Tags (space separated)");
        sb.AppendLine("<input type=\"text\" id=\"tagsearchbox\" onInput=\"filterTAGSUpdate(); return false;\" placeholder=\"Search tags..\">");
        sb.AppendLine("</form></span>");
        sb.AppendLine("<hr>");
        sb.AppendLine("<table class=\"itemtable\" id=\"itemtable\">");
        
  
        foreach (var item in items)
        {
            sb.AppendLine($"<tr class=\"itemrow\">");
            sb.AppendLine($"<td class=\"namecell\">{item.Name}</td>");

            if(item.Comment != null) {
                var itemmd = Markdown.ToHtml(item.Comment);
                sb.AppendLine($"<td class=\"commentcell\">{itemmd}</td>");
            }
            else {
                sb.AppendLine($"<td class=\"commentcell\"></td>");
            }            

            sb.AppendLine($"<td class=\"tagscell\">");
            // wrap each tag in a span with a class of tag
            foreach (var tag in item.Tags)
            {
                sb.AppendLine($"<span class=\"tag\">{tag}</span>");
            }
            sb.AppendLine("</td>");
            sb.AppendLine("<td class=\"utilitybox\">");
            sb.AppendLine($"<span class=\"itemmoddate\">{item.ModifiedDate.ToString("yyyy-MM-dd HH:mm:ss")}</span>");
            sb.AppendLine($"<span class=\"itemeditlink\"><a href=\"_edit.item.html?listid={item.ListId}&itemid={item.Id}\">Edit</a></span>");
            // call the deleteitem endpoint but then refresh the page as well
            sb.AppendLine($"<span class=\"itemdeletelink\"><a href=\"#\" onclick=\"deleteItem({item.ListId},{item.Id}); return;\">Delete</a></span>");
            sb.AppendLine("</td>");

            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</table>");
        // add a reference to the javascript files
        sb.AppendLine("<script src=\"_utils.js\"></script>");
        sb.AppendLine("<script src=\"_list_view.js\"></script>");
        sb.AppendLine("<script src=\"_mastobookmark.js\"></script>");

        
        await GlobalStatic.GeneratePageFooter(sb);
        DBg.d(LogLevel.Trace, $"Writing to {dest}");
        await File.WriteAllTextAsync(dest, sb.ToString());

    }
    public async Task GenerateRSSFeed(GeFeSLEDb db)
    {
        DBg.d(LogLevel.Trace, $"GenerateRssFeed {Id}");
        // create new database context

        var items = await db.Items.Where(item => item.ListId == Id).ToListAsync();
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
                        new XElement("link", $"http://{GlobalConfig.Hostname}:{GlobalConfig.Hostport}/rss/{Id}"),
                        new XElement("description", $"{Comment}"),
                        items.Select(item =>
                            new XElement("item",
                                new XElement("title", item.Name),
                                new XElement("link", $"http://{GlobalConfig.Hostname}:{GlobalConfig.Hostport}/showitems/{Id}/{item.Id}"),
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
        var items = await db.Items.Where(item => item.ListId == Id).ToListAsync();
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
}
