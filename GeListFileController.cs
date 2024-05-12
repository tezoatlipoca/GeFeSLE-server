
using System.Collections.Generic;
using System.IO;
using System.Text;
using Mastonet.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace GeFeSLE.Controllers
{


    public class GeListFileController : Controller
    {

        /// <summary>
        /// List of protected files "owned" by the application
        /// </summary>
        /// <remarks>
        /// Matches listing in 
        private static List<string> protectedFiles = new List<string>
        {
            "/lib/easymde/easymde.min.css",
            "/lib/easymde/easymde.min.js",
            "/__samplebodyhead.html",
            "/__samplefooter.html",
            "/__samplehead.html",
            "/_edit.item.html",
            "/_edit.item.js",
            "/_edit.list.html",
            "/_edit.list.js",
            "/_edituser.html",
            "/_edituser.js",
            "/_index.js",
            "/_list_view.js",
            "/_login.html",
            "/_login.js",
            "/_modal.google.js",
            "/_modal.mastodon.js",
            "/_modal.report.item.js",
            "/_password.change.html",
            "/_password.change.js",
            "/_utils.js",
            "/gefesle.default.css",
            "/gefesleff.png",
            "/_fileupload.js"
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

        private bool IsItAProtectedFile(string filename)
        {
            string fn = $"IsItAProtectedFile -- {filename}"; DBg.d(LogLevel.Trace, fn);
            bool isProtected = protectedFiles.Contains(filename);
            DBg.d(LogLevel.Debug, $"{fn} -- is {filename} a protected file?  {isProtected}");
            return isProtected;

        }

        private bool IsItInAGeListItem(string filename)
        {
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
            .Where(i => i.Comment.ToLower().Contains(searchPattern.ToLower()))
            .ToList();

            // iterate over the collection, print out the list id and item id
            foreach (var item in lists)
            {
                DBg.d(LogLevel.Debug, $"{fn} -- REFERENCED IN: {item.ListId} -- {item.Id}");
            }
            if (lists.Count == 0)
            {
                DBg.d(LogLevel.Debug, $"{fn} -- not referenced in any GeListItem");
            }
            return lists.Count > 0;

        }

        public async Task<StringBuilder> GetAllFilesInWWWRoot()
        {
            string fn = "GetAllFilesInWWWRoot";
            DBg.d(LogLevel.Trace, fn);

            string[] files = Directory.GetFiles(GlobalConfig.wwwroot, "*.*", SearchOption.AllDirectories);
            StringBuilder sb = new StringBuilder();
            GlobalStatic.GenerateHTMLHead(sb, "All Files in wwwroot");

            // Add CSS
            sb.AppendLine("<style>");
            sb.AppendLine(".red-row { color: red; }");
            sb.AppendLine("</style>");
            // we'll want to remove the wwwroot from file pathnames
            // the GlobalConfig.wwwroot could be a relative path. Convert that to an absolute path to match what
            // the test functions are expecting (paths relative TO the wwwroot)

            // convert GlobalConfig.wwwroot to absolute path
            string absoroot = Path.GetFullPath(GlobalConfig.wwwroot);

            sb.AppendLine("<table>");
            sb.AppendLine("<thead><tr><th>File</th><th>generated by a list?</th><th>protected?</th><th>upload 2 item?</th></tr></thead>");
            foreach (string file in files)
            {
                DBg.d(LogLevel.Debug, $"{fn} -- {file}");

                // remove everything before and including "\wwroot\" from filename
                var relpath = file.Substring(file.IndexOf(absoroot) + absoroot.Length);
                // change any \'s to /s
                relpath = relpath.Replace("\\", "/");

                var isListFile = IsItAListFile(relpath) ? "list file" : "";
                var isProtectedFile = IsItAProtectedFile(relpath) ? "protected" : "";
                var isInAGeListItem = IsItInAGeListItem(relpath) ? "upload" : "";

                if (relpath == "/index.html")
                {
                    isListFile = "list file";
                }

                var rowClass = (isListFile == "" && isProtectedFile == "" && isInAGeListItem == "") ? "red-row" : "";
                var msg = $"<tr class='{rowClass}'><td>{relpath}</td><td>{isListFile}</td><td>{isProtectedFile}</td><td>{isInAGeListItem}</td></tr>";
                DBg.d(LogLevel.Trace, msg);
                sb.AppendLine(msg);

            }
            sb.AppendLine("</table>");
            GlobalStatic.GeneratePageFooter(sb);
            return sb;
        }

        /// <summary>Verifies the required static `.html` and `.js` files for the web UI are present 
        /// in the wwwroot</summary>
        /// <remarks>For the specified file, if its one of the ones that is "static" and is
        /// bundled in the application as a resource at compile time, this makes sure that 
        /// it is present and unmodified; if not, it recreates it. 
        /// </remarks>
        /// <param name="filename">Filename (relative to configured wwwroot) to check. </param>
        /// <param name="ynot">Error/failure reason</param>
        /// <returns>
        /// * If file (is required/protected and) exists and is unmodified, returns true
        /// * If file (is required/protected and) doesn't exist/is modified, it is created/overwritten and this returns true
        /// * If file is not required/protected, returns true
        /// * Returns false on error along with ynot
        /// </returns>
        public bool VerifyPackagedResourceFile(string filename, out string? ynot)
        {
            string fn = "VerifyPackagedResourceFile"; DBg.d(LogLevel.Trace, fn);
            if (filename.IsNullOrEmpty()) { ynot = "filename is null or empty"; return false; }

            if (!IsItAProtectedFile(filename)) { ynot = "file is not protected"; return true; }

            // remember the filename is going to be relative to the wwwroot (which may also)
            // be a relative path from where we were launched from; combine them and get the 
            // absolute path of this file
            DBg.d(LogLevel.Trace, $"{fn} -- {GlobalConfig.wwwroot} + {filename}");
            // trim a starting slash, it futzes with relative pathing
            filename = filename.TrimStart('/');
            string absfilename = Path.GetFullPath(Path.Combine(GlobalConfig.wwwroot, filename));
            DBg.d(LogLevel.Trace, $"{fn} -- absolute path of file: {absfilename}");

            // does it exist? we're going to blow it away regardless, this is just for 
            // debugging purposes
            if (!System.IO.File.Exists(absfilename))
            {
                DBg.d(LogLevel.Trace, $"{fn} -- file doesn't exist");
                ynot = "file doesn't exist";

            }
            else
            {
                DBg.d(LogLevel.Trace, $"{fn} -- file DOES exist. CREATING");
            }
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = $"{assembly.GetName().Name}.wwwroot.{filename.Replace('/', '.')}";
                DBg.d(LogLevel.Trace, $"{fn} -- RECREATING: {resourceName}");
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        DBg.d(LogLevel.Critical, $"{fn} -- resource doesn't exist in binary!");
                        ynot = $"{filename} does not exist in binary!";
                        return false;
                    }

                    // Determine the file extension
                    string extension = Path.GetExtension(filename);

                    // List of extensions for binary files
                    var binaryExtensions = new List<string> { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".zip", ".exe", ".dll" };

                    // Check if the file is binary
                    if (binaryExtensions.Contains(extension))
                    {
                        // Handle binary files
                        using (var memoryStream = new MemoryStream())
                        {
                            stream.CopyTo(memoryStream);
                            var content = memoryStream.ToArray();
                            // Ensure the directory exists
                            var dir = Path.GetDirectoryName(absfilename);
                            Directory.CreateDirectory(dir);
                            // Write the file as binary data
                            System.IO.File.WriteAllBytes(absfilename, content);
                        }
                    }
                    else
                    {
                        // Handle text files
                        using (var reader = new StreamReader(stream))
                        {
                            var content = reader.ReadToEnd();
                            // Ensure the directory exists
                            var dir = Path.GetDirectoryName(absfilename);
                            Directory.CreateDirectory(dir);
                            // Write the file as text
                            System.IO.File.WriteAllText(absfilename, content);
                        }
                    }
                }
                ynot = null;
                return true;
            }
            catch (Exception e)
            {
                ynot = e.ToString();
                return false;
            }

        }

        public async Task CleanWWWRoot()
        {
            string fn = "CleanWWWRoot"; DBg.d(LogLevel.Trace, fn);

            // delete all files in GlobalConfig.wwwroot
            string[] files = Directory.GetFiles(GlobalConfig.wwwroot, "*.*", SearchOption.TopDirectoryOnly);
            foreach (string file in files)
            {
                DBg.d(LogLevel.Trace, $"{fn} -- deleting {file}");
                System.IO.File.Delete(file);
            }
            string dontDELETE = $"{GlobalConfig.wwwroot}\\{GlobalStatic.uploadsFolder}";
            string[] subdir = Directory.GetDirectories(GlobalConfig.wwwroot, "*", SearchOption.TopDirectoryOnly);
            foreach (string sub in subdir)
            {

                DBg.d(LogLevel.Trace, $"{fn} -- {dontDELETE} ? {sub}");
                if (sub != dontDELETE)
                {
                    DBg.d(LogLevel.Trace, $"{fn} -- deleting D:{sub}");
                    System.IO.Directory.Delete(sub, true);
                }
            }

            // var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            // foreach (var resourceName in assembly.GetManifestResourceNames())
            //     {
            //     Console.WriteLine(resourceName);
            //     }
        }

        public async Task FreshStart()
        {
            string fn = "FreshStart"; DBg.d(LogLevel.Trace, fn);
            await CleanWWWRoot();

            // verify all the protected files
            foreach (var file in protectedFiles)
            {
                DBg.d(LogLevel.Trace, $"{fn} -- {file}");
                string ynot;
                if (!VerifyPackagedResourceFile(file, out ynot))
                {
                    DBg.d(LogLevel.Error, $"{fn} -- {file} -- {ynot}");
                }
            }
            return;
        }

        public async Task<List<GeList>> WhatListIsItIn(string filename)
        {
            var searchPattern = GlobalConfig.Hostname + filename;
            DBg.d(LogLevel.Debug, $"Looking for Items that reference: >>{searchPattern}<<");

            List<GeListItem> items = _db.Items
            .Where(i => i.Comment.ToLower().Contains(searchPattern.ToLower()))
            .ToList();

            List<GeList> fileLists = new List<GeList> { };

            // iterate over the collection, print out the list id and item id
            foreach (var item in items)
            {
                DBg.d(LogLevel.Debug, $"REFERENCED IN: {item.ListId} -- {item.Id}");
                // now find the list with that item.ListId
                GeList list = await _db.Lists.FindAsync(item.ListId);
                if (list is null)
                {
                    DBg.d(LogLevel.Critical, $"HOW CAN I HAVE AN ITEM ASSIGNED TO AN INVALID LIST??");
                }
                else
                {
                    fileLists.Add(list);
                }

            }

            return fileLists;
        }

        public async Task ProtectUploadFiles()
        {
            DBg.d(LogLevel.Trace, null);

            // get the absolute path of the uploads folder
            string uploadFolderAbsPath = Path.Combine(GlobalConfig.wwwroot, GlobalStatic.uploadsFolder);
            DBg.d(LogLevel.Trace, $"Looking for files in uploads fldr: {uploadFolderAbsPath}");
            string[] files = Directory.GetFiles(uploadFolderAbsPath, "*.*", SearchOption.AllDirectories);
            foreach (string file in files)
            {
                // wait - the web path of the file is going to be relative; take off the abs path again
                string relfile = file.Replace(uploadFolderAbsPath, $"/{GlobalStatic.uploadsFolder}");
                relfile = relfile.Replace("\\", "/");
                DBg.d(LogLevel.Trace, $"Looking for mentions of {relfile}");
                List<GeList> filesLists = await WhatListIsItIn(relfile);
                if (filesLists.Count > 0)
                {
                    DBg.d(LogLevel.Trace, $"File {relfile} is in one or more lists.");
                    // iterate over them and find the LOWEST; TODO: manage this on a per list basis
                    // but if the same upload file is used in TWO LISTS, we have to grant it the restriction 
                    // level of the lowest or least secure one
                    GeListVisibility vislvl = GeListVisibility.Private;
                    string? vislistName = null;
                    foreach (GeList list in filesLists)
                    {
                        if (list.Visibility < vislvl)
                        {
                            vislvl = list.Visibility;
                            vislistName = list.Name;
                            DBg.d(LogLevel.Trace, $"File {relfile} is in list {list.Name} with visibility {list.Visibility}");
                        }
                    }
                    if (vislvl > GeListVisibility.Public && vislistName is not null)
                    {
                        ProtectedFiles.AddFile(relfile, vislistName);
                    }
                }
                else
                {
                    DBg.d(LogLevel.Trace, $"File {file} is not in any lists (ORPHAN)");
                }
            }

        }

        public void ProtectFiles(List<string> itemfiles, string listName)
        {
            if (itemfiles.Count > 0)
            {
                foreach (string file in itemfiles)
                {
                    DBg.d(LogLevel.Trace, $"ITEM ATTACHMENT PROTECTION SET: {file}");
                    // is the file REAL? 
                    // its absolute path is going to be GlobalConfig.wwwroot+file
                    string filepath = Path.Combine(GlobalConfig.wwwroot, file.TrimStart('/'));
                    if (System.IO.File.Exists(filepath))
                    {
                        ProtectedFiles.AddFile(file, listName);
                        DBg.d(LogLevel.Trace, $"Protected file re: list {listName}");
                    }
                    else
                    {
                        DBg.d(LogLevel.Warning, $"File {file} doesn't exist");
                    }
                }
            }
        }

        public void UnProtectFiles(List<string> itemfiles, string listName)
        {
            if (itemfiles.Count > 0)
            {
                foreach (string file in itemfiles)
                {
                    DBg.d(LogLevel.Trace, $"ITEM ATTACHMENT PROTECTION SET: {file}");
                    // is the file REAL? 
                    // its absolute path is going to be GlobalConfig.wwwroot+file
                    string filepath = Path.Combine(GlobalConfig.wwwroot, file.TrimStart('/'));
                    if (System.IO.File.Exists(filepath))
                    {
                        ProtectedFiles.RemoveFile(file);
                        DBg.d(LogLevel.Trace, $"UNprotected file re: list {listName}");
                    }
                    else
                    {
                        DBg.d(LogLevel.Warning, $"File {file} doesn't exist");
                    }
                }


            }
        }
    }
}

