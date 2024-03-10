using Microsoft.EntityFrameworkCore;
// using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
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
// using Microsoft.SqlServer.Server;
using GeFeSLE;
using SQLitePCL;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
// using Google.Apis.Auth.OAuth2.Flows;
// using Google.Apis.Auth.OAuth2;
// using Google.Apis.Util.Store;
// using Google.Apis.Auth;
// using Microsoft.AspNetCore.Http.Extensions;



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

// uncomment this line if we ever extend the IdentityUser class?
//builder.Services.AddDefaultIdentity<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = true)
//    .AddEntityFrameworkStores<GeFeSLEDb>();
builder.Services.AddIdentity<GeFeSLEUser, IdentityRole>(options =>
{
    options.Tokens.ProviderMap.Add("Default", new TokenProviderDescriptor(typeof(DataProtectorTokenProvider<GeFeSLEUser>)));
})
.AddEntityFrameworkStores<GeFeSLEDb>()
.AddDefaultTokenProviders();

builder.Services.AddDistributedMemoryCache(); // Stores session state in memory.
// TODO: save session state in database
builder.Services.AddSingleton<UserSessionService>();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30); // The session timeout.
    // TODO: load session timeout from config file
    options.Cookie.HttpOnly = true; // The session cookie is accessible only from the server side, not via JavaScript.
    options.Cookie.IsEssential = true; // The session cookie is essential, meaning it doesn't need user consent.
    options.Cookie.Name = GlobalStatic.sessionCookieName;
});
builder.Services.AddAuthentication(options =>
{
    // options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    // options.DefaultSignInScheme = JwtBearerDefaults.AuthenticationScheme;
    // options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;

    // options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    // options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    // options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;

    // options.DefaultAuthenticateScheme = IdentityConstants.ExternalScheme;
    // options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    // options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;


})
.AddCookie(options =>
{
    options.Cookie.Name = GlobalStatic.authCookieName;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.LoginPath = "/_login.html"; // Change this to your desired login path
    // this redirects any failure from the .RequireAuthorization() on endpoints.
    options.Events.OnRedirectToAccessDenied = async context =>
    {
        DBg.d(LogLevel.Trace, "Cookie - OnRedirectToAccessDenied");
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
        DBg.d(LogLevel.Trace, "Cookie - OnRedirectToLogin");
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
            DBg.d(LogLevel.Trace, $"Cookie - OnRedirectToLogin [API] returning: {context.Response.StatusCode}");
            return;
        }
        else
        {
            var sb = new StringBuilder();
            string requestedUrl = context.Request.Path + context.Request.QueryString;
            string msg = $"401 - You need to <a href=\"_login.html\">LOGIN</a> to access {requestedUrl}";
            await GlobalStatic.GenerateUnAuthPage(sb, msg);
            DBg.d(LogLevel.Trace, $"Cookie - OnRedirectToLogin [web] {msg}");
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
                DBg.d(LogLevel.Trace, "JWT - OnMessageReceived");
                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                DBg.d(LogLevel.Trace, "JWT - OnChallenge");
                return Task.CompletedTask;
            },

        };
    })

.AddGoogle(options =>
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
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SuperUser", policy => policy.RequireRole("SuperUser"));
    options.AddPolicy("listowner", policy => policy.RequireRole("listowner"));
    options.AddPolicy("contributor", policy => policy.RequireRole("contributor"));

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
    }
    catch (Exception ex)
    {
        DBg.d(LogLevel.Critical, $"Error occurred while seeding default roles in Database: {ex.Message}");
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
//app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession(); // Add this line to enable session.
app.UseAuthentication(); // must be before authorization
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Use(async (context, next) =>
    {
        DBg.d(LogLevel.Trace, "_Middleware_.Use");

        // like in our authentication redirects above, we want to 
        // detect if this is a CORS preflight request and if so, add the headers
        // so its not rejected from the plugins
        var origin = context.Request.Headers["Origin"].FirstOrDefault();
        var remoteIpAddress = context.Connection.RemoteIpAddress;
        DBg.d(LogLevel.Trace, $"_Request origin: {origin} - from {remoteIpAddress}");
        //GlobalStatic.dumpRequest(context);
        if (GlobalStatic.IsCorsRequest(context.Request))
        {
            GlobalStatic.AddCorsHeaders(context.Request, context.Response);
            // Handle preflight request
            if (context.Request.Method == "OPTIONS")
            {
                DBg.d(LogLevel.Trace, "_CORs Preflight");
                context.Response.StatusCode = 200;
                await context.Response.WriteAsync(string.Empty);
            }
        }
        // if we get here its not a Cors request or it IS
        // but its not a pre-flight
        // now we check to see if the file requested is in our list of protected

        var path = context.Request.Path.Value;
        if (path != null && ProtectedFiles.ContainsFile(path))
        {
            // is the user logged in? 
            var user = context.User.Identity;
            if (user != null && user.IsAuthenticated)
            {
                // no - make a nice redirect page like the normal UNAUTH page above. 
                DBg.d(LogLevel.Trace, $"Protected file {path} - requires authenticated user.");
                var sb = new StringBuilder();
                string msg = $"401 -You are not authorized to access {path}";
                await GlobalStatic.GenerateUnAuthPage(sb, msg);
                var result = Results.Content(sb.ToString(), "text/html");
                await result.ExecuteAsync(context);

            }
            else
            {
                // logged in. are they allowed to look at dis?
                // get db context
                var db = context.RequestServices.GetRequiredService<GeFeSLEDb>();
                string? ynot = "";
                bool isAllowed = ProtectedFiles.IsFileVisibleToUser(db, path, user!.Name, out ynot);
                if (!isAllowed)
                {
                    // no - make a nice redirect page like the normal UNAUTH page using the ynot message  
                    DBg.d(LogLevel.Trace, $"Protected file {path} - UNAUTH: {ynot}");
                    var sb = new StringBuilder();
                    string msg = $"401 -You are not authorized to access {path}<br>{ynot}";
                    await GlobalStatic.GenerateUnAuthPage(sb, msg);
                    var result = Results.Content(sb.ToString(), "text/html");
                    await result.ExecuteAsync(context);
                }
            }
        }

        // otherwise, do the normal thing
        await next.Invoke();

    });

app.UseCors(builder =>
        {
            builder.AllowAnyHeader()
                   .AllowAnyMethod()
                   .AllowCredentials();

        });

app.MapPost("/login", async (GeFeSLEDb db,
                    UserSessionService sessSvc,
                    HttpContext httpContext,
                    UserManager<GeFeSLEUser> userManager,
                    RoleManager<IdentityRole> roleManager) =>
{
    // Read the form data
    var form = await httpContext.Request.ReadFormAsync();
    DBg.d(LogLevel.Trace, "LOGIN: form data");
    Dictionary<string, string> formDictionary = form.ToDictionary(x => x.Key, x => x.Value.ToString());
    string jsonString = JsonConvert.SerializeObject(formDictionary);


    DBg.d(LogLevel.Trace, jsonString);
    string username = form["username"].ToString();
    string password = form["password"].ToString();
    string redirectUrl = form["redirectUrl"].ToString(); // this is null from the plugin
    DBg.d(LogLevel.Trace, $"LOGIN: {username} {password} {redirectUrl}");
    if (redirectUrl.IsNullOrEmpty())
    {
        redirectUrl = "/index.html";
    }
    // check the request headers to see if this is coming from the javascript API
    // should probably make this a method in GlobalStatic
    bool isJSApi = false;
    if (httpContext.Request.Headers.ContainsKey("GeFeSLE-XMLHttpRequest") &&
            (httpContext.Request.Headers["GeFeSLE-XMLHttpRequest"].ToString() == "true"))
    {
        DBg.d(LogLevel.Trace, "LOGIN: is JS API");
        isJSApi = true;
    }

    StringBuilder sb = new StringBuilder();
    // if username or password are null, return unauthorized
    if (username.IsNullOrEmpty() || password.IsNullOrEmpty())
    {
        string msg = "LOGIN: Username or password is null.";
        DBg.d(LogLevel.Trace, msg);
        if (isJSApi)
        {
            DBg.d(LogLevel.Trace, $"LOGIN: RETURNING 400 = {msg}");
            return Results.BadRequest(msg);

        }
        else
        {
            DBg.d(LogLevel.Trace, "LOGIN: REDIRECTING UNAUTH");
            await GlobalStatic.GenerateUnAuthPage(sb, msg);
            return Results.Content(sb.ToString(), "text/html");
        }
    }


    else
    {
        // check if the username and password are the backdoor admin

        if (GlobalConfig.backdoorAdmin != null &&
            username == GlobalConfig.backdoorAdmin.Username &&
            password == GlobalConfig.backdoorAdmin.Password)
        {
            var realizedRole = "SuperUser";
            if (isJSApi)
            {
                var token = sessSvc.createToken(username!, realizedRole);
                DBg.d(LogLevel.Trace, $"LOGIN: User {username} logged in as {realizedRole} VIA API RETURNING 200 + TOKEN");
                DBg.d(LogLevel.Trace, GlobalStatic.DumpToken(token).ToString());
                return Results.Ok(new
                {
                    token = token,
                    username = username,
                    role = realizedRole
                }


                );
            }
            else
            {
                DBg.d(LogLevel.Trace, "LOGIN: SU OK - REDIRECTING");
                sessSvc.createSession(httpContext, username!, "SuperUser");
                sessSvc.UpdateSessionAccessTime(httpContext, db);
                return Results.Redirect(redirectUrl!);
            }
        }
        else
        {
            // OR it IS the backdoor admin but wrong pwd. 
            if (username == GlobalConfig.backdoorAdmin?.Username)
            {
                string msg = "LOGIN: Backdoor admin password incorrect.";
                if (isJSApi)
                {
                    DBg.d(LogLevel.Trace, $"LOGIN: RETURNING 400 + {msg}");
                    return Results.BadRequest(msg);
                }
                else
                {
                    DBg.d(LogLevel.Trace, "LOGIN: REDIRECTING UNAUTH");
                    await GlobalStatic.GenerateUnAuthPage(sb, msg);
                    return Results.Content(sb.ToString(), "text/html");
                }
            }
            // NOT the backdoor admin, so check the userManager

            var user = await userManager.FindByNameAsync(username!);
            if (user is null)
            {
                string msg = $"LOGIN: Username {user} NOT FOUND.";
                if (isJSApi)
                {
                    DBg.d(LogLevel.Trace, $"LOGIN: RETURNING 400 + {msg}");
                    return Results.BadRequest(msg);
                }
                else
                {
                    DBg.d(LogLevel.Trace, "LOGIN: REDIRECTING UNAUTH");
                    await GlobalStatic.GenerateUnAuthPage(sb, msg);
                    return Results.Content(sb.ToString(), "text/html");
                }
            }
            else
            {
                var result = await userManager.CheckPasswordAsync(user, password!);
                if (result)
                {
                    // get the user's role
                    var roles = await userManager.GetRolesAsync(user);
                    var realizedRole = GlobalStatic.FindHighestRole(roles);


                    if (isJSApi)
                    {
                        var token = sessSvc.createToken(username!, realizedRole);
                        DBg.d(LogLevel.Trace, $"LOGIN: User {username} logged in as {realizedRole} VIA API RETURNING 200 + TOKEN");
                        return Results.Ok(new
                        {
                            token = token,
                            username = username,
                            role = realizedRole
                        }


                        );
                    }
                    else
                    {
                        sessSvc.createSession(httpContext, username!, realizedRole);
                        sessSvc.UpdateSessionAccessTime(httpContext, db);
                        DBg.d(LogLevel.Trace, "LOGIN: OK - RETURNING REDIRECT");
                        return Results.Redirect(redirectUrl!);
                    }
                }
                else
                {
                    string msg = $"LOGIN: Username {user} PASSWORD NOT CORRECT.";
                    if (isJSApi)
                    {
                        DBg.d(LogLevel.Trace, "LOGIN: RETURNING 400");
                        return Results.BadRequest(msg);
                    }
                    else
                    {
                        DBg.d(LogLevel.Trace, "LOGIN: REDIRECTING UNAUTH");
                        await GlobalStatic.GenerateUnAuthPage(sb, msg);
                        return Results.Content(sb.ToString(), "text/html");
                    }

                }
            }


        }
    }

});

// add an endpoint that adds a user to the database
app.MapPost("/adduser", async (GeFeSLEUser user, GeFeSLEDb db,
            UserSessionService sessSvc,
            HttpContext httpContext,
            UserManager<GeFeSLEUser> userManager,
            RoleManager<IdentityRole> roleManager) =>
{
    // Read the form data
    DBg.d(LogLevel.Trace, $"adduser: {user.UserName} {user.Email}");
    // if username AND email are null, return bad request
    if (user.UserName.IsNullOrEmpty() && user.Email.IsNullOrEmpty())
    {
        DBg.d(LogLevel.Trace, "adduser: username AND email is null ==> 400");
        return Results.BadRequest();
    }
    else
    {
        // if the username is empty, use the email. This will cover for google and Microsoft accounts. 
        if (user.UserName.IsNullOrEmpty())
        {
            user.UserName = user.Email;
        }
        // we should probably catch if the pwd is blank but for now, meh. 
        try
        {
            var result = await userManager.CreateAsync(user);
            if (result.Succeeded)
            {
                DBg.d(LogLevel.Trace, "adduser: user created");
                return Results.Created($"/showusers/{user.Id}", user);
            }
            else
            {
                foreach (var error in result.Errors)
                {
                    DBg.d(LogLevel.Trace, $"adduser - Error: {error.Code}, Description: {error.Description}");
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
app.MapPut("/modifyuser", async (GeFeSLEUser user, GeFeSLEDb db,
            UserSessionService sessSvc,
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
            UserSessionService sessSvc,
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
        UserSessionService sessSvc,
        HttpContext httpContext,
        UserManager<GeFeSLEUser> userManager,
        RoleManager<IdentityRole> roleManager) =>
{
    DBg.d(LogLevel.Trace, "showusers");
    sessSvc.UpdateSessionAccessTime(httpContext, db);

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
        UserSessionService sessSvc,
        HttpContext httpContext,
        UserManager<GeFeSLEUser> userManager,
        RoleManager<IdentityRole> roleManager) =>
{
    DBg.d(LogLevel.Trace, "showusers");
    sessSvc.UpdateSessionAccessTime(httpContext, db);
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


app.MapPost("/setpassword", async ([FromBody] JsonElement data,
        GeFeSLEDb db,
        UserSessionService sessSvc,
        HttpContext httpContext,
        UserManager<GeFeSLEUser> userManager,
        RoleManager<IdentityRole> roleManager) =>
{
    DBg.d(LogLevel.Trace, "setpassword");
    sessSvc.UpdateSessionAccessTime(httpContext, db);

    try
    {
        string? username = data.GetProperty("userName").GetString();
        string? newpassword = data.GetProperty("newPassword").GetString();
        // if either of these is null, return bad request
        if (username.IsNullOrEmpty() || newpassword.IsNullOrEmpty())
        {
            return Results.BadRequest();
        }
        else
        {

            // Get the user from UserManager
            var user = await userManager.FindByNameAsync(username!);
            if (user is null)
            {
                return Results.NotFound();
            }
            else
            {
                var token = await userManager.GeneratePasswordResetTokenAsync(user);
                var result = await userManager.ResetPasswordAsync(user, token, newpassword!);
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
        }
    }
    catch (Exception e)
    {
        DBg.d(LogLevel.Error, e.Message);
        return Results.Problem(e.Message, statusCode: 500);
    }

    // change this so it can only be invoked by SuperUser/leave open for now until
    // rework this to see what invoker's role is - all users can change THEIR password
    // but only SuperUser or admin can change other's

}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner,contributor"
});


app.MapGet("/getrole/{username}", async (string username,
        GeFeSLEDb db,
        UserSessionService sessSvc,
        HttpContext httpContext,
        UserManager<GeFeSLEUser> userManager,
        RoleManager<IdentityRole> roleManager) =>
{
    DBg.d(LogLevel.Trace, "getrole");
    sessSvc.UpdateSessionAccessTime(httpContext, db);

    // dont need checks for username==null, will 404 on that anyway
    var user = await userManager.FindByNameAsync(username);
    if (user is null)
    {
        DBg.d(LogLevel.Trace, $"getrole: user {username} not found");
        return Results.NotFound();
    }
    else
    {
        var role = await userManager.GetRolesAsync(user);
        if (role.Count == 0)
        {
            DBg.d(LogLevel.Trace, $"getrole: user {username} has no role");
            return Results.Ok();
        }
        else
        {
            DBg.d(LogLevel.Trace, $"getrole: user {username} has roles {role.ToString()}");
            return Results.Ok(role);
        }
    }

}
).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});


app.MapGet("/getmyrole", async (
        GeFeSLEDb db,
        UserSessionService sessSvc,
        HttpContext httpContext,
        UserManager<GeFeSLEUser> userManager,
        RoleManager<IdentityRole> roleManager) =>
{
    DBg.d(LogLevel.Trace, "getMyrole");
    sessSvc.UpdateSessionAccessTime(httpContext, db);

    // dont need checks for username==null, will 404 on that anyway
    // we also have a user from the context because of the authentication
    var requestuser = httpContext.User;
    string? username = requestuser?.Identity?.Name;
    // if they're the backdoor admin, return SuperUser
    // they won't be in the db. 
    if (username == null)
    {
        DBg.d(LogLevel.Error, "getMyrole: username is null - NOT SURE HOW WE GOT HERE");
        return Results.NotFound();

    }
    else if (username == GlobalConfig.backdoorAdmin?.Username)
    {
        return Results.Ok("SuperUser");
    }

    var user = await userManager.FindByNameAsync(username);
    if (user is null)
    {
        DBg.d(LogLevel.Trace, $"getrole: user {username} not found");
        return Results.NotFound();
    }
    else
    {
        var role = await userManager.GetRolesAsync(user);
        if (role.Count == 0)
        {
            DBg.d(LogLevel.Trace, $"getrole: user {username} has no role");
            return Results.Ok();
        }
        else
        {
            // get the highest role
            var realizedRole = GlobalStatic.FindHighestRole(role);
            DBg.d(LogLevel.Trace, $"getrole: user {username} has roles {role.ToString()}");
            return Results.Ok(realizedRole);
        }
    }

}
).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner,contributor"
});



app.MapGet("/setrole/{username}/{role}", async (string username,
        string role,
        GeFeSLEDb db,
        UserSessionService sessSvc,
        HttpContext httpContext,
        UserManager<GeFeSLEUser> userManager,
        RoleManager<IdentityRole> roleManager) =>
{
    DBg.d(LogLevel.Trace, "setrole");
    sessSvc.UpdateSessionAccessTime(httpContext, db);

    // dont need checks for username==null, will 404 on that anyway
    var user = await userManager.FindByNameAsync(username);
    if (user is null)
    {
        DBg.d(LogLevel.Trace, $"setrole: user {username} not found");
        return Results.NotFound();
    }
    else
    {
        var result = await userManager.AddToRoleAsync(user, role);
        if (result.Succeeded)
        {
            DBg.d(LogLevel.Trace, $"setrole: user {username} ASSIGNED to role {role}");
            return Results.Ok();
        }
        else
        {
            DBg.d(LogLevel.Trace, $"setrole: user {username} NOT ASSIGNED to role {role}");
            return Results.BadRequest(result.Errors);
        }
    }
}
).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});



app.MapGet("/showlists", async (GeFeSLEDb db, UserSessionService sessSvc, HttpContext httpContext) =>
{
    DBg.d(LogLevel.Trace, "showlists");


    sessSvc.UpdateSessionAccessTime(httpContext, db);

    return await db.Lists.ToListAsync();

});

app.MapGet("/showlists/{id}", async (int id, GeFeSLEDb db, UserSessionService sessSvc, HttpContext httpContext) =>
   {
       DBg.d(LogLevel.Trace, $"showlists/{id}");
       sessSvc.UpdateSessionAccessTime(httpContext, db);
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

app.MapPost("/addlist", async (GeList newlist, GeFeSLEDb db, UserSessionService sessSvc, HttpContext httpContext) =>
{
    DBg.d(LogLevel.Trace, $"addlist: {newlist}");

    // if the newlist.Name is null, return bad request
    if (newlist.Name.IsNullOrEmpty())
    {
        return Results.BadRequest("Cannot have a list with no name. A Horse maybe... but not a list.");
    }

    // if the newlist.Name is the same as an existing list, return bad request
    var list = await db.Lists.Where(l => l.Name == newlist.Name).FirstOrDefaultAsync();
    if (list is not null)
    {
        return Results.BadRequest("List with that name already exists");
    }

    db.Lists.Add(newlist);
    ProtectedFiles.AddList(newlist);
    await db.SaveChangesAsync();
    await newlist.GenerateHTMLListPage(db);
    await newlist.GenerateRSSFeed(db);
    await newlist.GenerateJSON(db);
    _ = GlobalStatic.GenerateHTMLListIndex(db);

    sessSvc.UpdateSessionAccessTime(httpContext, db);
    string msg = $"/showlists/{newlist.Id}";
    return Results.Created(msg, newlist);
}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});


app.MapPut("/modifylist", async (GeList inputList, GeFeSLEDb db, UserSessionService sessSvc, HttpContext httpContext) =>
{
    DBg.d(LogLevel.Trace, $"modifylist: {inputList}");
    sessSvc.UpdateSessionAccessTime(httpContext, db);
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
    // check to see if the visibility has changed ********
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


app.MapGet("/showitems/{listId}", async (int listId, GeFeSLEDb db, UserSessionService sessSvc, HttpContext httpContext) =>
{
    DBg.d(LogLevel.Trace, $"showitems/{listId}");
    sessSvc.UpdateSessionAccessTime(httpContext, db);
    var items = await db.Items.Where(item => item.ListId == listId).ToListAsync();
    return Results.Ok(items);
});

app.MapGet("/showitems/{listid}/{id}", async (int listId, int id, GeFeSLEDb db, UserSessionService sessSvc, HttpContext httpContext) =>
{
    DBg.d(LogLevel.Trace, $"showitems/{listId}/{id}");
    sessSvc.UpdateSessionAccessTime(httpContext, db);
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

app.MapPost("/additem/{listid}", async (int listid, GeListItem newitem, GeFeSLEDb db, UserSessionService sessSvc, HttpContext httpContext) =>
{

    DBg.d(LogLevel.Trace, $"additem/{listid} <- {System.Text.Json.JsonSerializer.Serialize(newitem)}");
    sessSvc.UpdateSessionAccessTime(httpContext, db);
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


app.MapPut("/modifyitem", async (GeListItem inputItem, GeFeSLEDb db, UserSessionService sessSvc, HttpContext httpContext) =>
{
    DBg.d(LogLevel.Trace, $"modifyitem: <- {System.Text.Json.JsonSerializer.Serialize(inputItem)}");
    sessSvc.UpdateSessionAccessTime(httpContext, db);
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
app.MapDelete("/deleteitem/{listid}/{id}", async (int listid, int id, GeFeSLEDb db, UserSessionService sessSvc, HttpContext httpContext) =>
{
    DBg.d(LogLevel.Trace, $"deleteitem/{listid}/{id}");
    sessSvc.UpdateSessionAccessTime(httpContext, db);
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


// add an endpoint that DELETEs a list
app.MapDelete("/deletelist/{id}", async (int id,
        GeFeSLEDb db,
        UserSessionService sessSvc,
        HttpContext httpContext) =>
{
    DBg.d(LogLevel.Trace, $"deletelist/{id}");
    sessSvc.UpdateSessionAccessTime(httpContext, db);
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



app.MapGet("/heartbeat", () => "OK  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "(server time)");

// add and endpoint that regenerates the html page for all lists
app.MapGet("/regenerate", async (GeFeSLEDb db, UserSessionService sessSvc, HttpContext httpContext) =>
{
    DBg.d(LogLevel.Trace, "regenerate");
    sessSvc.UpdateSessionAccessTime(httpContext, db);
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
app.MapGet("/regenerate/{listid}", async (int listid, GeFeSLEDb db, UserSessionService sessSvc, HttpContext httpContext) =>
{
    DBg.d(LogLevel.Trace, $"regenerate/{listid}");
    sessSvc.UpdateSessionAccessTime(httpContext, db);
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

app.MapGet("/googlelogin", async (HttpContext context) =>
{

    var properties = new AuthenticationProperties { RedirectUri = $"{GlobalConfig.Hostname}:{GlobalConfig.Hostport}/googlecallback" };
    DBg.d(LogLevel.Trace, $"googlelogin - sending {properties.RedirectUri} challenge");
    await context.ChallengeAsync(GoogleDefaults.AuthenticationScheme, properties);
});

app.MapGet("/googlecallback", async (HttpContext context,
        GeFeSLEDb db,
        UserSessionService sessSvc,
        UserManager<GeFeSLEUser> userManager,
        RoleManager<IdentityRole> roleManager
        ) =>
{
    DBg.d(LogLevel.Trace, "googlecallback");
    StringBuilder sb = new StringBuilder();
    var msg = "";
    var auth = await context.AuthenticateAsync(IdentityConstants.ExternalScheme);
    // look for auth success
    if (!auth.Succeeded)
    {
        msg = "External authentication error";
        await GlobalStatic.GenerateUnAuthPage(sb, msg);
        return Results.Content(sb.ToString(), "text/html");
    }

    // so at this point the user has already been authenticated by google
    // we need to sign the user into OUR system; create a session for them. 
    // yeah yeah there's signInManager.SignInAsync but we're not using that
    // couldn't get it to work properly 


    // get claimsPrincipal out of auth
    var claimsPrincipal = auth.Principal;
    string? email = claimsPrincipal.FindFirstValue(ClaimTypes.Email);
    if (email == null)
    {
        msg = "Google account does not have an email address";
        await GlobalStatic.GenerateUnAuthPage(sb, msg);
        return Results.Content(sb.ToString(), "text/html");
    }
    // find the user by email in our database
    GeFeSLEUser? user = await userManager.FindByEmailAsync(email);
    if (user is null)
    {
        msg = $"User {email} has not been added to the guest list yet!";
        await GlobalStatic.GenerateUnAuthPage(sb, msg);
        return Results.Content(sb.ToString(), "text/html");
    }
    else
    {
        // user exists. get their role. Add a claimsPrincipal for the role
        // and create a session for them.
        var roles = await userManager.GetRolesAsync(user);
        var realizedRole = GlobalStatic.FindHighestRole(roles);

        sessSvc.createSession(context, user.UserName!, realizedRole);
        msg = $"Welcome {user.UserName}! You are logged in as {realizedRole}";
        await GlobalStatic.GenerateLoginResult(sb, msg);
        return Results.Content(sb.ToString(), "text/html");
    }

});

app.MapPost("/mastoconnect", async (HttpContext context, UserSessionService sessSvc) =>
{
    DBg.d(LogLevel.Trace, "mastoconnect");
    string? instance = context.Request.Form["instance"];

    if (instance is null)
    {
        return Results.BadRequest("Specified Mastodon instance is null");
    }
    else
    {
        var realizedInstance = instance;
        // if the instance doesn't start with http:// or https://, add it
        if (!realizedInstance.StartsWith("http://") && !realizedInstance.StartsWith("https://"))
        {
            realizedInstance = "https://" + realizedInstance;
        }
        // ping the instance to see if it's up
        var client = new HttpClient();
        var response = new HttpResponseMessage();
        DBg.d(LogLevel.Trace, $"Pinging Mastodon instance {instance}");
        try
        {
            response = client.GetAsync($"{realizedInstance}/api/v1/instance").Result;
        }
        catch (Exception e)
        {
            return Results.BadRequest($"Mastodon instance {instance} is down/unreachable: {e.Message}");
        }
        if (response.IsSuccessStatusCode)
        {
            DBg.d(LogLevel.Debug, $"Mastodon instance {instance} is up");

            // construct our redirect Uri using our external hostname and port
            string redirectUri = Uri.EscapeDataString($"{GlobalConfig.Hostname}:{GlobalConfig.Hostport}/mastocallback");

            string scopes = GlobalStatic.mastoScopes;

            string appRegisterUrl = $"{realizedInstance}/api/v1/apps";
            DBg.d(LogLevel.Trace, $"appRegisterUrl: {appRegisterUrl}");

            string postData = $"client_name={GlobalStatic.mastoClient_Name}&redirect_uris={redirectUri}&scopes={scopes}&website={GlobalStatic.webSite}";
            DBg.d(LogLevel.Trace, $"postData: {postData}");

            var content = new StringContent(postData, Encoding.UTF8, "application/x-www-form-urlencoded");
            response = await client.PostAsync(appRegisterUrl, content);

            // response can be OK 200 or 422 Unprocessable Entity
            // if its 422, WE are going to return a bad request and itemize what could be wrong
            if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
            {
                var error = await response.Content.ReadAsStringAsync();
                return Results.BadRequest($"Mastodon instance {instance} returned 422: {error} - REDIRECT URI==HOSTNAME:HOSTPORT/mastocallback - check your config file. Sent: {postData}");
            }
            // mastodon API doesn't say anything about any other status code result, 422 and 200.
            else
            {
                // at this point our response contains client_id and client_secret
                // TODO: we're gonna want to save the client_id and client_secret
                // on a PER instance basis, we don't want to register a new app for EVERY user 
                // from that instance. 

                var token = await response.Content.ReadAsStringAsync();
                Token? tokenObject = JsonConvert.DeserializeObject<Token>(token);
                // in the deserialized token object we want properties client_id and client_secret
                if (tokenObject is null) return Results.BadRequest("Mastodon instance returned null token object");
                var appId = tokenObject.id;
                var clientId = tokenObject.client_id;
                var clientSecret = tokenObject.client_secret;
                DBg.d(LogLevel.Trace, $"clientId: {clientId} clientSecret: {clientSecret}");
                // now store these in user's cookie session
                context.Session.SetString("masto.app_id", appId!);
                context.Session.SetString("masto.client_id", clientId!);
                context.Session.SetString("masto.client_secret", clientSecret!);
                context.Session.SetString("masto.instance", instance);
                context.Session.SetString("masto.realizedInstance", realizedInstance);
            }
            // great and all but now we need to actually begin the Oauth2 process
            return Results.Redirect("/mastologin");
        }
        else
        {
            return Results.BadRequest($"Mastodon instance {instance} is down/unreachable: {response.StatusCode}");
        }
    }


});

app.MapGet("/mastologin", (HttpContext context) =>
{
    DBg.d(LogLevel.Trace, "mastologin");

    // unlike the google Oauth2 login, we have to manually construct the POST request to the Mastodon server to get 
    // the authorization URL
    // because we don't know what mastodon instance the user's going to specify ahead of time.
    // but we've already got these stored in the user's session cookie

    string? appId = context.Session.GetString("masto.app_id");
    string? clientId = context.Session.GetString("masto.client_id");
    string? clientSecret = context.Session.GetString("masto.client_secret");
    string? instance = context.Session.GetString("masto.instance");
    string? realizedInstance = context.Session.GetString("masto.realizedInstance");

    if (appId is null || clientId is null || clientSecret is null || instance is null || realizedInstance is null)
    {
        return Results.BadRequest("BAD/MISSING Mastodon parameters in session cookie - dunno, did you forget to _login.html -> /mastoconnect?");
    }
    string redirectUri = Uri.EscapeDataString($"{GlobalConfig.Hostname}:{GlobalConfig.Hostport}/mastocallback");
    DBg.d(LogLevel.Trace, $"redirectUri: {redirectUri}");
    string authorizationUrl = $"{realizedInstance}/oauth/authorize?client_id={clientId}&response_type=code&redirect_uri={redirectUri}&scope={Uri.EscapeDataString(GlobalStatic.mastoScopes)}";
    DBg.d(LogLevel.Trace, $"authorizationUrl: {authorizationUrl}");

    return Results.Redirect(authorizationUrl);
});


// new endpoint that handles the Mastodon Oauth2 callback
app.MapGet("/mastocallback", async (string code,
    HttpContext httpContext,
    UserSessionService sessSvc,
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
        sessSvc.AddAccessToken(httpContext, "mastodon", accessToken);
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
                sessSvc.createSession(httpContext, username!, "anonymous");
                var msg = $"Hi {username} from the fediverse; You've been logged in with role: anonymous. All this means is you can't modify anything, but at least now you show up in our server logs.";
                await GlobalStatic.GenerateLoginResult(sb, msg);
                return Results.Content(sb.ToString(), "text/html");
            }
            else
            {
                // they're in there, which means we've added them, probably to assign them a role
                var roles = await userManager.GetRolesAsync(localuser);
                var realizedRole = GlobalStatic.FindHighestRole(roles);
                sessSvc.createSession(httpContext, localuser.UserName!, realizedRole);
                var msg = $"Hi {username} from the fediverse; You've been logged in with role: {realizedRole}.";
                await GlobalStatic.GenerateLoginResult(sb, msg);
                return Results.Content(sb.ToString(), "text/html");
            }


        }

    }
});

// endpoint mastobookmarks to call GET /api/v1/bookmarks in mastodon API
app.MapGet("/mastobookmarks/{listid}", async (int listid,
            int num2Get,
            bool? unbookmark, // if the checkbox isn't, there's no value, so it's null
            GeFeSLEDb db,
            UserSessionService sessSvc,
            HttpContext httpContext) =>
{
    DBg.d(LogLevel.Trace, "mastobookmarks");
    sessSvc.UpdateSessionAccessTime(httpContext, db);

    // if there's no/null value for unbookmark, set it to false
    if (unbookmark is null) unbookmark = false;

    // check to see if the listid is valid
    var list = await db.Lists.FindAsync(listid);
    if (list is null) return Results.NotFound();

    if ((num2Get < 1) || (num2Get > 999)) return Results.BadRequest("num2Get must be between 1 and 999");

    // get the access token from the session service
    string? token = sessSvc.GetAccessToken(httpContext, "mastodon");
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

                // add the bookmark status class to the list
                var item = new GeListItem();
                item.ParseMastoStatus(status, listid);

                db.Items.Add(item);
                numGot++;
                if (numGot >= num2Get) break;
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
    return Results.Redirect($"/{list.Name}.html");
}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});

app.MapGet("/amloggedin", (UserSessionService sessSvc, HttpContext httpContext) =>
{
    DBg.d(LogLevel.Trace, "amloggedin");

    //GlobalStatic.dumpRequest(httpContext);

    var username = httpContext.User.Identity?.Name;
    DBg.d(LogLevel.Trace, $"username: {username}");
    var isAuthenticated = httpContext.User.Identity?.IsAuthenticated;
    DBg.d(LogLevel.Trace, $"isAuthenticated: {isAuthenticated}");
    // Check JWT token
    var jwtToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();

    if ((isAuthenticated == true && !string.IsNullOrEmpty(username)) || !string.IsNullOrEmpty(jwtToken))
    {
        return Results.Ok(true);
    }
    else
    {
        return Results.Ok(false);
    }
})
.AllowAnonymous()
.RequireAuthorization(new AuthorizeAttribute
{ AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme });

app.MapGet("/getlistuser/{listid}", async (int listid,
        GeFeSLEDb db,
        UserSessionService sessSvc,
        HttpContext httpContext,
        UserManager<GeFeSLEUser> userManager,
        RoleManager<IdentityRole> roleManager) =>
{
    DBg.d(LogLevel.Trace, "getlistuser");
    sessSvc.UpdateSessionAccessTime(httpContext, db);

    // middleware rejects the request if there isn't a listid. see if the list given is a real one
    GeList? list = await db.Lists.FindAsync(listid);
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
        UserSessionService sessSvc,
        HttpContext httpContext,
        UserManager<GeFeSLEUser> userManager,
        RoleManager<IdentityRole> roleManager) =>
{
    DBg.d(LogLevel.Trace, "getlistuser");
    sessSvc.UpdateSessionAccessTime(httpContext, db);
    // who am i? 
    var username = httpContext.User.Identity?.Name;
    if (username is null)
    {
        DBg.d(LogLevel.Trace, "getlistisee: username is null // Only public lists");
        // user can only see lists that are .Visibility== GeListvisibility.Public
        var listNames = await db.Lists.Where(l => l.Visibility == GeListVisibility.Public)
                                     .Select(l => l.Name)
                                     .ToListAsync();
        return Results.Ok(listNames);
    }
    else
    {
        // likewise if user is role SuperUser, they can see all
        GeFeSLEUser? user = await userManager.FindByNameAsync(username);
        if (user is null) return Results.BadRequest($"ERROR: User {username} is logged in but not in database?");
        else
        {
            var roles = await userManager.GetRolesAsync(user);
            var realizedRole = GlobalStatic.FindHighestRole(roles);
            if (realizedRole == "SuperUser")
            {
                DBg.d(LogLevel.Trace, $"getlistisee: SuperUser {username} // All lists");
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
                    string? ynot = "";
                    if (ProtectedFiles.IsListVisibleToUser(db, list, user.UserName, out ynot))
                    {
                        listnames.Add(list.Name!);
                    }

                }
                return Results.Ok(listnames);
            }

        }
    }
}).AllowAnonymous();


app.MapPost("/setlistuser", async ([FromBody] JsonElement data,
        GeFeSLEDb db,
        UserSessionService sessSvc,
        HttpContext httpContext,
        UserManager<GeFeSLEUser> userManager,
        RoleManager<IdentityRole> roleManager) =>
{
    sessSvc.UpdateSessionAccessTime(httpContext, db);

    // the FORM post will have a:
    // listname
    // username
    // role (one of listowner or contributor // can't be anything but)
    try
    {
        string? listname = data.GetProperty("listname").GetString();
        string? username = data.GetProperty("username").GetString();
        string? role = data.GetProperty("role").GetString();

        // if we don't get valid strings named as above, the exception will be caught. 
        // however they can still be null i.e. { "listname":null }
        if (listname.IsNullOrEmpty() || username.IsNullOrEmpty() || role.IsNullOrEmpty())
        {
            return Results.BadRequest("listname, username and role must be specified");
        }
        else if (role != "listowner" && role != "contributor") {
            return Results.BadRequest("role must be listowner or contributor");
        }
        else
        {
            // find the list
            GeList? list = await db.Lists.Where(l => l.Name == listname).FirstOrDefaultAsync();
            if (list == null)
            {
                return Results.BadRequest($"List {listname} does not exist");
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
                        if(user == null) return Results.BadRequest($"User {username} does not exist");

                        // only the list's creator or a SuperUser can add a listowner
                        // a listowner, SuperUser or the list's creator can add a contributor
                        var roles = await userManager.GetRolesAsync(caller);
                        var realizedRole = GlobalStatic.FindHighestRole(roles);

                        if(role == "listowner")
                        {
                            if ((list.Creator == caller) ||
                                (realizedRole == "SuperUser")) {

                                list.ListOwners.Add(user);
                                var msg =  $"{caller.UserName} Added {user.UserName} to {list.Name} as a listowner";
                                DBg.d(LogLevel.Information,msg);
                                return Results.Ok(msg);
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
                                list.ListOwners.Contains(caller)) {
                                    list.Contributors.Add(user);
                                    var msg = $"{caller.UserName} Added {user.UserName} to {list.Name} as a contributor";
                                    DBg.d(LogLevel.Information, msg);
                                    return Results.Ok(msg);
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
    }
    catch (Exception e)
    {
        return Results.BadRequest(e.Message);
    }
}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});



app.MapGet("/", () => { return Results.Redirect("/index.html"); });



// lets always generate index.html once before we start
// for a new setup, it won't exist. 

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var db = services.GetRequiredService<GeFeSLEDb>();

    // Call your method here
    _ = GlobalStatic.GenerateHTMLListIndex(db);
}
app.Run($"http://{GlobalConfig.Bind}:{GlobalConfig.Port}");









