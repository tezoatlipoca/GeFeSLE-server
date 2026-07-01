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
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.FileProviders;

using GeFeSLE.Controllers;
using Microsoft.AspNetCore.HttpOverrides;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using GeFeSLE.DTOs;
using System.Security.Cryptography;
using System.Net.Http.Headers;
using Markdig;
using System.Text.RegularExpressions;
using static EndpointLoggingHelpers;


// check a bunch of stuff; we MUST have a configuration file AND
// a database filename. If we don't have both, we have to bail out
// BUT only after the database context is specified
// for dotnet ef migraitions and updates - don't worry
// for migrations, we have a constructor class that the migration tool falls back on
bool bailAfterDBContext = false;

var activityPubMarkdownPipeline = new MarkdownPipelineBuilder()
    .UseSoftlineBreakAsHardlineBreak()
    .UseAutoLinks()
    .Build();

string? configFile = GlobalConfig.CommandLineParse(args);
string? dbName = null;

var builder = WebApplication.CreateBuilder(args);

// Configure for Windows Service if running as service
builder.Host.UseWindowsService();

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
    options.UseSqlite(
        $"Data Source={dbName}",
        sqliteOptions => sqliteOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));

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
    options.Cookie.Name = GlobalStatic.sessionCookieName;

    options.Cookie.HttpOnly = true; // prevent client from accessing the cookie
    options.Cookie.IsEssential = true; //user must accept this cookie
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.Domain = GlobalConfig.CookieDomain;
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

    options.Cookie.HttpOnly = true; // prevent client from accessing the cookie
    options.Cookie.IsEssential = true; //user must accept this cookie
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.Domain = GlobalConfig.CookieDomain;
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
            string msg = $"401 - You need to <a href=\"/_login.html\">LOGIN</a> to access {requestedUrl}";
            await GlobalStatic.GenerateUnAuthPage(sb, msg);
            DBg.d(LogLevel.Trace, $"{fn} - OnRedirectToLogin [web] {msg}");
            var result = Results.Content(sb.ToString(), "text/html");
            await result.ExecuteAsync(context.HttpContext);
        }

    };
})
.AddJwtBearer(options =>
    {
        string bearerRealm = $"{GlobalConfig.Hostname}";
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
                //var fn = "_Middleware.JWT_";
                context.Token = context.Request.Cookies[GlobalStatic.JWTCookieName]; // get token from cookie not rqst headers
                //DBg.d(LogLevel.Trace, $"{fn} OnMessageReceived"); // commented this out cause too noisy. 
                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                //var fn = "_Middleware.JWT_";
                //DBg.d(LogLevel.Trace, $"{fn} OnChallenge");   // same. Every. single. request. blegh.
                return Task.CompletedTask;
            },

        };
    });

if (!string.IsNullOrEmpty(GlobalConfig.googleClientID) && !string.IsNullOrEmpty(GlobalConfig.googleClientSecret))
{
    builder.Services.AddAuthentication().AddGoogle("Google", options =>
    {
        options.ClientId = GlobalConfig.googleClientID;
        options.ClientSecret = GlobalConfig.googleClientSecret;
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

    });
}

if (!string.IsNullOrEmpty(GlobalConfig.microsoftClientId) && !string.IsNullOrEmpty(GlobalConfig.microsoftClientSecret))
{
    builder.Services.AddAuthentication().AddMicrosoftAccount("Microsoft", options =>
    {
        options.ClientId = GlobalConfig.microsoftClientId;
        options.ClientSecret = GlobalConfig.microsoftClientSecret;
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
}

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SuperUser", policy => policy.RequireRole("SuperUser"));
    options.AddPolicy("listowner", policy => policy.RequireRole("listowner"));
    options.AddPolicy("contributor", policy => policy.RequireRole("contributor"));

});

builder.Services.AddControllers().AddNewtonsoftJson();

builder.Services.AddControllersWithViews();


builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.Configure<KestrelServerOptions>(options =>
 {
     //$"http://{GlobalConfig.Bind}:{GlobalConfig.Port}
     options.ListenAnyIP(GlobalConfig.Port);
 });

// lastly register our own controller services so they play nicely with the DI system
builder.Services.AddScoped<GeListController>();
builder.Services.AddScoped<GeListFileController>();

// Add Swagger/OpenAPI documentation services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() 
    { 
        // TODO: replace with assembly/build/soln file params, especially the version. 
        Title = "GeFeSLE API", 
        Version = "v0.1.2", 
        Description = "Generic, Federated, Subscribable List Engine - Server API",
        Contact = new() { Email = "tezoatlipoca@gmail.com" }
    });
    
    // Include XML comments if you want to add them later
    // var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    // c.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename));
    
    // Add JWT Bearer authentication support in Swagger UI
    c.AddSecurityDefinition("Bearer", new() 
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter 'Bearer' [space] and then your token in the text input below.\n\nExample: \"Bearer 12345abcdef\""
    });
    
    c.AddSecurityRequirement(new()
    {
        {
            new()
            {
                Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// Add Windows Service support
builder.Services.AddWindowsService();
builder.Services.AddHostedService<MaintenanceSchedulerService>();

var app = builder.Build();

RSA? activityPubSigningKey = null;
string? activityPubPublicKeyPem = null;

// Graceful shutdown: trap termination signals, stop the host, and flush database state once.
int shutdownRequested = 0;
int shutdownFlushCompleted = 0;

async Task FlushAndCloseDatabaseAsync(string reason)
{
    if (Interlocked.Exchange(ref shutdownFlushCompleted, 1) == 1)
    {
        return;
    }

    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GeFeSLEDb>();
        await db.SaveChangesAsync();
        await db.Database.CloseConnectionAsync();
        DBg.d(LogLevel.Information, $"Graceful shutdown database flush/close complete ({reason}).");
    }
    catch (Exception ex)
    {
        DBg.d(LogLevel.Error, $"Graceful shutdown database flush/close failed ({reason}): {ex.Message}");
    }
}

async Task RequestStopAsync(string reason)
{
    if (Interlocked.Exchange(ref shutdownRequested, 1) == 1)
    {
        return;
    }

    DBg.d(LogLevel.Information, $"Shutdown requested ({reason}). Stopping host...");
    try
    {
        await app.StopAsync(TimeSpan.FromSeconds(30));
    }
    catch (Exception ex)
    {
        DBg.d(LogLevel.Error, $"Error while stopping host ({reason}): {ex.Message}");
    }
}

app.Lifetime.ApplicationStopping.Register(() =>
{
    FlushAndCloseDatabaseAsync("ApplicationStopping").GetAwaiter().GetResult();
});

Console.CancelKeyPress += (_, e) =>
{
    // Keep the process alive long enough to run the host shutdown path.
    e.Cancel = true;
    _ = RequestStopAsync("SIGINT/CancelKeyPress");
};

AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    RequestStopAsync("ProcessExit").GetAwaiter().GetResult();
    FlushAndCloseDatabaseAsync("ProcessExit").GetAwaiter().GetResult();
};

PosixSignalRegistration? sigTermRegistration = null;
PosixSignalRegistration? sigQuitRegistration = null;
if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
{
    sigTermRegistration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
    {
        context.Cancel = true;
        _ = RequestStopAsync("SIGTERM");
    });

    sigQuitRegistration = PosixSignalRegistration.Create(PosixSignal.SIGQUIT, context =>
    {
        context.Cancel = true;
        _ = RequestStopAsync("SIGQUIT");
    });
}

app.Lifetime.ApplicationStopped.Register(() =>
{
    sigTermRegistration?.Dispose();
    sigQuitRegistration?.Dispose();
    DBg.d(LogLevel.Information, "Application stopped.");
});
// this configures the middleware to respect the X-Forwarded-For and X-Forwarded-Proto headers
// that are set by any reverse proxy server (nginx, apache, etc.)
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
    ForwardLimit = 1
};

forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
foreach (var knownProxy in GlobalConfig.KnownProxies)
{
    if (IPAddress.TryParse(knownProxy, out var proxyAddress))
    {
        forwardedHeadersOptions.KnownProxies.Add(proxyAddress);
    }
    else
    {
        DBg.d(LogLevel.Warning, $"Ignoring invalid ServerSettings:KnownProxies entry: {knownProxy}");
    }
}

app.UseForwardedHeaders(forwardedHeadersOptions);

// SeedRoles makes sure our roles in the IdentifyUser system are created
// here's where we would add any default database stuffing as well
// like a "sample" list or users or something.

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<GeFeSLEDb>();
        string dataSource = db.Database.GetDbConnection().DataSource;
        var pendingMigrations = (await db.Database.GetPendingMigrationsAsync()).ToList();
        var appliedMigrationsBefore = (await db.Database.GetAppliedMigrationsAsync()).ToList();

        DBg.d(LogLevel.Information,
            $"Startup DB target: {dataSource}. Applied migrations: {appliedMigrationsBefore.Count}. Pending migrations: {pendingMigrations.Count}");
        if (pendingMigrations.Count > 0)
        {
            DBg.d(LogLevel.Information, $"Pending migrations: {string.Join(", ", pendingMigrations)}");
        }

        db.Database.Migrate();

        var appliedMigrationsAfter = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        DBg.d(LogLevel.Information,
            $"Startup DB migration complete for {dataSource}. Applied migrations now: {appliedMigrationsAfter.Count}");

        GlobalStatic.SeedRoles(services).Wait();

        // also always make sure backdoorAdmin is a user in db
        if (GlobalConfig.backdoorAdmin != null &&
            GlobalConfig.backdoorAdmin.UserName != null)
        {
            var userManager = services.GetRequiredService<UserManager<GeFeSLEUser>>();
            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
            DBg.d(LogLevel.Trace, "Checking to see if backdoorAdmin in db..");
            string backdoorAdminName = GlobalConfig.backdoorAdmin.UserName.ToUpper();
            GeFeSLEUser? backdoorAdminUser = await userManager.FindByNameAsync(backdoorAdminName);
            if (backdoorAdminUser == null)
            {
                DBg.d(LogLevel.Trace, "backdoorAdmin not found in database, adding");
                DBg.d(LogLevel.Trace, "backdoorAdmin from config: " + GlobalConfig.backdoorAdmin.UserName);
                GlobalConfig.backdoorAdmin.UploadsPath = GeFeSLEUser.GetUploadsPath(GlobalConfig.backdoorAdmin.UserName, GlobalConfig.backdoorAdmin.Id, GlobalConfig.backdoorAdmin.Email);
                var result = await userManager.CreateAsync(GlobalConfig.backdoorAdmin);
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(GlobalConfig.backdoorAdmin, "SuperUser");

                }
                await db.SaveChangesAsync();
                GlobalConfig.backdoorAdmin = await userManager.FindByNameAsync(GlobalConfig.backdoorAdmin.UserName.ToUpper());
            } // backdoorAdmin NOT found in db. added
            else
            {
                DBg.d(LogLevel.Trace, "backdoorAdmin already found in database");
            }

            // always overwrite the backdoorAdmin password with the one from the config file
            // if one is specified
            GlobalConfig.backdoorAdmin = await userManager.FindByNameAsync(GlobalConfig.backdoorAdmin.UserName!.ToUpper());
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
    // Enable Swagger middleware for API documentation
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "GeFeSLE API v1");
        c.RoutePrefix = "swagger"; // Swagger UI available at /swagger
    });
}
else
{
    app.UseExceptionHandler("/Home/Error"); // improve this. actually define that route for one. 
    app.UseHsts();
}

// // DEBUGGING *******
// app.Use(async (context, next) =>
// {
//     // Log JWT token
//     var jwtToken = context.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
//     if (jwtToken != null)
//     {
//         var handler = new JwtSecurityTokenHandler();
//         var jwtSecurityToken = handler.ReadJwtToken(jwtToken);
//         var claims = jwtSecurityToken.Claims.Select(c => new { c.Type, c.Value });
//         DBg.d(LogLevel.Information, $"JWT Claims: {claims.ToString}");
//     }

//     // Log antiforgery token
//     var antiforgeryToken = context.Request.Headers["RequestVerificationToken"].FirstOrDefault();
//     if (antiforgeryToken != null)
//     {
//         DBg.d(LogLevel.Information, $"Antiforgery Token: {antiforgeryToken}");
//     }

//     await next.Invoke();
// });
// DEBUGGING*********



app.UseRouting();
app.UseSession(); // Add this line to enable session.
app.UseAuthentication(); // must be before authorization
app.UseAuthorization();

app.UseAntiforgery();

app.Use(async (context, next) =>
    {
        var fn = "_Middleware.Use_"; //DBg.d(LogLevel.Trace, fn);

        // like in our authentication redirects above, we want to 
        // detect if this is a CORS preflight request and if so, add the headers
        // so its not rejected from the plugins
        var origin = context.Request.Headers["Origin"].FirstOrDefault();
        if (string.IsNullOrEmpty(origin))
        {
            origin = context.Request.Headers["Referer"].FirstOrDefault();
            if (string.IsNullOrEmpty(origin))
            {
                origin = "(endpoint direct)";
            }
        }
        var remoteIpAddress = context.Connection.RemoteIpAddress;
        //DBg.d(LogLevel.Trace, $"{fn} Request origin: {origin} - from {remoteIpAddress}");
        //GlobalStatic.dumpRequest(context);

        // Handle HEAD requests by forwarding as GET but suppressing the body.
        // This keeps uptime checks and crawlers from hitting 405 on GET-only routes.
        if (context.Request.Method == "HEAD")
        {
            if (GlobalStatic.IsCorsRequest(context.Request))
            {
                GlobalStatic.AddCorsHeaders(context.Request, context.Response);
            }

            DBg.d(LogLevel.Information, $"{context.Request.Path} ({context.Request.Method}) <-- {remoteIpAddress} - {context.Request.Headers["User-Agent"].FirstOrDefault()}");
            context.Request.Method = "GET";
            await next.Invoke();
            context.Response.Body = Stream.Null;
            return;
        }

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

        var path = context.Request.Path.Value;
        //DBg.d(LogLevel.Trace, $"{fn} protected file check: {path}");
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
                if (ProtectedFiles.TryGetProtectionScope(path, out var protectionScope)
                    && !string.IsNullOrWhiteSpace(protectionScope)
                    && ProtectedFiles.IsInternalProtected(protectionScope))
                {
                    if (!ProtectedFiles.IsInternalPathVisibleToRole(protectionScope, sessionUser.Role, out var ynot))
                    {
                        DBg.d(LogLevel.Debug, $"{fn} Protected file {path} - UNAUTH: {ynot}");
                        var sb = new StringBuilder();
                        string msg = $"401 -You are not authorized to access {path}<br>{ynot}";
                        await GlobalStatic.GenerateUnAuthPage(sb, msg);
                        var result = Results.Content(sb.ToString(), "text/html");
                        await result.ExecuteAsync(context);
                        return;
                    }

                    DBg.d(LogLevel.Debug, $"{fn} Protected internal file {path} - ALLOWED for {sessionUser.UserName}.");
                    await next.Invoke();
                    return;
                }

                var db = context.RequestServices.GetRequiredService<GeFeSLEDb>();
                {
                    (bool isAllowed, string? ynot) = await ProtectedFiles.IsFileVisibleToUser(path, sessionUser.Id, sessionUser.Role, db);
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
                        DBg.d(LogLevel.Debug, $"{fn} Protected file {path} - ALLOWED for {sessionUser.UserName}.");

                    }
                }
            }
        }
        else
        {
            string msg = $"{path} ({context.Request.Method}) <-- {remoteIpAddress} - {sessionUser.UserName ?? "anonymous"} [{sessionUser.Role ?? "no role"}] using {context.Request.Headers["User-Agent"].FirstOrDefault()}";
            DBg.d(LogLevel.Information, msg);
        }

        // otherwise, do the normal thing
        try
        {
            await next.Invoke();

            // Static-file misses (e.g., unknown *.html) can return 404 before endpoint fallback.
            // Mirror fallback tracing so these requests are visible in logs the same way.
            string? requestPath = context.Request.Path.Value;
            if (!string.IsNullOrWhiteSpace(requestPath)
                && Path.HasExtension(requestPath)
                && context.Response.StatusCode == StatusCodes.Status404NotFound)
            {
                string requestFn = $"{requestPath} ({context.Request.Method})";
                DBg.d(LogLevel.Trace, requestFn);
                DBg.d(LogLevel.Error, $"{requestFn} --> 404");
            }
        }
        catch (Microsoft.AspNetCore.Http.BadHttpRequestException ex) when
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
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(GlobalConfig.wwwroot!),
    RequestPath = ""
});

app.UseWhen(context => GlobalStatic.IsFederationRequest(context.Request), federationApp =>
{
    federationApp.UseCors(builder =>
    {
        builder.AllowAnyHeader()
               .AllowAnyMethod()
               .AllowCredentials()
               .SetIsOriginAllowed(origin => GlobalStatic.IsOriginAllowed(origin, includePublicOrigins: true));
    });
});

app.UseWhen(context => !GlobalStatic.IsFederationRequest(context.Request), appBranch =>
{
    appBranch.UseCors(builder =>
    {
        builder.AllowAnyHeader()
               .AllowAnyMethod()
               .AllowCredentials()
               .SetIsOriginAllowed(origin => GlobalStatic.IsOriginAllowed(origin));
    });
});

// adds a user to the system; 
// we have to have at least a username or email; if username is missing we use the email AS the username
// WHO: SU and LO
// 400 - username AND email null
// 400 - any other reason the user as POSTed couldn't be added to the db. 

// TODO (For all USER endpoints): change to use a user DTO not the full user object. 
//   ALSO: rethink who can do what to which users. SU can do whatever, but LO should only 
//     be able to do/obtain user information for users who are contributors on THEIR lists.


app.MapPost("/users", async (UserCreateUpdateDto userDto, GeFeSLEDb db,

            HttpContext httpContext,
            UserManager<GeFeSLEUser> userManager,
            RoleManager<IdentityRole> roleManager) =>
{
    string fn = "/users (POST)"; DBg.d(LogLevel.Trace, fn);
    LogDtoIn(fn, nameof(UserCreateUpdateDto), userDto);
    // if username AND email are null, return bad request
    if (string.IsNullOrEmpty(userDto.UserName) && string.IsNullOrEmpty(userDto.Email))
    {
        string msg = "username AND email are both null";
        DBg.d(LogLevel.Trace, $"{fn} --> 400: {msg}");
        return Results.BadRequest();
    }
    else
    {
        // if the username is empty, use the email. This will cover for google and Microsoft accounts. 
        string normalizedUserName = string.IsNullOrEmpty(userDto.UserName) ? userDto.Email : userDto.UserName;
        string uploadsPath = GeFeSLEUser.GetUploadsPath(normalizedUserName, null, userDto.Email);
        var existingUsers = await userManager.Users.ToListAsync();
        if (existingUsers.Any(existingUser => string.Equals(existingUser.UploadsPath, uploadsPath, StringComparison.OrdinalIgnoreCase)))
        {
            string msg = $"A user already exists whose uploads folder would resolve to '{uploadsPath}'. Please choose a different username.";
            return BadRequestWithTrace(fn, msg);
        }
        var user = new GeFeSLEUser { UserName = normalizedUserName, Email = userDto.Email, PhoneNumber = userDto.PhoneNumber, UploadsPath = uploadsPath };
        try
        {
            var result = await userManager.CreateAsync(user);
            if (result.Succeeded)
            {
                // var token = await userManager.GeneratePasswordResetTokenAsync(user);
                // var result = await userManager.ResetPasswordAsync(user, token, newpassword!);
                DBg.d(LogLevel.Trace, $"{fn} --> 200: user created");
                var userRoles = await userManager.GetRolesAsync(user);
                var createdUserDto = user.ToResponseDto(userRoles);
                LogDtoOut(fn, nameof(UserResponseDto), createdUserDto);
                return Results.Created($"User created: {user.Id}", createdUserDto);
            }
            else
            {
                foreach (var error in result.Errors)
                {
                    DBg.d(LogLevel.Trace, $"{fn} --> 400: Error: {error.Code}, Description: {error.Description}");
                }
                return Results.BadRequest(result.Errors);
            }
        }
        catch (Exception e)
        {
            string msg = $"User NOT created: {e.Message}";
            return BadRequestWithTrace(fn, msg);
        }
    }
})
.WithEndpointDocs("users.post")
.RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});


// PUT to modify user info. user object has to serialize to GeFeSLEUser
//     TODO: create user DTO
// WHO: SU and LO
// 404 - specified user not found by id
// 200 - modified successfully
// 400 - any other reason w/ error details
app.MapPut("/users/{userid}", async (string userid, UserCreateUpdateDto userDto,
            GeFeSLEDb db,
            HttpContext httpContext,
            UserManager<GeFeSLEUser> userManager,
            RoleManager<IdentityRole> roleManager) =>
{
    string fn = $"/users/{userid} (PUT)"; DBg.d(LogLevel.Trace, fn);
    LogDtoIn(fn, nameof(UserCreateUpdateDto), userDto);

    var moduser = await userManager.FindByIdAsync(userid);
    if (moduser is null)
    {
        string msg = $"user {userid} not found";
        return NotFoundNoMessageWithTrace(fn, msg);
    }

    if (string.IsNullOrWhiteSpace(moduser.UploadsPath))
    {
        string proposedUserName = string.IsNullOrWhiteSpace(userDto.UserName) ? userDto.Email ?? moduser.UserName : userDto.UserName;
        string proposedEmail = userDto.Email ?? moduser.Email;
        string proposedUploadsPath = GeFeSLEUser.GetUploadsPath(proposedUserName, moduser.Id, proposedEmail);
        var otherUsers = await userManager.Users.Where(user => user.Id != moduser.Id).ToListAsync();
        if (otherUsers.Any(existingUser => string.Equals(existingUser.UploadsPath, proposedUploadsPath, StringComparison.OrdinalIgnoreCase)))
        {
            string msg = $"Another user already uses uploads folder '{proposedUploadsPath}'. Please choose a different username.";
            return BadRequestWithTrace(fn, msg);
        }

        moduser.UploadsPath = proposedUploadsPath;
    }

    moduser.UpdateFromDto(userDto);


    try
    {
        var result = await userManager.UpdateAsync(moduser);
        if (result.Succeeded)
        {
            string msg = "user modified";
            return OkWithTrace(fn, msg);
        }
        else
        {
            DBg.d(LogLevel.Trace, $"{fn} user not modified: ");
            foreach (var error in result.Errors)
            {
                DBg.d(LogLevel.Trace, $"Error: {error.Code}, Description: {error.Description}");
            }
            return Results.BadRequest(result.Errors);
        }
    }
    catch (Exception e)
    {
        string msg = e.Message;
        return BadRequestWithTrace(fn, msg);
    }


})
.WithEndpointDocs("users.userid.put")
.RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});

// deletes the specified user by id
// WHO: SU and LO
// 404 - specified user not found by id
// 200 - deleted successfully
// 400 - any other reason w/ error details
app.MapDelete("/users/{userid}", async (string userid,
            GeFeSLEDb db,
            HttpContext httpContext,
            UserManager<GeFeSLEUser> userManager,
            RoleManager<IdentityRole> roleManager) =>
{
    string fn = $"/users/{userid} (DEL)"; DBg.d(LogLevel.Trace, fn);

    var deluser = await userManager.FindByIdAsync(userid);
    if (deluser is null)
    {
        string msg = $"user {userid} not found";
        return NotFoundNoMessageWithTrace(fn, msg);
    }

    try
    {
        var result = await userManager.DeleteAsync(deluser);
        if (result.Succeeded)
        {
            return OkWithTrace(fn, "user deleted successfully");
        }
        else
        {
            DBg.d(LogLevel.Trace, $"{fn} user not deleted: ");
            foreach (var error in result.Errors)
            {
                DBg.d(LogLevel.Trace, $"Error deleting user: {error.Code}, Description: {error.Description}");
            }
            return Results.BadRequest(result.Errors);
        }
    }
    catch (Exception e)
    {
        string msg = $"Error occurred: {e.Message}";
        return BadRequestWithTrace(fn, msg);
    }

})
.WithEndpointDocs("users.userid.delete")
.RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme, // Authorization schemes
    Roles = "SuperUser,listowner"
});


// Gets the specified user (GeFeSLEUser) by username (not id); searching is normalized by
// uppercase, ergo is not case sensitive.
// TODO: if comparisons are case insensitive, checks fordup users should also be - check the add user post endpoint. 
// TODO: replace with a user DTO not the full object. 
// The reason we get by username here and NOT id humans aren't just a number!
// no seriously, call this by username to get the ID you use for other endpoints.
// TODO: guard against usernames what have special characters like fedi handles @user@instance or emails user@emailhost
//  (and by guard we mean support)
// 200 - user found by name (returns: user object)
// 404 - user not found by name
// WHO: SU and LO

app.MapGet("/users/{username}", async (string username,
        GeFeSLEDb db,
        HttpContext httpContext,
        UserManager<GeFeSLEUser> userManager) =>
{
    string fn = $"/users/{username} (GET)"; DBg.d(LogLevel.Trace, fn);
    GeFeSLEUser? me = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);

    var user = await userManager.FindByNameAsync(username.ToUpper());
    if (user is not null)
    {
        var userRoles = await userManager.GetRolesAsync(user);
        var userDto = user.ToResponseDto(userRoles);
        LogDtoOut(fn, nameof(UserResponseDto), userDto);
        return OkPayloadWithTrace(fn, userDto, $"user {username} returned");
    }
    else
    {
        return NotFoundNoMessageWithTrace(fn, $"user {username} not found");
    }

})
.WithEndpointDocs("users.username.get")
.RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});

// gets all users in the db. 
// WHO: SU and LO
// 200 - returns list of users 
// 202 - no users in db
app.MapGet("/users", async (GeFeSLEDb db,
        HttpContext httpContext,
        UserManager<GeFeSLEUser> userManager) =>
{
    string fn = "/users (GET)"; DBg.d(LogLevel.Trace, fn);
    GeFeSLEUser? me = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);

    var users = await userManager.Users.ToListAsync();
    // if there are no users,
    if (users.Count == 0)
    {
        return NoContentWithTrace(fn, "no users found");
    }
    else
    {
        var userDtos = new List<UserResponseDto>();
        foreach (var user in users)
        {
            var userRoles = await userManager.GetRolesAsync(user);
            userDtos.Add(user.ToResponseDto(userRoles));
        }
        LogDtoOut(fn, "List<UserResponseDto>", userDtos);
        return OkPayloadWithTrace(fn, userDtos, $"{userDtos.Count} users returned");
    }

})
.WithEndpointDocs("users.get")
.RequireAuthorization(new AuthorizeAttribute
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
    string fn = $"/users/{userid}/password (GET)"; DBg.d(LogLevel.Trace, fn);
    GeFeSLEUser? me = UserSessionService.UpdateSessionAccessTime(context, db, userManager);

    var user = await userManager.FindByIdAsync(userid);
    if (user is null)
    {
        string msg = $"user {userid} not found";
        return NotFoundWithTrace(fn, msg);
    }
    else
    {
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        return OkPayloadWithTrace(fn, token, "password reset token generated");
    }
})
.WithEndpointDocs("users.userid.password.get")
.RequireAuthorization(new AuthorizeAttribute
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
    string fn = $"/users/{userid}/password (DEL)"; DBg.d(LogLevel.Trace, fn);
    LogDtoIn(fn, nameof(PasswordChangeDto), passwordChangeDto);
    GeFeSLEUser? me = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    string msg;
    var user = await userManager.FindByIdAsync(userid);
    if (user is null)
    {
        msg = $"user {userid} not found";
        return NotFoundWithTrace(fn, msg);
    }
    else if (string.IsNullOrEmpty(passwordChangeDto.NewPassword))
    {
        msg = "new password is null";
        return BadRequestWithTrace(fn, msg);
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
            return OkWithTrace(fn, "password reset successful");
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

})
.WithEndpointDocs("users.userid.password.delete")
.RequireAuthorization(new AuthorizeAttribute
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
    string fn = $"/users/{userid}/password (POST)"; DBg.d(LogLevel.Trace, fn);
    LogDtoIn(fn, nameof(PasswordChangeDto), passwordChangeDto);
    GeFeSLEUser? me = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    string msg;
    var user = await userManager.FindByIdAsync(userid);
    if (user is null)
    {
        msg = $"user {userid} not found";
        return NotFoundWithTrace(fn, msg);
    }
    else if (string.IsNullOrEmpty(passwordChangeDto.NewPassword))
    {
        msg = "new password is null";
        return BadRequestWithTrace(fn, msg);
    }
    else if (string.IsNullOrEmpty(passwordChangeDto.ResetToken))
    {
        msg = "reset token is null";
        return BadRequestWithTrace(fn, msg);
    }
    else
    {
        var result = await userManager.ResetPasswordAsync(user, passwordChangeDto.ResetToken, passwordChangeDto.NewPassword);
        if (result.Succeeded)
        {
            return OkWithTrace(fn, "password changed successfully");
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

})
.WithEndpointDocs("users.userid.password.post")
.RequireAuthorization(new AuthorizeAttribute
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
        return NotFoundNoMessageWithTrace(fn, $"user {userid} not found");
    }
    else
    {
        IList<string> roles = await userManager.GetRolesAsync(user);
        if (roles.Count == 0)
        {
            return NoContentWithTrace(fn, $"user {user.UserName} has no role");
        }
        else
        {
            return OkPayloadWithTrace(fn, roles, $"user {user.UserName} has roles {System.Text.Json.JsonSerializer.Serialize(roles)}");
        }
    }

}
)
.WithEndpointDocs("users.userid.roles.get")
.RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});



app.MapPost("/users/{userid}/roles", async (string userid,
        [FromBody] List<string> roles,
        GeFeSLEDb db,
        HttpContext httpContext,
        UserManager<GeFeSLEUser> userManager,
        RoleManager<IdentityRole> roleManager) =>
{
    string fn = $"/users/{userid}/roles (POST)"; DBg.d(LogLevel.Trace, fn);
    LogDtoIn(fn, "List<string>", roles);
    GeFeSLEUser? me = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    var sessionUser = UserSessionService.amILoggedIn(httpContext);

    var user = await userManager.FindByIdAsync(userid);
    if (user is null)
    {
        string msg = $"user {userid} not found";
        return NotFoundWithTrace(fn, msg);
    }
    else
    {
        // print out the roles IList


        // ...

        DBg.d(LogLevel.Trace, $"{fn} roles: {JsonConvert.SerializeObject(roles)}");
        // for each role in the list, add the user to the role
        // note AddToRoleAsync will not add a user to a role they are already in
        bool success = true;
        List<string> added = new List<string>();
        var errors = new List<IdentityError>();
        foreach (var role in roles)
        {
            if (sessionUser.Role != "SuperUser" && role == "SuperUser")
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
            else
            {
                added.Add(role);

            }
        }
        if (success)
        {
            DBg.d(LogLevel.Information, $"{fn} -> user {userid} assigned to roles {System.Text.Json.JsonSerializer.Serialize(added)}");
            return OkWithTrace(fn, $"user {userid} assigned to roles {System.Text.Json.JsonSerializer.Serialize(added)}");
        }
        else
        {
            DBg.d(LogLevel.Information, $"{fn} -> user {userid} NOT ASSIGNED to roles {System.Text.Json.JsonSerializer.Serialize(roles)}");
            return Results.BadRequest(errors);
        }
    }
}
)
.WithEndpointDocs("users.userid.roles.post")
.RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});

app.MapDelete("/users/{userid}/roles/{role}", async (string userid,
        string role,
        GeFeSLEDb db,
        HttpContext httpContext,
        UserManager<GeFeSLEUser> userManager,
        RoleManager<IdentityRole> roleManager) =>
{
    string fn = $"/users/{userid}/roles/{role} (DEL)"; DBg.d(LogLevel.Trace, fn);
    GeFeSLEUser? me = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    var sessionUser = UserSessionService.amILoggedIn(httpContext);

    // dont need checks for username==null, will 404 on that anyway
    var user = await userManager.FindByIdAsync(userid);
    if (user is null)
    {
        return NotFoundNoMessageWithTrace(fn, $"user {userid} not found");
    }
    else
    {
        // get the sessionUser's role
        if (sessionUser.Role != "SuperUser" && role == "SuperUser")
        {
            string msg = "Insufficient permissions";
            return BadRequestWithTrace(fn, msg);
        }

        var result = await userManager.RemoveFromRoleAsync(user, role);
        if (result.Succeeded)
        {
            DBg.d(LogLevel.Trace, $"deleterole: user {userid} UNASSIGNED from role {role}");
            return OkWithTrace(fn, $"user {userid} unassigned from role {role}");
        }
        else
        {
            DBg.d(LogLevel.Trace, $"deleterole: user {userid} NOT UNASSIGNED to role {role}");
            return Results.BadRequest(result.Errors);
        }
    }
}
)
.WithEndpointDocs("users.userid.roles.role.delete")
.RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});

app.MapGet("/lists", async (GeFeSLEDb db,
    HttpContext httpContext) =>
{
    string fn = "/lists (GET)"; DBg.d(LogLevel.Trace, fn);

    var userManager = httpContext.RequestServices.GetRequiredService<UserManager<GeFeSLEUser>>();
    GeFeSLEUser? me = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    var sessionUser = UserSessionService.amILoggedIn(httpContext);

    List<GeList> lists = await db.Lists
        .Include(l => l.Creator)
        .Include(l => l.ListOwners)
        .Include(l => l.Contributors)
        .ToListAsync();
    List<GeList> visibleLists = new List<GeList>();
    foreach (GeList list in lists)
    {
        (bool isAllowed, string? ynot) = list.IsUserAllowedToView(me);
        if (isAllowed || sessionUser.Role == "SuperUser")
        {
            visibleLists.Add(list);
            if (!isAllowed && sessionUser.Role == "SuperUser")
            {
                DBg.d(LogLevel.Debug, $"{fn} SuperUser bypassed list permissions for {list.Name}");
            }
        }

    }
    if (visibleLists.Count == 0)
    {
        return NoContentWithTrace(fn, "no lists visible to user");
    }
    else
    {
        var visibleListIds = visibleLists.Select(l => l.Id).ToList();
        var itemCounts = await db.Items
            .Where(i => visibleListIds.Contains(i.ListId) && i.Visible && !i.IsDeleted)
            .GroupBy(i => i.ListId)
            .Select(g => new { ListId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ListId, x => x.Count);

        foreach (var list in visibleLists)
        {
            list.VisibleItemCount = itemCounts.TryGetValue(list.Id, out int count) ? count : 0;
        }

        var responseDtos = visibleLists.Select(l => l.ToResponseDto()).ToList();
        LogDtoOut(fn, "List<GeListResponseDto>", responseDtos);
        return OkPayloadWithTrace(fn, responseDtos, $"{responseDtos.Count} lists returned");
    }
})
.WithEndpointDocs("lists.get");

app.MapGet("/lists/{listid:int}", async (GeFeSLEDb db,
    int listid,
    HttpContext httpContext) =>
{
    string fn = $"/lists/{listid} (GET)"; DBg.d(LogLevel.Trace, fn);

    var userManager = httpContext.RequestServices.GetRequiredService<UserManager<GeFeSLEUser>>();
    GeFeSLEUser? me = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    var sessionUser = UserSessionService.amILoggedIn(httpContext);

    GeList list = await db.Lists
        .Include(l => l.Creator)
        .Include(l => l.ListOwners)
        .Include(l => l.Contributors)
        .FirstOrDefaultAsync(l => l.Id == listid);
    if (list is null)
    {
        return NotFoundNoMessageWithTrace(fn, $"list {listid} not found");
    }
    else
    {
        (bool isAllowed, string? ynot) = list.IsUserAllowedToView(me);
        if (isAllowed || sessionUser.Role == "SuperUser")
        {
            if (!isAllowed && sessionUser.Role == "SuperUser")
            {
                DBg.d(LogLevel.Warning, $"{fn} SuperUser bypassed list permissions for {list.Name}");
            }
            var listDto = list.ToResponseDto();
            LogDtoOut(fn, nameof(GeListResponseDto), listDto);
            return OkPayloadWithTrace(fn, listDto, $"list {listid} returned");

        }
        else
        {
            // TODO: contrive to return the ynot message as well.
            return UnauthorizedWithTrace(fn);
        }

    }
})
.WithEndpointDocs("lists.listid.get");

// creates a new list. 
// 400 - if list name is null or empty
// 400 - if list name is the same as an existing list
// 201 - if created successfully (returns: the new list object)
// 400 - could not determine who are you are (TODO: change this 401/403?)
// 400 - if bad characters in list name (ones that won't play nice with ActivityPub actor names or webfinger acct: handles)


app.MapPost("/lists", async (GeListDto newlistDto,
    GeFeSLEDb db,
    HttpContext httpContext,
    UserManager<GeFeSLEUser> userManager,
    RoleManager<IdentityRole> roleManager) =>
{
    string fn = "/lists (POST)"; DBg.d(LogLevel.Trace, fn);
    LogDtoIn(fn, nameof(GeListDto), newlistDto);

    // if the newlist.Name is null, return bad request
    // zTODO: extend with other checks - reserve dnames, bad characters that won't
    // play nice with ActivityPub actor names or webfinger acct: handles
    if (string.IsNullOrEmpty(newlistDto.Name))
    {
        string errMsg = "Cannot have a list with no name. A Horse maybe... but not a list.";
        return BadRequestWithTrace(fn, errMsg);
    }
    else if (newlistDto.Name == GlobalConfig.modListName)
    {
        string errMsg = $"List name {GlobalConfig.modListName} is RESERVED.";
        return BadRequestWithTrace(fn, errMsg);
    }
    else
    {
        // check for characters that will cause filesystem problems. 
        var invalidIndex = newlistDto.Name.IndexOfAny(Path.GetInvalidFileNameChars());
        if (invalidIndex >= 0)
        {
            string errMsg = $"List name contains invalid character '{newlistDto.Name[invalidIndex]}' at position {invalidIndex}.";
            return BadRequestWithTrace(fn, errMsg);
        }
    }
    DBg.d(LogLevel.Trace, $"{fn} - new list name: {newlistDto.Name}");

    // if the newlist.Name is the same as an existing list, return bad request
    var existingList = await db.Lists.Where(l => l.Name == newlistDto.Name).FirstOrDefaultAsync();
    if (existingList is not null)
    {
        string errMsg = "List with that name already exists";
        return BadRequestWithTrace(fn, errMsg);
    }
    GeFeSLEUser? me = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    if (me is null)
    {
        string errMsg = "Could not determine who you are";
        return BadRequestWithTrace(fn, errMsg);
    }
    // no need to check for roles, auth middleware already did it. 

    // check for newlist.ActivityPubId against characters allowed in AP handles
    // as specified in GlobalConfig.validAPListNameChars
    DBg.d(LogLevel.Trace, $"{fn} - new list AP id: {newlistDto.ActivityPubId}");
    if (!string.IsNullOrEmpty(newlistDto.ActivityPubId))
    {
        var invalidActivityPubIdChar = newlistDto.ActivityPubId
            .Select((c, index) => new { c, index })
            .FirstOrDefault(item => !GlobalConfig.validAPListNameChars.Contains(item.c));

        if (invalidActivityPubIdChar != null)
        {
            DBg.d(LogLevel.Trace, $"{fn} - invalid AP id character: {invalidActivityPubIdChar.c} at position {invalidActivityPubIdChar.index}");
            string errMsg = $"ActivityPubId contains invalid character {invalidActivityPubIdChar.c} starting at position {invalidActivityPubIdChar.index}.";
            return BadRequestWithTrace(fn, errMsg);
        }
    }



    // Map DTO to domain object
    var newlist = new GeList { Name = newlistDto.Name, Comment = newlistDto.Comment, ActivityPubId = newlistDto.ActivityPubId, Visibility = newlistDto.Visibility };

    newlist.Creator = me;
    newlist.ListOwners.Add(me);
    db.Lists.Add(newlist);
    await db.SaveChangesAsync();
    await ProtectedFiles.RefreshListCacheAsync(db, newlist.Id);
    await newlist.RegenerateAllFiles(db);
    string msg = $"/lists/{newlist.Id}";
    LogDtoOut(fn, nameof(GeList), newlist);
    return Results.Created(msg, newlist);
}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});


app.MapPut("/lists", async (HttpContext context,
    GeListDto inputList,
    GeFeSLEDb db,
    UserManager<GeFeSLEUser> userManager,
    RoleManager<IdentityRole> roleManager,
    GeListController geListController) =>
    {
        string fn = "/lists (PUT)"; DBg.d(LogLevel.Trace, fn);
        LogDtoIn(fn, nameof(GeListDto), inputList);
        GeListVisibility? oldVisibility = await db.Lists
            .AsNoTracking()
            .Where(l => l.Id == inputList.Id)
            .Select(l => (GeListVisibility?)l.Visibility)
            .FirstOrDefaultAsync();

        var result = await geListController.ListsPut(context, inputList);

        if (result is IStatusCodeHttpResult statusResult
            && statusResult.StatusCode >= 200
            && statusResult.StatusCode < 300)
        {
            GeList? updatedList = await db.Lists
                .Include(l => l.Creator)
                .Include(l => l.ListOwners)
                .FirstOrDefaultAsync(l => l.Id == inputList.Id);
            if (updatedList is not null)
            {
                bool wasPublic = oldVisibility == GeListVisibility.Public;
                bool isPublic = updatedList.Visibility == GeListVisibility.Public;

                if (wasPublic && !isPublic)
                {
                    await ActivityPubBroadcastService.RotateActivityPubItemIdsForListVisibilityDropAsync(
                        updatedList,
                        db,
                        (listForBroadcast, dbForBroadcast, itemForBroadcast, activityTypeForBroadcast, onlyFollowerForBroadcast) =>
                            ActivityPubBroadcastService.BroadcastActivityPubItemToFollowersAsync(
                                listForBroadcast,
                                dbForBroadcast,
                                itemForBroadcast,
                                activityTypeForBroadcast,
                                (listForNote, itemForNote) => ActivityPubPayloadFactory.BuildActivityPubItemNote(listForNote, itemForNote, activityPubMarkdownPipeline),
                                ActivityPubDeliveryUtils.ResolveActorInboxAsync,
                                (inboxUrl, actorUrl, activityPayload, successLogMessage) =>
                                    ActivityPubDeliveryUtils.SendSignedActivityPubMessageAsync(inboxUrl, actorUrl, activityPayload, successLogMessage, activityPubSigningKey),
                                onlyFollowerForBroadcast));
                }
                else if (!wasPublic && isPublic)
                {
                    await ActivityPubBroadcastService.BroadcastAllActivityPubItemsToFollowersAsync(
                        updatedList,
                        db,
                        "Create",
                        (listForBroadcast, dbForBroadcast, itemForBroadcast, activityTypeForBroadcast, onlyFollowerForBroadcast) =>
                            ActivityPubBroadcastService.BroadcastActivityPubItemToFollowersAsync(
                                listForBroadcast,
                                dbForBroadcast,
                                itemForBroadcast,
                                activityTypeForBroadcast,
                                (listForNote, itemForNote) => ActivityPubPayloadFactory.BuildActivityPubItemNote(listForNote, itemForNote, activityPubMarkdownPipeline),
                                ActivityPubDeliveryUtils.ResolveActorInboxAsync,
                                (inboxUrl, actorUrl, activityPayload, successLogMessage) =>
                                    ActivityPubDeliveryUtils.SendSignedActivityPubMessageAsync(inboxUrl, actorUrl, activityPayload, successLogMessage, activityPubSigningKey),
                                onlyFollowerForBroadcast));
                }

                await ActivityPubBroadcastService.BroadcastActivityPubActorUpdateToFollowersAsync(
                    updatedList,
                    db,
                    listForActor => ActivityPubActorFactory.BuildActivityPubListActor(listForActor, activityPubMarkdownPipeline, activityPubPublicKeyPem),
                    ActivityPubDeliveryUtils.ResolveActorInboxAsync,
                    (inboxUrl, actorUrl, activityPayload, successLogMessage) =>
                        ActivityPubDeliveryUtils.SendSignedActivityPubMessageAsync(inboxUrl, actorUrl, activityPayload, successLogMessage, activityPubSigningKey));
            }
        }

        return result;
    }).RequireAuthorization(new AuthorizeAttribute
    {
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
        Roles = "SuperUser,listowner"
    });


app.MapGet("/lists/{listId:int}/items", async (int listId,
        GeFeSLEDb db,
        UserManager<GeFeSLEUser> userManager,
        HttpContext httpContext) =>
{
    string fn = $"/lists/{listId}/items (GET)"; DBg.d(LogLevel.Trace, fn);
    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    var list = await db.Lists.FindAsync(listId);
    if (list is null)
    {
        return NotFoundWithTrace(fn, "List not found");
    }
    var items = await list.GetItems(db);
    // TODO: restrict access to items based on list permissions.
    var itemDtos = items.Select(i => i.ToResponseDto()).ToList();
    LogDtoOut(fn, "List<GeListItemResponseDto>", itemDtos);
    return OkPayloadWithTrace(fn, itemDtos, $"{itemDtos.Count} items returned for list {listId}");
})
.WithEndpointDocs("lists.listid.items.get");
// TODO: restreict access toitems based on list permissions. 


// retreives the specified item by id
// 200 - item found (and returned)
// 404 - item not found by id
// TODO: if item belongs to a list, need to verify user has permission to view item 
//     via list and user permissions. 
// WHO: anyone currently.. bad.


app.MapGet("/items/{id:int}", async (int id,
        GeFeSLEDb db,
        UserManager<GeFeSLEUser> userManager,
        HttpContext httpContext) =>
{
    string fn = $"/items/{id} (GET)"; DBg.d(LogLevel.Trace, fn);
    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    var showitem = await db.Items.FindAsync(id);
    if (showitem is not null)
    {
        if (showitem.RedirectToItemId.HasValue)
        {
            return RedirectWithTrace(fn, $"/items/{showitem.RedirectToItemId.Value}", $"redirecting item {id} to {showitem.RedirectToItemId.Value}");
        }
        var itemDto = showitem.ToResponseDto();
        LogDtoOut(fn, nameof(GeListItemResponseDto), itemDto);
        return OkPayloadWithTrace(fn, itemDto, $"item {id} returned");
    }
    else
    {
        return NotFoundNoMessageWithTrace(fn, $"item {id} not found");
    }
})
.WithEndpointDocs("items.id.get");

app.MapGet("/posts/{itemId:int}", async (int itemId, GeFeSLEDb db) =>
{
    string fn = $"/posts/{itemId} (GET)"; DBg.d(LogLevel.Trace, fn);

    GeListItem? item = await db.Items.FirstOrDefaultAsync(i => i.Id == itemId);
    if (item == null)
    {
        string msg = $"Item with id {itemId} not found";
        return NotFoundWithTrace(fn, msg);
    }

    if (item.RedirectToItemId.HasValue)
    {
        return RedirectWithTrace(fn, $"/posts/{item.RedirectToItemId.Value}", $"redirecting post {itemId} to {item.RedirectToItemId.Value}");
    }

    if (item.IsDeleted)
    {
        return StatusCodeWithTrace(fn, 410, "Item is deleted");
    }

    GeList? list = await db.Lists.FirstOrDefaultAsync(l => l.Id == item.ListId);
    if (list == null)
    {
        string msg = $"List with id {item.ListId} not found for item {itemId}";
        return NotFoundWithTrace(fn, msg);
    }

    if (list.Visibility != GeListVisibility.Public)
    {
        return StatusCodeWithTrace(fn, 403, "List is not public");
    }

    string hostBase = (GlobalConfig.Hostname ?? string.Empty).TrimEnd('/');
    string canonicalUrl = $"{hostBase}/posts/{item.Id}";
    string listFileName = $"{list.Name}.html";
    string listAnchorUrl = $"{hostBase}/{Uri.EscapeDataString(listFileName)}#{item.Id}";

    string title = string.IsNullOrWhiteSpace(item.Name) ? $"List item {item.Id}" : item.Name;
    string descriptionRaw = string.IsNullOrWhiteSpace(item.Comment) ? title : item.Comment;
    string description = descriptionRaw.Replace("\r", " ").Replace("\n", " ").Trim();
    if (description.Length > 280)
    {
        description = description.Substring(0, 277) + "...";
    }

    string contentHtml = string.Empty;
    if (!string.IsNullOrWhiteSpace(item.Comment))
    {
        contentHtml = Markdown.ToHtml(item.Comment, activityPubMarkdownPipeline);
    }

    var sb = new StringBuilder();
    sb.AppendLine("<!DOCTYPE html>");
    sb.AppendLine("<html>");
    sb.AppendLine("<head>");
    sb.AppendLine("<meta charset=\"utf-8\">");
    sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
    sb.AppendLine($"<title>{WebUtility.HtmlEncode(title)}</title>");
    sb.AppendLine($"<link rel=\"canonical\" href=\"{WebUtility.HtmlEncode(canonicalUrl)}\">");
    sb.AppendLine("<meta property=\"og:type\" content=\"article\">");
    sb.AppendLine($"<meta property=\"og:title\" content=\"{WebUtility.HtmlEncode(title)}\">");
    sb.AppendLine($"<meta property=\"og:description\" content=\"{WebUtility.HtmlEncode(description)}\">");
    sb.AppendLine($"<meta property=\"og:url\" content=\"{WebUtility.HtmlEncode(canonicalUrl)}\">");
    sb.AppendLine("<meta name=\"twitter:card\" content=\"summary\">");
    sb.AppendLine($"<meta name=\"twitter:title\" content=\"{WebUtility.HtmlEncode(title)}\">");
    sb.AppendLine($"<meta name=\"twitter:description\" content=\"{WebUtility.HtmlEncode(description)}\">");
    sb.AppendLine("</head>");
    sb.AppendLine("<body>");
    sb.AppendLine($"<h1>{WebUtility.HtmlEncode(title)}</h1>");
    if (!string.IsNullOrWhiteSpace(contentHtml))
    {
        sb.AppendLine($"<div>{contentHtml}</div>");
    }
    if (!string.IsNullOrWhiteSpace(item.OriginatorActorIri))
    {
        string actorIri = WebUtility.HtmlEncode(item.OriginatorActorIri);
        sb.AppendLine($"<div><strong>Actor:</strong> <a href=\"{actorIri}\" rel=\"nofollow noopener noreferrer\" target=\"_blank\">{actorIri}</a></div>");
    }
    if (!string.IsNullOrWhiteSpace(item.SourceAttributedToIri))
    {
        string attributedIri = WebUtility.HtmlEncode(item.SourceAttributedToIri);
        sb.AppendLine($"<div><strong>Attributed To:</strong> <a href=\"{attributedIri}\" rel=\"nofollow noopener noreferrer\" target=\"_blank\">{attributedIri}</a></div>");
    }
    if (!string.IsNullOrWhiteSpace(item.SourceObjectIri))
    {
        string objectIri = WebUtility.HtmlEncode(item.SourceObjectIri);
        sb.AppendLine($"<div><strong>Source Object:</strong> <a href=\"{objectIri}\" rel=\"nofollow noopener noreferrer\" target=\"_blank\">{objectIri}</a></div>");
    }
    sb.AppendLine($"<p><a href=\"{WebUtility.HtmlEncode(listAnchorUrl)}\">View in list</a></p>");
    sb.AppendLine("</body>");
    sb.AppendLine("</html>");

    return ContentWithTrace(fn, sb.ToString(), "text/html", "HTML response generated");
}).AllowAnonymous();

app.MapPost("/items", async (
    [FromBody] GeListItemCreateUpdateDto itemDto,
    GeFeSLEDb db,
    UserManager<GeFeSLEUser> userManager,
    HttpContext httpContext,
    GeListFileController geListFileController) =>
{
    string fn = "/items (POST)"; DBg.d(LogLevel.Trace, fn);
    LogDtoIn(fn, nameof(GeListItemCreateUpdateDto), itemDto);
    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    // if the ListId of itemDto is 0 (which is ok - no value in json int is 0), then set it to listid
    if (itemDto.ListId == 0)
    {
        return BadRequestWithTrace(fn, "New item listID is invalid.");
    }
    // Map DTO to domain object
    var newitem = new GeListItem { ListId = itemDto.ListId, Name = itemDto.Name, Comment = itemDto.Comment, IsComplete = itemDto.IsComplete, Visible = itemDto.Visible, Tags = new List<string>(itemDto.Tags) };
    db.Items.Add(newitem);
    await db.SaveChangesAsync();

    // find the list that corresponds to listid
    var list = await db.Lists.FindAsync(newitem.ListId);
    if (list is null)
    {
        return NotFoundWithTrace(fn, "List not found");
    }

    // "attachments" protection check - if the item references an upload we want to set the protection to match 
    // the list that its in
    List<string> itemfiles = newitem.LocalFiles();
    if (list.Visibility > GeListVisibility.Public)
    {
        // does this item contain any file references? 

        geListFileController.ProtectFiles(itemfiles, list.Name);
    }
    else
    {
        geListFileController.UnProtectFiles(itemfiles, list.Name); // TODO: handle situation when a file is in two lists of differing vis levels
    }

    await list.RegenerateAllFiles(db);
    await ActivityPubBroadcastService.BroadcastActivityPubItemToFollowersAsync(
        list,
        db,
        newitem,
        "Create",
        (listForNote, itemForNote) => ActivityPubPayloadFactory.BuildActivityPubItemNote(listForNote, itemForNote, activityPubMarkdownPipeline),
        ActivityPubDeliveryUtils.ResolveActorInboxAsync,
        (inboxUrl, actorUrl, activityPayload, successLogMessage) =>
            ActivityPubDeliveryUtils.SendSignedActivityPubMessageAsync(inboxUrl, actorUrl, activityPayload, successLogMessage, activityPubSigningKey));
    var createdItemDto = newitem.ToResponseDto();
    LogDtoOut(fn, nameof(GeListItemResponseDto), createdItemDto);
    return Results.Created($"/items/{newitem.Id}", createdItemDto);
}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner,contributor"
});

app.MapGet("/lists/{list:int}/files", async (int list,
    GeFeSLEDb db,
    HttpContext httpContext,
    UserManager<GeFeSLEUser> userManager) =>
{
    string fn = $"/lists/{list}/files (GET)"; DBg.d(LogLevel.Trace, fn);
    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);

    var listObj = await db.Lists
        .Include(l => l.Creator)
        .Include(l => l.ListOwners)
        .Include(l => l.Contributors)
        .FirstOrDefaultAsync(l => l.Id == list);

    if (listObj is null)
    {
        string msg = $"List {list} not found";
        return NotFoundWithTrace(fn, msg);
    }

    (bool canView, string? ynot) = listObj.IsUserAllowedToView(user);
    if (!canView)
    {
        string msg = ynot ?? "Not allowed to view list files";
        return BadRequestWithTrace(fn, msg);
    }

    var items = await db.Items.Where(i => i.ListId == list).ToListAsync();
    var listRefs = GeListFileController.ExtractUploadReferences(listObj.Comment)
        .Select(GeListFileController.NormalizeRelativePath)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    var itemResults = items.Select(item =>
    {
        var localFiles = item.LocalFiles()
            .Select(GeListFileController.NormalizeRelativePath)
            .ToList();
        var commentFiles = GeListFileController.ExtractUploadReferences(item.Comment)
            .Select(GeListFileController.NormalizeRelativePath)
            .ToList();
        var nameFiles = GeListFileController.ExtractUploadReferences(item.Name)
            .Select(GeListFileController.NormalizeRelativePath)
            .ToList();

        var allRefs = localFiles
            .Concat(commentFiles)
            .Concat(nameFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new
        {
            item.Id,
            item.ListId,
            item.Name,
            localFiles,
            commentFiles,
            nameFiles,
            allRefs
        };
    }).ToList();

    var allListRefs = listRefs
        .Concat(itemResults.SelectMany(i => i.allRefs))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    return OkPayloadWithTrace(fn, new
    {
        list = new
        {
            listObj.Id,
            listObj.Name,
            commentFiles = listRefs
        },
        items = itemResults,
        allRefs = allListRefs
    }, "list export payload returned");
})
.RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner,contributor"
});


app.MapPut("/items/{itemId:int}", async (int itemId,
        [FromBody] GeListItemCreateUpdateDto inputItemDto,
        GeFeSLEDb db,
        UserManager<GeFeSLEUser> userManager,
        HttpContext httpContext,
        GeListFileController geListFileController) =>
{
    string fn = $"/items/{itemId} (PUT)"; DBg.d(LogLevel.Trace, fn);
    LogDtoIn(fn, nameof(GeListItemCreateUpdateDto), inputItemDto);
    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    // Verify the item exists
    var moditem = await db.Items.FirstOrDefaultAsync(item => item.Id == itemId);
    // check for listowner or contributor of the list this item belongs to
    if (moditem is null)
    {
        return NotFoundNoMessageWithTrace(fn, $"item {itemId} not found");
    }
    // if the item's listid is different from the inputItem's listid, then we have to make sure the 
    // caller has modification rights to BOTH lists
    // first, find the OLD list. 
    var oldlist = await db.Lists
        .Include(l => l.Creator)
        .Include(l => l.ListOwners)
        .Include(l => l.Contributors)
        .FirstOrDefaultAsync(l => l.Id == moditem.ListId);
    if (oldlist is null)
    {
        string msg = $"Original list {moditem.ListId} not found";
        return NotFoundWithTrace(fn, msg);
    }
    // if the listid is changing, find the NEW list and check permissions on that too
    var itemMoved = false;
    GeList? destinationListForMove = null;
    if (moditem.ListId != inputItemDto.ListId)
    {
        itemMoved = true;
        destinationListForMove = await db.Lists
            .Include(l => l.Creator)
            .Include(l => l.ListOwners)
            .Include(l => l.Contributors)
            .FirstOrDefaultAsync(l => l.Id == inputItemDto.ListId);
        if (destinationListForMove is null)
        {
            string msg = $"New list {inputItemDto.ListId} not found";
            return NotFoundWithTrace(fn, msg);
        }
        (bool canModifyOld, string? ynotOld) = oldlist.IsUserAllowedToModify(user);
        (bool canModifyNew, string? ynotNew) = destinationListForMove.IsUserAllowedToModify(user);
        if (!canModifyOld || !canModifyNew)
        {            string reason = $"Cannot modify item. ";
            if (!canModifyOld)            {
                reason += $"No modify permissions on original list (id {oldlist.Id}, name {oldlist.Name}): {ynotOld}. ";
            }
            if (!canModifyNew)            {
                reason += $"No modify permissions on new list (id {destinationListForMove.Id}, name {destinationListForMove.Name}): {ynotNew}.";
            }
            return BadRequestWithTrace(fn, reason);
        }
    }
    bool wasVisible = moditem.Visible;
    int requestedItemId = moditem.Id;

    // otherwise, modify away boyo. 
    moditem.UpdateFromDto(inputItemDto);
    moditem.ListId = inputItemDto.ListId;
    bool visibilityChanged = wasVisible != moditem.Visible;

    await db.SaveChangesAsync();
    
    // "attachments" protection check - if the item references an upload we want to set the protection to match 
    // the list that its NOW .. CURRENTLY in -- IT MAY HAVE MOVED
    var nowlist = destinationListForMove ?? await db.Lists.FindAsync(inputItemDto.ListId);
    List<string> itemfiles = moditem.LocalFiles();
    if (nowlist.Visibility > GeListVisibility.Public)
    {
        // does this item contain any file references? 

        geListFileController.ProtectFiles(itemfiles, nowlist.Name);
    }
    else
    {
        // if the item is in a public list, but it is not visible, its attachments shouldn't be either:
        if (moditem.Visible)
        {
            geListFileController.UnProtectFiles(itemfiles, nowlist.Name); // TODO: handle situation when a file is in two lists of differing vis levels
        }
        else
        {
            geListFileController.ProtectFiles(itemfiles, GlobalConfig.modListName); // TODO: this means the image is only visible to superusers. we can do better. 
        }

    }
    bool rotatedItemIdOnVisibilityDrop = false;
    if (!itemMoved
        && visibilityChanged
        && wasVisible
        && !moditem.Visible
        && nowlist.Visibility == GeListVisibility.Public)
    {
        moditem = await ActivityPubBroadcastService.RotateActivityPubItemIdForVisibilityDropAsync(
            nowlist,
            db,
            moditem,
            successorVisible: false,
            (listForBroadcast, dbForBroadcast, itemForBroadcast, activityTypeForBroadcast, onlyFollowerForBroadcast) =>
                ActivityPubBroadcastService.BroadcastActivityPubItemToFollowersAsync(
                    listForBroadcast,
                    dbForBroadcast,
                    itemForBroadcast,
                    activityTypeForBroadcast,
                    (listForNote, itemForNote) => ActivityPubPayloadFactory.BuildActivityPubItemNote(listForNote, itemForNote, activityPubMarkdownPipeline),
                    ActivityPubDeliveryUtils.ResolveActorInboxAsync,
                    (inboxUrl, actorUrl, activityPayload, successLogMessage) =>
                        ActivityPubDeliveryUtils.SendSignedActivityPubMessageAsync(inboxUrl, actorUrl, activityPayload, successLogMessage, activityPubSigningKey),
                    onlyFollowerForBroadcast));
        rotatedItemIdOnVisibilityDrop = true;
    }

    if(itemMoved)
    {
        await oldlist.RegenerateAllFiles(db);
        await nowlist.RegenerateAllFiles(db);

        await ActivityPubBroadcastService.BroadcastMovedItemToFollowersAsync(
            oldlist,
            nowlist,
            db,
            moditem,
            (listForBroadcast, dbForBroadcast, itemForBroadcast, activityTypeForBroadcast, onlyFollowerForBroadcast) =>
                ActivityPubBroadcastService.BroadcastActivityPubItemToFollowersAsync(
                    listForBroadcast,
                    dbForBroadcast,
                    itemForBroadcast,
                    activityTypeForBroadcast,
                    (listForNote, itemForNote) => ActivityPubPayloadFactory.BuildActivityPubItemNote(listForNote, itemForNote, activityPubMarkdownPipeline),
                    ActivityPubDeliveryUtils.ResolveActorInboxAsync,
                    (inboxUrl, actorUrl, activityPayload, successLogMessage) =>
                        ActivityPubDeliveryUtils.SendSignedActivityPubMessageAsync(inboxUrl, actorUrl, activityPayload, successLogMessage, activityPubSigningKey),
                    onlyFollowerForBroadcast));
    }
    else
    {
        await nowlist.RegenerateAllFiles(db);

        if (visibilityChanged)
        {
            if (rotatedItemIdOnVisibilityDrop)
            {
                DBg.d(LogLevel.Debug, $"{fn} -- visibility drop delete already broadcast during ID rotation for item {requestedItemId} -> {moditem.Id}");
            }
            else if (nowlist.Visibility == GeListVisibility.Public)
            {
                string visibilityActivityType = moditem.Visible ? "Create" : "Delete";
                await ActivityPubBroadcastService.BroadcastActivityPubItemToFollowersAsync(
                    nowlist,
                    db,
                    moditem,
                    visibilityActivityType,
                    (listForNote, itemForNote) => ActivityPubPayloadFactory.BuildActivityPubItemNote(listForNote, itemForNote, activityPubMarkdownPipeline),
                    ActivityPubDeliveryUtils.ResolveActorInboxAsync,
                    (inboxUrl, actorUrl, activityPayload, successLogMessage) =>
                        ActivityPubDeliveryUtils.SendSignedActivityPubMessageAsync(inboxUrl, actorUrl, activityPayload, successLogMessage, activityPubSigningKey));
            }
            else
            {
                DBg.d(LogLevel.Debug, $"{fn} -- visibility changed for item {moditem.Id} but list {nowlist.Id} is not public; skipping ActivityPub visibility broadcast");
            }
        }
        else
        {
            await ActivityPubBroadcastService.BroadcastActivityPubItemToFollowersAsync(
                nowlist,
                db,
                moditem,
                "Update",
                (listForNote, itemForNote) => ActivityPubPayloadFactory.BuildActivityPubItemNote(listForNote, itemForNote, activityPubMarkdownPipeline),
                ActivityPubDeliveryUtils.ResolveActorInboxAsync,
                (inboxUrl, actorUrl, activityPayload, successLogMessage) =>
                    ActivityPubDeliveryUtils.SendSignedActivityPubMessageAsync(inboxUrl, actorUrl, activityPayload, successLogMessage, activityPubSigningKey));
        }
    }

    bool itemIdChanged = moditem.Id != requestedItemId;
    string clientMessage = itemIdChanged
        ? $"item updated; canonical id changed from {requestedItemId} to {moditem.Id}"
        : "item update saved";
    return OkPayloadWithTrace(fn, new
    {
        id = moditem.Id,
        previousId = requestedItemId,
        itemIdChanged,
        idRotationApplied = rotatedItemIdOnVisibilityDrop,
        message = clientMessage
    }, clientMessage);
    
}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner,contributor"
});


app.MapDelete("/items/{id:int}", async (int id,
        GeFeSLEDb db,
        UserManager<GeFeSLEUser> userManager,
        HttpContext httpContext) =>
{
    var fn = $"/items/{id} (DELETE)"; DBg.d(LogLevel.Trace, $"{fn}");
    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    // check if list owner is owner of, or can modify THIS list
    var delitem = await db.Items.FindAsync(id);
    if (delitem is null) {
        DBg.d(LogLevel.Error, $"{fn} -- item not found");
        return NotFoundNoMessageWithTrace(fn, $"item {id} not found");
    }
    var list = await db.Lists
        .Include(l => l.Creator)
        .Include(l => l.ListOwners)
        .Include(l => l.Contributors)
        .FirstOrDefaultAsync(l => l.Id == delitem.ListId);
    if (list is null) {
        DBg.d(LogLevel.Error, $"{fn} -- list not found");
        string msg = $"List {delitem.ListId} not found";
        return NotFoundWithTrace(fn, msg);
    }
    (bool canModify, string? ynot) = list.IsUserAllowedToModify(user);
    if (!canModify)    {
        string reason = $"Cannot delete item. No modify permissions on list (id {list.Id}, name {list.Name}): {ynot}.";
        DBg.d(LogLevel.Error, $"{fn} -- {reason}");
        return BadRequestWithTrace(fn, reason);
    }
    else {
        if (delitem.IsDeleted)
        {
            return OkWithTrace(fn, "item already deleted");
        }

        delitem.IsDeleted = true;
        delitem.ModifiedDate = DateTime.Now;
        await db.SaveChangesAsync();

        await ActivityPubBroadcastService.BroadcastActivityPubItemToFollowersAsync(
            list,
            db,
            delitem,
            "Delete",
            (listForNote, itemForNote) => ActivityPubPayloadFactory.BuildActivityPubItemNote(listForNote, itemForNote, activityPubMarkdownPipeline),
            ActivityPubDeliveryUtils.ResolveActorInboxAsync,
            (inboxUrl, actorUrl, activityPayload, successLogMessage) =>
                ActivityPubDeliveryUtils.SendSignedActivityPubMessageAsync(inboxUrl, actorUrl, activityPayload, successLogMessage, activityPubSigningKey));
        
        await list.RegenerateAllFiles(db);
        DBg.d(LogLevel.Information, $"{fn} -- item deleted successfully");
        return OkWithTrace(fn, $"item {id} deleted");
    }
})
.WithEndpointDocs("items.id.delete")
.RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});

app.MapDelete("/items/{itemid:int}/comments/{commentid:int}", async (
    int itemid,
    int commentid,
    GeFeSLEDb db,
    UserManager<GeFeSLEUser> userManager,
    HttpContext httpContext) =>
{
    string fn = $"/items/{itemid}/comments/{commentid} (DELETE)"; DBg.d(LogLevel.Trace, fn);
    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);

    GeListItemComment? comment = await db.ItemComments.FirstOrDefaultAsync(c => c.Id == commentid);
    if (comment is null)
    {
        string msg = $"Comment {commentid} not found";
        return NotFoundWithTrace(fn, msg);
    }

    if (comment.ItemId != itemid)
    {
        string msg = $"Comment {commentid} does not belong to item {itemid}";
        return BadRequestWithTrace(fn, msg);
    }

    GeList? list = await db.Lists
        .Include(l => l.Creator)
        .Include(l => l.ListOwners)
        .Include(l => l.Contributors)
        .FirstOrDefaultAsync(l => l.Id == comment.ListId);
    if (list is null)
    {
        string msg = $"List {comment.ListId} not found for comment {commentid}";
        return NotFoundWithTrace(fn, msg);
    }

    (bool canModify, string? ynot) = list.IsUserAllowedToModify(user);
    if (!canModify)
    {
        string msg = $"Cannot delete comment. No modify permissions on list (id {list.Id}, name {list.Name}): {ynot}.";
        return BadRequestWithTrace(fn, msg);
    }

    // Tombstone in place to preserve thread topology and prevent resurrection by later remote updates.
    comment.ActorIri = null;
    comment.AttributedToIri = null;
    comment.Name = null;
    comment.ContentHtml = null;
    comment.Summary = "<comment deleted>";
    comment.RawNoteJson = null;
    comment.UpdatedAt = DateTimeOffset.UtcNow;
    comment.ModifiedDate = DateTime.UtcNow;
    comment.LastReceivedAt = DateTime.UtcNow;

    await db.SaveChangesAsync();
    await list.GenerateHTMLListPage(db);

    await ActivityPubBroadcastService.BroadcastActivityPubCommentAnnounceToFollowersAsync(
        list,
        db,
        comment.RemoteObjectIri,
        $"Deleted remote comment for list item {comment.ItemId}",
        ActivityPubDeliveryUtils.ResolveActorInboxAsync,
        (inboxUrl, actorUrl, activityPayload, successLogMessage) =>
            ActivityPubDeliveryUtils.SendSignedActivityPubMessageAsync(inboxUrl, actorUrl, activityPayload, successLogMessage, activityPubSigningKey),
        null);

    return OkPayloadWithTrace(fn, new
    {
        itemId = comment.ItemId,
        commentId = comment.Id,
        remoteObject = comment.RemoteObjectIri,
        tombstoned = true
    }, $"Comment {commentid} tombstoned");
})
.WithEndpointDocs("items.itemid.comments.commentid.delete")
.RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme
});

// moves an item between two lists by patching only its list id
app.MapPatch("/items/{id:int}/list", async (
    int id,
    [FromBody] MoveItemDto data,
    GeFeSLEDb db,
    UserManager<GeFeSLEUser> userManager,
    HttpContext httpContext) =>
{
    string fn = $"/items/{id}/list (PATCH)"; DBg.d(LogLevel.Trace, fn);
    LogDtoIn(fn, nameof(MoveItemDto), data);

    var newlistid = data.listid;
    DBg.d(LogLevel.Trace, $"{fn} <-- {{ itemid: {id}, newlistid: {newlistid}}}");

    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    if (user is null)
    {
        return BadRequestWithTrace(fn, "Could not determine who you are");
    }

    var item = await db.Items.FindAsync(id);
    if (item is null)
    {
        string errMsg = $"Item {id} not found";
        return NotFoundWithTrace(fn, errMsg);
    }

    var oldlistid = item.ListId;
    var oldlist = await db.Lists
        .Include(l => l.Creator)
        .Include(l => l.ListOwners)
        .Include(l => l.Contributors)
        .FirstOrDefaultAsync(l => l.Id == oldlistid);
    if (oldlist is null)
    {
        string errMsg = $"Source list {oldlistid} not found";
        return NotFoundWithTrace(fn, errMsg);
    }

    var newlist = await db.Lists
        .Include(l => l.Creator)
        .Include(l => l.ListOwners)
        .Include(l => l.Contributors)
        .FirstOrDefaultAsync(l => l.Id == newlistid);
    if (newlist is null)
    {
        string errMsg = $"Destination list {newlistid} not found";
        return NotFoundWithTrace(fn, errMsg);
    }

    if (oldlistid == newlistid)
    {
        return OkPayloadWithTrace(fn, $"Item {id} already in list {newlistid}", $"item {id} already in list {newlistid}");
    }

    (bool canModifyOld, string? ynotOld) = oldlist.IsUserAllowedToModify(user);
    (bool canModifyNew, string? ynotNew) = newlist.IsUserAllowedToModify(user);
    if (!canModifyOld || !canModifyNew)
    {
        string reason = "Cannot move item. ";
        if (!canModifyOld)
        {
            reason += $"No modify permissions on source list (id {oldlist.Id}, name {oldlist.Name}): {ynotOld}. ";
        }
        if (!canModifyNew)
        {
            reason += $"No modify permissions on destination list (id {newlist.Id}, name {newlist.Name}): {ynotNew}.";
        }
        return BadRequestWithTrace(fn, reason);
    }

    item.ListId = newlistid;
    item.ModifiedDate = DateTime.Now;

    await db.SaveChangesAsync();

    await newlist.RegenerateAllFiles(db);
    await oldlist.RegenerateAllFiles(db);

    await ActivityPubBroadcastService.BroadcastMovedItemToFollowersAsync(
        oldlist,
        newlist,
        db,
        item,
        (listForBroadcast, dbForBroadcast, itemForBroadcast, activityTypeForBroadcast, onlyFollowerForBroadcast) =>
            ActivityPubBroadcastService.BroadcastActivityPubItemToFollowersAsync(
                listForBroadcast,
                dbForBroadcast,
                itemForBroadcast,
                activityTypeForBroadcast,
                (listForNote, itemForNote) => ActivityPubPayloadFactory.BuildActivityPubItemNote(listForNote, itemForNote, activityPubMarkdownPipeline),
                ActivityPubDeliveryUtils.ResolveActorInboxAsync,
                (inboxUrl, actorUrl, activityPayload, successLogMessage) =>
                    ActivityPubDeliveryUtils.SendSignedActivityPubMessageAsync(inboxUrl, actorUrl, activityPayload, successLogMessage, activityPubSigningKey),
                onlyFollowerForBroadcast));

    var msg = $"Item {id} moved from list {oldlistid} to list {newlistid}";
    return OkPayloadWithTrace(fn, msg, msg);
})
.WithEndpointDocs("items.id.list.patch")
.RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner,contributor"
});

// remove a tag from an item
app.MapDelete("/items/{itemid:int}/tags/{tag}", async (
    int itemid,
    string tag,
    GeFeSLEDb db,
    UserManager<GeFeSLEUser> userManager,
    HttpContext httpContext) =>
{
    var fn = $"/items/{itemid}/tags/{tag} (DELETE)"; DBg.d(LogLevel.Trace, fn);

    var gonetag = tag;
    DBg.d(LogLevel.Trace, $"{fn} <-- {{ itemid: {itemid}, tag: {gonetag}}}");


    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);

    // find the item in question
    var item = await db.Items.FindAsync(itemid);
    if (item is null)
    {
        string errMsg = $"Item {itemid} not found";
        return NotFoundWithTrace(fn, errMsg);
    }

    // now the tricky part - does the user have right listowner or contributor-ship or role to modify 
    // TODO: implement permissions check; for now rely on SU/listowner roles via middleware 

    // if gonetag is in the item's tags remove it. if not, we don't care
    item.Tags.Remove(gonetag);

    await db.SaveChangesAsync();
    
    // Regenerate the item's list to update HTML/RSS/JSON files and index
    var list = await db.Lists.FindAsync(item.ListId);
    if (list is not null)
    {
        await list.RegenerateAllFiles(db);

        await ActivityPubBroadcastService.BroadcastActivityPubItemToFollowersAsync(
            list,
            db,
            item,
            "Update",
            (listForNote, itemForNote) => ActivityPubPayloadFactory.BuildActivityPubItemNote(listForNote, itemForNote, activityPubMarkdownPipeline),
            ActivityPubDeliveryUtils.ResolveActorInboxAsync,
            (inboxUrl, actorUrl, activityPayload, successLogMessage) =>
                ActivityPubDeliveryUtils.SendSignedActivityPubMessageAsync(inboxUrl, actorUrl, activityPayload, successLogMessage, activityPubSigningKey));
    }

    var msg = $"Tag {gonetag} removed from item {itemid}";
    return OkPayloadWithTrace(fn, msg, msg);
})
.WithEndpointDocs("items.itemid.tags.tag.delete")
.RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner,contributor"
});

app.MapPut("/items/{itemid:int}/tags", async (
    int itemid,
    [FromBody] AddTagDto data,
    GeFeSLEDb db,
    UserManager<GeFeSLEUser> userManager,
    HttpContext httpContext) =>
{
    string fn = $"/items/{itemid}/tags (PUT)"; DBg.d(LogLevel.Trace, fn);
    LogDtoIn(fn, nameof(AddTagDto), data);

    var newtag = data.tag;
    DBg.d(LogLevel.Trace, $"{fn} <-- {{ itemid: {itemid}, tag: {newtag}}}");


    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);

    // find the item in question
    var item = await db.Items.FindAsync(itemid);
    if (item is null)
    {
        string errMsg = $"Item {itemid} not found";
        return NotFoundWithTrace(fn, errMsg);
    }

    // now the tricky part - does the user have right listowner or contributor-ship or role to modify 
    // TODO: implement permissions check; for now rely on SU/listowner roles via middleware 

    // Validate that the tag is not null, empty, or whitespace-only
    if (string.IsNullOrWhiteSpace(newtag))
    {
        return BadRequestWithTrace(fn, "Tag cannot be empty or whitespace-only");
    }

    // Helper function to parse quoted tags
    static List<string> ParseQuotedTags(string input)
    {
        var tags = new List<string>();
        var currentTag = new StringBuilder();
        bool inQuotes = false;
        bool escaped = false;

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];

            if (escaped)
            {
                currentTag.Append(c);
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                if (inQuotes)
                {
                    // End of quoted section
                    var tag = currentTag.ToString().Trim();
                    if (!string.IsNullOrEmpty(tag))
                    {
                        tags.Add(tag);
                    }
                    currentTag.Clear();
                    inQuotes = false;
                }
                else
                {
                    // Start of quoted section - save any accumulated unquoted tag first
                    var tag = currentTag.ToString().Trim();
                    if (!string.IsNullOrEmpty(tag))
                    {
                        tags.Add(tag);
                    }
                    currentTag.Clear();
                    inQuotes = true;
                }
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                // Space outside quotes - end current tag
                var tag = currentTag.ToString().Trim();
                if (!string.IsNullOrEmpty(tag))
                {
                    tags.Add(tag);
                }
                currentTag.Clear();
            }
            else
            {
                currentTag.Append(c);
            }
        }

        // Add any remaining tag
        var finalTag = currentTag.ToString().Trim();
        if (!string.IsNullOrEmpty(finalTag))
        {
            tags.Add(finalTag);
        }

        return tags;
    }

    // Parse the input tag(s) - might contain multiple space-separated or quoted tags
    var tagsToAdd = ParseQuotedTags(newtag);
    
    if (tagsToAdd.Count == 0)
    {
        return BadRequestWithTrace(fn, "No valid tags found");
    }

    var addedTags = new List<string>();
    var existingTags = new List<string>();

    foreach (var tagToAdd in tagsToAdd)
    {
        // Skip empty or whitespace-only tags
        if (string.IsNullOrWhiteSpace(tagToAdd))
            continue;

        var trimmedTag = tagToAdd.Trim();
        
        // Check if tag already exists
        if (!item.Tags.Contains(trimmedTag))
        {
            item.Tags.Add(trimmedTag);
            addedTags.Add(trimmedTag);
        }
        else
        {
            existingTags.Add(trimmedTag);
        }
    }

    string? msg = null;
    if (addedTags.Count > 0 && existingTags.Count > 0)
    {
        msg = $"Added tags: {string.Join(", ", addedTags)}. Already existed: {string.Join(", ", existingTags)}";
    }
    else if (addedTags.Count > 0)
    {
        msg = $"Added tags: {string.Join(", ", addedTags)}";
    }
    else
    {
        msg = $"All tags already exist: {string.Join(", ", existingTags)}";
    }
    await db.SaveChangesAsync();
    
    // Regenerate the item's list to update HTML/RSS/JSON files and index
    var list = await db.Lists.FindAsync(item.ListId);
    if (list is not null)
    {
        await list.RegenerateAllFiles(db);

        await ActivityPubBroadcastService.BroadcastActivityPubItemToFollowersAsync(
            list,
            db,
            item,
            "Update",
            (listForNote, itemForNote) => ActivityPubPayloadFactory.BuildActivityPubItemNote(listForNote, itemForNote, activityPubMarkdownPipeline),
            ActivityPubDeliveryUtils.ResolveActorInboxAsync,
            (inboxUrl, actorUrl, activityPayload, successLogMessage) =>
                ActivityPubDeliveryUtils.SendSignedActivityPubMessageAsync(inboxUrl, actorUrl, activityPayload, successLogMessage, activityPubSigningKey));
    }

    return OkPayloadWithTrace(fn, msg, msg);
}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner,contributor"
});


// add an endpoint that DELETEs a list
app.MapDelete("/lists/{id:int}", async (int id,
        GeFeSLEDb db,
        UserManager<GeFeSLEUser> userManager,
        HttpContext httpContext,
        GeListController geListController) =>
{
    await geListController.ListsDelete(httpContext, id);
})
.WithEndpointDocs("lists.id.delete")
.RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser, listowner"
});

// add and endpoint that regenerates the html page for all lists
app.MapGet("/lists/regen", async (GeFeSLEDb db,
        UserManager<GeFeSLEUser> userManager,
        HttpContext httpContext) =>
{
    string fn = "/lists/regen (GET)"; DBg.d(LogLevel.Trace, fn);
    var referer = httpContext.Request.Headers["Referer"].ToString();
    if (string.IsNullOrEmpty(referer)) referer = "/index.html";
    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    // add check for if listowner is owner of THIS list

    var lists = await db.Lists.ToListAsync();
    foreach (var list in lists)
    {
        await list.RegenerateAllFiles(db);
    }

    return RedirectWithTrace(fn, referer, "redirecting to referer");
})
.WithEndpointDocs("lists.regen.get")
.RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});

// add an endpoint that regenerates the html page for a list
app.MapGet("/lists/{listid}/regen", async (int listid,
        GeFeSLEDb db,
        UserManager<GeFeSLEUser> userManager,
        HttpContext httpContext) =>
{
    string fn = $"/lists/{listid}/regen (GET)"; DBg.d(LogLevel.Trace, fn);
    var referer = httpContext.Request.Headers["Referer"].ToString();
    if (string.IsNullOrEmpty(referer)) referer = "/index.html";
    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    // add check for if contributor is contributor of THIS list
    // add check for if listowner is owner of THIS list

    // find the list for this id
    var list = await db.Lists
        .Include(l => l.Creator)
        .Include(l => l.ListOwners)
        .Include(l => l.Contributors)
        .FirstOrDefaultAsync(l => l.Id == listid);
    if (list is null) return NotFoundNoMessageWithTrace(fn, $"list {listid} not found");
    else
    {
        await list.RegenerateAllFiles(db);
        return RedirectWithTrace(fn, referer, "redirecting to referer");
    }
})
.WithEndpointDocs("lists.listid.regen.get")
.RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner,contributor"
});

async Task<string> ResolveEffectiveRoleAsync(GeFeSLEUser user, UserManager<GeFeSLEUser> userManager, GeFeSLEDb db)
{
    var roles = await userManager.GetRolesAsync(user);
    var realizedRole = GlobalStatic.FindHighestRole(roles);
    if (!string.Equals(realizedRole, "anonymous", StringComparison.OrdinalIgnoreCase))
    {
        return realizedRole;
    }

    bool isListOwnerLike = await db.Lists.AnyAsync(l =>
        l.CreatorId == user.Id
        || l.ListOwners.Any(owner => owner.Id == user.Id));
    if (isListOwnerLike)
    {
        return "listowner";
    }

    bool isContributor = await db.Lists.AnyAsync(l =>
        l.Contributors.Any(contributor => contributor.Id == user.Id));
    if (isContributor)
    {
        return "contributor";
    }

    return realizedRole;
}

async Task<GeFeSLEUser?> ResolveOAuthUserAsync(ClaimsPrincipal claimsPrincipal, UserManager<GeFeSLEUser> userManager)
{
    var candidates = new List<string?>
    {
        claimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier),
        claimsPrincipal.FindFirstValue("sub"),
        claimsPrincipal.FindFirstValue("preferred_username"),
        claimsPrincipal.FindFirstValue(ClaimTypes.Name),
        claimsPrincipal.FindFirstValue(ClaimTypes.Email),
        claimsPrincipal.Identity?.Name
    }
    .Where(v => !string.IsNullOrWhiteSpace(v))
    .Select(v => v!.Trim())
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToList();

    foreach (var value in candidates)
    {
        var byId = await userManager.FindByIdAsync(value);
        if (byId is not null)
        {
            return byId;
        }

        var byName = await userManager.FindByNameAsync(value.ToUpperInvariant());
        if (byName is not null)
        {
            return byName;
        }
    }

    foreach (var value in candidates)
    {
        var byEmail = await userManager.Users
            .FirstOrDefaultAsync(u => u.Email != null && u.Email.ToUpper() == value.ToUpper());
        if (byEmail is not null)
        {
            return byEmail;
        }
    }

    return null;
}

app.MapGet("/oauthcallback", async (HttpContext context,
        GeFeSLEDb db,
        UserManager<GeFeSLEUser> userManager,
        RoleManager<IdentityRole> roleManager
        ) =>
{
    string fn = "/oauthcallback (GET)"; DBg.d(LogLevel.Trace, fn);
    StringBuilder sb = new StringBuilder();
    var msg = "";
    var auth = await context.AuthenticateAsync(IdentityConstants.ExternalScheme);
    // look for auth success
    if (!auth.Succeeded)
    {
        msg = $"External OAuth authentication error: {auth.Failure?.Message}";
        await GlobalStatic.GenerateUnAuthPage(sb, msg);
        return ContentWithTrace(fn, sb.ToString(), "text/html", "OAuth callback page generated");
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
        return ContentWithTrace(fn, sb.ToString(), "text/html", "OAuth callback page generated");
    }
    // find the user by email in our database
    // TODO: the user should have been "granted" with both username and email in our system the same
    //       Modify this so it checks for the OAuth user by either username OR email (or any other 
    //      identifying info? dunno !?)
    GeFeSLEUser? user = await ResolveOAuthUserAsync(claimsPrincipal, userManager);
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
        realizedRole = await ResolveEffectiveRoleAsync(user, userManager, db);
        DBg.d(LogLevel.Information, $"{fn} OAuth mapped user {user.Id}/{user.UserName} role resolved to {realizedRole}");
        msg = $"Welcome {username}! You are logged in as {realizedRole}";

    }
    await UserSessionService.createSession(context, user?.Id ?? "OAuth", username ?? "OAuth", realizedRole ?? "anonymous");
    UserSessionService.storeProvider(context, provider!);
    UserSessionService.AddAccessToken(context, provider!, accessToken!);
    await GlobalStatic.GenerateLoginResult(sb, msg);
    return ContentWithTrace(fn, sb.ToString(), "text/html", "OAuth callback page generated");
})
.WithEndpointDocs("oauthcallback.get");

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
    string fn = "/me (POST)"; DBg.d(LogLevel.Trace, fn);
    if (!context.Request.HasFormContentType)
    {
        return BadRequestWithTrace(fn, "No POST form data.");
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
        return BadRequestWithTrace(fn, "Could not deserialize POST form body to LoginDto");
    }
    LogDtoIn(fn, nameof(LoginDto), login);

    if (string.IsNullOrEmpty(login.OAuthProvider))
    {
        // check the request headers to see if this is coming from the javascript API
        bool isJSApi = false;
        if (GlobalStatic.IsAPIRequest(context.Request))
        {
            DBg.d(LogLevel.Trace, $"Is JS API");
            isJSApi = true;
        }
        StringBuilder sb = new StringBuilder();
        GeFeSLEUser? user = null;
        string msg = null;
        bool success = false;
        string? realizedRole = null;
        // not OAuth, so must be a local login. MUST have login+pwd
        if (string.IsNullOrEmpty(login.Username) || string.IsNullOrEmpty(login.Password))
        {
            msg = $"Username or password is null.";
            DBg.d(LogLevel.Trace, msg);
        }
        else
        {
            // find the user in our userManager by username
            user = await userManager.FindByNameAsync(login.Username.ToUpper());
            if (user is null)
            {
                msg = $"Username not found in database.";
                DBg.d(LogLevel.Trace, msg);
            } // user not in db
            else
            {
                var result = await userManager.CheckPasswordAsync(user, login.Password);
                if (result)
                {
                    realizedRole = await ResolveEffectiveRoleAsync(user, userManager, db);
                    success = true;


                } // good user pwd
                else
                {
                    msg = $"LOGIN: Username {user} PASSWORD NOT CORRECT.";
                    // bad login web

                } // bad user pwd
            } // user IN db
        }
        // ----- return login results
        if (!success)
        {
            if (isJSApi)
            {
                DBg.d(LogLevel.Trace, $"LOGIN: BAD - RETURNING 401");
                return UnauthorizedWithTrace(fn, msg ?? "Unauthorized");
            } // bad login -API
            else
            {
                DBg.d(LogLevel.Trace, $"LOGIN: BAD - RETURNING UNAUTH PAGE");
                await GlobalStatic.GenerateUnAuthPage(sb, msg ?? "Unauthorized");
                return ContentWithTrace(fn, sb.ToString(), "text/html", "login page generated");
            } // bad login - web
        }
        else
        {
            if (isJSApi)
            {
                DBg.d(LogLevel.Trace, $"--1784: userid: {user!.Id ?? "no userid"}, username: {user.UserName ?? "no username"}, role: {realizedRole ?? "no role"}");
                var token = UserSessionService.createJWToken(user!.Id ?? "OAuth", user.UserName ?? "OAuth", realizedRole ?? "anonymous");
                DBg.d(LogLevel.Trace, $"LOGIN: User {login.Username} logged in as {realizedRole} VIA API RETURNING 200 + TOKEN");
                await UserSessionService.createSession(context, user!.Id ?? "OAuth", user.UserName ?? "OAuth", realizedRole ?? "anonymous");
                _ = UserSessionService.UpdateSessionAccessTime(context, db, userManager);
                var antiForgeryTokens = antiforgery.GetAndStoreTokens(context);
                var loginResultDto = new
                {
                    username = login.Username,
                    role = realizedRole,
                    antiForgeryToken = antiForgeryTokens.RequestToken,
                    antiForgeryHeaderName = antiForgeryTokens.HeaderName
                };
                LogDtoOut(fn, "LoginResultDto", loginResultDto);
                return OkPayloadWithTrace(fn, loginResultDto, "login result returned");
            } // good login -API
            else
            {
                await UserSessionService.createSession(context, user!.Id ?? "OAuth", user.UserName ?? "OAuth", realizedRole ?? "anonymous");
                _ = UserSessionService.UpdateSessionAccessTime(context, db, userManager);
                _ = antiforgery.GetAndStoreTokens(context);
                DBg.d(LogLevel.Trace, $"LOGIN: OK - RETURNING REDIRECT");
                return RedirectWithTrace(fn, "/", "redirecting after login");
            } // good login - web
        }
    }
    // its OAuth
    DBg.d(LogLevel.Debug, $"login.OAuthProvider: {login.OAuthProvider}");
    AuthenticationProperties properties = new AuthenticationProperties { RedirectUri = $"{GlobalConfig.Hostname}/oauthcallback" };
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
                    return BadRequestWithTrace(fn, "Selected OAuth provider Mastodon but missing Mastodon instance.");
                }
                (bool isUP, string? ynot) = await MastoController.checkInstance(login.Instance);
                if (!isUP)
                {
                    string msg = $"Mastodon instance {login.Instance} is down/unreachable: {ynot}";
                    return BadRequestWithTrace(fn, msg);
                }
                else
                {
                    string instance = ynot; // steal that before we ovewrite it
                    (ApplicationToken? appToken, ynot) = await MastoController.registerAppWithInstance(instance);
                    if (appToken is null)
                    {
                        string msg = $"Could not register {GlobalStatic.applicationName} with {instance}: {ynot}";
                        return BadRequestWithTrace(fn, msg);
                    }
                    else
                    {
                        string authorizationUrl = MastoController.getMastodonOAuthUrl(appToken);
                        if (authorizationUrl is null)
                        {
                            string msg = $"Could not get authorization URL with this appToken: {appToken}";
                            return BadRequestWithTrace(fn, msg);
                        }
                        else
                        {
                            // store the appToken in the session cookie
                            MastoController.storeMastoToken(context, appToken);
                            return RedirectWithTrace(fn, authorizationUrl, $"redirecting to {authorizationUrl}");
                        }
                    }
                }
            }

        default:
            {
                return BadRequestWithTrace(fn, "Unknown OAuth provider");

            }

    }
    DBg.d(LogLevel.Trace, $"{authorizationScheme} OAuth - sending {properties.RedirectUri} challenge");
    return new CustomChallengeResult(authorizationScheme, properties);
})
.WithEndpointDocs("me.post");

// new endpoint that handles the Mastodon Oauth2 callback
app.MapGet("/mastocallback", async (string code,
    HttpContext httpContext,
    GeFeSLEDb db,

    UserManager<GeFeSLEUser> userManager,
    RoleManager<IdentityRole> roleManager) =>
{
    string fn = "/mastocallback (GET)"; DBg.d(LogLevel.Trace, fn);
    DBg.d(LogLevel.Trace, $"code: {code}");

    // now finally we have the code, we can use it to get the access token

    // retrieve the application token from the session cookie
    ApplicationToken? appToken = MastoController.getMastoToken(httpContext);


    if (appToken is null)
    {
        return BadRequestWithTrace(fn, "BAD/MISSING Mastodon parameters in session cookie - dunno, did you forget to _login.html -> /mastoconnect -> /mastologin?");
    }
    if (string.IsNullOrEmpty(GlobalConfig.mastoScopes))
    {
        return BadRequestWithTrace(fn, "BAD/MISSING Mastodon scopes in config"); // should never be null due to default value
    }

    string redirectUri = Uri.EscapeDataString($"{GlobalConfig.Hostname}/mastocallback");

    string grantType = "authorization_code";
    string tokenUrl = $"{appToken.instance}/oauth/token";
    string postData = $"client_id={appToken.client_id}&client_secret={appToken.client_secret}&grant_type={grantType}&code={code}&redirect_uri={redirectUri}&scope={GlobalConfig.mastoScopes}";
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
        string msg = $"Mastodon instance {appToken.instance} returned 422: {error} - Sent: {postData}";
        return BadRequestWithTrace(fn, msg);
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
        string credentialsUrl = $"{appToken.instance}/api/v1/accounts/verify_credentials";

        // handle this better
        if (token is null)
        {
            return UnauthorizedWithTrace(fn, "Mastodon token is null");
        }

        // create httpClient
        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + accessToken);
        response = await client.GetAsync(credentialsUrl);
        if (response.StatusCode != System.Net.HttpStatusCode.OK)
        {
            var error = await response.Content.ReadAsStringAsync();
            string msg = $"Mastodon instance {appToken.instance} returned  422: {error} - requested {credentialsUrl}";
            return BadRequestWithTrace(fn, msg);
        }
        else
        {
            // cast response.Content to a Mastonet.Entities.CredentialAccount object

            var newcontent = await response.Content.ReadAsStringAsync();
            var account = JsonConvert.DeserializeObject<Account>(newcontent);
            if (account is null)
            {
                return BadRequestWithTrace(fn, "Mastodon instance returned null account object");
            }

            // dump out the account object
            var accountDump = JsonConvert.SerializeObject(account, Formatting.Indented);
            DBg.d(LogLevel.Trace, $"account: {accountDump}");
            //return Results.Content($"<!DOCTYPE html><html><body><pre>{accountDump}</pre></body></html>", "text/html");

            // the username that WE will use will be their username on the mastodon instance
            // if the instance has http:// or https:// in it, strip it out
            var instancename = appToken.instance.Replace("http://", "").Replace("https://", "");
            var username = $"{account.UserName}@{instancename}";
            DBg.d(LogLevel.Trace, $"username: {username}");
            // look this username up in the database, tolerating either handle form:
            //   user@instance
            //   @user@instance
            string atUsername = username.StartsWith("@", StringComparison.Ordinal) ? username : $"@{username}";
            var handleCandidates = new[] { username, atUsername }
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            GeFeSLEUser? localuser = null;
            foreach (var handle in handleCandidates)
            {
                localuser = await userManager.FindByIdAsync(handle);
                if (localuser is not null) break;

                localuser = await userManager.FindByNameAsync(handle.ToUpperInvariant());
                if (localuser is not null) break;
            }

            if (localuser is null)
            {
                foreach (var handle in handleCandidates)
                {
                    localuser = await userManager.Users.FirstOrDefaultAsync(u =>
                        (u.UserName != null && u.UserName.ToUpper() == handle.ToUpper())
                        || (u.Email != null && u.Email.ToUpper() == handle.ToUpper())
                        || (u.Id != null && u.Id.ToUpper() == handle.ToUpper()));
                    if (localuser is not null) break;
                }
            }

            DBg.d(LogLevel.Trace, $"{fn} resolved localuser: {(localuser is null ? "(none)" : $"{localuser.Id}/{localuser.UserName}")}");
            // if they're not in there, that's fine. Add them, they can have 
            // anonymous role. Not sure why they're logging in tho
            StringBuilder sb = new StringBuilder();

            if (localuser is null)
            {
                await UserSessionService.createSession(httpContext, username!, username!, "anonymous");
                var msg = $"Hi {username} from the fediverse; You've been logged in with role: anonymous.";
                await GlobalStatic.GenerateLoginResult(sb, msg);
                return ContentWithTrace(fn, sb.ToString(), "text/html", "Mastodon callback page generated");
            }
            else
            {
                // they're in there, which means we've added them, probably to assign them a role
                var realizedRole = await ResolveEffectiveRoleAsync(localuser, userManager, db);
                await UserSessionService.createSession(httpContext, localuser.Id, localuser.UserName!, realizedRole);
                var msg = $"Hi {username} from the fediverse; You've been logged in with role: {realizedRole}.";
                await GlobalStatic.GenerateLoginResult(sb, msg);
                return ContentWithTrace(fn, sb.ToString(), "text/html", "Mastodon callback page generated");
            }


        }

    }
});


app.MapPost("/lists/{listid:int}", async Task<IResult> (HttpContext httpContext) =>
{
    int listid = int.Parse(httpContext.Request.RouteValues["listid"].ToString());
    GeListImportDto importListDto = await httpContext.Request.ReadFromJsonAsync<GeListImportDto>();

    var db = httpContext.RequestServices.GetRequiredService<GeFeSLEDb>();
    var userManager = httpContext.RequestServices.GetRequiredService<UserManager<GeFeSLEUser>>();
    var geListController = httpContext.RequestServices.GetRequiredService<GeListController>();

    string fn = $"/lists/{listid} (POST)"; DBg.d(LogLevel.Trace, fn);
    LogDtoIn(fn, nameof(GeListImportDto), importListDto);

    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    // get session user
    var sessionUser = UserSessionService.amILoggedIn(httpContext);
    // obtain the target list - if it doesn't exist return 404 list not found
    var list = await db.Lists
        .Include(l => l.Creator)
        .Include(l => l.ListOwners)
        .Include(l => l.Contributors)
        .FirstOrDefaultAsync(l => l.Id == listid);
    if (list is null)
    {
        string msg = $"List {listid} not found.";
        return NotFoundWithTrace(fn, msg);
    }
    // is the user allowed to modify this list? 
    (bool canMod, string? ynot) = list.IsUserAllowedToModify(user);
    if (!canMod && sessionUser.Role != "SuperUser")
    {
        string msg = ynot ?? "Cannot modify target list";
        return BadRequestWithTrace(fn, msg); // TODO: return a proper 403
    }
    else
    {
        if (!canMod && sessionUser.Role == "SuperUser")
        {
            DBg.d(LogLevel.Warning, $"{fn} SuperUser bypassed list permissions for {list.Name}");
        }
        return await Task.FromResult<IResult>(await geListController.ListImport(httpContext, importListDto, list, user)); // Change return type to Task<IResult>
    }

})
.WithEndpointDocs("lists.listid.post")
.RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser, listowner, contributor"
});

app.MapPost("/lists/query", async (
    GeListImportDto importListDto,
    GeFeSLEDb db,
    UserManager<GeFeSLEUser> userManager,
    HttpContext httpContext,
    GeListController geListController) =>
{
    string fn = "/lists/query (POST)"; DBg.d(LogLevel.Trace, fn);
    LogDtoIn(fn, nameof(GeListImportDto), importListDto);
    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    importListDto.Data = null;
    return await geListController.ListImport(httpContext, importListDto, null, user);
})
.WithEndpointDocs("lists.query.post")
.RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser, listowner, contributor"
});


app.MapGet("/me", async (HttpContext httpContext,
    IAntiforgery antiforgery,
    GeFeSLEDb db,
    UserManager<GeFeSLEUser> userManager) =>
{
    string fn = "/me (GET)"; DBg.d(LogLevel.Trace, fn);
    //GlobalStatic.DumpHTTPRequestHeaders(httpContext.Request);
    if (GlobalStatic.IsAPIRequest(httpContext.Request))
    {
        DBg.d(LogLevel.Trace, $"Is JS API");
    }
    else
    {
        DBg.d(LogLevel.Trace, $"{fn} Web request");
    }

    UserDto sessionUser = UserSessionService.amILoggedIn(httpContext);

    // Auto-heal authenticated sessions stuck with anonymous role by recomputing effective role.
    if (sessionUser.IsAuthenticated
        && string.Equals(sessionUser.Role, "anonymous", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(sessionUser.Id))
    {
        GeFeSLEUser? dbUser = await userManager.FindByIdAsync(sessionUser.Id);
        if (dbUser is null && !string.IsNullOrWhiteSpace(sessionUser.UserName))
        {
            dbUser = await userManager.FindByNameAsync(sessionUser.UserName.ToUpperInvariant());
        }
        if (dbUser is null && !string.IsNullOrWhiteSpace(sessionUser.UserName) && sessionUser.UserName.Contains('@'))
        {
            dbUser = await userManager.FindByEmailAsync(sessionUser.UserName);
        }
        if (dbUser is null && !string.IsNullOrWhiteSpace(sessionUser.UserName))
        {
            dbUser = await userManager.Users
                .FirstOrDefaultAsync(u =>
                    (u.UserName != null && u.UserName.ToUpper() == sessionUser.UserName.ToUpper())
                    || (u.Email != null && u.Email.ToUpper() == sessionUser.UserName.ToUpper()));
        }
        if (dbUser is not null)
        {
            var effectiveRole = await ResolveEffectiveRoleAsync(dbUser, userManager, db);
            if (!string.Equals(effectiveRole, "anonymous", StringComparison.OrdinalIgnoreCase))
            {
                await UserSessionService.createSession(
                    httpContext,
                    dbUser.Id,
                    dbUser.UserName ?? sessionUser.UserName ?? dbUser.Email ?? "OAuth",
                    effectiveRole);
                sessionUser = UserSessionService.amILoggedIn(httpContext);
            }
        }
    }

    
    if (sessionUser.IsAuthenticated)
    {
        var antiForgeryTokens = antiforgery.GetAndStoreTokens(httpContext);
        var meAuthenticatedDto = new
        {
            sessionUser.Id,
            sessionUser.UserName,
            sessionUser.Role,
            sessionUser.IsAuthenticated,
            antiForgeryToken = antiForgeryTokens.RequestToken,
            antiForgeryHeaderName = antiForgeryTokens.HeaderName
        };
        LogDtoOut(fn, "MeAuthenticatedDto", meAuthenticatedDto);
        return OkPayloadWithTrace(fn, meAuthenticatedDto, "authenticated session details returned");
    }

    LogDtoOut(fn, nameof(UserDto), sessionUser);
    return OkPayloadWithTrace(fn, sessionUser, "anonymous session details returned");
})
.WithEndpointDocs("me.get")
.AllowAnonymous()
.RequireAuthorization(new AuthorizeAttribute
{ AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme });

app.MapGet("/lists/{list:int}/users", async (int list,
        GeFeSLEDb db,

        HttpContext httpContext,
        UserManager<GeFeSLEUser> userManager,
        RoleManager<IdentityRole> roleManager) =>
{
    string fn = $"/lists/{list}/users (GET)"; DBg.d(LogLevel.Trace, fn);
    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);

    // middleware rejects the request if there isn't a listid. see if the list given is a real one
    // can't use .FindAsync because its LAZY and we want all the member List<T> users
    //GeList? list = await db.Lists.FindAsync(listid);
    GeList? listObj = await db.Lists.Include(l => l.Creator)
                                 .Include(l => l.ListOwners)
                                 .Include(l => l.Contributors)
                                 .FirstOrDefaultAsync(l => l.Id == list);
    if (listObj is null)
    {
        return NotFoundNoMessageWithTrace(fn, $"list {list} not found");
    }
    else
    {
        // take the list's .Creator, .ListOwners and .Contributors and return them as json
        var creatorDto = listObj.Creator?.ToSummaryDto();
        var listownersDto = listObj.ListOwners.Select(u => u.ToSummaryDto()).ToList();
        var contributorsDto = listObj.Contributors.Select(u => u.ToSummaryDto()).ToList();
        var result = new GeListUsersDto { Creator = creatorDto, ListOwners = listownersDto, Contributors = contributorsDto };
        LogDtoOut(fn, nameof(GeListUsersDto), result);
        return OkPayloadWithTrace(fn, result, $"list {list} users returned");
    }

}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});

app.MapPost("/lists/{list:int}/owners", async (int list,
    [FromBody] GeFeSLE.DTOs.AddListUserDto requestData,
        GeFeSLEDb db,
        HttpContext httpContext,
        UserManager<GeFeSLEUser> userManager) =>
{
    string fn = $"/lists/{list}/owners (POST)"; DBg.d(LogLevel.Trace, fn);
    LogDtoIn(fn, nameof(AddListUserDto), requestData);

    GeFeSLEUser? me = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);

    if (string.IsNullOrEmpty(requestData.Username))
    {
        var response = new ListUserOperationResponse
        {
            Success = false,
            Message = "username must be specified",
            ListId = list,
            Role = "listowner"
        };
        return BadRequestObjectWithTrace(fn, response, response.Message);
    }

    GeList? targetList = await db.Lists
        .Include(l => l.ListOwners)
        .Include(l => l.Contributors)
        .FirstOrDefaultAsync(l => l.Id == list);

    if (targetList == null)
    {
        var response = new ListUserOperationResponse
        {
            Success = false,
            Message = $"List {list} does not exist",
            ListId = list,
            Username = requestData.Username,
            Role = "listowner"
        };
        return NotFoundObjectWithTrace(fn, response, response.Message);
    }

    string? callerUserName = httpContext.User.Identity?.Name;
    if (string.IsNullOrEmpty(callerUserName))
    {
        var response = new ListUserOperationResponse
        {
            Success = false,
            Message = "Caller is not logged in",
            ListId = list,
            Username = requestData.Username,
            Role = "listowner"
        };
        return BadRequestObjectWithTrace(fn, response, response.Message);
    }

    GeFeSLEUser? caller = await userManager.FindByNameAsync(callerUserName!.ToUpper());
    if (caller == null)
    {
        var response = new ListUserOperationResponse
        {
            Success = false,
            Message = "Caller is not in the database",
            ListId = list,
            Username = requestData.Username,
            Role = "listowner"
        };
        return BadRequestObjectWithTrace(fn, response, response.Message);
    }

    GeFeSLEUser? user = await userManager.FindByNameAsync(requestData.Username.ToUpper());
    if (user == null)
    {
        var response = new ListUserOperationResponse
        {
            Success = false,
            Message = $"User {requestData.Username} does not exist",
            ListId = list,
            Username = requestData.Username,
            Role = "listowner"
        };
        return NotFoundObjectWithTrace(fn, response, response.Message);
    }

    var roles = await userManager.GetRolesAsync(caller);
    var realizedRole = GlobalStatic.FindHighestRole(roles);

    if ((targetList.Creator != caller) && (realizedRole != "SuperUser"))
    {
        string msg = "Only the list's creator or a SuperUser can add a listowner";
        return ProblemWithTrace(fn, msg, 403);
    }

    if (targetList.ListOwners.Contains(user))
    {
        var msg = $"{user.UserName} is already a listowner of {targetList.Name}";
        DBg.d(LogLevel.Information, msg);
        return OkPayloadWithTrace(fn, new ListUserOperationResponse
        {
            Success = true,
            Message = msg,
            Username = requestData.Username,
            ListId = list,
            Role = "listowner"
        }, msg);
    }

    targetList.ListOwners.Add(user);
    await db.SaveChangesAsync();
    await targetList.RegenerateAllFiles(db);
    await ProtectedFiles.RefreshListCacheAsync(db, targetList.Id);
    await ActivityPubBroadcastService.BroadcastActivityPubActorUpdateToFollowersAsync(
        targetList,
        db,
        listForActor => ActivityPubActorFactory.BuildActivityPubListActor(listForActor, activityPubMarkdownPipeline, activityPubPublicKeyPem),
        ActivityPubDeliveryUtils.ResolveActorInboxAsync,
        (inboxUrl, actorUrl, activityPayload, successLogMessage) =>
            ActivityPubDeliveryUtils.SendSignedActivityPubMessageAsync(inboxUrl, actorUrl, activityPayload, successLogMessage, activityPubSigningKey));
    var addedMsg = $"{caller.UserName} Added {user.UserName} to {targetList.Name} as a listowner";
    DBg.d(LogLevel.Information, addedMsg);
    return OkPayloadWithTrace(fn, new ListUserOperationResponse
    {
        Success = true,
        Message = addedMsg,
        Username = requestData.Username,
        ListId = list,
        Role = "listowner"
    }, addedMsg);
})
.RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});

app.MapPost("/lists/{list:int}/contributors", async (int list,
    [FromBody] GeFeSLE.DTOs.AddListUserDto requestData,
        GeFeSLEDb db,
        HttpContext httpContext,
        UserManager<GeFeSLEUser> userManager) =>
{
    string fn = $"/lists/{list}/contributors (POST)"; DBg.d(LogLevel.Trace, fn);
    LogDtoIn(fn, nameof(AddListUserDto), requestData);

    GeFeSLEUser? me = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);

    if (string.IsNullOrEmpty(requestData.Username))
    {
        var response = new ListUserOperationResponse
        {
            Success = false,
            Message = "username must be specified",
            ListId = list,
            Role = "contributor"
        };
        return BadRequestObjectWithTrace(fn, response, response.Message);
    }

    GeList? targetList = await db.Lists
        .Include(l => l.ListOwners)
        .Include(l => l.Contributors)
        .FirstOrDefaultAsync(l => l.Id == list);

    if (targetList == null)
    {
        var response = new ListUserOperationResponse
        {
            Success = false,
            Message = $"List {list} does not exist",
            ListId = list,
            Username = requestData.Username,
            Role = "contributor"
        };
        return NotFoundObjectWithTrace(fn, response, response.Message);
    }

    string? callerUserName = httpContext.User.Identity?.Name;
    if (string.IsNullOrEmpty(callerUserName))
    {
        var response = new ListUserOperationResponse
        {
            Success = false,
            Message = "Caller is not logged in",
            ListId = list,
            Username = requestData.Username,
            Role = "contributor"
        };
        return BadRequestObjectWithTrace(fn, response, response.Message);
    }

    GeFeSLEUser? caller = await userManager.FindByNameAsync(callerUserName!.ToUpper());
    if (caller == null)
    {
        var response = new ListUserOperationResponse
        {
            Success = false,
            Message = "Caller is not in the database",
            ListId = list,
            Username = requestData.Username,
            Role = "contributor"
        };
        return BadRequestObjectWithTrace(fn, response, response.Message);
    }

    GeFeSLEUser? user = await userManager.FindByNameAsync(requestData.Username.ToUpper());
    if (user == null)
    {
        var response = new ListUserOperationResponse
        {
            Success = false,
            Message = $"User {requestData.Username} does not exist",
            ListId = list,
            Username = requestData.Username,
            Role = "contributor"
        };
        return NotFoundObjectWithTrace(fn, response, response.Message);
    }

    var roles = await userManager.GetRolesAsync(caller);
    var realizedRole = GlobalStatic.FindHighestRole(roles);

    if ((targetList.Creator != caller) && (realizedRole != "SuperUser") && !targetList.ListOwners.Contains(caller))
    {
        string msg = "Only the list's creator, a SuperUser or a listowner can add a contributor";
        return ProblemWithTrace(fn, msg, 403);
    }

    if (targetList.Contributors.Contains(user))
    {
        var msg = $"{user.UserName} is already a contributor to {targetList.Name}";
        DBg.d(LogLevel.Information, msg);
        return OkPayloadWithTrace(fn, new ListUserOperationResponse
        {
            Success = true,
            Message = msg,
            Username = requestData.Username,
            ListId = list,
            Role = "contributor"
        }, msg);
    }

    targetList.Contributors.Add(user);
    await db.SaveChangesAsync();
    await targetList.RegenerateAllFiles(db);
    await ProtectedFiles.RefreshListCacheAsync(db, targetList.Id);
    await ActivityPubBroadcastService.BroadcastActivityPubActorUpdateToFollowersAsync(
        targetList,
        db,
        listForActor => ActivityPubActorFactory.BuildActivityPubListActor(listForActor, activityPubMarkdownPipeline, activityPubPublicKeyPem),
        ActivityPubDeliveryUtils.ResolveActorInboxAsync,
        (inboxUrl, actorUrl, activityPayload, successLogMessage) =>
            ActivityPubDeliveryUtils.SendSignedActivityPubMessageAsync(inboxUrl, actorUrl, activityPayload, successLogMessage, activityPubSigningKey));
    var addedMsg = $"{caller.UserName} Added {user.UserName} to {targetList.Name} as a contributor";
    DBg.d(LogLevel.Information, addedMsg);
    return OkPayloadWithTrace(fn, new ListUserOperationResponse
    {
        Success = true,
        Message = addedMsg,
        Username = requestData.Username,
        ListId = list,
        Role = "contributor"
    }, addedMsg);
})
.RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});

app.MapDelete("/lists/{list:int}/owners", async (int list,
        [FromBody] AddListUserDto requestData,
        GeFeSLEDb db,
        HttpContext httpContext,
        UserManager<GeFeSLEUser> userManager) =>
{
    string fn = $"/lists/{list}/owners (DELETE)"; DBg.d(LogLevel.Trace, fn);
    LogDtoIn(fn, nameof(AddListUserDto), requestData);

    GeFeSLEUser? me = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);

    if (string.IsNullOrEmpty(requestData.Username))
    {
        var response = new ListUserOperationResponse
        {
            Success = false,
            Message = "username must be specified",
            ListId = list,
            Role = "listowner"
        };
        return BadRequestObjectWithTrace(fn, response, response.Message);
    }

    GeList? targetList = await db.Lists
        .Include(l => l.ListOwners)
        .Include(l => l.Contributors)
        .FirstOrDefaultAsync(l => l.Id == list);

    if (targetList == null)
    {
        var response = new ListUserOperationResponse
        {
            Success = false,
            Message = $"List {list} does not exist",
            ListId = list,
            Username = requestData.Username,
            Role = "listowner"
        };
        return NotFoundObjectWithTrace(fn, response, response.Message);
    }

    string? callerUserName = httpContext.User.Identity?.Name;
    if (string.IsNullOrEmpty(callerUserName))
    {
        var response = new ListUserOperationResponse
        {
            Success = false,
            Message = "Caller is not logged in",
            ListId = list,
            Username = requestData.Username,
            Role = "listowner"
        };
        return BadRequestObjectWithTrace(fn, response, response.Message);
    }

    GeFeSLEUser? caller = await userManager.FindByNameAsync(callerUserName!.ToUpper());
    if (caller == null)
    {
        var response = new ListUserOperationResponse
        {
            Success = false,
            Message = "Caller is not in the database",
            ListId = list,
            Username = requestData.Username,
            Role = "listowner"
        };
        return BadRequestObjectWithTrace(fn, response, response.Message);
    }

    GeFeSLEUser? user = await userManager.FindByNameAsync(requestData.Username.ToUpper());
    if (user == null)
    {
        var response = new ListUserOperationResponse
        {
            Success = false,
            Message = $"User {requestData.Username} does not exist",
            ListId = list,
            Username = requestData.Username,
            Role = "listowner"
        };
        return NotFoundObjectWithTrace(fn, response, response.Message);
    }

    var roles = await userManager.GetRolesAsync(caller);
    var realizedRole = GlobalStatic.FindHighestRole(roles);

    if ((targetList.Creator != caller) && (realizedRole != "SuperUser"))
    {
        string errMsg = "Only the list's creator or a SuperUser can REMOVE a listowner";
        return ProblemWithTrace(fn, errMsg, 403);
    }

    if (!targetList.ListOwners.Contains(user))
    {
        return OkPayloadWithTrace(fn, new ListUserOperationResponse
        {
            Success = true,
            Message = $"{user.UserName} isn't a listowner of {targetList.Name}",
            Username = requestData.Username,
            ListId = list,
            Role = "listowner"
        }, $"{user.UserName} isn't a listowner of {targetList.Name}");
    }

    targetList.ListOwners.Remove(user);
    await db.SaveChangesAsync();
    await targetList.RegenerateAllFiles(db);
    await ProtectedFiles.RefreshListCacheAsync(db, targetList.Id);
    await ActivityPubBroadcastService.BroadcastActivityPubActorUpdateToFollowersAsync(
        targetList,
        db,
        listForActor => ActivityPubActorFactory.BuildActivityPubListActor(listForActor, activityPubMarkdownPipeline, activityPubPublicKeyPem),
        ActivityPubDeliveryUtils.ResolveActorInboxAsync,
        (inboxUrl, actorUrl, activityPayload, successLogMessage) =>
            ActivityPubDeliveryUtils.SendSignedActivityPubMessageAsync(inboxUrl, actorUrl, activityPayload, successLogMessage, activityPubSigningKey));
    var msg = $"{caller.UserName} REMOVED {user.UserName} FROM {targetList.Name} as a listowner";
    DBg.d(LogLevel.Information, msg);
    return OkPayloadWithTrace(fn, new ListUserOperationResponse
    {
        Success = true,
        Message = msg,
        Username = requestData.Username,
        ListId = list,
        Role = "listowner"
    }, msg);
})
.WithEndpointDocs("DeleteListOwner")
.RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});

app.MapDelete("/lists/{list:int}/contributors", async (int list,
        [FromBody] AddListUserDto requestData,
        GeFeSLEDb db,
        HttpContext httpContext,
        UserManager<GeFeSLEUser> userManager) =>
{
    string fn = $"/lists/{list}/contributors (DELETE)"; DBg.d(LogLevel.Trace, fn);
    LogDtoIn(fn, nameof(AddListUserDto), requestData);

    GeFeSLEUser? me = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);

    if (string.IsNullOrEmpty(requestData.Username))
    {
        var response = new ListUserOperationResponse
        {
            Success = false,
            Message = "username must be specified",
            ListId = list,
            Role = "contributor"
        };
        return BadRequestObjectWithTrace(fn, response, response.Message);
    }

    GeList? targetList = await db.Lists
        .Include(l => l.ListOwners)
        .Include(l => l.Contributors)
        .FirstOrDefaultAsync(l => l.Id == list);

    if (targetList == null)
    {
        var response = new ListUserOperationResponse
        {
            Success = false,
            Message = $"List {list} does not exist",
            ListId = list,
            Username = requestData.Username,
            Role = "contributor"
        };
        return NotFoundObjectWithTrace(fn, response, response.Message);
    }

    string? callerUserName = httpContext.User.Identity?.Name;
    if (string.IsNullOrEmpty(callerUserName))
    {
        var response = new ListUserOperationResponse
        {
            Success = false,
            Message = "Caller is not logged in",
            ListId = list,
            Username = requestData.Username,
            Role = "contributor"
        };
        return BadRequestObjectWithTrace(fn, response, response.Message);
    }

    GeFeSLEUser? caller = await userManager.FindByNameAsync(callerUserName!.ToUpper());
    if (caller == null)
    {
        var response = new ListUserOperationResponse
        {
            Success = false,
            Message = "Caller is not in the database",
            ListId = list,
            Username = requestData.Username,
            Role = "contributor"
        };
        return BadRequestObjectWithTrace(fn, response, response.Message);
    }

    GeFeSLEUser? user = await userManager.FindByNameAsync(requestData.Username.ToUpper());
    if (user == null)
    {
        var response = new ListUserOperationResponse
        {
            Success = false,
            Message = $"User {requestData.Username} does not exist",
            ListId = list,
            Username = requestData.Username,
            Role = "contributor"
        };
        return NotFoundObjectWithTrace(fn, response, response.Message);
    }

    var roles = await userManager.GetRolesAsync(caller);
    var realizedRole = GlobalStatic.FindHighestRole(roles);

    if ((targetList.Creator != caller) && (realizedRole != "SuperUser") && !targetList.ListOwners.Contains(caller))
    {
        string errMsg = "Only the list's creator, a SuperUser or a listowner can REMOVE a contributor";
        return ProblemWithTrace(fn, errMsg, 403);
    }

    if (!targetList.Contributors.Contains(user))
    {
        return OkPayloadWithTrace(fn, new ListUserOperationResponse
        {
            Success = true,
            Message = $"{user.UserName} isn't a contributor to {targetList.Name}",
            Username = requestData.Username,
            ListId = list,
            Role = "contributor"
        }, $"{user.UserName} isn't a contributor to {targetList.Name}");
    }

    targetList.Contributors.Remove(user);
    await db.SaveChangesAsync();
    await targetList.RegenerateAllFiles(db);
    await ProtectedFiles.RefreshListCacheAsync(db, targetList.Id);
    await ActivityPubBroadcastService.BroadcastActivityPubActorUpdateToFollowersAsync(
        targetList,
        db,
        listForActor => ActivityPubActorFactory.BuildActivityPubListActor(listForActor, activityPubMarkdownPipeline, activityPubPublicKeyPem),
        ActivityPubDeliveryUtils.ResolveActorInboxAsync,
        (inboxUrl, actorUrl, activityPayload, successLogMessage) =>
            ActivityPubDeliveryUtils.SendSignedActivityPubMessageAsync(inboxUrl, actorUrl, activityPayload, successLogMessage, activityPubSigningKey));
    var msg = $"{caller.UserName} REMOVED {user.UserName} FROM {targetList.Name} as a contributor";
    DBg.d(LogLevel.Information, msg);
    return OkPayloadWithTrace(fn, new ListUserOperationResponse
    {
        Success = true,
        Message = msg,
        Username = requestData.Username,
        ListId = list,
        Role = "contributor"
    }, msg);
})
.WithEndpointDocs("DeleteListContributor")
.RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});

app.MapGet("/session", async (HttpContext httpContext) =>
{
    string fn = "/session (GET)"; DBg.d(LogLevel.Trace, fn);

    StringBuilder sb = new StringBuilder();
    await GlobalStatic.GenerateHTMLHead(sb, "Session Debug Information");

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
    
    sb.AppendLine("<h1>Session Debug Information</h1>");
    sb.AppendLine($"<p><strong>SuperUser?:</strong> {httpContext.User.IsInRole("SuperUser")}</p>");
    sb.AppendLine($"<p><strong>Status:</strong> {msg}</p>");
    sb.AppendLine("<h2>Session Details</h2>");
    sb.AppendLine($"<pre>{niceSession}</pre>");
    
    // Add JavaScript to show admin and debug elements
    sb.AppendLine("<script src=\"/_utils.js\"></script>");
    sb.AppendLine("<script>");
    sb.AppendLine("document.addEventListener('DOMContentLoaded', function() {");
    sb.AppendLine("    showDebuggingElements();");
    sb.AppendLine("    showAdminSecrets();");
    sb.AppendLine("});");
    sb.AppendLine("</script>");
    
    DBg.d(LogLevel.Information, msg);

    await GlobalStatic.GeneratePageFooter(sb);
    return ContentWithTrace(fn, sb.ToString(), "text/html", "HTML response generated");
}).AllowAnonymous()
.RequireAuthorization(new AuthorizeAttribute
{ AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme });


app.MapGet("/me/delete", async (HttpContext httpContext) =>
{
    string fn = "/me/delete (GET)"; DBg.d(LogLevel.Trace, fn);

    await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    // delete every Cookie
    foreach (var cookie in httpContext.Request.Cookies)
    {
        httpContext.Response.Cookies.Append(cookie.Key, "", new CookieOptions { Expires = DateTime.UtcNow.AddDays(-1) });
    }
    // foreach (var cookie in httpContext.Request.Cookies)
    // {
    //     httpContext.Response.Cookies.Delete(cookie.Key);
    // }
    // kill all Session storage
    httpContext.Session.Clear();
    
    // create an html page with javascript that clears localStorage and sessionStorage
    StringBuilder sb = new StringBuilder();
    await GlobalStatic.GenerateHTMLHead(sb, "Session Cleanup");
    
    sb.AppendLine("<h1>Session Cleanup</h1>");
    sb.AppendLine("<p>Clearing session data and redirecting...</p>");
    sb.AppendLine("<script src=\"/_utils.js\"></script>");
    sb.AppendLine("<script>");
    sb.AppendLine("document.addEventListener('DOMContentLoaded', function() {");
    sb.AppendLine("    // Show admin elements before clearing and redirecting");
    sb.AppendLine("    showDebuggingElements();");
    sb.AppendLine("    showAdminSecrets();");
    sb.AppendLine("    localStorage.clear();");
    sb.AppendLine("    sessionStorage.clear();");
    sb.AppendLine("    setTimeout(function() { window.location.href = '/'; }, 1000);");
    sb.AppendLine("});");
    sb.AppendLine("</script>");
    
    await GlobalStatic.GeneratePageFooter(sb);
    return ContentWithTrace(fn, sb.ToString(), "text/html", "HTML response generated");
}).AllowAnonymous()
.RequireAuthorization(new AuthorizeAttribute
{ AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme });


app.MapGet("/", () => {
    string fn = "/ (GET)"; DBg.d(LogLevel.Trace, fn);
    return RedirectWithTrace(fn, "/index.html", "redirecting to index");
});

app.MapPost("/files", async (IFormFile file,
    IAntiforgery antiforgery,
    GeFeSLEDb db,
    UserManager<GeFeSLEUser> userManager,
    HttpContext httpContext) =>
{
    string fn = "/files (POST)"; DBg.d(LogLevel.Trace, fn);
    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);

    DBg.d(LogLevel.Trace, $"{fn} -- {UserSessionService.dumpClaims(httpContext)}");




    try
    {
        await antiforgery.ValidateRequestAsync(httpContext);
    }
    catch (Exception e)
    {
        return BadRequestWithTrace(fn, e.Message);
    }

    if (user is null)
    {
        return BadRequestWithTrace(fn, "User is null");
    }
    if (file is null)
    {
        return BadRequestWithTrace(fn, "No file uploaded");
    }
    if (file.Length > 0)
    {
        if (string.IsNullOrWhiteSpace(user.UploadsPath))
        {
            string candidateUploadsPath = GeFeSLEUser.GetUploadsPath(user.UserName, user.Id, user.Email);
            var otherUsers = await userManager.Users.Where(existingUser => existingUser.Id != user.Id).ToListAsync();
            if (otherUsers.Any(existingUser => string.Equals(existingUser.UploadsPath, candidateUploadsPath, StringComparison.OrdinalIgnoreCase)))
            {
                string msg = $"Another user already uses uploads folder '{candidateUploadsPath}'. Please choose a different username.";
                return BadRequestWithTrace(fn, msg);
            }

            user.UploadsPath = candidateUploadsPath;
            await userManager.UpdateAsync(user);
        }

        // the filepath will be wwwroot/uploads/user/filename
        string filePath = Path.Combine(GlobalConfig.wwwroot, GlobalStatic.uploadsFolder, user.UploadsPath, file.FileName);
        DBg.d(LogLevel.Trace, $"fileupload - file will be saved at (filepath): {filePath}");
        //creates the folder if it doesn't exist
        Directory.CreateDirectory(Path.GetDirectoryName(filePath));

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }
        // we want to return the URL of the file that was uploaded
        string relpath = $"{GlobalStatic.uploadsFolder}/{user.UploadsPath}/{file.FileName}";
        string url = $"{GlobalConfig.Hostname}/{relpath}";
        // proactively protect the file until the item it is registered in is added

        ProtectedFiles.AddFile(relpath, GlobalConfig.modListName);


        return OkPayloadWithTrace(fn, url, $"uploaded file URL {url}");
    }
    else
    {
        return BadRequestWithTrace(fn, "File is empty");
    }
}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner,contributor"
});

app.MapDelete("/files/{*file}", async (string file,
    IAntiforgery antiforgery,
    GeFeSLEDb db,
    GeListFileController geListFileController,
    UserManager<GeFeSLEUser> userManager,
    HttpContext httpContext) =>
{
    string fn = $"/files/{file} (DELETE)"; DBg.d(LogLevel.Trace, fn);
    _ = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);

    try
    {
        await antiforgery.ValidateRequestAsync(httpContext);
    }
    catch (Exception e)
    {
        return BadRequestWithTrace(fn, e.Message);
    }

    if (string.IsNullOrWhiteSpace(file))
    {
        return BadRequestWithTrace(fn, "No file path provided");
    }

    string decoded = WebUtility.UrlDecode(file).Replace("\\", "/").Trim();
    decoded = decoded.TrimStart('/');
    if (decoded.Contains("..", StringComparison.Ordinal))
    {
        return BadRequestWithTrace(fn, "Invalid file path");
    }

    string relPath = GeListFileController.NormalizeRelativePath(decoded);
    string uploadsPrefix = "/" + GlobalStatic.uploadsFolder + "/";
    bool isUploadPath = relPath.StartsWith(uploadsPrefix, StringComparison.OrdinalIgnoreCase);
    bool isDynamicallyProtected = ProtectedFiles.ContainsFile(relPath)
        || ProtectedFiles.ContainsFile(relPath.TrimStart('/'));

    if (GeListFileController.IsInternalProtectedPath(relPath))
    {
        return BadRequestWithTrace(fn, "Protected internal files cannot be deleted");
    }

    if (isDynamicallyProtected)
    {
        if (!isUploadPath)
        {
            return BadRequestWithTrace(fn, "Protected internal files cannot be deleted");
        }

        var referencedUploads = await geListFileController.GetAllReferencedUploadFilesAsync();
        if (referencedUploads.Contains(relPath))
        {
            return BadRequestWithTrace(fn, "Upload file is still referenced and cannot be deleted");
        }

        // stale protection entries can remain after association changes; clear them when truly orphaned
        ProtectedFiles.RemoveFile(relPath);
        ProtectedFiles.RemoveFile(relPath.TrimStart('/'));
    }

    bool isGeneratedListFile = string.Equals(relPath, "/index.html", StringComparison.OrdinalIgnoreCase);
    if (!isGeneratedListFile)
    {
        List<string> listNames = await db.Lists
            .Where(l => !string.IsNullOrWhiteSpace(l.Name))
            .Select(l => l.Name!)
            .ToListAsync();

        isGeneratedListFile = listNames.Any(listName =>
            string.Equals(relPath, $"/{listName}.html", StringComparison.OrdinalIgnoreCase)
            || string.Equals(relPath, $"/{listName}.json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(relPath, $"/rss-{listName}.xml", StringComparison.OrdinalIgnoreCase));
    }
    if (isGeneratedListFile)
    {
        return BadRequestWithTrace(fn, "Generated list files cannot be deleted");
    }

    if (!relPath.StartsWith(uploadsPrefix, StringComparison.OrdinalIgnoreCase))
    {
        return BadRequestWithTrace(fn, "Only files under uploads can be deleted");
    }

    string absRoot = Path.GetFullPath(GlobalConfig.wwwroot);
    string absFile = Path.GetFullPath(Path.Combine(absRoot, relPath.TrimStart('/')));
    if (!absFile.StartsWith(absRoot, StringComparison.OrdinalIgnoreCase))
    {
        return BadRequestWithTrace(fn, "Invalid file path");
    }

    if (!System.IO.File.Exists(absFile))
    {
        return NotFoundWithTrace(fn, "File not found");
    }

    System.IO.File.Delete(absFile);
    ProtectedFiles.RemoveFile(relPath);
    ProtectedFiles.RemoveFile(relPath.TrimStart('/'));

    return OkPayloadWithTrace(fn, new { deleted = relPath }, $"deleted file {relPath}");
})
.RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser,listowner"
});



app.MapGet("/actions/{processid}", (string processid) =>
{
    string fn = $"/actions/{processid} (GET)"; DBg.d(LogLevel.Trace, fn);
    var status = ProcessTracker.GetProcessStatus(processid);
    return OkPayloadWithTrace(fn, status, "process status returned");

});

app.MapGet("/actions", () =>
{
    string fn = "/actions (GET)"; DBg.d(LogLevel.Trace, fn);
    var status = ProcessTracker.GetProcesses();
    return OkPayloadWithTrace(fn, status, "process list returned");

});

app.MapGet("/files/orphan", async (GeListFileController geListFileController) =>
{
    string fn = "/files/orphan (GET)"; DBg.d(LogLevel.Trace, fn);
    StringBuilder sb = await geListFileController.GetAllFilesInWWWRoot();
    return ContentWithTrace(fn, sb.ToString(), "text/html", "HTML response generated");
}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser"
});

app.MapGet("/files/cleanup", async (GeListFileController geListFileController) =>
{
    string fn = "/files/cleanup (GET)"; DBg.d(LogLevel.Trace, fn);
    int deleted = await geListFileController.DeleteOrphanFilesAsync();
    DBg.d(LogLevel.Information, $"{fn} -- deleted {deleted} orphan files");
    return RedirectWithTrace(fn, "/files/orphan", $"deleted {deleted} orphan files");
}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser"
});

app.MapGet("/files/clean", async (GeListFileController geListFileController,
    HttpContext httpContext,
    GeFeSLEDb db,
    UserManager<GeFeSLEUser> userManager) =>
{
    string fn = "/files/clean (GET)"; DBg.d(LogLevel.Trace, fn);

    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);

    await geListFileController.FreshStart();
    // loads the "restricted" internal files into the protected files lookup cache
    ProtectedFiles.ReLoadFiles(db);
    // loads the "restricted" uploads/attachment files into the protected files lookup cache
    _ = geListFileController.ProtectUploadFiles();
    var lists = await db.Lists.ToListAsync();
    foreach (var list in lists)
    {
        DBg.d(LogLevel.Trace, $"{fn} -- rebuilding {list.Name}");
        await list.GenerateHTMLListPage(db);
        await list.GenerateRSSFeed(db);
        await list.GenerateJSON(db);
    }
    await GlobalStatic.GenerateHTMLListIndex(db);

    return RedirectWithTrace(fn, "/files/orphan", "redirecting after file cleanup");
}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser"
});

app.MapGet("/items/orphan", async (GeFeSLEDb db, bool delete = false) =>
{
    string fn = "/items/orphan (GET)"; DBg.d(LogLevel.Trace, fn);
    var geListIds = await db.Lists.Select(g => g.Id).ToListAsync();
    var geItemOrphans = await db.Items.Where(i => !geListIds.Contains(i.ListId)).ToListAsync();
    
    if(delete && geItemOrphans.Count > 0)
    {
        db.Items.RemoveRange(geItemOrphans);
        await db.SaveChangesAsync();
        return RedirectWithTrace(fn, "/items/orphan", "redirecting after orphan item delete");
    }

    StringBuilder sb = new StringBuilder();
    await GlobalStatic.GenerateHTMLHead(sb, "Orphaned Items Report");
    
    sb.AppendLine("<h1>Orphaned Items Report</h1>");
    if(geItemOrphans.Count == 0)
    {
        sb.AppendLine("<p><strong>Status:</strong> No orphaned items found - all items are properly associated with existing lists.</p>");
    }
    else
    {
        sb.AppendLine($"<p><strong>Status:</strong> Found {geItemOrphans.Count} orphaned item(s) that reference non-existent lists.</p>");
        sb.AppendLine("<div class=\"button admin\" onclick=\"window.location.href='/items/orphan?delete=true'\">Delete All Orphaned Items</div>");
        sb.AppendLine("<br><br>");
        sb.AppendLine("<h2>Orphaned Items List</h2>");
        sb.AppendLine("<table>");
        sb.AppendLine("<thead><tr><th>Item Name</th><th>Referenced List ID</th><th>Action</th></tr></thead>");
        sb.AppendLine("<tbody>");
        foreach (var item in geItemOrphans)
        {
            sb.AppendLine($"<tr>");
            sb.AppendLine($"<td>{item.Name ?? "Unnamed Item"}</td>");
            sb.AppendLine($"<td>{item.ListId}</td>");
            sb.AppendLine($"<td><a href=\"/_edit.item.html?listid={item.ListId}&itemid={item.Id}\" class=\"itemeditlink\">Edit Item</a></td>");
            sb.AppendLine($"</tr>");
        }
        sb.AppendLine("</tbody>");
        sb.AppendLine("</table>");
    }
    
    // Add JavaScript to show admin and debug elements
    sb.AppendLine("<script src=\"/_utils.js\"></script>");
    sb.AppendLine("<script>");
    sb.AppendLine("document.addEventListener('DOMContentLoaded', function() {");
    sb.AppendLine("    showDebuggingElements();");
    sb.AppendLine("    showAdminSecrets();");
    sb.AppendLine("});");
    sb.AppendLine("</script>");
    
    await GlobalStatic.GeneratePageFooter(sb);
    return ContentWithTrace(fn, sb.ToString(), "text/html", "HTML response generated");
}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser"
});

app.MapPost("/cleanup/empty-tags", async (GeFeSLEDb db) =>
{
    string fn = "/cleanup/empty-tags (POST)"; DBg.d(LogLevel.Trace, fn);
    
    var allItems = await db.Items.ToListAsync();
    int cleanedCount = 0;
    
    foreach (var item in allItems)
    {
        var originalCount = item.Tags.Count;
        // Remove null, empty, or whitespace-only tags
        item.Tags = item.Tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => tag.Trim()).ToList();
        
        if (item.Tags.Count != originalCount)
        {
            cleanedCount++;
        }
    }
    
    await db.SaveChangesAsync();
    
    // Regenerate all list pages to reflect the cleanup
    var lists = await db.Lists.ToListAsync();
    foreach (var list in lists)
    {
        await list.GenerateHTMLListPage(db);
    }
    
    return OkWithTrace(fn, $"Cleaned up empty tags from {cleanedCount} items");
}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser"
});


app.MapPost("/items/{itemid:int}/report", async (int itemid,
    GeFeSLEDb db,
    UserManager<GeFeSLEUser> userManager,
    RoleManager<IdentityRole> roleManager,
    GeListController geListController,
    HttpContext context,
    GeListFileController fileController) =>
{
    string fn = $"/items/{itemid}/report (POST)"; DBg.d(LogLevel.Trace, fn);
    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(context, db, userManager);

    // there's going to be a "reason" parameter, and if user == null, a user contact 
    // parameter in a form submission. get those two
    var reason = context.Request.Form["reason"];
    var userName = context.Request.Form["userName"];

    var item = await db.Items.FindAsync(itemid);
    if (item is null)
    {
        string msg = $"Item {itemid} not found";
        return NotFoundWithTrace(fn, msg);
    }

    var itemList = await db.Lists.FindAsync(item.ListId);
    if (itemList is null)
    {
        string msg = $"Item LIST {item.ListId} not found";
        return NotFoundWithTrace(fn, msg);
    }

    // Determine the reporter identity - if user is logged in, use their username
    // Otherwise, use the userName from the form (or "anonymous" if none provided)
    string reporterIdentity;
    if (user != null)
    {
        reporterIdentity = user.UserName ?? "logged-in-user-no-name";
    }
    else
    {
        reporterIdentity = string.IsNullOrEmpty(userName) ? "anonymous" : userName.ToString();
    }

    DBg.d(LogLevel.Trace, $"{fn} -- reporting item {itemid} in list {itemList.Name} for: {reason} by: {reporterIdentity}");
    // a reported item creates a reference item in a special MODERATOR list
    // that only SuperUsers and listowners can see. 
    // first, create the MODERATION list if it doesn't exist
    var modlist = await db.Lists.FirstOrDefaultAsync(l => l.Name == GlobalConfig.modListName);
    if (modlist == null)
    {
        DBg.d(LogLevel.Trace, $"{fn} Creating MODLIST named {GlobalConfig.modListName}");
        modlist = new GeList
        {
            Name = GlobalConfig.modListName,
            Visibility = GeListVisibility.ListOwners,
            Creator = user
        };

        db.Lists.Add(modlist);
        // save the db to get the list id
        await db.SaveChangesAsync();
    }
    // now create a new GeListItem IN the MODERATION list
    var moditem = new GeListItem
    {
        Name = $"{itemList.Name}#{itemid} <= by {reporterIdentity}",
        ListId = modlist.Id,
        Tags = { "REPORTED", itemList.Name }
    };
    moditem.Comment += $"Reported by {reporterIdentity}  ";
    moditem.Comment += $"Item has been preemptively marked as invisible pending moderation  ";
    moditem.Comment += $"Rationale for report:  ";
    moditem.Comment += $"{reason}  ";
    moditem.Comment += $"---------  ";
    moditem.Comment += $"<a href=\"_edit.item.html?listid={itemList.Id}&itemid={itemid}\">LINK TO VIEW/FIX</a>";
    // change the original item's visibility to false
    item.Visible = false;
    // apply visibility of MODERATION list to any attachments // TODO: deal when image is in more than one list w/ varying visibilities
    List<string> itemFiles = item.LocalFiles();
    fileController.ProtectFiles(itemFiles, GlobalConfig.modListName);
    // don't worry - when the item is modified again, its regular list permissions protection will be reapplied
    // (see the item itemmodify endpoint) 

    DBg.d(LogLevel.Trace, $"{fn} -- SAVING MOD ITEM: {moditem.Comment}");
    db.Items.Add(moditem);
    // save all changes
    await db.SaveChangesAsync();
    // regen the item's list
    _ = itemList.GenerateHTMLListPage(db);
    _ = itemList.GenerateRSSFeed(db);
    _ = itemList.GenerateJSON(db);

    // regen the MODLIST
    _ = modlist.GenerateHTMLListPage(db);
    return OkWithTrace(fn, "moderation item saved");

}).AllowAnonymous();

app.MapPost("/lists/{listid:int}/suggest", async (int listid,
    GeListItemCreateUpdateDto itemDto,
    GeFeSLEDb db,
    UserManager<GeFeSLEUser> userManager,
    HttpContext httpContext,
    GeListFileController geListFileController) =>
{
    string fn = $"/lists/{listid}/suggest (POST)"; DBg.d(LogLevel.Trace, fn);
    LogDtoIn(fn, nameof(GeListItemCreateUpdateDto), itemDto);
    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);
    // Map DTO to domain object
    var newitem = new GeListItem { ListId = itemDto.ListId, Name = itemDto.Name, Comment = itemDto.Comment, IsComplete = itemDto.IsComplete, Visible = false, Tags = new List<string>(itemDto.Tags) };
    // if the ListId of newitem is 0 (which is ok - no value in json int is 0), then set it to listid
    if (newitem.ListId == 0)
    {
        newitem.ListId = listid;
    }
    else
    {
        if (newitem.ListId != listid)
        {
            return BadRequestWithTrace(fn, "ListId does not match");
        }
    }

    GeList? newitemList = await db.Lists.FirstOrDefaultAsync(l => l.Id == newitem.ListId);
    if (newitemList is null)
    {
        string msg = $"${newitem.ListId} is not a vlaid list";
        return BadRequestWithTrace(fn, msg);
    }


    // now create the moderator review request // first get the
    // first, create the MODERATION list if it doesn't exist
    var modlist = await db.Lists.FirstOrDefaultAsync(l => l.Name == GlobalConfig.modListName);
    if (modlist == null)
    {
        DBg.d(LogLevel.Trace, $"{fn} Creating MODLIST named {GlobalConfig.modListName}");
        modlist = new GeList
        {
            Name = GlobalConfig.modListName,
            Visibility = GeListVisibility.ListOwners,
            Creator = user
        };

        db.Lists.Add(modlist);

    }
    // change the new item's visibility to false
    newitem.Visible = false;
    // save the db to get the list id; also save the new item so we can get ITS id
    DBg.d(LogLevel.Trace, $"{fn} -- SAVING SUGGESTION: {System.Text.Json.JsonSerializer.Serialize(newitem)}");
    db.Items.Add(newitem);
    await db.SaveChangesAsync();
    // now create a new GeListItem IN the MODERATION list
    var moditem = new GeListItem
    {
        Name = $"{newitemList.Name}#{newitem.Id} <= by {user?.UserName ?? "anonymous"}",
        ListId = modlist.Id,
        Tags = { "SUGGESTED", newitemList.Name }
    };
    moditem.Comment += $"SUGGESTED by {user?.UserName ?? "anonymous"}  ";
    moditem.Comment += $"Item has been preemptively marked as invisible pending approval  ";
    moditem.Comment += $"---------  ";
    moditem.Comment += $"<a href=\"_edit.item.html?listid={newitemList.Id}&itemid={newitem.Id}\">LINK TO APPROVE</a>";

    // apply visibility of MODERATION list to any attachments // TODO: deal when image is in more than one list w/ varying visibilities
    List<string> itemFiles = newitem.LocalFiles();
    geListFileController.ProtectFiles(itemFiles, GlobalConfig.modListName);
    // don't worry - when the item is modified again, its regular list permissions protection will be reapplied
    // (see the item itemmodify endpoint) 

    DBg.d(LogLevel.Trace, $"{fn} -- SAVING MOD ITEM: {moditem.Comment}");
    db.Items.Add(moditem);


    await db.SaveChangesAsync();

    List<string> itemfiles = newitem.LocalFiles();
    geListFileController.ProtectFiles(itemfiles, GlobalConfig.modListName);

    await newitemList.GenerateHTMLListPage(db);
    await newitemList.GenerateRSSFeed(db);
    await newitemList.GenerateJSON(db);
    await modlist.GenerateHTMLListPage(db);
    await modlist.GenerateRSSFeed(db);
    await modlist.GenerateJSON(db);

    var suggestedItemDto = newitem.ToResponseDto();
    LogDtoOut(fn, nameof(GeListItemResponseDto), suggestedItemDto);
    return Results.Created($"/showitems/{newitem.ListId}/{newitem.Id}", suggestedItemDto);
}).AllowAnonymous();

app.MapGet("/lists/export", async (GeFeSLEDb db) =>
{
    string fn = "/lists/export (GET)"; DBg.d(LogLevel.Trace, fn);
    string zipFile = GlobalStatic.SiteExport(db);
    // the zipFileName will be in wwwroot
    zipFile = $"{GlobalConfig.Hostname}/{zipFile}";
    DBg.d(LogLevel.Information, $"Exported site to {zipFile}");
    return RedirectWithTrace(fn, zipFile, $"redirecting to export archive {zipFile}");

}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser"
});

app.MapPost("/lists/import", async (IFormFile file,
    IAntiforgery antiforgery,
    GeFeSLEDb db,
    UserManager<GeFeSLEUser> userManager,
    HttpContext httpContext) =>
{
    string fn = "/lists/import (POST)"; DBg.d(LogLevel.Trace, fn);


    GeFeSLEUser? user = UserSessionService.UpdateSessionAccessTime(httpContext, db, userManager);

    DBg.d(LogLevel.Trace, $"{UserSessionService.dumpClaims(httpContext)}");

    try
    {
        await antiforgery.ValidateRequestAsync(httpContext);
    }
    catch (Exception e)
    {
        return BadRequestWithTrace(fn, e.Message);
    }

    if (user is null)
    {
        return BadRequestWithTrace(fn, "User is null");
    }
    if (file is null)
    {
        return BadRequestWithTrace(fn, "No file uploaded");
    }
    if (file.Length > 0)
    {
        if (string.IsNullOrWhiteSpace(user.UploadsPath))
        {
            string candidateUploadsPath = GeFeSLEUser.GetUploadsPath(user.UserName, user.Id, user.Email);
            var otherUsers = await userManager.Users.Where(existingUser => existingUser.Id != user.Id).ToListAsync();
            if (otherUsers.Any(existingUser => string.Equals(existingUser.UploadsPath, candidateUploadsPath, StringComparison.OrdinalIgnoreCase)))
            {
                string msg = $"Another user already uses uploads folder '{candidateUploadsPath}'. Please choose a different username.";
                return BadRequestWithTrace(fn, msg);
            }

            user.UploadsPath = candidateUploadsPath;
            await userManager.UpdateAsync(user);
        }

        // the filepath will be wwwroot/uploads/user/filename
        string filePath = Path.Combine(GlobalConfig.wwwroot, GlobalStatic.uploadsFolder, user.UploadsPath, file.FileName);
        DBg.d(LogLevel.Trace, $"fileupload - file will be saved at (filepath): {filePath}");
        //creates the folder if it doesn't exist
        Directory.CreateDirectory(Path.GetDirectoryName(filePath));

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }
        string results = await GlobalStatic.SiteImport(db, filePath, user);
        return OkPayloadWithTrace(fn, results, $"import produced {results}");
    }
    else
    {
        return BadRequestWithTrace(fn, "File is empty");
    }

}).RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "SuperUser"
});

//============================================================== ACTIVITY PUB IMPLEMENTATION

(activityPubSigningKey, activityPubPublicKeyPem) =
    await ActivityPubKeyLoader.LoadFromConfigAsync(GlobalConfig.ActivityPubPrivateKeyPemFile);
ActivityPubActivityLogStore.RegisterMaintenanceTasks();

app.MapNodeInfoEndpoints();

app.MapGet("/Home/Error", () =>
{
    string fn = "/Home/Error (GET)"; DBg.d(LogLevel.Trace, fn);
    return ProblemWithTrace(fn, "Unhandled server error", StatusCodes.Status500InternalServerError);
}).AllowAnonymous();

// webfinger. /.well-known/webfinger?resource=acct:username@hostname
app.MapGet("/.well-known/webfinger", async (string resource, GeFeSLEDb db) =>
{
    string fn = "/.well-known/webfinger (GET)"; DBg.d(LogLevel.Trace, fn);
    return await ActivityPubEndpointService.GetWebfingerAsync(resource, db);
});

app.MapGet("/apv1/activities/{activityId}", async (string activityId) =>
{
    string fn = $"/apv1/activities/{activityId} (GET)"; DBg.d(LogLevel.Trace, fn);
    var readResult = await ActivityPubActivityLogStore.TryReadActivityPayloadAsync(activityId);
    if (readResult.Found && readResult.Payload is not null)
    {
        return ContentWithTrace(fn, readResult.Payload, "application/activity+json", $"activity {activityId} returned");
    }

    if (!string.IsNullOrWhiteSpace(readResult.Error))
    {
        DBg.d(LogLevel.Warning, $"{fn} failed reading activity payload for {activityId}: {readResult.Error}");
    }

    return NotFoundNoMessageWithTrace(fn, $"activity {activityId} not found");
}).AllowAnonymous();

// GET /apv1/lists/{listId}
// returns ActivityStreams Actor with: 
// id: https://{host}/apv1/lists/{listId}
// inbox: https://{host}/apv1/lists/{listId}/inbox
// outbox: https://{host}/apv1/lists/{listId}/outbox
// followers: https://{host}/apv1/lists/{listId}/followers

app.MapGet("/apv1/lists/{listId:int}", async (int listId, GeFeSLEDb db) =>
{
    string fn = $"/apv1/lists/{listId} (GET)"; DBg.d(LogLevel.Trace, fn);
    return await ActivityPubEndpointService.GetListActorAsync(
        listId,
        db,
        list => ActivityPubActorFactory.BuildActivityPubListActor(list, activityPubMarkdownPipeline, activityPubPublicKeyPem));
});

// GET /apv1/lists/{listId}/outbox
// basically this is the same as GET /lists/{listId} but
// returns a Collection/OrderedCollection of items as ActivityPub Notes..
// TODOL: support pagination. 
app.MapGet("/apv1/lists/{listId:int}/outbox", async (int listId, GeFeSLEDb db) =>
{
    string fn = $"/apv1/lists/{listId}/outbox (GET)"; DBg.d(LogLevel.Trace, fn);
    return await ActivityPubEndpointService.GetListOutboxAsync(listId, db);
}).AllowAnonymous();

// GET /apv1/lists/{listId}/items/{itemId}
// returns an ActivityPub Note
// if deleted: 410 Gone
// BadRequest if item doesn't belong to that list. 

app.MapGet("/apv1/lists/{listId:int}/items/{itemId:int}", async (int listId, int itemId, GeFeSLEDb db) =>
{
    string fn = $"/apv1/lists/{listId}/items/{itemId} (GET)"; DBg.d(LogLevel.Trace, fn);
    return await ActivityPubEndpointService.GetListItemAsync(
        listId,
        itemId,
        db,
        (list, item) => ActivityPubPayloadFactory.BuildActivityPubItemNote(list, item, activityPubMarkdownPipeline));
});

app.MapGet("/apv1/items/{itemId:int}", async (int itemId, GeFeSLEDb db) =>
{
    string fn = $"/apv1/items/{itemId} (GET)"; DBg.d(LogLevel.Trace, fn);
    return await ActivityPubEndpointService.GetItemAsync(
        itemId,
        db,
        (list, item) => ActivityPubPayloadFactory.BuildActivityPubItemNote(list, item, activityPubMarkdownPipeline));
}).AllowAnonymous();

app.MapGet("/apv1/items/{itemId:int}/likes", async (int itemId, GeFeSLEDb db) =>
{
    string fn = $"/apv1/items/{itemId}/likes (GET)"; DBg.d(LogLevel.Trace, fn);
    return await ActivityPubEndpointService.GetItemLikesAsync(itemId, db);
}).AllowAnonymous();

app.MapGet("/apv1/comments/{commentId:int}", async (int commentId, GeFeSLEDb db) =>
{
    string fn = $"/apv1/comments/{commentId} (GET)"; DBg.d(LogLevel.Trace, fn);
    return await ActivityPubEndpointService.GetCommentAsync(commentId, db);
}).AllowAnonymous();

// GET /apv1/lists/{listId}/items
// exactly the same as the outbox above. 
// TODO: support pagination. 
// actually, just redirect. be sure to include the query string
// in case its provided pagination gunk
app.MapGet("/apv1/lists/{listId:int}/items", async (int listId, HttpContext httpContext) =>
{
    string fn = $"/apv1/lists/{listId}/items (GET)"; DBg.d(LogLevel.Trace, fn);
    return RedirectWithTrace(fn, $"/apv1/lists/{listId}/outbox{httpContext.Request.QueryString}", $"redirecting to list {listId} outbox");
});

// GET /apv1/lists/{listId}/followers
// returns an ActivityPub Collection of followers
app.MapGet("/apv1/lists/{listId:int}/followers", async (int listId, GeFeSLEDb db) =>
{
    string fn = $"/apv1/lists/{listId}/followers (GET)"; DBg.d(LogLevel.Trace, fn);
    return await ActivityPubEndpointService.GetFollowersAsync(listId, db);
});

// POST /apv1/lists/{listId}/inbox
// receives ActivityPub activities from other servers
// via ApActivityDtos... actions like Create or Delete to Follow or unfollow.
app.MapPost("/apv1/lists/{listId:int}/inbox", async (int listId, [FromBody] JsonElement activityJson, GeFeSLEDb db) =>
{
    return await ActivityPubInboxService.HandleListInboxAsync(
        listId,
        activityJson,
        db,
        ActivityPubPayloadFactory.ReadIriFromActivityPubNode,
        ActivityPubDeliveryUtils.ResolveActorInboxAsync,
        (inboxUrl, localActorUrl, sourceActivityId, sourceActivityType, sourceActorIri, sourceObjectIri, accepted, statusMessage)
            => ActivityPubInboxService.SendActivityPubFollowAckAsync(
                inboxUrl,
                localActorUrl,
                sourceActivityId,
                sourceActivityType,
                sourceActorIri,
                sourceObjectIri,
                accepted,
                statusMessage,
                (targetInboxUrl, actorUrl, activityPayload, successLogMessage)
                    => ActivityPubDeliveryUtils.SendSignedActivityPubMessageAsync(targetInboxUrl, actorUrl, activityPayload, successLogMessage, activityPubSigningKey)),
        (listForBroadcast, dbForBroadcast, itemForBroadcast, activityTypeForBroadcast, onlyFollowerForBroadcast) =>
            ActivityPubBroadcastService.BroadcastActivityPubItemToFollowersAsync(
                listForBroadcast,
                dbForBroadcast,
                itemForBroadcast,
                activityTypeForBroadcast,
                (listForNote, itemForNote) => ActivityPubPayloadFactory.BuildActivityPubItemNote(listForNote, itemForNote, activityPubMarkdownPipeline),
                ActivityPubDeliveryUtils.ResolveActorInboxAsync,
                (inboxUrl, actorUrl, activityPayload, successLogMessage) =>
                    ActivityPubDeliveryUtils.SendSignedActivityPubMessageAsync(inboxUrl, actorUrl, activityPayload, successLogMessage, activityPubSigningKey),
                onlyFollowerForBroadcast),
        (listForBroadcast, dbForBroadcast, remoteCommentObjectIri, reason, onlyFollowerForBroadcast) =>
            ActivityPubBroadcastService.BroadcastActivityPubCommentAnnounceToFollowersAsync(
                listForBroadcast,
                dbForBroadcast,
                remoteCommentObjectIri,
                reason,
                ActivityPubDeliveryUtils.ResolveActorInboxAsync,
                (inboxUrl, actorUrl, activityPayload, successLogMessage) =>
                    ActivityPubDeliveryUtils.SendSignedActivityPubMessageAsync(inboxUrl, actorUrl, activityPayload, successLogMessage, activityPubSigningKey),
                onlyFollowerForBroadcast));
}).AllowAnonymous();



//--------------------------------------------------------------------------- MUST BE LAST
// dynamic actor redirect: /{actorName} -> /apv1/lists/{listId}
// THIS HAS TO BE THE VERY LAST ENDPOINT
// because anything else that doesn't hit a more precise endpoint route/mapping will fall through
// to here. 
// Note that static files are handled way up at the beginning before any endpoint routing.
// TODO: that isn't actually working properly IF the regex is removed from here. 
// Fix the routing properly - static files have to served FIRST.
// 
app.MapGet("/{actorName:regex(^[A-Za-z0-9_-]+$)}", async (string actorName, GeFeSLEDb db, HttpContext httpContext) =>
{
    string fn = $"/{actorName} (GET)"; DBg.d(LogLevel.Trace, fn);
    return await ActivityPubEndpointService.GetActorNameRedirectAsync(fn, actorName, db);
}).AllowAnonymous();

app.MapFallback((Delegate)((HttpContext httpContext) =>
{
    string path = httpContext.Request.Path.Value ?? "/";
    string fn = $"{path} ({httpContext.Request.Method})"; DBg.d(LogLevel.Trace, fn);
    string msg = $"resource {path} not found";
    return NotFoundWithTrace(fn, msg);
})).AllowAnonymous();



//============================================================== STARTUP TASKS
// lets always generate index.html once before we start
// for a new setup, it won't exist. 
// Mutex to ensure only one of us is running


bool createdNew;
using (var mutex = new Mutex(true, GlobalStatic.applicationName, out createdNew))
{
    if (createdNew)
    {
        using (var scope = app.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            var db = services.GetRequiredService<GeFeSLEDb>();
            var geListFileController = services.GetRequiredService<GeListFileController>();
            // make sure our embedded files are always fresh and THERE
            await geListFileController.FreshStart();
            // regen all the lists anew
            var lists = await db.Lists.ToListAsync();
            foreach (var list in lists)
            {
                await list.GenerateHTMLListPage(db);
                await list.GenerateRSSFeed(db);
                await list.GenerateJSON(db);
            }
            // generates the index afresh
            _ = GlobalStatic.GenerateHTMLListIndex(db);
            // loads the "restricted" internal files into the protected files lookup cache
            ProtectedFiles.ReLoadFiles(db);
            // loads the "restricted" uploads/attachment files into the protected files lookup cache
            _ = geListFileController.ProtectUploadFiles();
        }
        DBg.d(LogLevel.Information, "Startup tasks completed successfully  =========================>");
        app.Run();
    }
    else
    {
        Console.WriteLine("Another instance of the application is already running.");
    }
}







