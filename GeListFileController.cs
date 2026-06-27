
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Mastonet.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;

namespace GeFeSLE.Controllers
{


    public class GeListFileController : Controller
    {
        public sealed class FileAuditRow
        {
            public string RelativePath { get; set; } = string.Empty;
            public long SizeBytes { get; set; }
            public bool IsListFile { get; set; }
            public bool IsProtectedFile { get; set; }
            public bool IsReferencedUpload { get; set; }
            public List<UploadReferenceLocation> UploadReferences { get; set; } = new();
        }

        public sealed class UploadReferenceLocation
        {
            public string ListName { get; set; } = string.Empty;
            public int ItemId { get; set; }
        }

        private static string ToHumanReadableSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
            double size = bytes;
            int unitIndex = 0;

            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            if (unitIndex == 0)
            {
                return $"{bytes}{units[unitIndex]}";
            }

            if (size >= 10)
            {
                return $"{Math.Round(size):0}{units[unitIndex]}";
            }

            return $"{size:0.0}{units[unitIndex]}";
        }

        /// <summary>
        /// List of protected files "owned" by the application
        /// </summary>
        /// <remarks>
        /// Matches listing in 
        private static List<string> protectedFiles = new List<string>
        {
            "/lib/easymde/easymde.min.css",
            "/lib/easymde/easymde.min.js",
            // "/lib/emoji_js/emoji.min.js",   // NOTE _ not - >> .NET BUILD subs this
            // "/lib/emoji_js/emoji.js",       // NOTE _ not - >> .NET BUILD subs this
            // "/lib/emoji_js/emoji.css",      // NOTE _ not - >> .NET BUILD subs this
            // "/lib/emoji_js/jquery.emoji.js", // NOTE _ not - >> .NET BUILD subs this
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

        public static string NormalizeRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string normalized = path.Replace("\\", "/").Trim();
            if (!normalized.StartsWith("/"))
            {
                normalized = "/" + normalized;
            }

            return normalized;
        }

        public static bool IsInternalProtectedPath(string path)
        {
            string normalized = NormalizeRelativePath(path);
            return protectedFiles.Contains(normalized);
        }

        public static List<string> ExtractUploadReferences(string? text)
        {
            var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(text))
            {
                return refs.ToList();
            }

            string uploadsSegment = "/" + GlobalStatic.uploadsFolder + "/";
            int start = 0;
            while (start < text.Length)
            {
                int idx = text.IndexOf(uploadsSegment, start, StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                {
                    break;
                }

                int end = idx;
                while (end < text.Length)
                {
                    char ch = text[end];
                    if (char.IsWhiteSpace(ch) || ch == '"' || ch == '\'' || ch == '<' || ch == '>' || ch == ')' || ch == '(')
                    {
                        break;
                    }
                    end++;
                }

                string candidate = text.Substring(idx, end - idx);
                candidate = WebUtility.UrlDecode(candidate);
                refs.Add(NormalizeRelativePath(candidate));
                start = end;
            }

            return refs.Where(r => r.StartsWith(uploadsSegment, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public async Task<HashSet<string>> GetAllReferencedUploadFilesAsync()
        {
            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lists = await _db.Lists.ToListAsync();
            var items = await _db.Items.ToListAsync();

            foreach (var list in lists)
            {
                foreach (var fileRef in ExtractUploadReferences(list.Comment))
                {
                    referenced.Add(fileRef);
                }
            }

            foreach (var item in items)
            {
                foreach (var fileRef in item.LocalFiles())
                {
                    referenced.Add(NormalizeRelativePath(fileRef));
                }

                foreach (var fileRef in ExtractUploadReferences(item.Comment))
                {
                    referenced.Add(fileRef);
                }

                foreach (var fileRef in ExtractUploadReferences(item.Name))
                {
                    referenced.Add(fileRef);
                }
            }

            return referenced;
        }

        public async Task<Dictionary<string, List<UploadReferenceLocation>>> GetReferencedUploadLocationsByPathAsync()
        {
            var refsByPath = new Dictionary<string, List<UploadReferenceLocation>>(StringComparer.OrdinalIgnoreCase);
            var listNamesById = await _db.Lists.ToDictionaryAsync(l => l.Id, l => l.Name ?? string.Empty);
            var items = await _db.Items.ToListAsync();

            foreach (var item in items)
            {
                if (!listNamesById.TryGetValue(item.ListId, out string? listName) || string.IsNullOrWhiteSpace(listName))
                {
                    continue;
                }

                var itemRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var fileRef in item.LocalFiles())
                {
                    itemRefs.Add(NormalizeRelativePath(fileRef));
                }

                foreach (var fileRef in ExtractUploadReferences(item.Comment))
                {
                    itemRefs.Add(fileRef);
                }

                foreach (var fileRef in ExtractUploadReferences(item.Name))
                {
                    itemRefs.Add(fileRef);
                }

                foreach (var fileRef in itemRefs)
                {
                    if (!refsByPath.TryGetValue(fileRef, out var locations))
                    {
                        locations = new List<UploadReferenceLocation>();
                        refsByPath[fileRef] = locations;
                    }

                    if (!locations.Any(l => l.ItemId == item.Id && l.ListName == listName))
                    {
                        locations.Add(new UploadReferenceLocation
                        {
                            ListName = listName,
                            ItemId = item.Id
                        });
                    }
                }
            }

            return refsByPath;
        }

        public async Task<List<FileAuditRow>> GetFileAuditRowsAsync()
        {
            string absoroot = Path.GetFullPath(GlobalConfig.wwwroot);
            string[] files = Directory.GetFiles(GlobalConfig.wwwroot, "*.*", SearchOption.AllDirectories);
            var referencedUploads = await GetAllReferencedUploadFilesAsync();
            var uploadLocationsByPath = await GetReferencedUploadLocationsByPathAsync();
            var rows = new List<FileAuditRow>();

            foreach (string file in files)
            {
                string relpath = file.Substring(file.IndexOf(absoroot, StringComparison.OrdinalIgnoreCase) + absoroot.Length);
                relpath = NormalizeRelativePath(relpath);

                bool isListFile = IsItAListFile(relpath);
                bool isProtectedFile = IsItAProtectedFile(relpath);
                bool isReferencedUpload = referencedUploads.Contains(relpath);

                if (relpath == "/index.html")
                {
                    isListFile = true;
                }

                rows.Add(new FileAuditRow
                {
                    RelativePath = relpath,
                    SizeBytes = new FileInfo(file).Length,
                    IsListFile = isListFile,
                    IsProtectedFile = isProtectedFile,
                    IsReferencedUpload = isReferencedUpload,
                    UploadReferences = uploadLocationsByPath.TryGetValue(relpath, out var refs) ? refs : new List<UploadReferenceLocation>()
                });
            }

            return rows;
        }

        public async Task<List<string>> GetOrphanFilesAsync()
        {
            var rows = await GetFileAuditRowsAsync();
            return rows
                .Where(r => !r.IsListFile && !r.IsProtectedFile && !r.IsReferencedUpload)
                .Select(r => r.RelativePath)
                .ToList();
        }

        public async Task<int> DeleteOrphanFilesAsync()
        {
            string absRoot = Path.GetFullPath(GlobalConfig.wwwroot);
            var orphans = await GetOrphanFilesAsync();
            int deleted = 0;

            foreach (var rel in orphans)
            {
                string abs = Path.GetFullPath(Path.Combine(absRoot, rel.TrimStart('/')));
                if (!abs.StartsWith(absRoot, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (System.IO.File.Exists(abs))
                {
                    System.IO.File.Delete(abs);
                    ProtectedFiles.RemoveFile(rel);
                    deleted++;
                }
            }

            return deleted;
        }

        public async Task<StringBuilder> GetAllFilesInWWWRoot()
        {
            string fn = "GetAllFilesInWWWRoot";
            DBg.d(LogLevel.Trace, fn);

            var rows = await GetFileAuditRowsAsync();
            StringBuilder sb = new StringBuilder();
            await GlobalStatic.GenerateHTMLHead(sb, "File Orphan Report");

            sb.AppendLine("<h1>File Report</h1>");
            sb.AppendLine("<p>This report shows all files in the wwwroot directory and categorizes them by their usage.</p>");
            sb.AppendLine("<p><strong>Red entries</strong> indicate files that may be orphaned (not referenced by any list, not protected, and not uploaded to any item).</p>");
            sb.AppendLine("<div class=\"button admin\" onclick=\"window.location.href='/files/cleanup'\">Delete All Orphaned Files</div>");
            sb.AppendLine("<br><br>");

            sb.AppendLine("<table>");
            sb.AppendLine("<thead><tr><th>File</th><th>generated by a list?</th><th>protected?</th><th>upload reference</th></tr></thead>");
            foreach (var row in rows)
            {
                string relpath = row.RelativePath;
                string isListFile = row.IsListFile ? "list file" : "";
                string isProtectedFile = row.IsProtectedFile ? "protected" : "";
                string isReferencedUpload = "";
                if (row.IsReferencedUpload)
                {
                    if (row.UploadReferences.Count > 0)
                    {
                        isReferencedUpload = string.Join("<br>", row.UploadReferences
                            .OrderBy(r => r.ListName)
                            .ThenBy(r => r.ItemId)
                            .Select(r =>
                            {
                                string listNameEncoded = Uri.EscapeDataString(r.ListName);
                                string href = $"/{listNameEncoded}.html#{r.ItemId}";
                                string label = $"{WebUtility.HtmlEncode(r.ListName)} #{r.ItemId}";
                                return $"<a href=\"{href}\" target=\"_blank\">{label}</a>";
                            }));
                    }
                    else
                    {
                        isReferencedUpload = "upload";
                    }
                }
                bool isOrphan = !row.IsListFile && !row.IsProtectedFile && !row.IsReferencedUpload;
                string fileSize = ToHumanReadableSize(row.SizeBytes);

                string fileCell = WebUtility.HtmlEncode(relpath);
                if (relpath.StartsWith("/" + GlobalStatic.uploadsFolder + "/", StringComparison.OrdinalIgnoreCase))
                {
                    fileCell = $"<a href=\"{WebUtility.HtmlEncode(relpath)}\" target=\"_blank\">{WebUtility.HtmlEncode(relpath)}</a>";
                }
                fileCell += $" ({fileSize})";

                string actionCell = "";
                if (isOrphan)
                {
                    string encoded = Uri.EscapeDataString(relpath.TrimStart('/'));
                    actionCell = $"<button class=\"button admin\" onclick=\"deleteOrphanFile('{encoded}')\">Delete</button>";
                }

                var rowClass = isOrphan ? "red-row" : "";
                var msg = $"<tr class='{rowClass}'><td>{fileCell}</td><td>{isListFile}</td><td>{isProtectedFile}</td><td>{isReferencedUpload}</td><td>{actionCell}</td></tr>";
                //DBg.d(LogLevel.Trace, msg);
                sb.AppendLine(msg);

            }
            sb.AppendLine("</table>");
            
            // Add JavaScript to show admin and debug elements
            sb.AppendLine("<script src=\"/_utils.js\"></script>");
            sb.AppendLine("<script>");
            sb.AppendLine("document.addEventListener('DOMContentLoaded', function() {");
            sb.AppendLine("    showDebuggingElements();");
            sb.AppendLine("    showAdminSecrets();");
            sb.AppendLine("});");
            sb.AppendLine("async function deleteOrphanFile(filePath) {");
            sb.AppendLine("    if (!confirm('Delete orphan file ' + decodeURIComponent(filePath) + '?')) { return; }");
            sb.AppendLine("    await amloggedin();");
            sb.AppendLine("    const antiForgeryToken = localStorage.getItem('antiForgeryToken');");
            sb.AppendLine("    const antiForgeryHeaderName = localStorage.getItem('antiForgeryHeaderName') || 'RequestVerificationToken';");
            sb.AppendLine("    const headers = { 'GeFeSLE-XMLHttpRequest': 'true' };");
            sb.AppendLine("    if (antiForgeryToken) { headers[antiForgeryHeaderName] = antiForgeryToken; }");
            sb.AppendLine("    const response = await fetch('/files/' + filePath, { method: 'DELETE', headers });");
            sb.AppendLine("    if (!response.ok) {");
            sb.AppendLine("        const text = await response.text();");
            sb.AppendLine("        alert('Delete failed: ' + text);");
            sb.AppendLine("        return;");
            sb.AppendLine("    }");
            sb.AppendLine("    window.location.reload();");
            sb.AppendLine("}");
            sb.AppendLine("</script>");
            
            await GlobalStatic.GeneratePageFooter(sb);
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
            if (string.IsNullOrEmpty(filename)) { ynot = "filename is null or empty"; return false; }

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
            string dontDELETE = Path.Combine(GlobalConfig.wwwroot,GlobalStatic.uploadsFolder);
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

            // dump all embedded resource names:
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var resourceNames = assembly.GetManifestResourceNames();

            foreach (var resourceName in resourceNames)
            {
                DBg.d(LogLevel.Trace, $"Embedded resource: {resourceName}");
            }



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
            // if the uploads folder doesn't exist, create it
            if (!Directory.Exists(uploadFolderAbsPath))
            {
                DBg.d(LogLevel.Warning, $"Uploads folder doesn't exist. Creating it.");
                Directory.CreateDirectory(uploadFolderAbsPath);
            }
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

