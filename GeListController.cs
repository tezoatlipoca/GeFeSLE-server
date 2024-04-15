
using System.IO;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GeFeSLE.Controllers
{
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
    }
}
