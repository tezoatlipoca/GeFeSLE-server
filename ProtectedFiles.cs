using System.Collections.Concurrent;
using GeFeSLE;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SQLitePCL;
using Newtonsoft.Json; // Add this import statement

public static class ProtectedFiles
{
    // define a bunch of defaults: List<string>
    private static readonly Dictionary<string, string> internalFiles = new Dictionary<string, string> {
    //    { "/_edit.item.html", "contributor" },
    //    { "/_edit.item.js", "contributor" }, REMOVED (for suggestions)
       { "/_edit.list.js", "listowner" },
       { "/_edit.list.html", "listowner" },
       { "/_edituser.html", "SuperUser" }, 
       { "/_edituser.js", "SuperUser" },
       { "/_mastobookmark.html", "listowner" },
       { "/_mastobookmark.js", "listowner" }

    };


// "D:\repos\GeFeSLE-server\wwwroot\_modal.google.js"
// "D:\repos\GeFeSLE-server\wwwroot\_modal.mastodon.js"
// "D:\repos\GeFeSLE-server\wwwroot\_password.change.html"
// "D:\repos\GeFeSLE-server\wwwroot\_password.change.js"

    private static ConcurrentDictionary<string, string> Files = new ConcurrentDictionary<string, string>();
    // first string is the FILE name, the second is the list name, which is the KEY to the next dictionary
    // basically: check Files above for the filename. If its an "internal protected file" OR
    // its associated with a List that is not public, it will be in here. The VALUE is the NAME of the LIST
    // which you use to look up the List object in the next dictionary (saves a DB query)
    private static ConcurrentDictionary<string, GeList> Lists = new ConcurrentDictionary<string, GeList>();


    // add a file to the list
    public static void AddFile(string path, string listName)
    {
        Files.TryAdd(path, listName);
        DBg.d(LogLevel.Trace, $"ProtectedFiles.AddFile: {path} - list name: {listName}");
    }


    // remove a file from the list
    public static void RemoveFile(string path)
    {
        var removed = "";
        Files.TryRemove(path, out removed);
        DBg.d(LogLevel.Trace, $"ProtectedFiles.RemoveFile: {path} - list name: {removed}");
    }

    // check if a file is in the list
    public static bool ContainsFile(string path)
    {
        return Files.ContainsKey(path);
    }

    // call this on:
    // 1. startup
    // 2. when a list is created
    // 3. when a list is deleted
    // 4. when a list's visibility is changed
    // 5. when a list's name is changed
    // 6. when a list's contributors are changed
    // 7. when a list's owners are changed
    // 8. when a list's creator is changed

    public static void ReLoadFiles(GeFeSLEDb db)
    {
        // get a list of all GeList in the database that have .Visibility > GeListVisibility.Public
        var lists = db.Lists
         .Include(list => list.Creator)
         .Include(list => list.ListOwners)
         .Include(list => list.Contributors)
         .Where(list => list.Visibility > GeListVisibility.Public)
         .ToList();

        foreach (var list in lists)
        {

            AddList(list);

        }
        foreach ((string file, string minrole) in internalFiles)
        {
            AddFile(file, minrole);
        }
    }
    public static void AddList(GeList list)
    {
        string fn = "ProtectedFiles.AddList";
        DBg.d(LogLevel.Trace, $"{fn} -- {list.Name}");
        if (list.Name != null && list.Visibility > GeListVisibility.Public)
        {
            AddFile($"/rss-{list.Name}.xml", list.Name);
            AddFile($"/{list.Name}.json", list.Name);
            AddFile($"/{list.Name}.html", list.Name);
            Lists.TryAdd(list.Name!, list);
        }
        else {
            DBg.d(LogLevel.Information, $"{fn} skipping name: {list.Name} or visibility: {list.Visibility}");
            
        }
    }
    public static bool RemoveList(GeList list)
    {
        string fn = "ProtectedFiles.RemoveList";
        DBg.d(LogLevel.Trace, $"{fn} -- {list.Name}");
        if (list.Name != null && list.Visibility == GeListVisibility.Public)
        {
            RemoveFile($"/rss-{list.Name}.xml");
            RemoveFile($"/{list.Name}.json");
            RemoveFile($"/{list.Name}.html");
            return Lists.TryRemove(list.Name!, out _);
        }
        else {
            DBg.d(LogLevel.Information, $"{fn} NOT REMOVING name: {list.Name}, visibility: {list.Visibility}");
            return false;
        }
        
    }

    public static async Task<(bool, string?)> IsFileVisibleToUser(string path,
        GeFeSLEUser user,
        UserManager<GeFeSLEUser> userManager)
    {
        var fn = "IsFileVisibleToUser"; DBg.d(LogLevel.Trace, fn);

        string? ynot;

        Files.TryGetValue(path, out var listName);

        // get user's highest realizedRole
        IList<string> roles = await userManager.GetRolesAsync(user);
        if (IsInternalProtected(listName))
        {
            if (IsInternalProtectedVisibleToUser(listName, roles, out ynot))
            {
                ynot = $"{fn} file {path} is internal protected file: {ynot}";
                return (true, ynot);
            }
            else
            {
                ynot = $"{fn} file {path} is internal protected file: {ynot}";
                return (false, ynot);
            }
        }

        DBg.d(LogLevel.Trace, $"{fn} file {path} is not an interal protected file.");
        GeList? list = Lists.TryGetValue(listName!, out var tempList)
            ? tempList
            : null;
        // did we get a list? 
        if (list == null)
        {
            DBg.d(LogLevel.Critical, $"{fn}: {path} - {user.UserName} - NO LIST ASSOCIATED - WHY AM I HERE?");
            ynot = "No list associated with this file! Why am I here?";
            return (false, ynot);
        }

        (bool isVis, ynot) = IsListVisibleToUser(list, user, roles);
        if (isVis)
        {
            ynot = $"Path {path} is visible: {ynot}";
            return (isVis, ynot);
        }
        else
        {
            ynot = $"Path {path} is NOT visible: {ynot}";
            return (isVis, ynot);

        };
    }

    public static bool IsInternalProtected(string listName)
    {
        string fn = "IsInternalProtected"; DBg.d(LogLevel.Trace, fn);
        if (listName == "SuperUser" || listName == "listowner" || listName == "contributor")
        {
            return true;
        }
        else return false;
    }

    // For "protected" files like the edit and modify pages, internal .js files
    // we protect them with listnames==role_required_to_view
    // if a file's "listname" is one of these, compare with user's role
    public static bool IsInternalProtectedVisibleToUser(string listName, IList<string> roles, out string ynot)
    {
        string fn = "IsInternalProtectedVisibleToUser"; DBg.d(LogLevel.Trace, fn);
        bool isVis = false;
        switch (listName)
        {
            case "SuperUser":
                if (roles.Contains("SuperUser"))
                {
                    isVis = true;
                    ynot = "User is a super user";
                }
                else ynot = "User is not a super user";
                break;
            case "listowner":
                if (roles.Contains("SuperUser") || roles.Contains("listowner"))
                {
                    isVis = true;
                    ynot = "User is a super user or list owner";
                }
                else ynot = "User is not a super user or list owner";
                break;
            case "contributor":
                if (roles.Contains("SuperUser") || roles.Contains("listowner") || roles.Contains("contributor"))
                {
                    isVis = true;
                    ynot = "User is a super user, list owner, or contributor";
                }
                else
                {
                    ynot = "User is not a super user, list owner, or contributor";
                }
                break;
            default:
                ynot = "File is not internal protected file";
                break;
        }
        return isVis;
    }

    public static (bool, string?) IsListVisibleToUser(
        GeList list,
        GeFeSLEUser user,
        IList<string> roles)
    {
        string fn = "IsListVisibleToUser"; DBg.d(LogLevel.Trace, fn);
        string? ynot;
        bool isVis = false;
        if (list.Visibility == GeListVisibility.Public)
        {
            DBg.d(LogLevel.Critical, $"{fn}: - {user.UserName} - LIST IS PUBLIC - WHY AM I HERE? ");
            ynot = "List is public";
            isVis = true;
        }
        else if (roles.Contains("SuperUser"))
        {
            DBg.d(LogLevel.Trace, $"{fn}: - {user.UserName} - USER IS SUPERUSER");
            ynot = "User is a super user";
            isVis = true;
        }
        else
        {
            (bool allowed, ynot) = list.IsUserAllowedToView(user);
            if (allowed)
            {
                DBg.d(LogLevel.Trace, $"{fn}: - {user.UserName} - USER IS EXPLICITLY ALLOWED TO VIEW LIST");
                isVis = true;
            }
            else
            {
                DBg.d(LogLevel.Critical, $"ProtectedFiles.IsFileVisibleToUser: - HOW WE GET HERE? {user.UserName} {list.Name}");
            }
        }
        return (isVis, ynot);
    }
}




