using Microsoft.EntityFrameworkCore;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Identity;

public static class GlobalStatic
{
    public static string applicationName = "GeFeSLE";
    public static string authCookieName = "GeFeSLEAuthCookie"; // if you change this, change function amLoggedIn in frontend _utils.js too
    public static string sessionCookieName = "GeFeSLESessionCookie";
    public static string antiForgeryCookieName = "GeFeSLEAntiForgeryCookie";
    public static string webSite = "https://awadwatt.com/gefesle";

    // TODO: move these to config file for users to create their own. 
    public static string googleClientID = "633241786177-0mlrsg1leiu9i3et858idmmn9rtrc2fi.apps.googleusercontent.com";
    public static string googleClientSecret = "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";

    public static string microsoftClientId = "7167c2f7-7c2f-4039-8195-a046c6b05032";
    public static string microsoftClientSecret = "Bk98Q~rgkjDANYv8RMoKVQJ0D-1Usa4cBkXBLbbs";
    // secret ID: 504b1740-ce89-40fe-bbbf-b470393259e1

    public static string mastoClient_Name = "GeFeSLE";
    public static string mastoScopes = "read write:bookmarks";

    public static List<string> roleNames = new List<string> { "SuperUser", "listowner", "contributor" };

    // if on, a bunch of shortcuts of convenience are rendered in html pages.
    public static bool CompileTimeDebugging = true;

    public static async Task GenerateHTMLListIndex(GeFeSLEDb db)
    {
        DBg.d(LogLevel.Trace, "GenerateHTMLListIndex");
        // get all the lists
        var lists = await db.Lists.ToListAsync();
        //if (lists.Count == 0) return;

        var sb = new StringBuilder();
        await GenerateHTMLHead(sb, $"{GlobalConfig.sitetitle} - Index of lists");
        if (GlobalConfig.bodyHeader != null)
        {
            var header = await File.ReadAllTextAsync(GlobalConfig.bodyHeader);
            sb.AppendLine(header);
        }
        sb.AppendLine($"<h1 class=\"indextitle\">{GlobalConfig.sitetitle}</h1>");
        sb.AppendLine($"<p><a id=\"indexeditlink\" class=\"indexeditlink\" href=\"_edit.list.html\" style=\"display: none;\">Add new list</a> ");
        // write a line that calls the regenerate endpoint and refreshes this page
        sb.AppendLine($"<a id=\"indexregenlink\" class=\"indexregenlink\" href=\"\" onclick=\"interceptRegen(event)\" style=\"display: none;\">REGEN</a></p>");
        sb.AppendLine("<ul class=\"indexuloflists\">");
        if (lists.Count == 0)
        {
            sb.AppendLine("<h3 class=\"indexliitem\">No lists here yet!</h3>");
        }
        else
        {
            foreach (var list in lists)
            {
                sb.AppendLine($"<li class=\"indexliitem\"><a href=\"{list.Name}.html\">{list.Name}</a>");
                sb.AppendLine($"<span class=\"indexeditlink\" style=\"display: none;\"><a href=\"_edit.list.html?listid={list.Id}\">edit</a></span>");
                sb.AppendLine($"<span class=\"indexeditlink\" style=\"display: none;\"><a href=\"#\" onclick=\"deleteList({list.Id}); return;\">Delete</a></span>");


                sb.AppendLine("</li>");
            }
        }
        sb.AppendLine("</ul>");
        sb.AppendLine("<script src=\"_utils.js\"></script>");
        sb.AppendLine("<script src=\"_index.js\"></script>");
        sb.AppendLine("<div id=\"result\"></div>");

        await GeneratePageFooter(sb);

        var dest = Path.Combine(GlobalConfig.wwwroot!, "index.html");
        DBg.d(LogLevel.Trace, $"Writing to {dest}");
        await File.WriteAllTextAsync(dest, sb.ToString());

    }
    // generates everything from the footer to the closing html tag
    // including the closing body tag
    public static async Task GeneratePageFooter(StringBuilder sb)
    {
        sb.AppendLine("<footer>");
        if (GlobalConfig.bodyFooter != null)
        {
            var footer = await File.ReadAllTextAsync(GlobalConfig.bodyFooter);
            sb.AppendLine(footer);
        }


        sb.AppendLine($"<p class=\"byline\">Generated by GeFeSLE {GlobalConfig.bldVersion} at {DateTime.Now}</p>");
        sb.AppendLine($"<p class=\"owner\">Owner: {GlobalConfig.owner}</p>");
        sb.AppendLine("</footer>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
    }

    // generates everything up to and including the opening body tag
    public static async Task GenerateHTMLHead(StringBuilder sb, string pagetitle)
    {
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html>");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.AppendLine("<link rel=\"icon\" href=\"/gefesle.ff.png\" type=\"image/x-icon\">");


        if (GlobalConfig.htmlHead != null)
        {
            // Kestrel (the .NET web server) doesn't support the <!--#include virtual="filename" --> directive, so we have to read the file and inject it into the output
            var head = await File.ReadAllTextAsync(GlobalConfig.htmlHead);
            sb.AppendLine(head);
        }
        else
        {
            sb.AppendLine("<link rel=\"stylesheet\" type=\"text/css\" href=\"/gefesle.default.css\">");
        }
        sb.AppendLine($"<title>{pagetitle}</title>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body >");
        
        sb.AppendLine($"<p class=\"debugging\" style=\"display: none;\">[ Debugging is ON |");
            sb.AppendLine($"<a href=\"/session\">Session</a> |");
            sb.AppendLine($"<a href=\"/regenerate\">REGEN</a> |");
            sb.AppendLine($"<a href=\"/showusers\">show users</a> | ");
            sb.AppendLine($"<a href=\"/_edituser.html\">edit users</a> | ");
            sb.AppendLine($"<a href=\"/killsession\">nerf session</a> | ");
        sb.AppendLine("]</p>");
        
        sb.AppendLine($"<p>[ <a href=\"_login.html\">login</a> ]</p>");
        

    }

    public static async Task GenerateUnAuthPage(StringBuilder sb, string msg)
    {
        DBg.d(LogLevel.Trace, "GenerateUnAuthPage");
        // get all the lists

        await GenerateHTMLHead(sb, $"{GlobalConfig.sitetitle} - UNAUTHORIZED");

        sb.AppendLine($"<h1 class=\"indextitle\">WHUPS</h1>");
        sb.AppendLine($"<p style=\"color: red;\">{msg}</p>");
        sb.AppendLine("<p>Go back to <a href=\"/_login.html\">the login page?</a></p>");
        await GeneratePageFooter(sb);
    }

    public static async Task GenerateLoginResult(StringBuilder sb, string msg)
    {
        DBg.d(LogLevel.Trace, "GenerateLoginResult");
        // get all the lists

        await GenerateHTMLHead(sb, $"{GlobalConfig.sitetitle} - Login Success!");

        sb.AppendLine($"<h1 class=\"indextitle\">SUCCESS</h1>");
        sb.AppendLine($"<p style=\"color: green;\">{msg}</p>");
        sb.AppendLine("<h2><a href=\"/index.html\">CMON IN</a></h2>");
        await GeneratePageFooter(sb);
    }

    public static bool IsCorsRequest(HttpRequest request)
    {
        return request.Headers.ContainsKey("Origin") &&
               (request.Headers["Origin"].ToString().Contains("localhost") ||
                request.Headers["Origin"].ToString().StartsWith("moz-extension://") ||
                request.Headers["Origin"].ToString().StartsWith("chrome-extension://"));
    }

    public static void AddCorsHeaders(HttpRequest request, HttpResponse response)
    {
        // add this to prevent CORS rejection from the plugin's XMLHttpRequest
        var origin = request.Headers["Origin"].FirstOrDefault();
        response.Headers.Append("Access-Control-Allow-Origin", origin);
        response.Headers.Append("Access-Control-Allow-Credentials", "true");
        response.Headers.Append("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
        response.Headers.Append("Access-Control-Allow-Headers", "Origin, X-Requested-With, Content-Type, Accept, Authorization, GeFeSLE-XMLHttpRequest, RequestVerificationToken");

    }

    public static bool IsAPIRequest(HttpRequest request)
    {
        if (request.Headers.ContainsKey("GeFeSLE-XMLHttpRequest") &&
            (request.Headers["GeFeSLE-XMLHttpRequest"].ToString() == "true"))
        {
            return true;
        }
        return false;
    }

    public static void DumpHTTPRequestHeaders(HttpRequest request)
    {


        DBg.d(LogLevel.Trace, "**DumpHTTPRequestHeaders");
        // serialize the full request object to see what's in it
        var jsonString = JsonConvert.SerializeObject(request.Headers, Formatting.Indented);
        DBg.d(LogLevel.Trace, $"**** Request.Headers *****");
        DBg.d(LogLevel.Trace, jsonString);
        DBg.d(LogLevel.Trace, "***************************");
        // extract the jwt bearer token if there is one
        var token = request.Headers["Authorization"].FirstOrDefault();
        if (token != null)
        {
            DBg.d(LogLevel.Trace, "**** JWT token found in Request.Headers *****");
            // split off the word "Bearer" from the token
            token = token.Split(" ")[1];
            // if the plugin hasn't logged in yet, token will be "undefined"
            if (token == "undefined")
            {
                token = null;
                DBg.d(LogLevel.Trace, "**** JWT token is 'undefined' (no plugin login yet) *****");
            }
            else
            {
                var sb = GlobalStatic.DumpToken(token);
                DBg.d(LogLevel.Trace, sb.ToString());
            }
        }
        else
        {
            DBg.d(LogLevel.Trace, "**** No JWT token found in Request.Headers *****");
        }
    }

    public static StringBuilder DumpToken(string token)
    {
        var sb = new StringBuilder();
        var handler = new JwtSecurityTokenHandler();
        JwtSecurityToken? jsonToken = handler.ReadToken(token) as JwtSecurityToken;
        if (jsonToken == null)
        {
            sb.AppendLine("**** JWT token is not valid *****");
            return sb;
        }
        else
        {
            sb.AppendLine("   Token: " + token);
            sb.AppendLine("   Header:");
            foreach (var claim in jsonToken!.Header)
            {
                sb.AppendLine($"     {claim.Key}: {claim.Value}");
            }
            sb.AppendLine("   Payload:");
            foreach (var claim in jsonToken.Claims)
            {
                sb.AppendLine($"     {claim.Type}: {claim.Value}");
            }
            return sb;
        }
    }

    public static void dumpRequest(HttpContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Request:");
        sb.AppendLine("Path: " + context.Request.Path);
        sb.AppendLine("Method: " + context.Request.Method);
        sb.AppendLine("Scheme: " + context.Request.Scheme);
        // sb.AppendLine("Headers:");
        // foreach (var header in context.Request.Headers)
        // {
        //     sb.AppendLine($"{header.Key}: {header.Value}");
        // }
        // dump out any cookies
        sb.AppendLine("Cookies:");
        foreach (var cookie in context.Request.Cookies)
        {
            sb.AppendLine($"{cookie.Key}: {cookie.Value}");
        }


        sb.AppendLine("Querystring:" + context.Request.QueryString);
        sb.AppendLine("Query:");
        foreach (var query in context.Request.Query)
        {
            sb.AppendLine($"{query.Key}: {query.Value}");
        }
        DBg.d(LogLevel.Trace, sb.ToString());
    }
    public static string FindHighestRole(IList<string> roles)
    {
        if (roles.Count == 0)
        {
            return "anonymous";

        }
        else if (roles.Count > 1)
        {
            // find the highest one in precdence and use that
            var highestRole = GlobalStatic.roleNames
                .OrderBy(role => GlobalStatic.roleNames.IndexOf(role))
                .FirstOrDefault(role => roles.Contains(role));
            if (highestRole is not null)
            {
                return highestRole;
            }
            else // not sure how this would happen, but just in case
            {
                return "anonymous";
            }
        }
        else
        {
            return roles[0];
        }
    }
// this make sure we have the desired roles in the database
// SuperUser - only one who can add admins
// admins - can add/remove lists; can add/remove users
// listowner - can add/remove items from a list
// user - this is anyone who logs in - can view lists and items

async public static Task SeedRoles(IServiceProvider serviceProvider)

{
    var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();

    IdentityResult roleResult;

    foreach (var roleName in GlobalStatic.roleNames)
    {
        var roleExist = await roleManager.RoleExistsAsync(roleName);
        if (!roleExist)
        {
            //create the roles and seed them to the database
            roleResult = await roleManager.CreateAsync(new IdentityRole(roleName));
        }
    }
}



}

