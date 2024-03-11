using System.Collections.Concurrent;
using GeFeSLE;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SQLitePCL;
using Newtonsoft.Json; // Add this import statement

public static class ProtectedFiles
{
    // define a bunch of defaults: List<string>
    private static readonly string[] internalFiles = new string[] { 
        "/_edit.item.html", 
        "/_edit.item.js", 
        "/_edit.list.html",
        "/_edituser.html",
        "/_edituser.js",
        "/_mastobookmark.html",
        "/_mastobookmark.js"
    };


    private static ConcurrentDictionary<string, string> Files = new ConcurrentDictionary<string, string>();
    // first string is the FILE name, the second is the list name, which is the KEY to the next dictionary
    private static ConcurrentDictionary<string, GeList> Lists = new ConcurrentDictionary<string, GeList>();


    // add a file to the list
    public static void AddFile(string path, string listName)
    {
        Files.TryAdd(path, listName);
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
        foreach (string file in internalFiles)
        {
            AddFile(file, "internal");
        }
    }
    public static void AddList(GeList list)
    {
        DBg.d(LogLevel.Trace, $"ProtectedFiles.AddList: {list.Name}");
        if (list.Name != null)
        {
            AddFile($"/rss-{list.Name}.xml", list.Name);
            AddFile($"/{list.Name}.json", list.Name);
            AddFile($"/{list.Name}.html", list.Name);
            Lists.TryAdd(list.Name!, list);
        }
    }
    public static bool RemoveList(GeList list)
    {
        DBg.d(LogLevel.Trace, $"ProtectedFiles.RemoveList: {list.Name}");
        if (list.Name != null)
        {
            RemoveFile($"/rss-{list.Name}.xml");
            RemoveFile($"/{list.Name}.json");
            RemoveFile($"/{list.Name}.html");
            return Lists.TryRemove(list.Name!, out _);
        }
        return false;
    }

    public static async Task<(bool, string?)> IsFileVisibleToUser(HttpContext context,
        string path,
        GeFeSLEUser user,
        UserManager<GeFeSLEUser> userManager)
    {

        

        var serializedFiles = JsonConvert.SerializeObject(Files, Formatting.Indented);
        DBg.d(LogLevel.Trace, $"HERE DA FILES: {serializedFiles}");

        string? ynot = null;

        GeList? list = Files.TryGetValue(path, out var listName) && Lists.TryGetValue(listName, out var tempList)
            ? tempList
            : null;
        // did we get a list? 
        if (list == null)
        {
            DBg.d(LogLevel.Critical, $"ProtectedFiles.IsFileVisibleToUser: {path} - {user.UserName} - NO LIST ASSOCIATED - WHY AM I HERE?");
            ynot = "No list associated with this file! Why am I here?";
            return (false, ynot);
        }

        (bool isVis, ynot) = await IsListVisibleToUser(context, list, user, userManager);
        if(isVis)
        {
            ynot = $"Path {path} is visible: {ynot}";
            return (isVis, ynot);
        }
        else
        {
            ynot = $"Path {path} is NOT visible: {ynot}";
            return (isVis,ynot);

        };
    }

    public static async Task<(bool, string?)> IsListVisibleToUser(HttpContext context,
        GeList list,
        GeFeSLEUser user,
        UserManager<GeFeSLEUser> userManager)
    {
        // so we got a list. 
        // is the list public?
        string? ynot = null;
        if (list.Visibility == GeListVisibility.Public)
        {
            DBg.d(LogLevel.Critical, $"ProtectedFiles.IsFileVisibleToUser: - {user.UserName} - LIST IS PUBLIC - WHY AM I HERE? ");
            ynot = "List is public";
            return (true, ynot);
        }

        bool isSuperUser = await userManager.IsInRoleAsync(user, "SuperUser");
        if (UserSessionService.amILoggedIn(context) && isSuperUser)
        {
            DBg.d(LogLevel.Critical, $"ProtectedFiles.IsFileVisibleToUser: - {user.UserName} - USER IS SUPERUSER");
            ynot = "User is a super user";
            return (true, ynot);
        }

        switch (list.Visibility)
        {
            case GeListVisibility.Contributors:
                if (list.Contributors.Contains(user) || list.ListOwners.Contains(user) || list.Creator == user)
                {
                    DBg.d(LogLevel.Trace, $"Related list is CONTRIB access. User is a contributor/owner/creator.");
                    return (true, ynot);
                }
                else
                {
                    ynot = $"User {user.UserName} is not a contributor/list owner/creator of list {list.Name}";
                    return (false, ynot);
                }
            case GeListVisibility.ListOwners:
                if (list.ListOwners.Contains(user) || list.Creator == user)
                {
                    DBg.d(LogLevel.Trace, $"Related list is OWNER access. User is a list owner/creator.");
                    return (true, ynot);
                }
                else
                {
                    ynot = $"User {user.UserName} is not a list owner/creator of list {list.Name}";
                    return (false, ynot);
                }
            case GeListVisibility.Private:

                if (list.Creator == user)
                {
                    DBg.d(LogLevel.Trace, $"Related list is PRIVATE access. User is the creator.");
                    return (true, ynot);
                }
                else
                {
                    ynot = $"User {user.UserName} is not the creator of list {list.Name}";
                    return (false, ynot);
                }
            case GeListVisibility.Public:
                ynot = "List is public";
                return (true, ynot);

        }
        DBg.d(LogLevel.Critical, $"ProtectedFiles.IsFileVisibleToUser: - HOW WE GET HERE? {user.UserName} {list.Name}");
        ynot = "How did we get here?";
        return (false, ynot);
    }
}
        
    

    
