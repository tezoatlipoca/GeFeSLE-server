
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
    public class GeListController : Controller
    {
        private readonly GeFeSLEDb _db;
        private readonly UserManager<GeFeSLEUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public GeListController(GeFeSLEDb db,
            UserManager<GeFeSLEUser> userManager,
            RoleManager<IdentityRole> roleManager)
        {
            _db = db;
            _userManager = userManager;
            _roleManager = roleManager;
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

            await _db.SaveChangesAsync();
            await modlist.GenerateHTMLListPage(_db);
            await modlist.GenerateRSSFeed(_db);
            await modlist.GenerateJSON(_db);
            if (namechange)
            {
                GlobalStatic.GenerateHTMLListIndex(_db);
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
            if (importer is null)
            {
                return Results.BadRequest("No valid source service provided.");
            }
            else if (!importer.IsValid())
            {
                return Results.BadRequest($"Unsupported service {importer.Service}");
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
                switch (importer.Service)
                {
                    case "Microsoft:StickyNotes":
                        return await ListsPostImportMSStickyNotes(httpContext, importer, destlist, user);
                    case "Google:Tasks":
                        if (importer.Data is null)
                        {
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

            // iterate through the list of GeListItems
            foreach (GeListItem item in geListItems)
            {
                // set the listId of the item to the listid
                item.ListId = destList.Id;
                // add a blurb to the end of the .comment field saying who imported this item from where and when
                item.Comment += GlobalStatic.ImportAttribution(user.UserName, "Microsoft Sticky Notes", destList.Name);


                // add the item to the database
                _db.Items.Add(item);
            }
            // save the changes to the database
            await _db.SaveChangesAsync();

            // regenerate all the list artifacts
            _ = destList.GenerateHTMLListPage(_db);
            _ = destList.GenerateRSSFeed(_db);
            _ = destList.GenerateJSON(_db);

            return Results.Redirect($"/{destList.Name}.html");
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

            List<(string, string)> taskLists = await GoogleController.getGoogleTaskLists(token);

            if (taskLists is null)
            {
                return Results.NotFound($"No Google Task Lists for {user.UserName} found.");
            }
            if (taskLists.Count == 0) return Results.NotFound($"No Google Task Lists for {user.UserName} found.");

            StringBuilder listChoosePage = await GoogleController.makeTaskListChooser(taskLists, _db, httpContext, _userManager, user);

            return Results.Content(listChoosePage.ToString(), "text/html");
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


            List<GeListItem> tasks = await GoogleController.getGoogleTasks(importer.Data, token);

            if (tasks is null)
            {
                return Results.NotFound($"No Google Tasks for {user.UserName} found.");
            }
            int numtasks = tasks.Count;
            if (numtasks == 0) return Results.NotFound($"No Google Tasks for {user.UserName} found.");

            foreach (GeListItem item in tasks)
            {
                item.ListId = destList.Id;
                item.Comment += GlobalStatic.ImportAttribution(user.UserName, $"Google Task List {importer.Data}", destList.Name);
                _db.Items.Add(item);
            }
            await _db.SaveChangesAsync();

            // regenerate all the list artifacts
            _ = destList.GenerateHTMLListPage(_db);
            _ = destList.GenerateRSSFeed(_db);
            _ = destList.GenerateJSON(_db);

            //TODO: list.function that is responsible for a list's file name
            // do for each file type. 
            return Results.Redirect($"/{destList.Name}.html");
        }


        public async Task<IResult> ListsPostImportMastodonBookmarks(HttpContext httpContext,
            GeListImportDto importer,
            GeList destList,
            GeFeSLEUser user)
        {
            // num2Get and unbookmark are packaged as json in the importer.Data parameter
            // in the json form of: {"num2Get": 10, "unbookmark": true}
            MastoImportParams? mastoParams;
            try {
                mastoParams = System.Text.Json.JsonSerializer.Deserialize<MastoImportParams>(importer.Data);
                // improve this w/ try catch
            }
            catch (Exception e)
            {
                DBg.d(LogLevel.Error, $"Error deserializing Mastodon Import Parameters: {e.Message}");
                return Results.BadRequest($"Invalid Mastodon Import Parameters: {importer.Data} - e.g. {{num2Get: 10, unbookmark: true}}");
            }
            if(mastoParams is null) return Results.BadRequest($"Invalid Mastodon Import Parameters: {mastoParams}");
            
            DBg.d(LogLevel.Trace, $"unbookmark: {mastoParams.unbookmark}");


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

            // array of strings to hold the status IDs of the statuses to unbookmark
            List<string> unbookmarkIDs = new List<string>();

            // create httpClient
            var client = new HttpClient();
            bool stillMorePages = true;

            int numGot = 0;
            client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);

            var apiUrl = $"{appToken.instance}/api/v1/bookmarks";

            while (stillMorePages && (numGot < mastoParams.num2Get))
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
                    return Results.NotFound();
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


                    if (Systemstatuses is null) return Results.NotFound();
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

                            _db.Items.Add(item);

                            // add the item.statusID to unbookmarkIDs
                            if (mastoParams.unbookmark == true)
                            {
                                DBg.d(LogLevel.Trace, $"unbookmarking {status.Id}");
                                unbookmarkIDs.Add(status.Id);
                            }
                            // only "count" the imported bookmarks
                            numGot++;
                            if (numGot >= mastoParams.num2Get) break;

                        }

                    }
                    await _db.SaveChangesAsync();
                }
                DBg.d(LogLevel.Trace, $"numGot: {numGot}");
                if (numGot >= mastoParams.num2Get) break;
            } // end of while loop!
              // we don't care about waiting for these tasks to complete. 
            _ = destList.GenerateHTMLListPage(_db);
            _ = destList.GenerateRSSFeed(_db);
            _ = destList.GenerateJSON(_db);
            _ = MastoController.unbookmarkMastoItems(token, appToken.instance, unbookmarkIDs);
            return Results.Redirect($"/{destList.Name}.html");
        }
    }
}

