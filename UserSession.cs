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
using Microsoft.Extensions.Primitives;


public static class UserSessionService
{

    // public static void ConfigureServices(IServiceCollection services)
    // {
    //     services.AddSingleton<UserSessionService>();
    //     // Other service registrations...
    // }


    public static GeFeSLEUser? UpdateSessionAccessTime(HttpContext context,
            GeFeSLEDb db,
            UserManager<GeFeSLEUser> userManager)
    {
        var fn = "UpdateSessionAccessTime"; DBg.d(LogLevel.Trace, fn);

        var sessionUser = amILoggedIn(context);
        if (sessionUser.IsAuthenticated)
        {
            GeFeSLEUser? user = userManager.FindByIdAsync(sessionUser.Id).Result;
            if (user != null)
            {
                user.LastAccessTime = DateTime.UtcNow;
                _ = db.SaveChangesAsync();
                // var roles = userManager.GetRolesAsync(user).Result;         //TODO: use role from DTo not this
                // string realizedRole = GlobalStatic.FindHighestRole(roles);

                DBg.d(LogLevel.Debug, $"{fn}: User {sessionUser.UserName} [{sessionUser.Role}] TO {context.Request.Path} FROM {context.Connection.RemoteIpAddress}");
                return user;
            } // user in db 
            else
            {
                // could be an anonymous OAuth user - someone who has logged in via an OAuth source 
                // but they're not in our database (i.e. have been invited/added by a legit usesr)
                DBg.d(LogLevel.Debug, $"{fn}: User {sessionUser.UserName} (OAuth guest) [{sessionUser.Role}] to {context.Request.Path} from {context.Connection.RemoteIpAddress}");
                return null;
            } // user NOT in db.

        }
        else // not authenticated
        {
            DBg.d(LogLevel.Debug, $"{fn}: Anonymous access TO {context.Request.Path} FROM {context.Connection.RemoteIpAddress}");
            return null;
        }

    }


    public static string createJWToken(string username, string role)
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
    public static void storeProvider(HttpContext httpContext, string provider)
    {
        DBg.d(LogLevel.Trace, $"storeProvider: {provider}");
        httpContext.Session.SetString("provider", provider);
    }

    public static string? getProvider(HttpContext httpContext)
    {
        DBg.d(LogLevel.Trace, "getProvider");
        return httpContext.Session.GetString("provider");
    }


    public static void createSession(HttpContext httpContext, string userid, string username, string role)
    {
        DBg.d(LogLevel.Trace, "createSession");
        // create a claims identity
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userid),
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
            else
            {
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
    public static UserDto amILoggedIn(HttpContext context)
    {
        string fn = "amILoggedIn"; DBg.d(LogLevel.Trace, fn);
        UserDto sessionUser = new UserDto();
        //GlobalStatic.dumpRequest(httpContext);
        sessionUser.Id = context.User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
        sessionUser.UserName = context.User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;
        //DBg.d(LogLevel.Trace, $"{fn} username: {username}");
        sessionUser.IsAuthenticated = context.User.Identity?.IsAuthenticated ?? false;
        //DBg.d(LogLevel.Trace, $"{fn} isAuthenticated: {isAuthenticated}");
        sessionUser.Role = context.User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;
        DBg.d(LogLevel.Debug, $"{fn} returning {sessionUser}");
        return sessionUser;

    }



    public static async Task<string?> dumpSession(HttpContext context)
    {
        var fn = "dumpSession"; DBg.d(LogLevel.Trace, fn);
        IdentityUser? user = context.User?.Identity as IdentityUser;
        var claims = context.User?.Claims.Select(c => new { c.Type, c.Value });
        var cookies = context.Request.Headers["Cookie"].ToString();
        var prettyCookies = PrettifyCookieHeader(cookies);
        context.Request.Headers["Cookie"] = "have been prettified - see below";

        // also get the session data:
        var sessionData = new Dictionary<string, string>();
        foreach (var key in context.Session.Keys)
        {
            sessionData[key] = context.Session.GetString(key);
        }


        var serializableContext = new
        {
            Request = new
            {
                context.Request.Method,
                context.Request.Scheme,
                context.Request.Host,
                context.Request.Path,
                context.Request.QueryString,
                context.Request.Headers,
                context.Request.ContentType,
                context.Request.ContentLength,
                context.Request.Protocol
            },
            Response = new
            {
                context.Response.StatusCode,
                Headers = context.Response.Headers.ToDictionary(h => h.Key, h => h.Value.ToString())
            },
            User = new
            {
                user,
                claims
            },
            context.TraceIdentifier,
            context.Connection.Id,
            prettyCookies,
            Session = sessionData
        };


        var session = JsonConvert.SerializeObject(serializableContext, new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            Formatting = Formatting.Indented
        });

        //DBg.d(LogLevel.Trace, $"{fn}: {session}");
        return session;
    }

    private static List<KeyValuePair<string, string>> PrettifyCookieHeader(string cookieHeader)
    {
        var cookies = cookieHeader.Split(";", StringSplitOptions.RemoveEmptyEntries);
        var prettyCookies = new List<KeyValuePair<string, string>>();
        foreach (var cookie in cookies)
        {
            // split it into name and value
            var parts = cookie.Split("=", StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                prettyCookies.Add(new KeyValuePair<string, string>(parts[0].Trim(), parts[1].Trim()));
            }
        }
        return prettyCookies;
    }
}


