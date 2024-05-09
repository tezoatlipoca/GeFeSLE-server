
using System.Collections.Generic;
using System.IO;
using System.Text;
using Mastonet.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GeFeSLE.Controllers
{
    

    public class GeListFileController : Controller
    {

        private List<string> protectedFiles = new List<string>
        {
            "_edit.list.js",
            "_edituser.html",
            "_edituser.js",
            "_index.js",
            "_list_view.js",
            "_login.html",
            "_login.js",
            "_modal.google.js",
            "_modal.mastodon.js",
            "_password.change.html",
            "_password.change.js",
            "_utils.js",
            "gefesle.default.css",
            "gefesle.ff.png",
            "__samplebodyhead.html",
            "__samplefooter.html",
            "__samplehead.html",
            "_edit.item.html",
            "_edit.item.js",
            "_edit.list.html"
        };

        private readonly GeFeSLEDb _db;
        private readonly UserManager<GeFeSLEUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public GeListFileController(GeFeSLEDb db,
            UserManager<GeFeSLEUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IServiceScopeFactory serviceScopeFactory)
        {
            _db = db;
            _userManager = userManager;
            _roleManager = roleManager;
            _serviceScopeFactory = serviceScopeFactory;
        }

        // returns true if the file is one that is PRODUCED when a list is regenerated from db
        // i.e. listname.html, listname.json, rss-listname.xml
        private bool IsItAListFile(string filename)
        {
            string fn = $"IsItAListFile -- {filename}";
            DBg.d(LogLevel.Trace, fn);

            // get a list of all the list names in our db
            var lists = _db.Lists.Select(l => l.Name).ToList();
            // make that a hashset for fast lookup
            var listSet = new HashSet<string>(lists);
            // check if the filename is in the list
            // a - remove any .xml, .html or .json extension
            // b - remove any rss- prefix
            // c - check if the result is in the list of List names
            string listName = Path.GetFileNameWithoutExtension(filename);
            listName = listName.Replace("rss-", "");
            bool isListFile = listSet.Contains(listName);
            DBg.d(LogLevel.Debug, $"{fn} -- is {filename} a list file?  {isListFile}");
            return isListFile;

        }

        private bool IsItAProtectedFile(string filename) {
            string fn = $"IsItAProtectedFile -- {filename}";
            DBg.d(LogLevel.Trace, fn);
            bool isProtected = protectedFiles.Contains(filename);
            DBg.d(LogLevel.Debug, $"{fn} -- is {filename} a protected file?  {isProtected}");
            return isProtected;

        }

        private bool IsItInAGeListItem(string filename) {
            string fn = $"IsItInAGeListItem -- {filename}";
            DBg.d(LogLevel.Trace, fn);
         
            // all files uploaded to our service and referenced in a GeListItem's comment
            // are going to be referenced with a path similar to:
            //  ![receipt](https://lists.awadwatt.com/uploads/backadmin/screenshot-2024-05-09T00-42-31.139Z.png
            // or generalized like: 
            // (GlobalConfig.hostname + "/uploads/" + user.UserName + "/" + filename)
            // but since that uploads folder is IN the wwwroot,the filename we take in this method 
            // is going to a) start with [/]uploads and be after b) GlobalConfig.Hostname
            // so find every GeListItem in the db that matches that pattern
            var searchPattern = GlobalConfig.Hostname + filename;
            DBg.d(LogLevel.Debug, $"{fn} -- looking for Items that reference: >>{searchPattern}<<");
         
            var lists = _db.Items
            .Where(i => i.Comment.ToLower().Contains(searchPattern))
            .ToList();

            // iterate over the collection, print out the list id and item id
            foreach (var item in lists) {
                DBg.d(LogLevel.Debug, $"{fn} -- REFERENCED IN: {item.ListId} -- {item.Id}");
            }
            if (lists.Count == 0) {
                DBg.d(LogLevel.Debug, $"{fn} -- not referenced in any GeListItem");
            }
            return lists.Count > 0;
        
        }

        public async Task<StringBuilder> GetAllFilesInWWWRoot() {
            string fn = "GetAllFilesInWWWRoot";
            DBg.d(LogLevel.Trace, fn);
            
            string[] files = Directory.GetFiles(GlobalConfig.wwwroot, "*.*", SearchOption.AllDirectories);
            StringBuilder sb = new StringBuilder();
            GlobalStatic.GenerateHTMLHead(sb, "All Files in wwwroot");
            foreach (string file in files) {
                DBg.d(LogLevel.Debug, $"{fn} -- {file}");
                var isListFile = IsItAListFile(file);
                var isProtectedFile = IsItAProtectedFile(file);
                var isInAGeListItem = IsItInAGeListItem(file);

                var msg =  $"{fn} -- {file} -- isListFile: {isListFile} -- isProtectedFile: {isProtectedFile} -- isInAGeListItem: {isInAGeListItem}";
                DBg.d(LogLevel.Trace, msg);
                sb.AppendLine(msg);

            }
            GlobalStatic.GeneratePageFooter(sb);
            return sb; 
        }

      
    }
}

