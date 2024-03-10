using System.Collections.Concurrent;
using GeFeSLE;
using Microsoft.EntityFrameworkCore;

public static class ProtectedFiles
{



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

    public static bool IsFileVisibleToUser(GeFeSLEDb db, string path, string? username, out string? ynot)
    {
        DBg.d(LogLevel.Trace, $"ProtectedFiles.IsFileVisibleToUser: {path} - {username}");
        // at this pt the path should be a valid file in ur list of files AND
        // the user should a) exist // we'll have to trust that the user is logged in
        // but lets eliminate these cases first.
        ynot = null;
        // get the GeList that this file is associated with
        GeList? list = Files.TryGetValue(path, out var listName) && Lists.TryGetValue(listName, out var tempList)
    ? tempList
    : null;
        // did we get a list? 
        if (list == null)
        {
            DBg.d(LogLevel.Critical, $"ProtectedFiles.IsFileVisibleToUser: {path} - {username} - NO LIST ASSOCIATED - WHY AM I HERE?");
            ynot = "No list associated with this file! Why am I here?";
            return false;
        }

        if(IsListVisibleToUser(db, list, username!, out ynot)) {
            ynot = $"Path {path} is visible to user {username}";
            return true;
        }
        else {
            ynot = $"Path {path} is NOT visible to user {username} because {ynot}";
            return false;
        
        };
    }

    public static bool IsListVisibleToUser(GeFeSLEDb db, GeList list, string? username, out string? ynot) {
        // so we got a list. 
        // is the list public?
        ynot = null;
        if (list.Visibility == GeListVisibility.Public)
        {
            DBg.d(LogLevel.Critical, $"ProtectedFiles.IsFileVisibleToUser: - {username} - LIST IS PUBLIC - WHY AM I HERE? ");
            ynot = "List is public";
            return true;
        }

        // get the user; account for backdoor admin which may not be in our IdentityUser db
        if ((GlobalConfig.backdoorAdmin != null) &&
            (username != null) &&
            (username == GlobalConfig.backdoorAdmin.Username))
        {
            ynot = "Backdoor admin";
            return true;
        }

        else
        {
            var user = db.Users.FirstOrDefault(u => u.UserName == username);
            if (user == null)
            {
                DBg.d(LogLevel.Critical, $"ProtectedFiles.IsFileVisibleToUser: - {username} - USER NOT IN DB - WHY AM I HERE? ");
                ynot = "User not found";
                return false;
            }
            else if (db.UserRoles.Any(ur => ur.UserId == user.Id && ur.RoleId == "SuperUser"))
            {
                ynot = "User is a super user";
                return true;
            }
            else
            {
                switch (list.Visibility)
                {
                    case GeListVisibility.Contributors:
                        if (list.Contributors.Contains(user) || list.ListOwners.Contains(user) || list.Creator == user)
                        {
                            DBg.d(LogLevel.Trace, $"Related list is CONTRIB access. User is a contributor/owner/creator.");
                            return true;
                        }
                        else
                        {
                            ynot = $"User {username} is not a contributor/list owner/creator of list {list.Name}";
                            return false;
                        }
                    case GeListVisibility.ListOwners:
                        if (list.ListOwners.Contains(user) || list.Creator == user)
                        {
                            DBg.d(LogLevel.Trace, $"Related list is OWNER access. User is a list owner/creator.");
                            return true;
                        }
                        else
                        {
                            ynot = $"User {username} is not a list owner/creator of list {list.Name}";
                            return false;
                        }
                    case GeListVisibility.Private:

                        if (list.Creator == user)
                        {
                            DBg.d(LogLevel.Trace, $"Related list is PRIVATE access. User is the creator.");
                            return true;
                        }
                        else
                        {
                            ynot = $"User {username} is not the creator of list {list.Name}";
                            return false;
                        }
                    case GeListVisibility.Public:
                        ynot = "List is public";
                        return true;

                }
            }
        }
        DBg.d(LogLevel.Critical, $"ProtectedFiles.IsFileVisibleToUser: - WHY AM I HERE? ");
        return false;
    }
}