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
        var fn = "UpdateSessionAccessTime"; DBg.d(LogLevel.Trace, fn);
        
        var sessionUser = amILoggedIn(context);
        if (sessionUser.UserIdentityIsAuthenticated)
        {
            GeFeSLEUser? user = mapUserNameToDBUser(sessionUser.UserIdentityName!, userManager).Result;
            if (user != null)
            {
                user.LastAccessTime = DateTime.UtcNow;
                _ = db.SaveChangesAsync();
                var roles = userManager.GetRolesAsync(user).Result;
                string realizedRole = GlobalStatic.FindHighestRole(roles);

                DBg.d(LogLevel.Information, $"{fn}: User {user.UserName} [{realizedRole}] TO {context.Request.Path} FROM {context.Connection.RemoteIpAddress}");
                return user;
            } // user in db 
            else
            {
                // could be an anonymous OAuth user - someone who has logged in via an OAuth source 
                // but they're not in our database (i.e. have been invited/added by a legit usesr)
                DBg.d(LogLevel.Information, $"{fn}: User {sessionUser.UserIdentityName} (OAuth guest) [{sessionUser.UserClaimsRole}] to {context.Request.Path} from {context.Connection.RemoteIpAddress}");
                return null;
            } // user NOT in db.

        }
        else // not authenticated
        {
            DBg.d(LogLevel.Information, $"{fn}: Anonymous access TO {context.Request.Path} FROM {context.Connection.RemoteIpAddress}");
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

    public static async Task<GeFeSLEUser?> mapUserNameToDBUser(string username,
        UserManager<GeFeSLEUser> userManager)
    {
        string? fn = "mapUserNameToDBUser"; DBg.d(LogLevel.Trace, fn);
        
        if (username == null)
        {
            DBg.d(LogLevel.Trace, $"{fn} null username");
            return null;
        }
        else
        {
            GeFeSLEUser? user = await userManager.FindByNameAsync(username);
            if (user == null)
            {
                DBg.d(LogLevel.Debug, $"{fn} username {username} does NOT exist in the Database!");
                return null;
            }
            else {
                DBg.d(LogLevel.Debug, $"{fn} username {user.UserName} does exist in the Database!");
                return user;
            }
        }
    }

    // This function is the sole arbiter of "what does 'logged in' mean?"
    // A logged in user session for our purposes has a username, is authenticated, and has a role in the claims. 
    // Returns a tuple of username, isAuthenticated and first/default role claims
    // TODO: should unpack/investigate ALL claims, not just the first one. 
    // TODO: handle missing context.User/Identity gracefully
    // TODO: in all uses, see if we check for username == null and isAuthenticated == false; if so, 
    //       can we even have .isAuthenticated with a non-nul username? i.e. can we just check isAuthenticated?
    public static (string? UserIdentityName, bool UserIdentityIsAuthenticated, string? UserClaimsRole) amILoggedIn(HttpContext context)
    {
        string fn = "amILoggedIn"; DBg.d(LogLevel.Trace, fn);
        //GlobalStatic.dumpRequest(httpContext);

        var username = context.User?.Identity?.Name ?? null;
        DBg.d(LogLevel.Trace, $"{fn} username: {username}");
        bool isAuthenticated = context.User.Identity?.IsAuthenticated ?? false;
        DBg.d(LogLevel.Trace, $"{fn} isAuthenticated: {isAuthenticated}");
    
        string? role = null;
        if (isAuthenticated == true && !string.IsNullOrEmpty(username))
        {
            DBg.d(LogLevel.Trace, $"{fn} - valid cookie/jwt session");
            role = context.User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;
        }

        var returnTuple = (username, isAuthenticated, role);
        DBg.d(LogLevel.Debug, $"{fn} returning {{ username: {returnTuple.username}, isAuthenticated: {returnTuple.isAuthenticated}, role: {returnTuple.role} }}");
        return returnTuple;
    }
    


    public static async Task<string?> dumpSession(HttpContext context,
        string username,
        UserManager<GeFeSLEUser> userManager)
    {
        var fn = "dumpSession"; DBg.d(LogLevel.Trace, fn);
        
        GeFeSLEUser? user = await mapUserNameToDBUser(username, userManager);
        if (user != null)
        
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
            DBg.d(LogLevel.Trace, $"{fn}: {session}");
            return session;
        }
        else {
            DBg.d(LogLevel.Error, $"{fn}: no user in db");
            return null;
        }
    }


}


