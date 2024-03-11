using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Microsoft.VisualBasic;
using System.IdentityModel.Tokens.Jwt; // Add this directive for JWT token
using System.Text; // Add this directive for Encoding
using GeFeSLE;
using Microsoft.AspNetCore.Identity;


public static class UserSessionService
{

    // public static void ConfigureServices(IServiceCollection services)
    // {
    //     services.AddSingleton<UserSessionService>();
    //     // Other service registrations...
    // }

    public static void UserLoggedOut(HttpContext context)
    {
        // if its not null, get the user name
        var username = context.User?.Identity?.Name;

        context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        DBg.d(LogLevel.Trace, "UserLoggedOut: " + username);
    }

    public static GeFeSLEUser? UpdateSessionAccessTime(HttpContext context,
            GeFeSLEDb db,
            UserManager<GeFeSLEUser> userManager)
    {
        DBg.d(LogLevel.Trace, "UpdateSessionAccessTime");
        if (amILoggedIn(context))
        {
            GeFeSLEUser? user = getWhoIAm(context, userManager).Result;
            if (user != null)
            {
                user.LastAccessTime = DateTime.UtcNow;
                _ = db.SaveChangesAsync();
                var roles = userManager.GetRolesAsync(user).Result;
                string realizedRole = GlobalStatic.FindHighestRole(roles);

                DBg.d(LogLevel.Trace, "UpdateSessionAccessTime: " + user.UserName + " [" + realizedRole + "] to " + context.Request.Path + " from " + context.Connection.RemoteIpAddress);
                return user;
            } // user in db 
            else
            {
                DBg.d(LogLevel.Critical, "UpdateSessionAccessTime: User not found in database, but they're authenticated??");
                return null;
            } // user NOT in db.

        }
        else // not authenticated
        {
            DBg.d(LogLevel.Trace, "UpdateSessionAccessTime: Anonymous access to " + context.Request.Path + " from " + context.Connection.RemoteIpAddress);
            return null;
        }

    }


    public static string createToken(string username, string role)
    {
        // create a claims identity
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role)
        };
        var claimsIdentity = new ClaimsIdentity(claims, "jwt");
        var principal = new ClaimsPrincipal(claimsIdentity);
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(GlobalConfig.apiTokenSecretKey!);
        DBg.d(LogLevel.Trace, "Token Create Time: " + DateTime.UtcNow.ToString());
        var expiretime = DateTime.UtcNow.Add(GlobalConfig.apiTokenDuration);
        DBg.d(LogLevel.Trace, "Token Expires: " + expiretime.ToString());
        string bearerRealm = $"{GlobalConfig.Hostname}:{GlobalConfig.Hostport}";
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = principal.Identity as ClaimsIdentity,
            Expires = expiretime,
            Audience = bearerRealm,
            Issuer = bearerRealm,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = tokenHandler.WriteToken(token);

        return tokenString;
    }

    public static void createSession(HttpContext httpContext, string username, string role)
    {
        DBg.d(LogLevel.Trace, "createSession");
        // create a claims identity
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role)
        };
        DBg.d(LogLevel.Trace, "createSession: " + username + " [" + role + "]");

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        // create the auth properties
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30)
        };

        // sign in the user
        httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity),
            authProperties);





    }

    // a method that takes username and an OAuth2AccessTokenResponse and stores it in the user session
    public static void AddAccessToken(HttpContext context, string provider, string accessToken)
    {
        DBg.d(LogLevel.Trace, "AddAccessTokenResponse");
        var username = context.User?.Identity?.Name;

        if (provider == null || accessToken == null)
        {
            DBg.d(LogLevel.Error, "AddAccessTokenResponse: provider or accessToken is null");
            return;
        }
        else
        {
            context.Session.SetString(provider, accessToken);
        }

    }

    // a method that takes username and provider and returns the OAuth2AccessTokenResponse
    public static string? GetAccessToken(HttpContext context, string provider)
    {
        DBg.d(LogLevel.Trace, "GetAccessTokenResponse");
        if (provider == null)
        {
            DBg.d(LogLevel.Error, "GetAccessTokenResponse: provider is null");
            return null;
        }
        else
        {

            string? token = context.Session.GetString(provider);
            if (token == null)
            {
                DBg.d(LogLevel.Error, "GetAccessTokenResponse: No token found for provider: " + provider);
                return null; // Add null check here
            }
            return token;
        }

    }

    public static async Task<GeFeSLEUser?> getWhoIAm(HttpContext context,
        UserManager<GeFeSLEUser> userManager)
    {
        DBg.d(LogLevel.Trace, "getWhoIAm");
        DBg.d(LogLevel.Trace, "getWhoIAm: " + context.User?.Identity?.Name);
        string? username = context.User?.Identity?.Name;
        if (username == null)
        {
            DBg.d(LogLevel.Trace, "getWhoIAm: null username");
            return null;
        }
        else
        {
            GeFeSLEUser? user = await userManager.FindByNameAsync(username);
            if (user == null)
            {
                DBg.d(LogLevel.Error, $"getWhoIAm: NO DB user {username}!");
                return null;
            }
            DBg.d(LogLevel.Trace, $"getWhoIAm: {user.UserName}");
            return user;
        }
    }

    public static bool amILoggedIn(HttpContext context)
    {
        DBg.d(LogLevel.Trace, "amILoggedIn");
        string? username = context.User?.Identity?.Name;
        bool loggedIn = context.User?.Identity?.IsAuthenticated == true;

        DBg.d(LogLevel.Trace, $"amILoggedIn: {username} - {loggedIn}");
        return loggedIn;
    }

    public static async Task<List<string>> getRoles(GeFeSLEUser user,
        UserManager<GeFeSLEUser> userManager)
    {
        DBg.d(LogLevel.Trace, "getMyRoles");
        var roles = await userManager.GetRolesAsync(user);
        DBg.d(LogLevel.Trace, $"getMyRoles: {user.UserName} [{string.Join(", ", roles)}]");
        return roles.ToList();
    }

    public static async Task<string?> dumpSession(HttpContext context,
        UserManager<GeFeSLEUser> userManager)
    {
        DBg.d(LogLevel.Trace, "dumpSession");

        GeFeSLEUser? user = await getWhoIAm(context, userManager);
        if (user == null)
        {
            return null;
        }
        else
        {
            var roles = await userManager.GetRolesAsync(user!);
            var claims = context.User?.Claims.Select(c => new { c.Type, c.Value });
            var session = JsonConvert.SerializeObject(new
            {
                user,
                roles,
                claims
            }, new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                Formatting = Formatting.Indented
            });
            DBg.d(LogLevel.Trace, $"dumpSession: {session}");
            return session;
        }
    }


}


