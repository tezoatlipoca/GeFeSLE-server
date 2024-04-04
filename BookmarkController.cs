using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.FileSystemGlobbing.Internal.PathSegments;
// public class BookmarkController : Controller
// {
//     private readonly IBookmarkService _bookmarkService;

//     public BookmarkController(IBookmarkService bookmarkService)
//     {
//         _bookmarkService = bookmarkService;
//     }

//     [HttpGet]
//     public async Task<IActionResult> GetBookmarks()
//     {
//         var bookmarks = await _bookmarkService.GetBookmarks();
//         return Ok(bookmarks);
//     }

//     [HttpPost]
//     public async Task<IActionResult> AddBookmark([FromBody] Bookmark bookmark)
//     {
//         await _bookmarkService.AddBookmark(bookmark);
//         return Ok();
//     }

//     [HttpDelete]
//     public async Task<IActionResult> DeleteBookmark([FromBody] Bookmark bookmark)
//     {
//         await _bookmarkService.DeleteBookmark(bookmark);
//         return Ok();
//     }
// }

// This class is a controller that attempts to parse Netscape-Bookmark-File-1 "HTML"
// files into a list of GeListItems.
// Its a work in progress. The idea is to recurse through folders of bookmarks
// any name of containing folder and any folder further up the tree gets added as a tag
// (besides the existing tags and "quick url" aliases of the bookmark)
// The recursion is currently bork. 

public static class BookmarkController
{
    public static List<GeListItem> ImportNetscapeBookmarkFile(string filePath)
    {
        var bookmarks = new List<GeListItem>();
        if (!File.Exists(filePath)) throw new FileNotFoundException("File not found", filePath);

        string[] lines = File.ReadAllLines(filePath);
        var newlines = fixShittyNetscapeBookmarksHTML(lines);
        DBg.d(LogLevel.Trace, $"lines: {newlines.Length}");

        //string newfile = "C:\\Users\\downe\\Desktop\\fixed.html";
        //File.WriteAllLines(newfile, lines);

        var htmlDocument = new HtmlDocument();

        htmlDocument.LoadHtml(string.Join("\n", newlines));
        if (htmlDocument.ParseErrors != null && htmlDocument.ParseErrors.Any())
        {
            foreach (var error in htmlDocument.ParseErrors)
            {
                DBg.d(LogLevel.Trace, error.Code + ": " + error.Reason);
            }
        }
        // <DT><H3 ADD_DATE="1674350926" LAST_MODIFIED="1707265366">****FOLDER NAME*****</H3>
        // <DL><p>
        //     <DT><A HREF="https://support.mozilla.org/products/firefox" ADD_DATE="1674350926" LAST_MODIFIED="1707265366">Get Help</A>
        //     <DT><A HREF="https://support.mozilla.org/kb/customize-firefox-controls-buttons-and-toolbars?utm_source=firefox-browser&utm_medium=default-bookmarks&utm_campaign=customize" ADD_DATE="1674350926" LAST_MODIFIED="1707265366">Customize Firefox</A>
        //     <DT><A HREF="https://www.mozilla.org/contribute/" ADD_DATE="1674350926" LAST_MODIFIED="1707265366">Get Involved</A>
        //     <DT><A HREF="https://www.mozilla.org/about/" 
        //         ADD_DATE="1674350926" LAST_MODIFIED="1707265366" 
        //         SHORTCUTURL="keyword1" TAGS="mastodon">****BOOK MARK NAME***</A>
        // // </DL><p>

        // every folder name in the breadcrumb => convert to tag
        // every tag => convert to tag
        // in the above example, the last item gets 2 tags: <mastodon> and <folder name>


        var rootDLNode = htmlDocument.DocumentNode.Descendants("DL").FirstOrDefault();

        // stringify the root node for debugging
        if (rootDLNode == null)
        {
            DBg.d(LogLevel.Error, "rootDLNode is null");
            return null;
        }
        else
        {
            DBg.d(LogLevel.Trace, "rootDLNode found");

        }

        Stack<string> folderStack = new Stack<string>();

        processDLNode(rootDLNode, folderStack);

        return bookmarks;
    }
    public static void processDLNode(HtmlNode node, Stack<string> folderStack)
    {
        int stackCount = folderStack.Count;
        char c = '_';
        string stackLead = new string(c, stackCount);
        string fn = $"pDLN{stackLead}>"; DBg.d(LogLevel.Trace, fn);
        if (node == null)
        {
            DBg.d(LogLevel.Error, $"{fn} - node is null");
            return;
        }
        else {
            DBg.d(LogLevel.Trace, $"{fn} - node: {node.Name} ");
        }
        // get the immediate children of the <p> node
        var children = node.ChildNodes;
        //DBg.d(LogLevel.Trace, $"{fn} - node: {node.Name} html: {node.InnerHtml}");
        DBg.d(LogLevel.Trace, $"{fn} - children: {children.Count}");
        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            DBg.d(LogLevel.Trace, $"{fn} - child: {child.Name}");
            if(child.Name == "#text") continue;
            // whitespace,newlines in HTML count as text nodes. ugh I hate xml sometimes.
            else if (string.Equals(child.Name, "DT", StringComparison.OrdinalIgnoreCase) 
                && string.Equals(child.FirstChild.Name, "H3", StringComparison.OrdinalIgnoreCase))
            {
                DBg.d(LogLevel.Trace, $"{fn} - found folder: {child.FirstChild.InnerText}");
                // this is a folder; recurse on the NEXT sibling
                folderStack.Push(child.FirstChild.InnerText);
                processDLNode(child.NextSibling, folderStack);
                // when we return skip the next sibling
                i++; i++; // (skip its text node too)
            }
            else if (string.Equals(child.Name, "DT", StringComparison.OrdinalIgnoreCase) 
    && string.Equals(child.FirstChild.Name, "A", StringComparison.OrdinalIgnoreCase))
            {
                // this is a bookmark; process it
                processDTANode(child, folderStack);
            }
        }
    }
    public static void processDTANode(HtmlNode node, Stack<string> folderStack)
    {
        int stackCount = folderStack.Count;
        char c = '_';
        string stackLead = new string(c, stackCount);
        string fn = $"pDLA{stackLead}>"; DBg.d(LogLevel.Trace, fn);
        if (node == null)
        {
            DBg.d(LogLevel.Error, $"{fn} - node is null");
            return;
        }
        var a = node.FirstChild;
        if (a.Name != "A")
        {
            DBg.d(LogLevel.Error, $"{fn} - expected <A> got {a.Name}");
            return;
        }
        else
        {
            var href = a.Attributes["HREF"]?.Value;
            var addDate = a.Attributes["ADD_DATE"]?.Value;
            var lastModified = a.Attributes["LAST_MODIFIED"]?.Value;
            var shortcutUrl = a.Attributes["SHORTCUTURL"]?.Value;
            var tags = a.Attributes["TAGS"]?.Value;
            var linkname = a.InnerText;

            DBg.d(LogLevel.Trace, $"{fn} - href: {href} txt: {linkname} tags: {tags} stack: {string.Join(">", folderStack)}");

        }
    }
    // <!DOCTYPE NETSCAPE-Bookmark-file-1>
    // sigh. these aren't even valid html or xml. We have to doctor it a bit
    // to use any xpath capable tool on them. 
    // NERF: 
    //     <!DOCTYPE NETSCAPE-Bookmark-file-1>
    // <!-- This is an automatically generated file.
    //      It will be read and overwritten.
    //      DO NOT EDIT! -->
    // <META HTTP-EQUIV="Content-Type" CONTENT="text/html; charset=UTF-8">
    // <TITLE>Bookmarks</TITLE>
    // <H1>Bookmarks</H1>
    // ALSO:
    //  Delete any <p> from the end of any line, they are NEVER terminated. 
    //  every line starting with <DT> needs to get wrapped with a trailing </DT>
    //  properly escape any unescape amps (e.g. "&utm_" => "&amp;utm_")

    public static string[] fixShittyNetscapeBookmarksHTML(string[] lines)
    {
        string doctype = @"^\<\!DOCTYPE.*$";
        string comment = @"^\<\!--.*$";
        string meta = @"^\<META.*$";
        string meta2 = @"^.*\<\/META\>$";
        string title = @"^\<TITLE.*$";
        string h1 = @"^\<H1.*$";
        string unmatchedp = @"(?<=\s*</DL>)<p>$";
        string unmatchedp2 = @"(?<=\s*<DL>)<p>$";
        string unmatchedp_replace = "";
        string opendt = @"^\s*<DT>";
        string badamp = @"&(?!(amp|lt|gt|apos|quot);)";

        var newLines = new List<string>();
        for (int i = 0; i < lines.Length; i++)
        {
            if (Regex.IsMatch(lines[i], doctype, RegexOptions.IgnoreCase)) continue;
            if (Regex.IsMatch(lines[i], comment, RegexOptions.IgnoreCase))
            {
                i++; i++; continue;
            }
            if (Regex.IsMatch(lines[i], meta, RegexOptions.IgnoreCase)) continue;
            if (Regex.IsMatch(lines[i], meta2, RegexOptions.IgnoreCase)) continue;
            if (Regex.IsMatch(lines[i], title, RegexOptions.IgnoreCase)) continue;
            if (Regex.IsMatch(lines[i], h1, RegexOptions.IgnoreCase)) continue;

            if (Regex.IsMatch(lines[i], unmatchedp, RegexOptions.IgnoreCase))
            {
                lines[i] = Regex.Replace(lines[i], unmatchedp, unmatchedp_replace);
            }
            if (Regex.IsMatch(lines[i], unmatchedp2, RegexOptions.IgnoreCase))
            {
                lines[i] = Regex.Replace(lines[i], unmatchedp2, unmatchedp_replace);
            }


            // if this line doesn't end with a >
            // and the next one doesn't start with one (+whitespace)
            // then they are both the same line. 
            if (!Regex.IsMatch(lines[i], @"\>\s*$", RegexOptions.IgnoreCase) && !Regex.IsMatch(lines[i + 1], @"^\s*\<", RegexOptions.IgnoreCase))
            {
                lines[i] += lines[i + 1];
                DBg.d(LogLevel.Trace, $"merged lines: {lines[i]}");
                lines[i + 1] = "";
                i++;

            }
            if (Regex.IsMatch(lines[i], opendt, RegexOptions.IgnoreCase))
            {
                //DBg.d(LogLevel.Trace, $"fixing line: {lines[i]}");
                lines[i] += "</DT>";
            }

            lines[i] = Regex.Replace(lines[i], badamp, "&amp;");
            // REMEMBER TO REVERSE THIS BEFORE ADDING TO GELISTITEM!!! (the ampersands might be important in the URL; doubt it but..)

            newLines.Add(lines[i]);
        }
        return newLines.ToArray();
    }

}