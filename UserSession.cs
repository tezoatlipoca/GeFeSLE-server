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


public class UserSessionService
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<UserSessionService>();
        // Other service registrations...
    }

    public void UserLoggedOut(HttpContext context)
    {
        // if its not null, get the user name
        var username = context.User?.Identity?.Name;

        context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        DBg.d(LogLevel.Trace, "UserLoggedOut: " + username);
    }

    public void UpdateSessionAccessTime(HttpContext context, GeFeSLEDb db)
    {
        // context and db will always be valid

        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var username = context.User?.Identity?.Name;
            var role = context.User?.FindFirst(ClaimTypes.Role)?.Value;

            // Access the GeFeSLEUser that matches the claims principal
            var user = db.Users.FirstOrDefault(u => u.UserName == username);

            if (user != null)
            {
                // Perform actions with the user...
                user.LastAccessTime = DateTime.UtcNow;
                db.SaveChanges();
                DBg.d(LogLevel.Trace, "UpdateSessionAccessTime: " + username + " [" + role + "] to " + context.Request.Path + " from " + context.Connection.RemoteIpAddress);

            }
            else
            {
                // wait - how did the user get here if they arent in the database?
                // unless they're the super user
                if (username == GlobalConfig.backdoorAdmin!.Username)
                {
                    DBg.d(LogLevel.Trace, "UpdateSessionAccessTime: Superuser " + username + " [" + role + "] to " + context.Request.Path + " from " + context.Connection.RemoteIpAddress);
                }
                else
                {
                    DBg.d(LogLevel.Critical, "UpdateSessionAccessTime: User " + username + " not found in database, but they're authenticated as " + role);
                }
                
            }
        }
        else // not authenticated
        {
            DBg.d(LogLevel.Trace, "UpdateSessionAccessTime: Anonymous access to " + context.Request.Path + " from " + context.Connection.RemoteIpAddress);
        }

    }


    public string createToken(string username, string role)
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

    public void createSession(HttpContext httpContext, string username, string role)
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
    public void AddAccessToken(HttpContext context, string provider, string accessToken)
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
    public string? GetAccessToken(HttpContext context, string provider)
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





}


