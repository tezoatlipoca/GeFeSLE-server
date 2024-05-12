
using System.Collections.Generic;
using System.IO;
using System.Text;
using Mastonet.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GeFeSLE.Controllers
{
    // TODO MOVE TO MASTO CONTROLLER
    public class MastoImportParams
    {
        public int num2Get { get; set; }
        public bool unbookmark { get; set; }
    }
    public class GoogleTaskList
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    public class GeListController : Controller
    {
        private readonly GeFeSLEDb _db;
        private readonly UserManager<GeFeSLEUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        private readonly GeListFileController _fileController;

        public GeListController(GeFeSLEDb db,
            UserManager<GeFeSLEUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IServiceScopeFactory serviceScopeFactory,
            GeListFileController geListFileController)
        {
            _db = db;
            _userManager = userManager;
            _roleManager = roleManager;
            _serviceScopeFactory = serviceScopeFactory;
            _fileController = geListFileController;
        }

        public async Task<IActionResult> ListsDelete(HttpContext httpContext,
            int id)
        {
            string fn = "/lists (DELETE)";
            DBg.d(LogLevel.Trace, fn);

            GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, _db, _userManager);
            var sessionUser = UserSessionService.amILoggedIn(httpContext);
            var modlist = await _db.Lists.FindAsync(id);
            if (modlist is null)
            {
                return NotFound();
            }
            (bool canMod, string? whyNot) = modlist.IsUserAllowedToModify(user);
            if (!canMod && sessionUser.Role != "SuperUser")
            {
                ContentResult cr = new ContentResult();
                cr.StatusCode = StatusCodes.Status403Forbidden;
                cr.Content = whyNot;
                return cr;
            }

            var filename = $"{modlist.Name}.html";
            var dest = Path.Combine(GlobalConfig.wwwroot!, filename);
            if (System.IO.File.Exists(dest))
            {
                DBg.d(LogLevel.Trace, $"{fn} Deleting {dest}");
                System.IO.File.Delete(dest);
            }
            // also delete the rss feed
            filename = $"rss-{modlist.Name}.xml";
            dest = Path.Combine(GlobalConfig.wwwroot!, filename);
            if (System.IO.File.Exists(dest))
            {
                DBg.d(LogLevel.Trace, $"{fn} Deleting {dest}");
                System.IO.File.Delete(dest);
            }
            // also delete the rss feed
            filename = $"{modlist.Name}.json";
            dest = Path.Combine(GlobalConfig.wwwroot!, filename);
            if (System.IO.File.Exists(dest))
            {
                DBg.d(LogLevel.Trace, $"{fn} Deleting {dest}");
                System.IO.File.Delete(dest);
            }
            _db.Lists.Remove(modlist);
            // also delete all items in the list
            _db.Items.Where(item => item.ListId == id);

            await _db.SaveChangesAsync();
            _ = GlobalStatic.GenerateHTMLListIndex(_db);
            return Ok();
        }


        public async Task<IActionResult> ListsPut(HttpContext httpContext,
            GeListDto inputList)
        {
            string fn = "/lists (PUT)";
            DBg.d(LogLevel.Trace, fn);


            string dumpList = System.Text.Json.JsonSerializer.Serialize(inputList);

            DBg.d(LogLevel.Trace, $"{fn}: {dumpList}");
            GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, _db, _userManager);
            var sessionUser = UserSessionService.amILoggedIn(httpContext);
            var modlist = await _db.Lists.FindAsync(inputList.Id);
            var namechange = false;
            if (modlist is null)
            {
                return NotFound();
            }
            // if check the role of the user; if they're superuser or creator of the list, they can modify it
            // if they're a listowner or contributor they can only modify the list if they're listed as
            // listowner or contributor ON the list itself. 
            (bool canMod, string? whyNot) = modlist.IsUserAllowedToModify(user);
            if (!canMod && sessionUser.Role != "SuperUser")
            {
                ContentResult cr = new ContentResult();
                cr.StatusCode = StatusCodes.Status403Forbidden;
                cr.Content = whyNot;
                return cr;

                //return new StatusCodeResult(StatusCodes.Status403Forbidden) { Value = whyNot }; // Nope, RequestDelegate error

                //return BadRequest(whyNot); //I know it shoudl be a 403 but 
                // there is no Results.Forbidden() method
                // and constructing a new StatusCodeResult(403) breaks
                // the min.api paradigm (): Delegate 'RequestDelegate' does not take x arguments
            }

            // if the name of the list has changed, delete the old html file; new one is created below anyway
            if (modlist.Name != inputList.Name)
            {
                // if the new name is a reserved list name, say no
                if(inputList.Name == GlobalConfig.modListName) {
                    return BadRequest($"List name {GlobalConfig.modListName} is RESERVED.");
                }

                var filename = $"{modlist.Name}.html";
                var dest = Path.Combine(GlobalConfig.wwwroot!, filename);
                if (System.IO.File.Exists(dest))
                {
                    DBg.d(LogLevel.Trace, $"{fn} Deleting {dest}");
                    System.IO.File.Delete(dest);
                }
                // also delete the rss feed
                filename = $"rss-{modlist.Name}.xml";
                dest = Path.Combine(GlobalConfig.wwwroot!, filename);
                if (System.IO.File.Exists(dest))
                {
                    DBg.d(LogLevel.Trace, $"{fn} Deleting {dest}");
                    System.IO.File.Delete(dest);
                }

                namechange = true;
            }

            modlist.Name = inputList.Name;
            modlist.Comment = inputList.Comment;
            modlist.ModifiedDate = DateTime.Now;
            modlist.SetVisibility(inputList.Visibility);
            _ = ProtectAttachments(modlist);
            await _db.SaveChangesAsync();
            await modlist.GenerateHTMLListPage(_db);
            await modlist.GenerateRSSFeed(_db);
            await modlist.GenerateJSON(_db);
            if (namechange)
            {
                _ = GlobalStatic.GenerateHTMLListIndex(_db);
            }
            return Ok();
        }
        // this method brokers calls to all import-to-lists-from-other-services methods
        // sent via the POST /lists/{list} or /ists/ endpoint
        public async Task<IResult> ListImport(HttpContext httpContext,
            GeListImportDto importer,
            GeList? destlist,
            GeFeSLEUser user)
        {
            string fn = "ListImport"; DBg.d(LogLevel.Trace, fn);
            DBg.d(LogLevel.Trace, $"importer: {System.Text.Json.JsonSerializer.Serialize(importer)}");
            if (importer is null)
            {
                return Results.BadRequest("No valid source service provided.");
            }
            else if (user is null)
            {

                var sb = new StringBuilder();
                string msg = $"You need to login. ";

                await GlobalStatic.GenerateUnAuthPage(sb, msg);
                return Results.Content(sb.ToString(), "text/html");

            }

            else
            {
                DBg.d(LogLevel.Trace, $"{fn} -- {importer.Service}");
                switch (importer.Service)
                {
                    case "Microsoft:StickyNotes":
                        return await ListsPostImportMSStickyNotes(httpContext, importer, destlist, user);
                    case "Google:Tasks":
                        DBg.d(LogLevel.Trace, $"{fn} -- Google:Tasks");
                        if (importer.Data is null)
                        {
                            DBg.d(LogLevel.Trace, $"{fn} -- google 2 get lists");
                            return await ListsPostGetGoogleTaskLists(httpContext, importer, user);
                        }
                        else
                        {
                            return await ListsPostImportGoogleTasks(httpContext, importer, destlist, user);
                        }
                    case "Mastodon:Bookmarks":
                        return await ListsPostImportMastodonBookmarks(httpContext, importer, destlist, user);
                    default:
                        return Results.BadRequest($"Unsupported service {importer.Service}");
                }
            }


        }

        // TODO: move this to MicrosoftController.cs
        public async Task<IResult> ListsPostImportMSStickyNotes(HttpContext httpContext,
            GeListImportDto importer,
            GeList destList,
            GeFeSLEUser user)
        {
            // get the access token from the session service
            string? token = UserSessionService.GetAccessToken(httpContext, ImportService.Microsoft);
            // handle this better
            if (token is null)
            {
                string msg = $"You need to login/authorize w/ Microsoft - I don't have a token for you. ";

                return Results.BadRequest(msg);
            }

            List<GeListItem> geListItems = await MicrosoftController.getMicrosoftOutlookTasks(httpContext, token);
            if (geListItems is null) return Results.NotFound($"No Sticky Notes for {user.UserName} found.");
            int numitems = geListItems.Count;
            if (numitems == 0) return Results.NotFound($"No Sticky Notes for {user.UserName} found.");
            int imported = 0;
            // iterate through the list of GeListItems
            foreach (GeListItem item in geListItems)
            {
                // set the listId of the item to the listid
                item.ListId = destList.Id;
                // add a blurb to the end of the .comment field saying who imported this item from where and when
                item.Comment += GlobalStatic.ImportAttribution(user.UserName, "Microsoft Sticky Notes", destList.Name);


                // add the item to the database
                _db.Items.Add(item);
                imported++;
            }
            // save the changes to the database
            await _db.SaveChangesAsync();

            // regenerate all the list artifacts
            _ = destList.GenerateHTMLListPage(_db);
            _ = destList.GenerateRSSFeed(_db);
            _ = destList.GenerateJSON(_db);

            //return Results.Redirect($"/{destList.Name}.html");
            return Results.Ok($"Imported {imported} tasks from Microsoft Sticky Notes into {destList.Name}");
        }

        public async Task<IResult> ListsPostGetGoogleTaskLists(HttpContext httpContext,
            GeListImportDto importer,
            GeFeSLEUser user)
        {
            var fn = "ListsPostGetGoogleTaskLists"; DBg.d(LogLevel.Trace, fn);


            // get the access token from the session service
            string? token = UserSessionService.GetAccessToken(httpContext, ImportService.Google);
            // handle this better
            if (token is null)
            {
                string msg = $"You need to login/authorize w/ Google - I don't have a token for you. ";

                return Results.BadRequest(msg);
            }

            List<Google.Apis.Tasks.v1.Data.TaskList> taskLists = await GoogleController.getGoogleTaskLists(token);

            if (taskLists is null)
            {
                return Results.NotFound($"No Google Task Lists for {user.UserName} found.");
            }
            if (taskLists.Count == 0) return Results.NotFound($"No Google Task Lists for {user.UserName} found.");

            //StringBuilder listChoosePage = await GoogleController.makeTaskListChooser(taskLists, _db, httpContext, _userManager, user);
            //return Results.Content(listChoosePage.ToString(), "text/html");
            //JSONify the list of task lists
            string json = System.Text.Json.JsonSerializer.Serialize(taskLists);
            DBg.d(LogLevel.Trace, $"{fn} --> {json}");
            return Results.Ok(taskLists);
        }

        public async Task<IResult> ListsPostImportGoogleTasks(HttpContext httpContext,
            GeListImportDto importer,
            GeList destList,
            GeFeSLEUser user)
        {
            var fn = "ListsPostImportGoogleTasks"; DBg.d(LogLevel.Trace, fn);

            // get the access token from the session service
            string? token = UserSessionService.GetAccessToken(httpContext, ImportService.Google);
            // handle this better
            if (token is null)
            {
                string msg = $"You need to login/authorize w/ Google - I don't have a token for you. ";
                return Results.BadRequest(msg);
            }
            if (importer.Data is null)
            {
                return Results.NotFound("No Google Task List ID specified.");

            }
            // the google task list id is in the importer.Data field
            // obtain it


            List<GeListItem> tasks = await GoogleController.getGoogleTasks(importer.Data, token);

            if (tasks is null)
            {
                return Results.NotFound($"No Google Tasks for {user.UserName} in list {importer.Data} found.");
            }
            int numtasks = tasks.Count;
            if (numtasks == 0) return Results.NotFound($"No Google Tasks for {user.UserName} in list {importer.Data} found.");
            int imported = 0;
            foreach (GeListItem item in tasks)
            {
                item.ListId = destList.Id;
                item.Comment += GlobalStatic.ImportAttribution(user.UserName, $"Google Task List {importer.Data}", destList.Name);
                _db.Items.Add(item);
                imported++;
            }
            await _db.SaveChangesAsync();

            // regenerate all the list artifacts
            _ = destList.GenerateHTMLListPage(_db);
            _ = destList.GenerateRSSFeed(_db);
            _ = destList.GenerateJSON(_db);

            //TODO: list.function that is responsible for a list's file name
            // do for each file type. 
            return Results.Ok($"Imported {imported} tasks from Google Task List {importer.Data} into {destList.Name}");
        }


        public async Task<IResult> ListsPostImportMastodonBookmarks(HttpContext httpContext,
            GeListImportDto importer,
            GeList destList,
            GeFeSLEUser user)
        {
            // num2Get and unbookmark are packaged as json in the importer.Data parameter
            DBg.d(LogLevel.Trace, $"importer.Data: {importer.Data}");
            // in the json form of: {"num2Get": 10, "unbookmark": true}
            MastoImportParams? mastoParams;
            try
            {
                mastoParams = System.Text.Json.JsonSerializer.Deserialize<MastoImportParams>(importer.Data);
                // improve this w/ try catch
            }
            catch (Exception e)
            {
                DBg.d(LogLevel.Error, $"Error deserializing Mastodon Import Parameters: {e.Message}");
                return Results.BadRequest($"Invalid Mastodon Import Parameters: {importer.Data} - e.g. {{\"num2Get\": 10, \"unbookmark\": true}}");
            }
            if (mastoParams is null) return Results.BadRequest($"Invalid Mastodon Import Parameters: {mastoParams}");

            DBg.d(LogLevel.Trace, $"unbookmark: {mastoParams.unbookmark}");
            DBg.d(LogLevel.Trace, $"num2Get: {mastoParams.num2Get}");

            if ((mastoParams.num2Get < 1) || (mastoParams.num2Get > 999)) return Results.BadRequest("num2Get must be between 1 and 999");

            // get the access token from the session service
            string? token = UserSessionService.GetAccessToken(httpContext, "mastodon");
            // handle this better
            if (token is null)
            {
                string msg = $"You need to login/authorize w/ Mastodon - I don't have a token for you. ";

                return Results.BadRequest(msg);
            }
            ApplicationToken appToken = MastoController.getMastoToken(httpContext);
            var processtoken = Guid.NewGuid().ToString();
            ProcessTracker.StartProcess(processtoken, $"{user.UserName} -- import {mastoParams.num2Get} Mastodon Bookmarks --> {destList.Name} <-- {appToken.instance}");
            BackgroundMastodonImport(appToken, token, processtoken, mastoParams.num2Get, mastoParams.unbookmark, destList, user);

            return Results.Ok(processtoken);
        }

        public async void BackgroundMastodonImport(ApplicationToken appToken, string? token, string processtoken, int num2Get, bool unbookmark, GeList destList, GeFeSLEUser user)
        {
            // can't use _db for this one because WE are disposed of once the endpoint request returns
            // and if we just create a new context we don't leverage any of the benefits from
            // the dependancy injection. So we're going to create a new scope and get a new context
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var scopedb = scope.ServiceProvider.GetRequiredService<GeFeSLEDb>();


                // array of strings to hold the status IDs of the statuses to unbookmark
                List<string> unbookmarkIDs = new List<string>();

                // create httpClient
                var client = new HttpClient();
                bool stillMorePages = true;

                int numGot = 0;
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);

                var apiUrl = $"{appToken.instance}/api/v1/bookmarks";


                // we need to improve this to handle exceptions etc. 


                while (stillMorePages && (numGot < num2Get))
                {
                    DBg.d(LogLevel.Trace, $"apiUrl: {apiUrl}");
                    var response = await client.GetAsync(apiUrl);
                    var content = await response.Content.ReadAsStringAsync();

                    // if the results are paged in the http response header we'll get a link header
                    // that looks like this:
                    // <https://mastodon.social/api/v1/bookmarks?max_id=123456>; rel="next"
                    // get that next link and use it to get the next page of bookmarks
                    var nextLink = response.Headers.GetValues("Link").FirstOrDefault();
                    if (nextLink is not null)
                    {
                        // parse the next link to get the url
                        var nextUrl = nextLink.Split(';')[0].Trim('<', '>');
                        apiUrl = nextUrl;
                    }
                    else
                    {
                        stillMorePages = false;
                    }
                    // back to processing THIS page. 
                    // the content is going to be an array of Status class objects
                    if (content is null)
                    {
                        //return Results.NotFound();
                        ProcessTracker.UpdateProcess(processtoken, "End of statuses");
                    }
                    else
                    {
                        // there's a bug in the Newtonsoft JSON library, when it deserializes the statuses, 
                        // it doesn't get the media_attachments. So we're going to use the System.Text.Json library
                        // TODO: go log that bug w/ Newtonsoft. 2 reproduce just switch back to their deserializer and
                        //  dump out the json - media attachments are missing. 
                        Status[]? Systemstatuses = System.Text.Json.JsonSerializer.Deserialize<Status[]>(content);

                        //Status[]? NewtonsoftStatuses = JsonConvert.DeserializeObject<Status[]>(content);
                        //var sys = JsonConvert.SerializeObject(Systemstatuses[0], Formatting.Indented);
                        //var newt = JsonConvert.SerializeObject(NewtonsoftStatuses[0], Formatting.Indented);

                        //StringBuilder sb = new StringBuilder();
                        //sb.AppendLine($"<!DOCTYPE html><html><body><table><tr><td style=\"vertical-align: top;\">Systemstatuses: <br><pre>{sys}</pre></td><td style=\"vertical-align: top;\">NewtonsoftStatuses:<br><pre>{newt}</pre></td></tr></table></body></html>");
                        //return Results.Content(sb.ToString(), "text/html");


                        if (Systemstatuses is null)
                        {
                            //return Results.NotFound(); 
                            ProcessTracker.UpdateProcess(processtoken, "End of statuses");

                        }
                        else
                        {
                            DBg.d(LogLevel.Trace, $"statuses: {Systemstatuses.Length}");
                            // iterate over the statuses and print them out
                            foreach (Status status in Systemstatuses)
                            {
                                // check to see the status's visibility
                                // only import it if its public or unlisted

                                if (status.Visibility != Mastonet.Visibility.Public &&
                                    status.Visibility != Mastonet.Visibility.Unlisted)
                                {
                                    DBg.d(LogLevel.Trace, $"skipping {status.Id} because its visibility is {status.Visibility}");

                                }
                                else
                                {
                                    // add the bookmark status class to the list
                                    var item = new GeListItem();
                                    item.ParseMastoStatus(status, destList.Id);
                                    item.Comment += GlobalStatic.ImportAttribution(user.UserName, $"Mastodon ({appToken.instance})", destList.Name);

                                    scopedb.Items.Add(item);

                                    // add the item.statusID to unbookmarkIDs
                                    if (unbookmark == true)
                                    {
                                        DBg.d(LogLevel.Trace, $"unbookmarking {status.Id}");
                                        unbookmarkIDs.Add(status.Id);
                                    }
                                    // only "count" the imported bookmarks
                                    numGot++;
                                    ProcessTracker.UpdateProcess(processtoken, $"Imported {numGot} of {num2Get} Mastodon Bookmarks");
                                    if (numGot >= num2Get) break;

                                }

                            }
                            await scopedb.SaveChangesAsync();
                        }
                    }
                    DBg.d(LogLevel.Trace, $"numGot: {numGot}");
                    if (numGot >= num2Get) break;
                } // end of while loop!
                  // we don't care about waiting for these tasks to complete. 
                ProcessTracker.UpdateProcess(processtoken, "Generating HTML pages...");
                await destList.GenerateHTMLListPage(scopedb);
                await destList.GenerateRSSFeed(scopedb);
                await destList.GenerateJSON(scopedb);
                ProcessTracker.UpdateProcess(processtoken, "Unbookmarking Mastodon Items...");
                await MastoController.unbookmarkMastoItems(token, appToken.instance, unbookmarkIDs);
                //return Results.Redirect($"/{destList.Name}.html");
                ProcessTracker.UpdateProcess(processtoken, "Completed");

            }
        }

        // given the list in question (and assuming its visibility has changed)
        // goes and
        // finds all items in the list
        // for each item, finds any referenced upload files
        // for each of those, sets file visibility protection to match the list
        public async Task ProtectAttachments(GeList list)
        {
            string fn = "ProtectAttachments"; DBg.d(LogLevel.Trace, fn);
            bool protect = true;
            if(list.Visibility > GeListVisibility.Public) {
                protect = true;
            } else {
                protect = false;
            }
             
            List<GeListItem> listItems = _db.Items.Where(item => item.ListId == list.Id).ToList();
            if(listItems.Count > 0) {
                foreach(GeListItem item in listItems) {
                    List<string> files = item.LocalFiles();
                    if(files.Count > 0) {
                        if(protect) {
                            _fileController.ProtectFiles(files, list.Name);
                        }
                        else {
                            _fileController.UnProtectFiles(files, list.Name);
                        }
                    }
                }
            } else {
                DBg.d(LogLevel.Trace, $"{fn} -- no items to process");
            }
            

            return;
        }
    }
}

