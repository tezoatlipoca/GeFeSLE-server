using Mastonet.Entities;
using Google.Apis.Tasks.v1.Data;
using Newtonsoft.Json;
using System.Text;
using Microsoft.AspNetCore.Identity;
using GeFeSLE;
using Microsoft.EntityFrameworkCore;


public static class GoogleController
{

    private static string GOOGLE_TASKS_API_ME_LISTS_URL = "https://www.googleapis.com/tasks/v1/users/@me/lists";
    private static string GOOGLE_TASKS_API_LIST_TASKS_URL = "https://tasks.googleapis.com/tasks/v1/lists/{tasklist}/tasks";
    public static string GOOGLE_TASKS_API_TASKS_OAUTH_SCOPE = "https://www.googleapis.com/auth/tasks";

    //https://developers.google.com/tasks/reference/rest/v1/tasklists/list
    // returns
    // {
    //   "kind": string,
    //   "etag": string,
    //   "nextPageToken": string,
    //   "items": [
    //     {
    //       object (TaskList)
    //     }
    //   ]
    // }
    //
    // where each Tasklist is
    // 
    // {
    //   "kind": string,
    //   "id": string,
    //   "etag": string,
    //   "title": string,
    //   "updated": string,
    //   "selfLink": string
    // }
    // 
    // we need 
    // title (to show to user)
    // id (to get tasks)

    public static async Task<List<Google.Apis.Tasks.v1.Data.TaskList>> getGoogleTaskLists(string? accessToken)
    {
        string fn = "getGoogleTaskLISTS";
        DBg.d(LogLevel.Trace, fn);

        List<Google.Apis.Tasks.v1.Data.TaskList> listNames = new List<Google.Apis.Tasks.v1.Data.TaskList>();

        if (accessToken == null)
        {
            DBg.d(LogLevel.Error, $"{fn} - No access token found in session");
            return listNames;
        }

        // get the list of task lists
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + accessToken);
        DBg.d(LogLevel.Trace, $"{fn} - Getting task lists from {GOOGLE_TASKS_API_ME_LISTS_URL}");
        DBg.d(LogLevel.Trace, $"{fn} - Access token: {accessToken}");
        var response = await client.GetAsync(GOOGLE_TASKS_API_ME_LISTS_URL);
        // handle various response.statusCodes
        switch (response.StatusCode)
        {
            // TODO: add a case in here to check for .Forbidden, which is where the API has to be enabled
            // in your Google account. Put instructions on how to do that:
            // https://console.developers.google.com/apis/api/tasks.googleapis.com/overview?project=633241786177
            case System.Net.HttpStatusCode.OK:
                var content = await response.Content.ReadAsStringAsync();
                // convert content to dynamic json object
                var taskLists = JsonConvert.DeserializeObject<Google.Apis.Tasks.v1.Data.TaskLists>(content);
                // Now you can access the task lists through taskLists.Items
                if(taskLists == null || taskLists.Items == null)
                {
                    DBg.d(LogLevel.Error, $"{fn} - No task lists found");
                    return listNames;
                }
                foreach (TaskList taskList in taskLists.Items)
                {
                    DBg.d(LogLevel.Trace, $"{fn} - Found task list: {taskList.Title} ({taskList.Id})");
                    listNames.Add(taskList);
                }
                break;
            default:
                string error = dumpResponse(response);
                DBg.d(LogLevel.Error, $"{fn} - Error getting task lists: {error}");
                break;

        }
        return listNames;
    }

    public static string dumpResponse(HttpResponseMessage response)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"StatusCode: {response.StatusCode}");
        sb.AppendLine($"ReasonPhrase: {response.ReasonPhrase}");
        sb.AppendLine($"Headers: {response.Headers}");
        sb.AppendLine($"Content: {response.Content}");
        string dumpResponse = sb.ToString();
        return dumpResponse;
    }
    public static string dumpRequest(HttpRequestMessage request)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"Method: {request.Method}");
        sb.AppendLine($"RequestUri: {request.RequestUri}");
        sb.AppendLine($"Headers: {request.Headers}");
        sb.AppendLine($"Content: {request.Content}");
        string dumpRequest = sb.ToString();
        return dumpRequest;
    }

    public static async Task<List<GeListItem>> getGoogleTasks(string listid, string accessToken)
    {
        string fn = "getGoogleTasks";
        DBg.d(LogLevel.Trace, fn);

        List<GeListItem> geListItems = new List<GeListItem>();

        if (accessToken == null)
        {
            DBg.d(LogLevel.Error, $"{fn} - No access token found in session");
            return geListItems;
        }
        if (listid == null)
        {
            DBg.d(LogLevel.Error, $"{fn} - No list id given");
            return geListItems;
        }

        // get the list of task lists
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + accessToken);

        var url = GOOGLE_TASKS_API_LIST_TASKS_URL;
        // replace the "{tasklist}" in the url with the listId
        url = url.Replace("{tasklist}", listid);

        var response = await client.GetAsync(url);
        // handle various response.statusCodes
        switch (response.StatusCode)
        {
            case System.Net.HttpStatusCode.OK:
                var content = await response.Content.ReadAsStringAsync();

                var listOfTasks = JsonConvert.DeserializeObject<Google.Apis.Tasks.v1.Data.Tasks>(content);
                // Now you can access the task lists through taskLists.Items
                foreach (Google.Apis.Tasks.v1.Data.Task task in listOfTasks.Items)
                {
                    GeListItem newItem = parseGoogleTask(task);
                    geListItems.Add(newItem);
                }



                break;
            default:
                DBg.d(LogLevel.Error, $"{fn} - Error getting task lists: {response.StatusCode}");
                break;

        }
        return geListItems;
    }

    // {
    //   "completed": null,
    //   "deleted": null,
    //   "due": null,
    //   "etag": "\"MTc0ODI3MDg0Nw\"",
    //   "hidden": null,
    //   "id": "MTAyNzkyMjI5MDU5NzY5NDE0MjA6MDo1NDYxOTE0MzQ",
    //   "kind": "tasks#task",
    //   "links": [],
    //   "notes": null,
    //   "parent": null,
    //   "position": "00000000000000000001",
    //   "selfLink": "https://www.googleapis.com/tasks/v1/lists/MTAyNzkyMjI5MDU5NzY5NDE0MjA6MDow/tasks/MTAyNzkyMjI5MDU5NzY5NDE0MjA6MDo1NDYxOTE0MzQ",
    //   "status": "needsAction",
    //   "title": "",
    //   "updated": "2019-06-18T01:29:59.000Z",
    //   "webViewLink": "https://tasks.google.com/task/10279222905976941420:0:546191434"
    // }



    private static GeListItem parseGoogleTask(Google.Apis.Tasks.v1.Data.Task task)
    {
        string fn = "parseGoogleTask";
        DBg.d(LogLevel.Trace, fn);

        if (task == null)
        {
            DBg.d(LogLevel.Error, $"{fn} - task is null");
            return null;
        }

        // deserialize task and print it
        Console.WriteLine(JsonConvert.SerializeObject(task, Formatting.Indented));


        GeListItem newItem = new GeListItem();
        newItem.Name = task.Title;
        newItem.Comment = task.Notes;
        //newItem. = task.Due;
        // newItem.Completed = task.Status == "completed";
        return newItem;
    }

    // Add this line


    // Add other necessary using directives

    public static async Task<StringBuilder> makeTaskListChooser(List<(string, string)> taskLists,
        GeFeSLEDb db,
        HttpContext httpContext,
        UserManager<GeFeSLEUser> userManager,
        GeFeSLEUser me)
    {
        string fn = "makeTaskListChooser";
        DBg.d(LogLevel.Trace, fn);

        StringBuilder sb = new StringBuilder();
        await GlobalStatic.GenerateHTMLHead(sb, "Choose a Task List");
        sb.AppendLine("<h1>Choose a Google Task List & Destination List</h1>");
        sb.AppendLine("<p>Source (Google) task list</p>");
        sb.AppendLine("<form>");
        sb.AppendLine("<select name=\"sourceList\">");
        foreach ((string, string) taskList in taskLists)
        {
            sb.AppendLine($"<option value=\"{taskList.Item2}\">{taskList.Item1}</opton>");
        }
        sb.AppendLine("</select>");
        sb.AppendLine("<p>Destination list</p>");
        sb.AppendLine("<select name=\"destinationList\">");
        List<GeList> lists = await db.Lists.ToListAsync();
        IList<string> myRoles = await userManager.GetRolesAsync(me);
        foreach (GeList list in lists)
        {
            (bool canISee, string? ynot) = ProtectedFiles.IsListVisibleToUser(list, me, myRoles);
            if (canISee)
            {
                sb.AppendLine($"<option value=\"{list.Id}\">{list.Name}</option>");
            }
        }
        sb.AppendLine("</select>");
        sb.AppendLine("</form>");

        sb.AppendLine("<script>");
        sb.AppendLine("function redirectToImport() {");
        sb.AppendLine("  var sourceList = document.getElementsByName('sourceList')[0].value;");
        sb.AppendLine("  var destinationList = document.getElementsByName('destinationList')[0].value;");
        sb.AppendLine("  window.location.href = '/googletasklistimport/' + sourceList + '/' + destinationList;");
        sb.AppendLine("}");
        sb.AppendLine("</script>");
        sb.AppendLine("<button onclick='redirectToImport()'>Import Tasks</button>");


        await GlobalStatic.GeneratePageFooter(sb);
        return sb;
    }

}

