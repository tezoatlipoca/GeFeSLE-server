using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Text;
using Mastonet.Entities;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using GeFeSLE;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.OAuth;



// check a bunch of stuff; we MUST have a configuration file AND
// a database filename. If we don't have both, we have to bail out
// BUT only after the database context is specified
// for dotnet ef migraitions and updates - don't worry
// for migrations, we have a constructor class that the migration tool falls back on
bool bailAfterDBContext = false;

string? configFile = GlobalConfig.CommandLineParse(args);
string? dbName = null;

var builder = WebApplication.CreateBuilder(args);
if (string.IsNullOrEmpty(configFile))
{
    DBg.d(LogLevel.Critical, "No configuration specified or file not found. Exiting.");
}
else
{
    // we don't care if the file isn't found -- if this is a add-migration or update-database
    // then the file won't be created anyway. 
    builder.Configuration.AddJsonFile(configFile, optional: true, reloadOnChange: true);
    // the ONE thing we insist on from the config file is the database name
    dbName = GlobalConfig.ParseConfigFile(builder.Configuration);
    if (dbName == null)
    {
        DBg.d(LogLevel.Critical, "Database name not found in configuration file. Exiting.");
        bailAfterDBContext = true;
    }



}

DBg.d(LogLevel.Information, $"GeFeSLE:{GlobalConfig.bldVersion}");

// add the MY SQL database context; we know it MUST exist thanks to ParseConfigFile
// TODO: investigate other non default connection string params: 
//   Mode, Cache, Password, foreign key enforcement etc. 
// TODO: extend this to allow OTHER database providers and connection strings
builder.Services.AddDbContext<GeFeSLEDb>(options =>
    options.UseSqlite($"Data Source={dbName}"));

// if the above fails i.e. it might be because we're performing a migration 
// using the dotnet ef tools - in which case don't want to continue with the
// rest of the app.
// don't worry - the migration will create the database using a connection string
// provided on the command line and using the DBContextFactory in GeFeSLEDb.cs
if (bailAfterDBContext)
{
    Environment.Exit(1);
}


builder.Services.AddIdentity<GeFeSLEUser, IdentityRole>(options =>
{
    options.Tokens.ProviderMap.Add("Default", new TokenProviderDescriptor(typeof(DataProtectorTokenProvider<GeFeSLEUser>)));
})
.AddEntityFrameworkStores<GeFeSLEDb>()
.AddDefaultTokenProviders();

builder.Services.AddDistributedMemoryCache(); // Stores session state in memory.
// TODO: save session state in database
//builder.Services.AddSingleton<UserSessionService>();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30); // The session timeout.
    // TODO: load session timeout from config file
    options.Cookie.HttpOnly = true; // The session cookie is accessible only from the server side, not via JavaScript.
    options.Cookie.IsEssential = true; // The session cookie is essential, meaning it doesn't need user consent.
    options.Cookie.Name = GlobalStatic.sessionCookieName;
    options.Cookie.SameSite = SameSiteMode.None; // The session cookie can be sent in a cross-site context.
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // The session cookie is always sent over HTTPS.
});
builder.Services.AddAuthentication(options =>
{
    // options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    // options.DefaultSignInScheme = JwtBearerDefaults.AuthenticationScheme;
    // options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;

    options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    // options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;

    // options.DefaultAuthenticateScheme = IdentityConstants.ExternalScheme;
    // options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    // options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;


})
.AddCookie(options =>
{
    options.Cookie.Name = GlobalStatic.authCookieName;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    // TODO: load session timeout from config file
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.None; // The authentication cookie can be sent in a cross-site context.
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // The authentication cookie is always sent over HTTPS.
    options.LoginPath = "/_login.html"; // Change this to your desired login path
    // this redirects any failure from the .RequireAuthorization() on endpoints.
    options.Events.OnRedirectToAccessDenied = async context =>
    {
        var fn = "cookie middleware"; DBg.d(LogLevel.Trace, $"{fn} - OnRedirectToAccessDenied");
        // detect if this is a CORS preflight request and if so, add the headers
        // to prevent it from being rejected
        if (GlobalStatic.IsCorsRequest(context.Request))
        {
            GlobalStatic.AddCorsHeaders(context.Request, context.Response);
        }
        // we want to differentiate between requests from our javascript front end
        // logic vs. requests on the endpoints directly. our javascript gets a code
        // and it will figure out how to handle it/present to user. 
        //
        // 403 is "i know who you are you just can't do this"
        // 401 is "i don't know who you are, go log in"
        if (GlobalStatic.IsAPIRequest(context.Request))
        {
            context.Response.StatusCode = 403;
            DBg.d(LogLevel.Trace, $"Cookie - OnRedirectToAccessDenied [API] returning: {context.Response.StatusCode}");
            return;
        }
        else
        {
            var sb = new StringBuilder();
            string requestedUrl = context.Request.Path + context.Request.QueryString;
            string msg = $"403 -You are not authorized to access {requestedUrl}";
            await GlobalStatic.GenerateUnAuthPage(sb, msg);
            DBg.d(LogLevel.Trace, $"Cookie - OnRedirectToAccessDenied [web] {msg}");
            var result = Results.Content(sb.ToString(), "text/html");
            await result.ExecuteAsync(context.HttpContext);
        }
    };
    // this is what fires when the user has not logged in yet; 401 Unauthorized
    // rationale for 401 when unauth, but a redirect when insufficiently authed
    // is the client side js needs an easy prompt to go log in. 
    options.Events.OnRedirectToLogin = async context =>
    {
        var fn = "cookie middleware"; DBg.d(LogLevel.Trace, $"{fn} - OnRedirectToLogin");
        // detect if this is a CORS preflight request and if so, add the headers
        // to prevent it from being rejected
        if (GlobalStatic.IsCorsRequest(context.Request))
        {
            GlobalStatic.AddCorsHeaders(context.Request, context.Response);
        }
        // dump out the full Request object
        if (GlobalStatic.IsAPIRequest(context.Request))
        {
            context.Response.StatusCode = 401;
            DBg.d(LogLevel.Trace, $"{fn} - OnRedirectToLogin [API] returning: {context.Response.StatusCode}");
            return;
        }
        else
        {
            var sb = new StringBuilder();
            string requestedUrl = context.Request.Path + context.Request.QueryString;
            string msg = $"401 - You need to <a href=\"_login.html\">LOGIN</a> to access {requestedUrl}";
            await GlobalStatic.GenerateUnAuthPage(sb, msg);
            DBg.d(LogLevel.Trace, $"{fn} - OnRedirectToLogin [web] {msg}");
            var result = Results.Content(sb.ToString(), "text/html");
            await result.ExecuteAsync(context.HttpContext);
        }

    };
})
.AddJwtBearer(options =>
    {
        string bearerRealm = $"{GlobalConfig.Hostname}:{GlobalConfig.Hostport}";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = bearerRealm,
            ValidAudience = bearerRealm,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(GlobalConfig.apiTokenSecretKey!))
        };
        options.Events = new JwtBearerEvents
        {

            OnMessageReceived = context =>
            {
                var fn = "_Middleware.JWT_";
                DBg.d(LogLevel.Trace, $"{fn} OnMessageReceived");
                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                var fn = "_Middleware.JWT_";
                DBg.d(LogLevel.Trace, $"{fn} OnChallenge");
                return Task.CompletedTask;
            },

        };
    })

.AddGoogle("Google", options =>
    {
        options.ClientId = GlobalStatic.googleClientID;
        options.ClientSecret = GlobalStatic.googleClientSecret;
        // Use the authorization code grant type - this is default for .AddGoogle
        //options.SaveTokens = true;
        options.SignInScheme = IdentityConstants.ExternalScheme;
        options.Events.OnCreatingTicket = (context) =>
            {
                if (context != null)
                {
                    context?.Identity?.AddClaim(new Claim(ClaimTypes.AuthenticationMethod, "Google"));
                }
                return Task.CompletedTask;
            };
        //google keep
        //ggogle tasks
        //google saved/interests

        //options.Scope.Add("https://www.googleapis.com/auth/tasks");
        options.Scope.Add(GoogleController.GOOGLE_TASKS_API_TASKS_OAUTH_SCOPE);
        //options.Scope.Add("https://www.googleapis.com/auth/tasks.readonly");

        options.Events = new OAuthEvents
        {
            OnCreatingTicket = context =>
            {
                var accessToken = context.AccessToken;
                DBg.d(LogLevel.Trace, "Google - OnCreatingTicket - token: " + accessToken);
                UserSessionService.AddAccessToken(context.HttpContext, "Google", accessToken!);
                return Task.CompletedTask;
            }
        };

    })
.AddMicrosoftAccount("Microsoft", options =>
{
    options.ClientId = GlobalStatic.microsoftClientId;
    options.ClientSecret = GlobalStatic.microsoftClientSecret;
    options.SignInScheme = IdentityConstants.ExternalScheme;
    options.Events.OnCreatingTicket = (context) =>
    {
        if (context != null)
        {
            context?.Identity?.AddClaim(new Claim(ClaimTypes.AuthenticationMethod, "Microsoft"));
        }
        return Task.CompletedTask;
    };

    // there are several locations where "notes" and lists of "things" are 
    // stored in Microsoft's Graph API. 
    // first we have the Windows 10+ "Sticky Notes" app. These are actually 
    // elements stored in Outlook mail:
    // https://graph.microsoft.com/v1.0/me/MailFolders/notes/messages

    options.Scope.Add("https://graph.microsoft.com/Mail.Read");



    // To-Do --> outlook Tasks
    // OneNote 
    // Microsoft Lists (coughLAMEcough)

    // we have to EXPLICITLY store the access token for the Microsoft Graph API
    // if we want to make use of it across multiple requests/endpoints.
    options.Events = new OAuthEvents
    {
        OnCreatingTicket = context =>
        {
            var accessToken = context.AccessToken;
            DBg.d(LogLevel.Trace, "Microsoft - OnCreatingTicket - token: " + accessToken);
            UserSessionService.AddAccessToken(context.HttpContext, "Microsoft", accessToken!);
            return Task.CompletedTask;
        }
    };

});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SuperUser", policy => policy.RequireRole("SuperUser"));
    options.AddPolicy("listowner", policy => policy.RequireRole("listowner"));
    options.AddPolicy("contributor", policy => policy.RequireRole("contributor"));

});

builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = GlobalStatic.antiForgeryCookieName;
    options.Cookie.SameSite = SameSiteMode.None;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.None
        : CookieSecurePolicy.Always;

});

builder.Services.AddControllersWithViews();

builder.Services.AddDatabaseDeveloperPageExceptionFilter();



var app = builder.Build();

// SeedRoles makes sure our roles in the IdentifyUser system are created
// here's where we would add any default database stuffing as well
// like a "sample" list or users or something.

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<GeFeSLEDb>();
        db.Database.Migrate();

        GlobalStatic.SeedRoles(services).Wait();

        // also always make sure backdoorAdmin is a user in db
        if (GlobalConfig.backdoorAdmin != null &&
            GlobalConfig.backdoorAdmin.UserName != null)
        {
            var userManager = services.GetRequiredService<UserManager<GeFeSLEUser>>();
            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
            DBg.d(LogLevel.Trace, "Checking to see if backdoorAdmin in db..");
            GeFeSLEUser? backdoorAdminUser = await userManager.FindByNameAsync(GlobalConfig.backdoorAdmin.UserName);
            if (backdoorAdminUser == null)
            {
                DBg.d(LogLevel.Trace, "backdoorAdmin not found in database, adding");
                DBg.d(LogLevel.Trace, "backdoorAdmin from config: " + GlobalConfig.backdoorAdmin.UserName);
                var result = await userManager.CreateAsync(GlobalConfig.backdoorAdmin);
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(GlobalConfig.backdoorAdmin, "SuperUser");

                }
                await db.SaveChangesAsync();
                GlobalConfig.backdoorAdmin = await userManager.FindByNameAsync(GlobalConfig.backdoorAdmin.UserName);
            } // backdoorAdmin NOT found in db. added
            else
            {
                DBg.d(LogLevel.Trace, "backdoorAdmin already found in database");
            }

            // always overwrite the backdoorAdmin password with the one from the config file
            // if one is specified
            GlobalConfig.backdoorAdmin = await userManager.FindByNameAsync(GlobalConfig.backdoorAdmin.UserName);
            if (GlobalConfig.backdoorAdminPassword != null)
            {
                var removePasswordResult = await userManager.RemovePasswordAsync(GlobalConfig.backdoorAdmin!);
                if (removePasswordResult.Succeeded)
                {
                    var addPasswordResult = await userManager.AddPasswordAsync(GlobalConfig.backdoorAdmin!, GlobalConfig.backdoorAdminPassword);
                    if (addPasswordResult.Succeeded)
                    {
                        DBg.d(LogLevel.Trace, "backdoorAdmin password reset to match config file: " + GlobalConfig.backdoorAdminPassword);
                    }
                    else
                    {
                        DBg.d(LogLevel.Error, "CAN'T USE backdoorAdmin pwd from CONFIG FILE:");
                        // Log the errors
                        foreach (var error in addPasswordResult.Errors)
                        {
                            DBg.d(LogLevel.Error, error.Description);
                        }
                        // exit the application; having a crap backdoor pwd is a no no.
                        Environment.Exit(1);
                    }
                }
                else
                {
                    // Log the errors
                    foreach (var error in removePasswordResult.Errors)
                    {
                        DBg.d(LogLevel.Error, error.Description);
                    }
                }
            }
            await db.SaveChangesAsync();

        } // backdoorAdmin provided from config file. 
    } // end try
    catch (Exception ex)
    {
        DBg.d(LogLevel.Critical, $"Error occurred while seeding default roles & super user in Database: {ex.Message}");
        Environment.Exit(1);
    }
}

// setup session middleware ---------------------------------
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error"); // improve this. actually define that route for one. 
    app.UseHsts();
}

app.UseRouting();
app.UseSession(); // Add this line to enable session.
app.UseAuthentication(); // must be before authorization
app.UseAuthorization();

app.UseAntiforgery();

app.Use(async (context, next) =>
    {
        var fn = "_Middleware.Use_"; DBg.d(LogLevel.Trace, fn);

        // like in our authentication redirects above, we want to 
        // detect if this is a CORS preflight request and if so, add the headers
        // so its not rejected from the plugins
        var origin = context.Request.Headers["Origin"].FirstOrDefault();
        if (origin.IsNullOrEmpty())
        {
            origin = context.Request.Headers["Referer"].FirstOrDefault();
            if (origin.IsNullOrEmpty())
            {
                origin = "(endpoint direct)";
            }
        }
        var remoteIpAddress = context.Connection.RemoteIpAddress;
        DBg.d(LogLevel.Trace, $"{fn} Request origin: {origin} - from {remoteIpAddress}");
        //GlobalStatic.dumpRequest(context);
        if (GlobalStatic.IsCorsRequest(context.Request))
        {
            GlobalStatic.AddCorsHeaders(context.Request, context.Response);
            // Handle preflight request
            if (context.Request.Method == "OPTIONS")
            {
                DBg.d(LogLevel.Trace, $"{fn} _CORs Preflight");
                context.Response.StatusCode = 200;
                await context.Response.WriteAsync(string.Empty);
                return;
            }
        }
        // if we get here its not a Cors request or it IS
        // but its not a pre-flight
        // now we check to see if the file requested is in our list of protected

        var db = context.RequestServices.GetRequiredService<GeFeSLEDb>();
        var userManager = context.RequestServices.GetRequiredService<UserManager<GeFeSLEUser>>();
        var roleManager = context.RequestServices.GetRequiredService<RoleManager<IdentityRole>>();

        var path = context.Request.Path.Value;
        DBg.d(LogLevel.Trace, $"{fn} protected file check: {path}");
        UserDto sessionUser = UserSessionService.amILoggedIn(context);

        if (path != null && ProtectedFiles.ContainsFile(path))
        {
            // is the user logged in? 

            if (!sessionUser.IsAuthenticated)
            {
                // no - make a nice redirect page like the normal UNAUTH page above. 
                DBg.d(LogLevel.Debug, $"{fn} Protected file {path} - requires authenticated user. 401-Reject");
                var sb = new StringBuilder();
                string msg = $"401 -You are not authorized to access {path}";
                await GlobalStatic.GenerateUnAuthPage(sb, msg);
                var result = Results.Content(sb.ToString(), "text/html");
                await result.ExecuteAsync(context);
                return;
            }
            else
            {
                var user = await UserSessionService.mapUserNameToDBUser(sessionUser.UserName, userManager);
                // if the user isn't in the db (null here) but we're authenticated we have an issue
                // maybe someone deleted them from db after they logged in. 
                if (user is null)
                {
                    var msg = $"{fn} User {sessionUser.UserName} is authenticated but not in database. Logging them out!";
                    DBg.d(LogLevel.Warning, msg);
                    context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    var sb = new StringBuilder();
                    await GlobalStatic.GenerateUnAuthPage(sb, msg);
                    var result = Results.Content(sb.ToString(), "text/html");
                    await result.ExecuteAsync(context);
                    return;
                }
                else
                {
                    (bool isAllowed, string? ynot) = await ProtectedFiles.IsFileVisibleToUser(path, user, userManager);
                    if (!isAllowed)
                    {
                        // no - make a nice redirect page like the normal UNAUTH page using the ynot message  
                        DBg.d(LogLevel.Debug, $"{fn} Protected file {path} - UNAUTH: {ynot}");
                        var sb = new StringBuilder();
                        string msg = $"401 -You are not authorized to access {path}<br>{ynot}";
                        await GlobalStatic.GenerateUnAuthPage(sb, msg);
                        var result = Results.Content(sb.ToString(), "text/html");
                        await result.ExecuteAsync(context);
                        return;
                    }
                    else
                    {
                        DBg.d(LogLevel.Debug, $"{fn} Protected file {path} - ALLOWED for {user.UserName}.");

                    }
                }
            }
        }
        else
        {
            string msg = $"{path} <-- {sessionUser.UserName ?? "anonymous"} [{sessionUser.Role ?? "no role"}] from {remoteIpAddress}";
            DBg.d(LogLevel.Information, msg);
        }

        // otherwise, do the normal thing
        try
        {
            await next.Invoke();
        }
        catch (BadHttpRequestException ex) when
         (ex.InnerException is AntiforgeryValidationException)
        {
            var antiForgeryEx = ex.InnerException as AntiforgeryValidationException;
            // Log the error if needed
            // _logger.LogError(ex);
            DBg.d(LogLevel.Error, $"AntiforgeryValidationException: {antiForgeryEx.Message}");
            context.Response.Clear();
            context.Response.StatusCode = 400; // Or any status code you want to return
            context.Response.ContentType = "application/json";

            var responseBody = new
            {
                error = new
                {
                    message = antiForgeryEx.Message,
                    type = antiForgeryEx.GetType().Name
                }
            };

            await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(responseBody));

            return;
        }
    });


//app.UseHttpsRedirection();
app.UseStaticFiles();


app.UseCors(builder =>
        {
            builder.AllowAnyHeader()
                   .AllowAnyMethod()
                   .AllowCredentials();

        });



// add an endpoint that adds a user to the database
app.MapPost("/users", async (GeFeSLEUser user, GeFeSLEDb db,

            HttpContext httpContext,
            UserManager<GeFeSLEUser> userManager,
            RoleManager<IdentityRole> roleManager) =>
{
    string fn = "/users (POST)"; DBg.d(LogLevel.Trace, $"{fn}: {user.UserName} {user.Email}");
    // if username AND email are null, return bad request
    if (user.UserName.IsNullOrEmpty() && user.Email.IsNullOrEmpty())
    {
        DBg.d(LogLevel.Trace, $"{fn} username AND email are both null ==> 400");
        return Results.BadRequest();
    }
    else
    {
        // if the username is empty, use the email. This will cover for google and Microsoft accounts. 
        if (user.UserName.IsNullOrEmpty())
        {
            user.UserName = user.Email;
        }
        try
        {
            var result = await userManager.CreateAsync(user);
            if (result.Succeeded)
            {
                // var token = await userManager.GeneratePasswordResetTokenAsync(user);
                // var result = await userManager.ResetPasswordAsync(user, token, newpassword!);
                DBg.d(LogLevel.Trace, $"{fn} user created");
                return Results.Created($"/users/{user.Id}", user);
            }
            else
            {
                foreach (var error in result.Errors)
                {
                    DBg.d(LogLevel.Trace, $"{fn} - Error: {error.Code}, Description: {error.Description}");
                }
                return Results.BadRequest(result.Errors);
            }
        }
        catch (Exception e)
        {
            return Results.BadRequest($"adduser - Error: {e.Message}");
        }
    }

}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});


// add an endpoint that modifies a user in the database
app.MapPut("/modifyuser", async (GeFeSLEUser user,
            GeFeSLEDb db,
            HttpContext httpContext,
            UserManager<GeFeSLEUser> userManager,
            RoleManager<IdentityRole> roleManager) =>
{
    // Read the form data
    DBg.d(LogLevel.Trace, "modifyuser");
    DBg.d(LogLevel.Trace, $"modifyuser: {user.UserName} {user.Email}");

    var moduser = await userManager.FindByIdAsync(user.Id);
    if (moduser is null) return Results.NotFound();

    moduser.UserName = user.UserName;
    moduser.Email = user.Email;
    moduser.PhoneNumber = user.PhoneNumber;

    try
    {
        var result = await userManager.UpdateAsync(moduser);
        if (result.Succeeded)
        {
            DBg.d(LogLevel.Trace, "modifyuser: user modified");
            // if the role is not null, add the user to the role

            return Results.Ok();
        }
        else
        {
            DBg.d(LogLevel.Trace, "modifyuser: user not modified: ");
            foreach (var error in result.Errors)
            {
                DBg.d(LogLevel.Trace, $"Error: {error.Code}, Description: {error.Description}");
            }
            return Results.BadRequest(result.Errors);
        }
    }
    catch (Exception e)
    {
        return Results.BadRequest(e);
    }


}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});


// deleteuser endpoint
app.MapGet("/deleteuser/{id}", async (string id, GeFeSLEDb db,

            HttpContext httpContext,
            UserManager<GeFeSLEUser> userManager,
            RoleManager<IdentityRole> roleManager) =>
{
    // Read the form data
    DBg.d(LogLevel.Trace, "deleteuser");
    DBg.d(LogLevel.Trace, $"deleteuser: {id}");

    var deluser = await userManager.FindByIdAsync(id);
    if (deluser is null) return Results.NotFound();

    try
    {
        var result = await userManager.DeleteAsync(deluser);
        if (result.Succeeded)
        {
            DBg.d(LogLevel.Trace, "deleteuser: user deleted");
            return Results.Redirect("/_edituser.html");
        }
        else
        {
            DBg.d(LogLevel.Trace, "deleteuser: user not deleted: ");
            foreach (var error in result.Errors)
            {
                DBg.d(LogLevel.Trace, $"Error: {error.Code}, Description: {error.Description}");
            }
            return Results.BadRequest(result.Errors);
        }
    }
    catch (Exception e)
    {
        return Results.BadRequest(e);
    }

}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});




app.MapGet("/showusers/{username}", async (string username,
        GeFeSLEDb db,

        HttpContext httpContext,
        UserManager<GeFeSLEUser> userManager,
        RoleManager<IdentityRole> roleManager) =>
{
    DBg.d(LogLevel.Trace, "showusers");
    GeFeSLEUser? me = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);

    DBg.d(LogLevel.Trace, $"showusers: {username}");
    var user = await userManager.FindByNameAsync(username);
    if (user is not null)
    {
        return Results.Ok(user);
    }
    else
    {
        return Results.NotFound();
    }

}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});


app.MapGet("/showusers", async (GeFeSLEDb db,

        HttpContext httpContext,
        UserManager<GeFeSLEUser> userManager,
        RoleManager<IdentityRole> roleManager) =>
{
    DBg.d(LogLevel.Trace, "showusers");
    GeFeSLEUser? me = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    DBg.d(LogLevel.Trace, "showusers: all users");
    // that's fine return ALL users in UserManager
    var users = await userManager.Users.ToListAsync();
    // if there are no users,
    if (users.Count == 0)
    {
        return Results.NotFound();
    }
    else
    {
        return Results.Ok(users);
    }

}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});

// If a user is in the database (and they have successfully logged in)
// generates a pwd reset token
// NOTE: that by requiring authentication this does mean that
// if a user has forgotten their pwd, a SuperUser can HARD reset it
// but they can't request a pwd reset themselves
// This is deliberate until we have outbound email and checks to ensure
// all local users have actual email accounts (we need verification etc.)

app.MapGet("/users/{userid}/password", async (string userid,
    HttpContext context,
    GeFeSLEDb db,
    UserManager<GeFeSLEUser> userManager) =>
{
    string fn = "/users/{userid}/password (GET)"; DBg.d(LogLevel.Trace, fn);
    GeFeSLEUser? me = UserSessionService.UpdateSessionAccessTime(context, db, userManager);

    var user = await userManager.FindByIdAsync(userid);
    if (user is null)
    {
        DBg.d(LogLevel.Trace, $"{fn} user {userid} not found");
        return Results.NotFound();
    }
    else
    {
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        return Results.Ok(token);
    }
}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser, listowner, contributor"
});

// A SuperUser can reset a user's password to an arbitrary value
app.MapDelete("/users/{userid}/password", async (string userid,
        [FromBody] PasswordChangeDto passwordChangeDto,
        GeFeSLEDb db,
        HttpContext httpContext,
        UserManager<GeFeSLEUser> userManager,
        RoleManager<IdentityRole> roleManager) =>
{
    string fn = "/users/{userid}/password (DEL)"; DBg.d(LogLevel.Trace, fn);
    GeFeSLEUser? me = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    string msg;
    var user = await userManager.FindByIdAsync(userid);
    if (user is null)
    {
        msg = $"{fn} user {userid} not found";
        DBg.d(LogLevel.Trace, msg);
        return Results.NotFound();
    }
    else if (passwordChangeDto.NewPassword.IsNullOrEmpty())
    {
        msg = $"{fn} new password is null";
        DBg.d(LogLevel.Trace, msg);
        return Results.BadRequest(msg);
    }
    // else if (passwordChangeDto.ResetToken.IsNullOrEmpty())
    // {
    //    msg = $"{fn} reset token is null";
    //    DBg.d(LogLevel.Trace, msg);
    //    return Results.BadRequest(msg);
    //}
    else
    {
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, passwordChangeDto.NewPassword);
        if (result.Succeeded)
        {
            return Results.Ok();
        }
        else
        {
            foreach (var error in result.Errors)
            {
                DBg.d(LogLevel.Trace, $"Error: {error.Code}, Description: {error.Description}");
            }
            return Results.BadRequest(result.Errors);
        }
    }

}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser"
});

// a user who has previous requested the reset token can change it
// here. Again, like with the pwd reset token endpoint, this is 
// limited to users who have already authenticated.
// no "I forget my pwd" requests here. Have to be reset by SuperUsers.
// (because we have no email validation in place yet)
app.MapPost("/users/{userid}/password", async (string userid,
        [FromBody] PasswordChangeDto passwordChangeDto,
        GeFeSLEDb db,
        HttpContext httpContext,
        UserManager<GeFeSLEUser> userManager,
        RoleManager<IdentityRole> roleManager) =>
{
    string fn = "/users/{userid}/password (POST)"; DBg.d(LogLevel.Trace, fn);
    GeFeSLEUser? me = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    string msg;
    var user = await userManager.FindByIdAsync(userid);
    if (user is null)
    {
        msg = $"{fn} user {userid} not found";
        DBg.d(LogLevel.Trace, msg);
        return Results.NotFound();
    }
    else if (passwordChangeDto.NewPassword.IsNullOrEmpty())
    {
        msg = $"{fn} new password is null";
        DBg.d(LogLevel.Trace, msg);
        return Results.BadRequest(msg);
    }
    else if (passwordChangeDto.ResetToken.IsNullOrEmpty())
    {
        msg = $"{fn} reset token is null";
        DBg.d(LogLevel.Trace, msg);
        return Results.BadRequest(msg);
    }
    else
    {
        var result = await userManager.ResetPasswordAsync(user, passwordChangeDto.ResetToken, passwordChangeDto.NewPassword);
        if (result.Succeeded)
        {
            return Results.Ok();
        }
        else
        {
            foreach (var error in result.Errors)
            {
                DBg.d(LogLevel.Trace, $"Error: {error.Code}, Description: {error.Description}");
            }
            return Results.BadRequest(result.Errors);
        }
    }

}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner,contributor"
});

// return List<string> of all assigned APPLICATION roles for a user
// Note these are for base API access, access to individual lists
// will depend on list owner/creator, list ownership and contributor assignments
app.MapGet("/users/{userid}/roles", async (string userid,
        GeFeSLEDb db,
        HttpContext httpContext,
        UserManager<GeFeSLEUser> userManager,
        RoleManager<IdentityRole> roleManager) =>
{
    string fn = $"/users/{userid}/roles (GET)"; DBg.d(LogLevel.Trace, fn);
    
    GeFeSLEUser? me = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);

    var user = await userManager.FindByIdAsync(userid);
    if (user is null)
    {
        DBg.d(LogLevel.Information, $"{fn} --> {userid} not found");
        return Results.NotFound();
    }
    else
    {
        IList<string> roles = await userManager.GetRolesAsync(user);
        if (roles.Count == 0)
        {
            DBg.d(LogLevel.Information, $"{fn} --> {user.UserName} has no role");
            return Results.Ok(); //TODO: change to no content
        }
        else
        {
            DBg.d(LogLevel.Information, $"{fn} --> {user.UserName} has roles {System.Text.Json.JsonSerializer.Serialize(roles)}");
            return Results.Ok(roles);
        }
    }

}
).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});



app.MapPost("/users/{userid}/roles", async (string userid,
        [FromBody] IList<string> roles,
        GeFeSLEDb db,
        HttpContext httpContext,
        UserManager<GeFeSLEUser> userManager,
        RoleManager<IdentityRole> roleManager) =>
{
    string fn = $"/users/{userid}/roles (POST)";
    DBg.d(LogLevel.Trace, fn);
    GeFeSLEUser? me = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    var sessionUser = UserSessionService.amILoggedIn(httpContext);

    var user = await userManager.FindByIdAsync(userid);
    if (user is null)
    {
        DBg.d(LogLevel.Trace, $"{fn} user {userid} not found");
        return Results.NotFound();
    }
    else
    {
        // for each role in the list, add the user to the role
        // note AddToRoleAsync will not add a user to a role they are already in
        bool success = true;
        List<string> added = new List<string>();
        var errors = new List<IdentityError>();
        foreach (var role in roles)
        {
            if(sessionUser.Role != "SuperUser" && role == "SuperUser")
            {
                DBg.d(LogLevel.Trace, $"{fn} user {userid} NOT ASSIGNED to role {role}: Insufficient permissions");
                errors.Add(new IdentityError { Code = "403", Description = "Insufficient permissions" });
                success = false;
                continue;
            }
            var result = await userManager.AddToRoleAsync(user, role);
            if (!result.Succeeded)
            {
                DBg.d(LogLevel.Trace, $"{fn} user {userid} NOT ASSIGNED to role {role}: {result.Errors}");
                foreach (IdentityError error in result.Errors)
                {
                    errors.Add(error);
                }
                success = false;
            }
            else {
                added.Add(role);
            
            }
        }
        if (success) {
            DBg.d(LogLevel.Information, $"{fn} -> user {userid} assigned to roles {System.Text.Json.JsonSerializer.Serialize(added)}");
            return Results.Ok();
        }
        else {
            DBg.d(LogLevel.Information, $"{fn} -> user {userid} NOT ASSIGNED to roles {System.Text.Json.JsonSerializer.Serialize(roles)}");
            return Results.BadRequest(errors);
        }
    }
}
).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});

app.MapGet("/deleterole/{username}/{role}", async (string username,
        string role,
        GeFeSLEDb db,

        HttpContext httpContext,
        UserManager<GeFeSLEUser> userManager,
        RoleManager<IdentityRole> roleManager) =>
{
    DBg.d(LogLevel.Trace, "deleterole");
    GeFeSLEUser? me = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);

    // dont need checks for username==null, will 404 on that anyway
    var user = await userManager.FindByNameAsync(username);
    if (user is null)
    {
        DBg.d(LogLevel.Trace, $"deleterole: user {username} not found");
        return Results.NotFound();
    }
    else
    {
        var result = await userManager.RemoveFromRoleAsync(user, role);
        if (result.Succeeded)
        {
            DBg.d(LogLevel.Trace, $"deleterole: user {username} UNASSIGNED from role {role}");
            return Results.Ok();
        }
        else
        {
            DBg.d(LogLevel.Trace, $"deleterole: user {username} NOT UNASSIGNED to role {role}");
            return Results.BadRequest(result.Errors);
        }
    }
}
).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});

app.MapGet("/showlists", async (GeFeSLEDb db, HttpContext httpContext) =>
{
    DBg.d(LogLevel.Trace, "showlists");

    var userManager = httpContext.RequestServices.GetRequiredService<UserManager<GeFeSLEUser>>();
    GeFeSLEUser? me = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);

    return await db.Lists.ToListAsync();

});

app.MapGet("/showlists/{id}", async (int id,
            GeFeSLEDb db,
            UserManager<GeFeSLEUser> userManager,
            HttpContext httpContext) =>
   {
       DBg.d(LogLevel.Trace, $"showlists/{id}");
       GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
       var showlist = await db.Lists.FindAsync(id);
       // if showlist is not null, then return it
       if (showlist is not null)
       {
           DBg.d(LogLevel.Trace, $"showlists/{id} found: {showlist}");
           return Results.Ok(showlist);
       }
       else
       {
           DBg.d(LogLevel.Trace, $"showlists/{id} not found");
           return Results.NotFound();
       }
   });

app.MapPost("/addlist", async (GeList newlist,
    GeFeSLEDb db,
    HttpContext httpContext,
    UserManager<GeFeSLEUser> userManager,
    RoleManager<IdentityRole> roleManager) =>
{
    var fn = "/addlist"; DBg.d(LogLevel.Trace, fn);

    // if the newlist.Name is null, return bad request
    if (newlist.Name.IsNullOrEmpty())
    {
        return Results.BadRequest("Cannot have a list with no name. A Horse maybe... but not a list.");
    }
    DBg.d(LogLevel.Trace, $"{fn} - new list name: {newlist.Name}");

    // if the newlist.Name is the same as an existing list, return bad request
    var list = await db.Lists.Where(l => l.Name == newlist.Name).FirstOrDefaultAsync();
    if (list is not null)
    {
        return Results.BadRequest("List with that name already exists");
    }
    GeFeSLEUser? me = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    if (me is null)
    {
        return Results.BadRequest("Could not determine who you are");
    }
    // no need to check for roles, auth middleware already did it. 


    newlist.Creator = me;
    newlist.ListOwners.Add(me);
    db.Lists.Add(newlist);
    ProtectedFiles.AddList(newlist);
    await db.SaveChangesAsync();
    await newlist.GenerateHTMLListPage(db);
    await newlist.GenerateRSSFeed(db);
    await newlist.GenerateJSON(db);
    _ = GlobalStatic.GenerateHTMLListIndex(db);

    string msg = $"/showlists/{newlist.Id}";
    return Results.Created(msg, newlist);
}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});


app.MapPut("/modifylist", async (GeList inputList,
    GeFeSLEDb db,
    UserManager<GeFeSLEUser> userManager,
    HttpContext httpContext) =>
{
    string dumpList = System.Text.Json.JsonSerializer.Serialize(inputList);

    DBg.d(LogLevel.Trace, $"modifylist: {dumpList}");
    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    var modlist = await db.Lists.FindAsync(inputList.Id);
    var namechange = false;
    if (modlist is null) return Results.NotFound();

    // if the name of the list has changed, delete the old html file; new one is created below anyway
    if (modlist.Name != inputList.Name)
    {

        var filename = $"{modlist.Name}.html";
        var dest = Path.Combine(GlobalConfig.wwwroot!, filename);
        if (File.Exists(dest))
        {
            DBg.d(LogLevel.Trace, $"Deleting {dest}");
            File.Delete(dest);
        }
        // also delete the rss feed
        filename = $"rss-{modlist.Name}.xml";
        dest = Path.Combine(GlobalConfig.wwwroot!, filename);
        if (File.Exists(dest))
        {
            DBg.d(LogLevel.Trace, $"Deleting {dest}");
            File.Delete(dest);
        }

        namechange = true;
    }

    modlist.Name = inputList.Name;
    modlist.Comment = inputList.Comment;
    modlist.ModifiedDate = DateTime.Now;
    modlist.SetVisibility(inputList.Visibility);

    await db.SaveChangesAsync();
    await modlist.GenerateHTMLListPage(db);
    await modlist.GenerateRSSFeed(db);
    await modlist.GenerateJSON(db);
    if (namechange)
    {
        await GlobalStatic.GenerateHTMLListIndex(db);
    }
    return Results.Ok();
}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});


app.MapGet("/showitems/{listId}", async (int listId,
        GeFeSLEDb db,
        UserManager<GeFeSLEUser> userManager,
        HttpContext httpContext) =>
{
    DBg.d(LogLevel.Trace, $"showitems/{listId}");
    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    var items = await db.Items.Where(item => item.ListId == listId).ToListAsync();
    return Results.Ok(items);
});

app.MapGet("/showitems/{listid}/{id}", async (int listId,
        int id,
        GeFeSLEDb db,
        UserManager<GeFeSLEUser> userManager,
        HttpContext httpContext) =>
{
    DBg.d(LogLevel.Trace, $"showitems/{listId}/{id}");
    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    var showitem = await db.Items.FindAsync(id);
    if (showitem is not null)
    {
        return Results.Ok(showitem);
    }
    else
    {
        return Results.NotFound();
    }
});

app.MapPost("/additem/{listid}", async (int listid,
    GeListItem newitem,
    GeFeSLEDb db,
    UserManager<GeFeSLEUser> userManager,
    HttpContext httpContext) =>
{

    DBg.d(LogLevel.Trace, $"additem/{listid} <- {System.Text.Json.JsonSerializer.Serialize(newitem)}");
    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    // if the ListId of newitem is 0 (which is ok - no value in json int is 0), then set it to listid
    if (newitem.ListId == 0)
    {
        newitem.ListId = listid;
    }
    else
    {
        if (newitem.ListId != listid) return Results.BadRequest("ListId does not match");
    }
    db.Items.Add(newitem);
    await db.SaveChangesAsync();
    // find the list that corresponds to listid
    var list = await db.Lists.FindAsync(listid);
    if (list is null) return Results.NotFound();
    await list.GenerateHTMLListPage(db);
    await list.GenerateRSSFeed(db);
    await list.GenerateJSON(db);
    return Results.Created($"/showitems/{newitem.ListId}/{newitem.Id}", newitem);
}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner,contributor"
});


app.MapPut("/modifyitem", async (GeListItem inputItem,
        GeFeSLEDb db,
        UserManager<GeFeSLEUser> userManager,
        HttpContext httpContext) =>
{
    DBg.d(LogLevel.Trace, $"modifyitem: <- {System.Text.Json.JsonSerializer.Serialize(inputItem)}");
    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    var moditem = await db.Items.FirstOrDefaultAsync(item => item.Id == inputItem.Id && item.ListId == inputItem.ListId);
    // check for listowner or contributor of the list this item belongs to
    if (moditem is null) return Results.NotFound();

    moditem.Name = inputItem.Name;
    moditem.Comment = inputItem.Comment;
    moditem.IsComplete = inputItem.IsComplete;
    moditem.Tags = inputItem.Tags;
    moditem.ModifiedDate = DateTime.Now;

    await db.SaveChangesAsync();
    var list = await db.Lists.FindAsync(inputItem.ListId);
    if (list is null) return Results.NotFound();
    await list.GenerateHTMLListPage(db);
    await list.GenerateRSSFeed(db);
    await list.GenerateJSON(db);
    return Results.Ok();
}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner,contributor"
});


// add an endpoint that DELETEs an item from a list
app.MapDelete("/deleteitem/{listid}/{id}", async (int listid,
        int id,
        GeFeSLEDb db,
        UserManager<GeFeSLEUser> userManager,
        HttpContext httpContext) =>
{
    DBg.d(LogLevel.Trace, $"deleteitem/{listid}/{id}");
    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    // check if list owner is owner of THIS list
    var delitem = await db.Items.FindAsync(id);
    if (delitem is null) return Results.NotFound();
    db.Items.Remove(delitem);
    await db.SaveChangesAsync();
    var list = await db.Lists.FindAsync(listid);
    if (list is null) return Results.NotFound();
    await list.GenerateHTMLListPage(db);
    await list.GenerateRSSFeed(db);
    await list.GenerateJSON(db);
    return Results.Ok();
}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});



// moves an item between two lists
// yes, we could also use the /deleteitem and /additem endpoints but is less of a hit
app.MapPost("/moveitem", async (
    [FromBody] MoveItemDto data,
    GeFeSLEDb db,
    UserManager<GeFeSLEUser> userManager,
    HttpContext httpContext) =>
{
    var fn = "/moveitem"; DBg.d(LogLevel.Trace, fn);

    // stringify the data and log it
    string dumpData = System.Text.Json.JsonSerializer.Serialize(data);
    DBg.d(LogLevel.Trace, $"{fn} -- dump data {dumpData}");

    var itemid = data.itemid;
    var newlistid = data.listid;
    DBg.d(LogLevel.Trace, $"{fn} <-- {{ itemid: {itemid}, newlistid: {newlistid}}}");


    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);

    // find the item in question
    var item = await db.Items.FindAsync(itemid);
    if (item is null) return Results.NotFound($"Item {itemid} not found");
    var oldlistid = item.ListId;
    var newlist = await db.Lists.FindAsync(newlistid);
    if (newlist is null) return Results.NotFound($"Destination list {newlistid} not found");
    var oldlist = await db.Lists.FindAsync(oldlistid);
    // we'll never actually get this unless something has gone very bork
    if (oldlist is null) return Results.NotFound($"Source list {oldlistid} not found");

    // now the tricky part - does the user have right listowner or contributor-ship or role to modify 
    // TODO: implement permissions check; for now rely on SU/listowner roles via middleware 

    item.ListId = newlistid;

    await db.SaveChangesAsync();

    // regenerate both old AND new lists
    _ = newlist.GenerateHTMLListPage(db);
    _ = newlist.GenerateRSSFeed(db);
    _ = newlist.GenerateJSON(db);
    _ = oldlist.GenerateHTMLListPage(db);
    _ = oldlist.GenerateRSSFeed(db);
    _ = oldlist.GenerateJSON(db);
    var msg = $"Item {itemid} moved from list {oldlistid} to list {newlistid}";
    return Results.Ok(msg);
}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});

// yeah we could edit an item but quick and dirty for now
// note that this function does not care if we get a tag with spaces
// vs. some interpretations of tags where spaces delimit
app.MapPost("/removetag", async (
    [FromBody] RemoveTagDto data,
    GeFeSLEDb db,
    UserManager<GeFeSLEUser> userManager,
    HttpContext httpContext) =>
{
    var fn = "/removetag"; DBg.d(LogLevel.Trace, fn);

    // stringify the data and log it
    string dumpData = System.Text.Json.JsonSerializer.Serialize(data);
    DBg.d(LogLevel.Trace, $"{fn} -- dump data {dumpData}");

    var itemid = data.itemid;
    var gonetag = data.tag;
    DBg.d(LogLevel.Trace, $"{fn} <-- {{ itemid: {itemid}, tag: {gonetag}}}");


    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);

    // find the item in question
    var item = await db.Items.FindAsync(itemid);
    if (item is null) return Results.NotFound($"Item {itemid} not found");

    // now the tricky part - does the user have right listowner or contributor-ship or role to modify 
    // TODO: implement permissions check; for now rely on SU/listowner roles via middleware 

    // if gonetag is in the item's tags remove it. if not, we don't care
    item.Tags.Remove(gonetag);

    await db.SaveChangesAsync();
    // TODO: regenerate item's list

    var msg = $"Tag {gonetag} removed from item {itemid}";
    return Results.Ok(msg);
}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner,contributor"
});

app.MapPost("/addtag", async (
    [FromBody] AddTagDto data,
    GeFeSLEDb db,
    UserManager<GeFeSLEUser> userManager,
    HttpContext httpContext) =>
{
    var fn = "/addtag"; DBg.d(LogLevel.Trace, fn);

    // stringify the data and log it
    string dumpData = System.Text.Json.JsonSerializer.Serialize(data);
    DBg.d(LogLevel.Trace, $"{fn} -- dump data {dumpData}");

    var itemid = data.itemid;
    var newtag = data.tag;
    DBg.d(LogLevel.Trace, $"{fn} <-- {{ itemid: {itemid}, tag: {newtag}}}");


    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);

    // find the item in question
    var item = await db.Items.FindAsync(itemid);
    if (item is null) return Results.NotFound($"Item {itemid} not found");

    // now the tricky part - does the user have right listowner or contributor-ship or role to modify 
    // TODO: implement permissions check; for now rely on SU/listowner roles via middleware 

    string? msg = null;
    // if newtag is NOT the item's tags add it. List<T> doesn't prevent duplicates
    if (!item.Tags.Contains(newtag))
    {
        item.Tags.Add(newtag);
        msg = $"Tag {newtag} added to item {itemid}";
    }
    else
    {
        msg = $"Tag {newtag} already exists in item {itemid}";

    }
    await db.SaveChangesAsync();
    // TODO: regenerate item's list

    return Results.Ok(msg);
}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner,contributor"
});




// add an endpoint that DELETEs a list
app.MapDelete("/deletelist/{id}", async (int id,
        GeFeSLEDb db,
        UserManager<GeFeSLEUser> userManager,
        HttpContext httpContext) =>
{
    DBg.d(LogLevel.Trace, $"deletelist/{id}");
    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    var dellist = await db.Lists.FindAsync(id);
    if (dellist is null) return Results.NotFound();
    db.Lists.Remove(dellist);
    // also delete all items in the list
    var delitems = await db.Items.Where(item => item.ListId == id).ToListAsync();
    await db.SaveChangesAsync();
    await GlobalStatic.GenerateHTMLListIndex(db);
    return Results.Ok();
}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser"
});


// add and endpoint that regenerates the html page for all lists
app.MapGet("/regenerate", async (GeFeSLEDb db,
        UserManager<GeFeSLEUser> userManager,
        HttpContext httpContext) =>
{
    DBg.d(LogLevel.Trace, "regenerate");
    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    // add check for if listowner is owner of THIS list

    var lists = await db.Lists.ToListAsync();
    foreach (var list in lists)
    {
        await list.GenerateHTMLListPage(db);
        await list.GenerateRSSFeed(db);
        await list.GenerateJSON(db);
    }
    await GlobalStatic.GenerateHTMLListIndex(db);
    return Results.Ok($"Regenerated {lists.Count} lists");
}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});


// add an endpoint that regenerates the html page for a list
app.MapGet("/regenerate/{listid}", async (int listid,
        GeFeSLEDb db,
        UserManager<GeFeSLEUser> userManager,
        HttpContext httpContext) =>
{
    DBg.d(LogLevel.Trace, $"regenerate/{listid}");
    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    // add check for if contributor is contributor of THIS list
    // add check for if listowner is owner of THIS list

    // find the list for this id
    var list = await db.Lists.FindAsync(listid);
    if (list is null) return Results.NotFound();
    else
    {
        await list.GenerateHTMLListPage(db);
        await list.GenerateRSSFeed(db);
        await list.GenerateJSON(db);
        await GlobalStatic.GenerateHTMLListIndex(db);
        return Results.Ok($"Regenerated list {listid}");
    }
}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner,contributor"
});





app.MapGet("/oauthcallback", async (HttpContext context,
        GeFeSLEDb db,
        UserManager<GeFeSLEUser> userManager,
        RoleManager<IdentityRole> roleManager
        ) =>
{
    DBg.d(LogLevel.Trace, "oauthcallback");
    StringBuilder sb = new StringBuilder();
    var msg = "";
    var auth = await context.AuthenticateAsync(IdentityConstants.ExternalScheme);
    // look for auth success
    if (!auth.Succeeded)
    {
        msg = $"External OAuth authentication error: {auth.Failure?.Message}";
        await GlobalStatic.GenerateUnAuthPage(sb, msg);
        return Results.Content(sb.ToString(), "text/html");
    }

    // so at this point the user has already been authenticated by google or whatever
    // we need to sign the user into OUR system; create a session for them. 

    DBg.d(LogLevel.Trace, "-------------------------------------------------");
    UserSessionService.dumpSession(context);
    DBg.d(LogLevel.Trace, "-------------------------------------------------");


    // get provider out of the auth
    var provider = auth.Properties.Items[".AuthScheme"];
    // get the access_token out of the auth
    var accessToken = auth.Properties.GetTokenValue("access_token");
    DBg.d(LogLevel.Trace, $"provider: {provider} accessToken: {accessToken}");

    // get claimsPrincipal out of auth
    var claimsPrincipal = auth.Principal;
    string? email = claimsPrincipal.FindFirstValue(ClaimTypes.Email);
    if (email == null)
    {
        msg = "OAuth account does not have an email address";
        await GlobalStatic.GenerateUnAuthPage(sb, msg);
        return Results.Content(sb.ToString(), "text/html");
    }
    // find the user by email in our database
    // TODO: the user should have been "granted" with both username and email in our system the same
    //       Modify this so it checks for the OAuth user by either username OR email (or any other 
    //      identifying info? dunno !?)
    GeFeSLEUser? user = await userManager.FindByEmailAsync(email);
    string? realizedRole = null;
    string username = null;
    if (user is null)
    {
        msg = $"Hi {email} from the OAuth; You've been logged in with role: anonymous. All this means is you can't modify anything, but at least now you show up in our server logs.";
        realizedRole = "anonymous";
        username = email;
    }
    else
    {
        // user exists. get their role. Add a claimsPrincipal for the role
        // and create a session for them.
        username = user!.UserName;
        var roles = await userManager.GetRolesAsync(user);
        realizedRole = GlobalStatic.FindHighestRole(roles);
        msg = $"Welcome {username}! You are logged in as {realizedRole}";

    }
    UserSessionService.createSession(context, user.Id ?? "OAuth", username, realizedRole);
    UserSessionService.storeProvider(context, provider!);
    UserSessionService.AddAccessToken(context, provider!, accessToken!);
    await GlobalStatic.GenerateLoginResult(sb, msg);
    return Results.Content(sb.ToString(), "text/html");


});


// we cannot use LoginDto directly in the endpoint lambda inputs 
// because otherwise the middleware will try to bind the incoming
// request/form body to it, and then it wants an antiforgerytoken
// and that's a world of pain. Read it manually from the request body. 

app.MapPost("/me", async (HttpContext context,
    GeFeSLEDb db,
    IAntiforgery antiforgery,
    UserManager<GeFeSLEUser> userManager,
    RoleManager<IdentityRole> roleManager) =>
{
    string fn = "/me"; DBg.d(LogLevel.Trace, fn);

    if (!context.Request.HasFormContentType)
    {
        return Results.BadRequest("No form data.");
    }
    var form = await context.Request.ReadFormAsync();
    var login = new LoginDto
    {
        Username = form["Username"],
        Password = form["Password"],
        OAuthProvider = form["OAuthProvider"],
        Instance = form["Instance"]
    };

    if (!login.IsValid())
    {
        return Results.BadRequest("Could not deserialize body to LoginDto");
    }
    DBg.d(LogLevel.Trace, $"{fn} - login: {System.Text.Json.JsonSerializer.Serialize(login)}");

    if (login.OAuthProvider.IsNullOrEmpty())
    {
        // check the request headers to see if this is coming from the javascript API
        // should probably make this a method in GlobalStatic
        bool isJSApi = false;
        if (GlobalStatic.IsAPIRequest(context.Request))
        {
            DBg.d(LogLevel.Trace, $"{fn} LOGIN: is JS API");
            isJSApi = true;
        }
        StringBuilder sb = new StringBuilder();
        GeFeSLEUser? user = null; 
        string msg = null;
        bool success = false;
        string? realizedRole = null;
        // not OAuth, so must be a local login. MUST have login+pwd
        if (login.Username.IsNullOrEmpty() || login.Password.IsNullOrEmpty())
        {
            msg = $"{fn} LOGIN: Username or password is null.";
            DBg.d(LogLevel.Trace, msg);
        }
        else
        {
            // find the user in our userManager by username
            user = await userManager.FindByNameAsync(login.Username);
            if (user is null)
            {
                msg = $"{fn} LOGIN: Username not found in database.";
                DBg.d(LogLevel.Trace, msg);
            } // user not in db
            else
            {
                var result = await userManager.CheckPasswordAsync(user, login.Password);
                if (result)
                {
                    // get the user's role
                    var roles = await userManager.GetRolesAsync(user);
                    realizedRole = GlobalStatic.FindHighestRole(roles);
                    success = true;


                } // good user pwd
                else
                {
                    msg = $"{fn} LOGIN: Username {user} PASSWORD NOT CORRECT.";
                    // bad login web

                } // bad user pwd
            } // user IN db
        }
        // ----- return login results
        if (!success)
        {
            if (isJSApi)
            {
                DBg.d(LogLevel.Trace, $"{fn} LOGIN: BAD - RETURNING 401");
                return Results.Unauthorized();
            } // bad login -API
            else
            {
                DBg.d(LogLevel.Trace, $"{fn} LOGIN: BAD - RETURNING UNAUTH PAGE");
                await GlobalStatic.GenerateUnAuthPage(sb, msg);
                return Results.Content(sb.ToString(), "text/html");
            } // bad login - web
        }
        else
        {
            if (isJSApi)
            {
                var token = UserSessionService.createJWToken(login.Username, realizedRole);
                var antiforgerytoken = antiforgery.GetAndStoreTokens(context);
                DBg.d(LogLevel.Trace, $"{fn} LOGIN: User {login.Username} logged in as {realizedRole} VIA API RETURNING 200 + TOKEN");

                return Results.Ok(new
                {
                    token,
                    antiforgerytoken,
                    username = login.Username,
                    role = realizedRole,
                });
            } // good login -API
            else
            {
                UserSessionService.createSession(context, user.Id, user.UserName, realizedRole);
                _ = UserSessionService.UpdateSessionAccessTime(context, db, userManager);
                DBg.d(LogLevel.Trace, $"{fn} LOGIN: OK - RETURNING REDIRECT");
                return Results.Redirect("/");
            } // good login - web
        }
    }
    // its OAuth
    DBg.d(LogLevel.Debug, $"{fn} - login.OAuthProvider: {login.OAuthProvider}");
    AuthenticationProperties properties = new AuthenticationProperties { RedirectUri = $"{GlobalConfig.Hostname}:{GlobalConfig.Hostport}/oauthcallback" };
    string? authorizationScheme = null;
    switch (login.OAuthProvider)
    {
        case "microsoft":
            {
                authorizationScheme = MicrosoftAccountDefaults.AuthenticationScheme;
                break;
            }
        case "google":
            {
                authorizationScheme = GoogleDefaults.AuthenticationScheme;
                break;
            }
        case "mastodon":
            {
                if (login.Instance is null)
                {
                    return Results.BadRequest("Selected OAuth provider Mastodon but missing Mastodon instance.");
                }
                (bool isUP, string? ynot) = await MastoController.checkInstance(login.Instance);
                if (!isUP)
                {
                    return Results.BadRequest($"Mastodon instance {login.Instance} is down/unreachable: {ynot}");
                }
                else
                {
                    string instance = ynot; // steal that before we ovewrite it
                    (ApplicationToken? appToken, ynot) = await MastoController.registerAppWithInstance(instance);
                    if (appToken is null)
                    {
                        return Results.BadRequest($"Could not register {GlobalStatic.applicationName} with {instance}: {ynot}");
                    }
                    else
                    {
                        string authorizationUrl = MastoController.getMastodonOAuthUrl(appToken);
                        if (authorizationUrl is null)
                        {
                            return Results.BadRequest($"Could not get authorization URL with this appToken: {appToken}");
                        }
                        else
                        {
                            return Results.Redirect(authorizationUrl);
                        }
                    }
                }
            }

        default:
            {
                return Results.BadRequest("Unknown OAuth provider");

            }

    }
    DBg.d(LogLevel.Trace, $"{fn} {authorizationScheme} OAuth - sending {properties.RedirectUri} challenge");
    return new CustomChallengeResult(authorizationScheme, properties);

});

// new endpoint that handles the Mastodon Oauth2 callback
app.MapGet("/mastocallback", async (string code,
    HttpContext httpContext,

    UserManager<GeFeSLEUser> userManager,
    RoleManager<IdentityRole> roleManager) =>
{
    DBg.d(LogLevel.Trace, "mastocallback");
    DBg.d(LogLevel.Trace, $"code: {code}");

    // now finally we have the code, we can use it to get the access token
    // manually construct the POST request to the Mastodon server to get the access token
    string? clientId = httpContext.Session.GetString("masto.client_id");
    string? clientSecret = httpContext.Session.GetString("masto.client_secret");
    string? instance = httpContext.Session.GetString("masto.instance");
    string? realizedInstance = httpContext.Session.GetString("masto.realizedInstance");
    if (clientId is null || clientSecret is null || instance is null || realizedInstance is null)
    {
        return Results.BadRequest("BAD/MISSING Mastodon parameters in session cookie - dunno, did you forget to _login.html -> /mastoconnect -> /mastologin?");
    }

    string redirectUri = Uri.EscapeDataString($"{GlobalConfig.Hostname}:{GlobalConfig.Hostport}/mastocallback");

    string grantType = "authorization_code";
    string tokenUrl = $"{realizedInstance}/oauth/token";
    string postData = $"client_id={clientId}&client_secret={clientSecret}&grant_type={grantType}&code={code}&redirect_uri={redirectUri}&scope={GlobalStatic.mastoScopes}";
    // create httpClient
    var client = new HttpClient();
    var content = new StringContent(postData, Encoding.UTF8, "application/x-www-form-urlencoded");
    var response = await client.PostAsync(tokenUrl, content);

    // from the masto api, our possible return values are 
    // 200 OK
    // 400 Bad Request
    // 401 Unauthorized - but effectively, if its not 200, something hinky is going on
    // just bail entirely
    if (response.StatusCode != System.Net.HttpStatusCode.OK)
    {
        var error = await response.Content.ReadAsStringAsync();
        return Results.BadRequest($"Mastodon instance {instance} returned 422: {error} - Sent: {postData}");
    }
    else
    {
        // probably check for a 201 or whatever..
        // now that we have the access token, store it for use later in other endpoints
        var token = await response.Content.ReadAsStringAsync();
        var jsonToken = JObject.Parse(token);
        var accessToken = jsonToken["access_token"]!.ToString();
        DBg.d(LogLevel.Trace, $"token: {accessToken}");
        UserSessionService.AddAccessToken(httpContext, "mastodon", accessToken);
        // we STILL don't know who this user is, and we haven't
        // LOGGED them in. 
        string credentialsUrl = $"{realizedInstance}/api/v1/accounts/verify_credentials";

        // handle this better
        if (token is null) return Results.Unauthorized();

        // create httpClient
        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + accessToken);
        response = await client.GetAsync(credentialsUrl);
        if (response.StatusCode != System.Net.HttpStatusCode.OK)
        {
            var error = await response.Content.ReadAsStringAsync();
            return Results.BadRequest($"Mastodon instance {instance} returned 422: {error} - requested {credentialsUrl}");
        }
        else
        {
            // cast response.Content to a Mastonet.Entities.CredentialAccount object

            var newcontent = await response.Content.ReadAsStringAsync();
            var account = JsonConvert.DeserializeObject<Account>(newcontent);
            if (account is null) return Results.BadRequest("Mastodon instance returned null account object");

            // dump out the account object
            var accountDump = JsonConvert.SerializeObject(account, Formatting.Indented);
            DBg.d(LogLevel.Trace, $"account: {accountDump}");
            //return Results.Content($"<!DOCTYPE html><html><body><pre>{accountDump}</pre></body></html>", "text/html");

            // the username that WE will use will be their username on the mastodon instance
            var username = $"{account.UserName}@{instance}";
            DBg.d(LogLevel.Trace, $"username: {username}");
            // look this username up in the database, see if they exist
            // if so, get the roles and log them in

            var localuser = await userManager.FindByNameAsync(username!);
            // if they're not in there, that's fine. Add them, they can have 
            // anonymous role. Not sure why they're logging in tho
            StringBuilder sb = new StringBuilder();

            if (localuser is null)
            {
                UserSessionService.createSession(httpContext, null, username!, "anonymous");
                var msg = $"Hi {username} from the fediverse; You've been logged in with role: anonymous. All this means is you can't modify anything, but at least now you show up in our server logs.";
                await GlobalStatic.GenerateLoginResult(sb, msg);
                return Results.Content(sb.ToString(), "text/html");
            }
            else
            {
                // they're in there, which means we've added them, probably to assign them a role
                var roles = await userManager.GetRolesAsync(localuser);
                var realizedRole = GlobalStatic.FindHighestRole(roles);
                UserSessionService.createSession(httpContext, localuser.Id, localuser.UserName!, realizedRole);
                var msg = $"Hi {username} from the fediverse; You've been logged in with role: {realizedRole}.";
                await GlobalStatic.GenerateLoginResult(sb, msg);
                return Results.Content(sb.ToString(), "text/html");
            }


        }

    }
});

app.MapGet("/microsoftstickynotes/{listid}", async (
        int listid,
        GeFeSLEDb db,
        UserManager<GeFeSLEUser> userManager,
        HttpContext httpContext) =>
{
    DBg.d(LogLevel.Trace, "microsoftstickynotes");
    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);

    // get the access token from the session service
    string? token = UserSessionService.GetAccessToken(httpContext, "Microsoft");
    // handle this better
    if (token is null)
    {
        var sb = new StringBuilder();
        string msg = $"You need to login/authorize w/ Microsoft - I don't have a token for you. ";

        await GlobalStatic.GenerateUnAuthPage(sb, msg);
        return Results.Content(sb.ToString(), "text/html");
    }

    // obtain the list - if it doesn't exist return 404 list not found
    var list = await db.Lists.FindAsync(listid);
    if (list is null) return Results.NotFound($"List {listid} not found.");

    List<GeListItem> geListItems = await MicrosoftController.getMicrosoftOutlookTasks(httpContext, token);
    if (geListItems is null) return Results.NotFound($"No Sticky Notes for {user.UserName} found.");
    int numitems = geListItems.Count;
    if (numitems == 0) return Results.NotFound($"No Sticky Notes for {user.UserName} found.");

    // iterate through the list of GeListItems
    foreach (GeListItem item in geListItems)
    {
        // set the listId of the item to the listid
        item.ListId = list.Id;
        // add a blurb to the end of the .comment field saying who imported this item from where and when
        item.Comment += GlobalStatic.ImportAttribution(user.UserName, "Microsoft Sticky Notes", list.Name);


        // add the item to the database
        db.Items.Add(item);
    }
    // save the changes to the database
    await db.SaveChangesAsync();

    // regenerate all the list artifacts
    _ = list.GenerateHTMLListPage(db);
    _ = list.GenerateRSSFeed(db);
    _ = list.GenerateJSON(db);

    return Results.Ok($"Retreived {numitems} Sticky Notes..");

}).AllowAnonymous()
.RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser"
});



app.MapGet("/googletasklists", async (
        GeFeSLEDb db,
        UserManager<GeFeSLEUser> userManager,
        HttpContext httpContext) =>
{
    var fn = "/googletasklists";
    DBg.d(LogLevel.Trace, fn);
    GeFeSLEUser? me = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    if (me is null)
    {
        var sb = new StringBuilder();
        string msg = $"You need to login. ";

        await GlobalStatic.GenerateUnAuthPage(sb, msg);
        return Results.Content(sb.ToString(), "text/html");
    }
    // get the access token from the session service
    string? token = UserSessionService.GetAccessToken(httpContext, "Google");
    // handle this better
    if (token is null)
    {
        var sb = new StringBuilder();
        string msg = $"You need to login/authorize w/ Google - I don't have a token for you. ";

        await GlobalStatic.GenerateUnAuthPage(sb, msg);
        return Results.Content(sb.ToString(), "text/html");
    }

    List<(string, string)> taskLists = await GoogleController.getGoogleTaskLists(token);

    if (taskLists is null)
    {
        return Results.NotFound($"No Google Task Lists for {me.UserName} found.");
    }
    if (taskLists.Count == 0) return Results.NotFound($"No Google Task Lists for {me.UserName} found.");

    StringBuilder listChoosePage = await GoogleController.makeTaskListChooser(taskLists, db, httpContext, userManager, me);

    return Results.Content(listChoosePage.ToString(), "text/html");

}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser, listowner, contributor"
});

app.MapGet("/googletasklistimport/{glistid}/{listid}", async (
        string glistid,
        int listid,
        GeFeSLEDb db,
        UserManager<GeFeSLEUser> userManager,
        HttpContext httpContext) =>
{
    var fn = "/googletasklistimport";
    DBg.d(LogLevel.Trace, fn);
    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);

    // get the access token from the session service
    string? token = UserSessionService.GetAccessToken(httpContext, "Google");
    // handle this better
    if (token is null)
    {
        var sb = new StringBuilder();
        string msg = $"You need to login/authorize w/ Google - I don't have a token for you. ";

        await GlobalStatic.GenerateUnAuthPage(sb, msg);
        return Results.Content(sb.ToString(), "text/html");
    }
    if (glistid is null)
    {
        return Results.NotFound("No Google Task List ID specified.");

    }
    // find the GeLIst for this listid
    var list = await db.Lists.FindAsync(listid);
    if (list is null) return Results.NotFound();

    List<GeListItem> tasks = await GoogleController.getGoogleTasks(glistid, token);

    if (tasks is null)
    {
        return Results.NotFound($"No Google Tasks for {user.UserName} found.");
    }
    int numtasks = tasks.Count;
    if (numtasks == 0) return Results.NotFound($"No Google Tasks for {user.UserName} found.");

    foreach (GeListItem item in tasks)
    {
        item.ListId = list.Id;
        item.Comment += GlobalStatic.ImportAttribution(user.UserName, "Google Task List", list.Name);
        db.Items.Add(item);
    }
    await db.SaveChangesAsync();

    // regenerate all the list artifacts
    _ = list.GenerateHTMLListPage(db);
    _ = list.GenerateRSSFeed(db);
    _ = list.GenerateJSON(db);

    //TODO: list.function that is responsible for a list's file name
    // do for each file type. 
    return Results.Redirect($"/{list.Name}.html");

}).AllowAnonymous()
.RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser"
});


// endpoint mastobookmarks to call GET /api/v1/bookmarks in mastodon API
app.MapGet("/mastobookmarks/{listid}", async (int listid,
            int num2Get,
            bool? unbookmark, // if the checkbox isn't, there's no value, so it's null
            GeFeSLEDb db,
            UserManager<GeFeSLEUser> userManager,
            HttpContext httpContext) =>
{
    DBg.d(LogLevel.Trace, "mastobookmarks");
    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);

    // if there's no/null value for unbookmark, set it to false
    if (unbookmark is null)
    {
        unbookmark = false;
    }

    DBg.d(LogLevel.Trace, $"unbookmark: {unbookmark}");

    // check to see if the listid is valid
    var list = await db.Lists.FindAsync(listid);
    if (list is null) return Results.NotFound();

    if ((num2Get < 1) || (num2Get > 999)) return Results.BadRequest("num2Get must be between 1 and 999");

    // get the access token from the session service
    string? token = UserSessionService.GetAccessToken(httpContext, "mastodon");
    // handle this better
    if (token is null)
    {
        var sb = new StringBuilder();
        string msg = $"You need to login/authorize w/ Mastodon - I don't have a token for you. ";

        await GlobalStatic.GenerateUnAuthPage(sb, msg);
        return Results.Content(sb.ToString(), "text/html");
    }



    var instance = httpContext.Session.GetString("masto.instance");
    var realizedInstance = httpContext.Session.GetString("masto.realizedInstance");

    // array of strings to hold the status IDs of the statuses to unbookmark
    List<string> unbookmarkIDs = new List<string>();

    // create httpClient
    var client = new HttpClient();
    bool stillMorePages = true;

    int numGot = 0;
    client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);

    var apiUrl = $"{realizedInstance}/api/v1/bookmarks";

    while (stillMorePages && (numGot < num2Get))
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
                    item.ParseMastoStatus(status, listid);
                    item.Comment += GlobalStatic.ImportAttribution(user.UserName, $"Mastodon ({realizedInstance})", list.Name);

                    db.Items.Add(item);

                    // add the item.statusID to unbookmarkIDs
                    if (unbookmark == true)
                    {
                        DBg.d(LogLevel.Trace, $"unbookmarking {status.Id}");
                        unbookmarkIDs.Add(status.Id);
                    }
                    // only "count" the imported bookmarks
                    numGot++;
                    if (numGot >= num2Get) break;

                }

            }
            await db.SaveChangesAsync();
        }
        DBg.d(LogLevel.Trace, $"numGot: {numGot}");
        if (numGot >= num2Get) break;
    } // end of while loop!
      // we don't care about waiting for these tasks to complete. 
    _ = list.GenerateHTMLListPage(db);
    _ = list.GenerateRSSFeed(db);
    _ = list.GenerateJSON(db);

    _ = MastoController.unbookmarkMastoItems(token, realizedInstance, unbookmarkIDs);




    return Results.Redirect($"/{list.Name}.html");
}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});


app.MapGet("/me", (HttpContext httpContext) =>
{
    var fn = "/me"; DBg.d(LogLevel.Trace, fn);

    UserDto sessionUser = UserSessionService.amILoggedIn(httpContext);
    DBg.d(LogLevel.Information, $"{fn} --> {sessionUser}");
    return Results.Ok(sessionUser);
    })
.AllowAnonymous()
.RequireAuthorization(new AuthorizeAttribute
{ AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme });

app.MapGet("/getlistuser/{listid}", async (int listid,
        GeFeSLEDb db,

        HttpContext httpContext,
        UserManager<GeFeSLEUser> userManager,
        RoleManager<IdentityRole> roleManager) =>
{
    DBg.d(LogLevel.Trace, "getlistuser");
    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);

    // middleware rejects the request if there isn't a listid. see if the list given is a real one
    // can't use .FindAsync because its LAZY and we want all the member List<T> users
    //GeList? list = await db.Lists.FindAsync(listid);
    GeList? list = await db.Lists.Include(l => l.Creator)
                                 .Include(l => l.ListOwners)
                                 .Include(l => l.Contributors)
                                 .FirstOrDefaultAsync(l => l.Id == listid);
    if (list is null) return Results.NotFound();
    else
    {
        // take the list's .Creator, .ListOwners and .Contributors and return them as json
        var creator = list.Creator;
        var listowners = list.ListOwners;
        var contributors = list.Contributors;
        var result = new { creator, listowners, contributors };
        return Results.Ok(result);
    }

}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});

app.MapGet("/getlistisee", async (
        GeFeSLEDb db,
        HttpContext httpContext,
        UserManager<GeFeSLEUser> userManager,
        RoleManager<IdentityRole> roleManager) =>
{
    var fn = "/getlistisee"; DBg.d(LogLevel.Trace, fn);
    var sessionUser = UserSessionService.amILoggedIn(httpContext);
    GeFeSLEUser? me = null;
    if (sessionUser.IsAuthenticated)
    {
        me = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    }
    if (!sessionUser.IsAuthenticated || me is null)
    {
        DBg.d(LogLevel.Trace, $"{fn} username is null/notlogged in // Only public lists");
        // user can only see lists that are .Visibility== GeListvisibility.Public
        var listNames = await db.Lists.Where(l => l.Visibility == GeListVisibility.Public)
                                     .Select(l => l.Name)
                                     .ToListAsync();
        return Results.Ok(listNames);
    }
    else
    {
        IList<string> roles = await userManager.GetRolesAsync(me);
        if (roles.Contains("SuperUser"))
        {
            DBg.d(LogLevel.Trace, $"{fn}: SuperUser {me.UserName} // All lists");
            var listNames = await db.Lists.Select(l => l.Name).ToListAsync();
            return Results.Ok(listNames);
        }
        else
        {
            // user can only see lists that they are Creators, ListOwners of or Contributors of
            List<GeList> lists = await db.Lists.ToListAsync();
            List<string> listnames = new List<string>();

            foreach (GeList list in lists)
            {
                (bool canISee, string? ynot) = ProtectedFiles.IsListVisibleToUser(list, me, roles);
                if (canISee)
                {
                    listnames.Add(list.Name!);
                }
                else
                {
                    DBg.d(LogLevel.Trace, $"{fn}: {me.UserName} can't see {list.Name} because {ynot}");
                }

            }
            return Results.Ok(listnames);
        }

    }
}).AllowAnonymous();


app.MapPost("/setlistuser", async ([FromBody] JsonElement data,
        GeFeSLEDb db,

        HttpContext httpContext,
        UserManager<GeFeSLEUser> userManager,
        RoleManager<IdentityRole> roleManager) =>
{
    DBg.d(LogLevel.Trace, "setlistuser");
    // dump the form data in human readable form
    // for each key in form data print key and value
    foreach (var property in data.EnumerateObject())
    {
        DBg.d(LogLevel.Trace, $"{property.Name}: {property.Value}");
    }



    GeFeSLEUser? me = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);

    // the FORM post will have a:
    // listid
    // username
    // role (one of listowner or contributor // can't be anything but)
    // try
    // {
    string? listid = data.GetProperty("listid").GetString();
    string? username = data.GetProperty("username").GetString();
    string? role = data.GetProperty("role").GetString();

    // if we don't get valid strings named as above, the exception will be caught. 
    // however they can still be null i.e. { "listname":null }
    if (listid.IsNullOrEmpty() || username.IsNullOrEmpty() || role.IsNullOrEmpty())
    {
        return Results.BadRequest("listid, username and role must be specified");
    }
    else if (role != "listowner" && role != "contributor")
    {
        return Results.BadRequest("role must be listowner or contributor");
    }
    else
    {
        // find the list. List 
        GeList? list = await db.Lists.FindAsync(int.Parse(listid));
        if (list == null)
        {
            return Results.BadRequest($"List {listid} does not exist");
        }
        else
        {
            // find the user who is calling this endpoint. 
            string? callerUserName = httpContext.User.Identity?.Name;
            if (callerUserName.IsNullOrEmpty()) return Results.BadRequest("Caller is not logged in");
            // not sure how we get here with .Authentication working, but whatever
            else
            {
                GeFeSLEUser? caller = await userManager.FindByNameAsync(callerUserName!);
                if (caller == null) return Results.BadRequest("Caller is not in the database");
                else
                {
                    // find the user we want to add to the list
                    GeFeSLEUser? user = await userManager.FindByNameAsync(username!);
                    if (user == null) return Results.BadRequest($"User {username} does not exist");

                    // only the list's creator or a SuperUser can add a listowner
                    // a listowner, SuperUser or the list's creator can add a contributor
                    var roles = await userManager.GetRolesAsync(caller);
                    var realizedRole = GlobalStatic.FindHighestRole(roles);

                    if (role == "listowner")
                    {
                        if ((list.Creator == caller) ||
                            (realizedRole == "SuperUser"))
                        {
                            if (list.ListOwners.Contains(user))
                            {
                                var msg = $"{user.UserName} is already a listowner of {list.Name}";
                                DBg.d(LogLevel.Information, msg);
                                return Results.Ok(msg);
                            }
                            else
                            {
                                list.ListOwners.Add(user);
                                _ = db.SaveChangesAsync();
                                var msg = $"{caller.UserName} Added {user.UserName} to {list.Name} as a listowner";
                                DBg.d(LogLevel.Information, msg);
                                return Results.Ok(msg);
                            }
                        }
                        else
                        {
                            return Results.BadRequest("Only the list's creator or a SuperUser can add a listowner");
                        }
                    }
                    else if (role == "contributor")
                    {
                        if ((list.Creator == caller) ||
                            (realizedRole == "SuperUser") ||
                            list.ListOwners.Contains(caller))
                        {
                            if (list.Contributors.Contains(user))
                            {
                                var msg = $"{user.UserName} is already a contributor to {list.Name}";
                                DBg.d(LogLevel.Information, msg);
                                return Results.Ok(msg);
                            }
                            else
                            {
                                list.Contributors.Add(user);
                                _ = db.SaveChangesAsync();
                                var msg = $"{caller.UserName} Added {user.UserName} to {list.Name} as a contributor";
                                DBg.d(LogLevel.Information, msg);
                                return Results.Ok(msg);
                            }
                        }

                        else
                        {
                            return Results.BadRequest("Only the list's creator, a SuperUser or a listowner can add a contributor");
                        }
                    }
                    else
                    {
                        return Results.BadRequest("role must be listowner or contributor");
                    }
                }
            }
        }
    }
    // }
    // catch (Exception e)
    // {
    //     return Results.BadRequest(e.Message);
    // }
}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});

app.MapPost("/deletelistuser", async ([FromBody] JsonElement data,
        GeFeSLEDb db,

        HttpContext httpContext,
        UserManager<GeFeSLEUser> userManager,
        RoleManager<IdentityRole> roleManager) =>
{
    DBg.d(LogLevel.Trace, "deletelistuser");
    // dump the form data in human readable form
    // for each key in form data print key and value
    foreach (var property in data.EnumerateObject())
    {
        DBg.d(LogLevel.Trace, $"{property.Name}: {property.Value}");
    }



    GeFeSLEUser? me = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);

    // the FORM post will have a:
    // listid
    // username
    // role (one of listowner or contributor // can't be anything but)
    // try
    // {
    string? listid = data.GetProperty("listid").GetString();
    string? username = data.GetProperty("username").GetString();
    string? role = data.GetProperty("role").GetString();

    // if we don't get valid strings named as above, the exception will be caught. 
    // however they can still be null i.e. { "listname":null }
    if (listid.IsNullOrEmpty() || username.IsNullOrEmpty() || role.IsNullOrEmpty())
    {
        return Results.BadRequest("listid, username and role must be specified");
    }
    else if (role != "listowner" && role != "contributor")
    {
        return Results.BadRequest("role must be listowner or contributor");
    }
    else
    {
        // find the list. don't use findAsync cause that lazy loads the member collections boo
        GeList? list = await db.Lists
            .Include(l => l.ListOwners)
            .Include(l => l.Contributors)
            .FirstOrDefaultAsync(l => l.Id == int.Parse(listid));
        if (list == null)
        {
            return Results.BadRequest($"List {listid} does not exist");
        }
        else
        {
            // find the user who is calling this endpoint. 
            string? callerUserName = httpContext.User.Identity?.Name;
            if (callerUserName.IsNullOrEmpty()) return Results.BadRequest("Caller is not logged in");
            // not sure how we get here with .Authentication working, but whatever
            else
            {
                GeFeSLEUser? caller = await userManager.FindByNameAsync(callerUserName!);
                if (caller == null) return Results.BadRequest("Caller is not in the database");
                else
                {
                    // find the user we want to add to the list
                    GeFeSLEUser? user = await userManager.FindByNameAsync(username!);
                    if (user == null) return Results.BadRequest($"User {username} does not exist");

                    // only the list's creator or a SuperUser can add a listowner
                    // a listowner, SuperUser or the list's creator can add a contributor
                    var roles = await userManager.GetRolesAsync(caller);
                    var realizedRole = GlobalStatic.FindHighestRole(roles);

                    if (role == "listowner")
                    {
                        if ((list.Creator == caller) ||
                            (realizedRole == "SuperUser"))
                        {
                            if (!list.ListOwners.Contains(user))
                            {
                                var msg = $"{user.UserName} isn't a listowner of {list.Name}";
                                DBg.d(LogLevel.Information, msg);
                                return Results.Ok(msg);
                            }
                            else
                            {
                                list.ListOwners.Remove(user);
                                _ = db.SaveChangesAsync();
                                var msg = $"{caller.UserName} REMOVED {user.UserName} FROM {list.Name} as a listowner";
                                DBg.d(LogLevel.Information, msg);
                                return Results.Ok(msg);
                            }
                        }
                        else
                        {
                            return Results.BadRequest("Only the list's creator or a SuperUser can REMOVE a listowner");
                        }
                    }
                    else if (role == "contributor")
                    {
                        if ((list.Creator == caller) ||
                            (realizedRole == "SuperUser") ||
                            list.ListOwners.Contains(caller))
                        {
                            if (!list.Contributors.Contains(user))
                            {
                                var msg = $"{user.UserName} isn't a contributor to {list.Name}";
                                DBg.d(LogLevel.Information, msg);
                                return Results.Ok(msg);
                            }
                            else
                            {
                                list.Contributors.Remove(user);
                                _ = db.SaveChangesAsync();
                                var msg = $"{caller.UserName} REMOVED {user.UserName} FROM {list.Name} as a contributor";
                                DBg.d(LogLevel.Information, msg);
                                return Results.Ok(msg);
                            }
                        }

                        else
                        {
                            return Results.BadRequest("Only the list's creator, a SuperUser or a listowner can REMOVE a contributor");
                        }
                    }
                    else
                    {
                        return Results.BadRequest("role must be listowner or contributor");
                    }
                }
            }
        }
    }
    // }
    // catch (Exception e)
    // {
    //     return Results.BadRequest(e.Message);
    // }
}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});

app.MapGet("/session", async (HttpContext httpContext) =>
{
    string fn = "/session"; DBg.d(LogLevel.Trace, fn);

    StringBuilder sb = new StringBuilder();
    sb.AppendLine("<!DOCTYPE html><html><body>");

    var sessionUser = UserSessionService.amILoggedIn(httpContext);
    string? niceSession = null;
    niceSession = await UserSessionService.dumpSession(httpContext);

    string? msg = null;

    if (sessionUser.IsAuthenticated)
    {
        //that's fine, that may just mean they weren't in the database. 
        msg = $"{fn} --> username: {sessionUser.UserName} role: {sessionUser.Role}";
    }
    else
    {
        msg = $"{fn} --> Anonymous guest session.";
    }
    sb.AppendLine($"SuperUser?: {httpContext.User.IsInRole("SuperUser")}");
    sb.AppendLine("<br>");
    sb.AppendLine($"<p>{msg}</p><pre>{niceSession}</pre>");
    DBg.d(LogLevel.Information, msg);

    sb.AppendLine("</body></html>");
    return Results.Content(sb.ToString(), "text/html");
}).AllowAnonymous()
.RequireAuthorization(new AuthorizeAttribute
{ AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme });


// 
app.MapGet("/me/delete", (HttpContext httpContext) =>
{
    string fn = "/me"; DBg.d(LogLevel.Trace, fn);

    httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    // delete every Cookie
    foreach (var cookie in httpContext.Request.Cookies)
    {
        httpContext.Response.Cookies.Delete(cookie.Key);
    }
    // kill all Session storage
    httpContext.Session.Clear();
    // create an html page with javascript that clears localStorage and sessionStorage
    StringBuilder sb = new StringBuilder();
    sb.AppendLine("<!DOCTYPE html><html><body>");
    sb.AppendLine("<script>");
    sb.AppendLine("localStorage.clear();");
    sb.AppendLine("sessionStorage.clear();");
    sb.AppendLine("window.location.href = '/';");
    sb.AppendLine("</script>");
    sb.AppendLine("</body></html>");
    return Results.Content(sb.ToString(), "text/html");
}).AllowAnonymous()
.RequireAuthorization(new AuthorizeAttribute
{ AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme });


app.MapGet("/", () => { return Results.Redirect("/index.html"); });

app.MapPost("/fileuploadxfer", async (IFormFile file,
    IAntiforgery antiforgery,
    GeFeSLEDb db,
    UserManager<GeFeSLEUser> userManager,
    HttpContext httpContext) =>
{
    DBg.d(LogLevel.Trace, "fileupload");
    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);

    try
    {
        await antiforgery.ValidateRequestAsync(httpContext);
    }
    catch (Exception e)
    {
        return Results.BadRequest(e.Message);
    }

    if (user is null) return Results.BadRequest("User is null");
    if (file is null) return Results.BadRequest("No file uploaded");
    if (file.Length > 0)
    {
        // the filepath will be wwwroot/uploads/user/filename
        string filePath = Path.Combine($"{GlobalConfig.wwwroot}/uploads/{user.UserName}", file.FileName);
        DBg.d(LogLevel.Trace, $"fileupload - file will be saved at (filepath): {filePath}");
        //creates the folder if it doesn't exist
        Directory.CreateDirectory(Path.GetDirectoryName(filePath));

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }
        // we want to return the URL of the file that was uploaded
        string url = $"{GlobalConfig.Hostname}:{GlobalConfig.Hostport}/uploads/{user.UserName}/{file.FileName}";




        return Results.Ok(url);
    }
    else
    {
        return Results.BadRequest("File is empty");
    }
}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner,contributor"
});



// lets always generate index.html once before we start
// for a new setup, it won't exist. 

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var db = services.GetRequiredService<GeFeSLEDb>();

    // Call your method here
    _ = GlobalStatic.GenerateHTMLListIndex(db);
    ProtectedFiles.ReLoadFiles(db);
}

app.Run($"http://{GlobalConfig.Bind}:{GlobalConfig.Port}");









