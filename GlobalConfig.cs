using System.Reflection;
using GeFeSLE;


public static class GlobalConfig
{
    // define get and set methods for port, bind, hostname, and hostport
    // the difference between Bind+Port and Hostname is that Bind+Port is the address that the server listens on, 
    //while Hostname is the address (and port) that the server tells clients to connect to
    // to handle reverse proxies, nats etc. redirects blah blah
    // its the latter that gets written to HTML and RSS and Federation elements; i.e. the valid back reference to this instance.
    public static int Port { get; set; }
    public static string? Bind { get; set; }
    public static string? Hostname { get; set; }

    public static bool isSecure { get; set; } = false;

    public static string? CookieDomain { get; set; }
    
    public static string? wwwroot { get; set; }

    public static string? modListName {get; set;} 

    public static LogLevel CURRENT_LEVEL { get; set; }

    public static bool Debugging { get; set; } // enables debug tools in the page header. 

    // store the static HTML "injection" files for site header, top of body and page footers. 
    // note these are filenames, not the actual HTML. But when used, you should read the file and inject it into the output.
    public static string? htmlHead { get; set; }
    public static string? bodyHeader { get; set; }
    public static string? bodyFooter { get; set; }
    public static string? bldVersion { get; set; }
    public static string? sitetitle { get; set; }
    public static string? owner { get; set; }

    public static GeFeSLEUser? backdoorAdmin { get; set; }
    public static string? backdoorAdminPassword { get; set; } = null;

    // the secret key for JWT bearer tokens - issued to API clients when they log in.
    // we're using only the default HS256 algorithm, so your secret key SHOULD be 
    // at least 32 characters long, the longer the better and with a mix of uc/lc/numbers/specials.  
    // these will last for the specified duration below
    public static string? apiTokenSecretKey { get; set; }

    public static TimeSpan apiTokenDuration { get; set; } = TimeSpan.Parse("1.00:00:00"); // 1 day   

    public static string? googleClientID;
    public static string? googleClientSecret;

    public static string? microsoftClientId;
    public static string? microsoftClientSecret;
    
    public static string? microsoftTenantId;

    public static string? mastoClient_Name = "GeFeSLE";
    public static string? mastoScopes = "read write:bookmarks";

    // constructor that receives a builder.Configuration and configures the application
    public static string? ParseConfigFile(IConfiguration config)
    {
        DBg.d(LogLevel.Debug, "ParseConfigFile");
        // configure the application
        Port = config.GetValue<int>("ServerSettings:Port");
        if (Port == 0) Port = 5000;
        Bind = config.GetValue<string>("ServerSettings:Bind");
        if (Bind == null) Bind = "localhost";
        Hostname = config.GetValue<string>("ServerSettings:Hostname");
        if (Hostname == null) Hostname = "http://localhost";
        
        // parse hostname. if it starts with https://, then we're secure
        if (Hostname.StartsWith("https://"))
        {
            isSecure = true;
        }


        // the cookie domain for the site is Hostname without the protocol
        CookieDomain = Hostname.Replace("http://", "").Replace("https://", "");
        // the cookie domain should have any port number removed
        CookieDomain = CookieDomain.Split(':')[0];


        wwwroot = config.GetValue<string>("ServerSettings:wwwroot");
        if (wwwroot == null) wwwroot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        // if wwwroot doesn't exist, create it
        if (!Directory.Exists(wwwroot))
        {
            Directory.CreateDirectory(wwwroot);
        }

        modListName = config.GetValue<string>("ServerSettings:modLIstName");
        if(modListName == null) modListName = "MODERATION";

        DBg.d(LogLevel.Debug, $"Port: {Port}");
        DBg.d(LogLevel.Debug, $"Bind: {Bind}");
        DBg.d(LogLevel.Debug, $"Hostname: {Hostname}");
        DBg.d(LogLevel.Debug, $"wwwroot: {wwwroot}");

        // get filenames for static html head, body header and body footer from the config file
        // also note these are ASSUMED to be found IN wwwroot (a logical place to put them)
        htmlHead = config.GetValue<string>("SiteCustomize:sitehead");
        if (htmlHead == null) htmlHead = "sitehead.html";
        htmlHead = Path.Combine(wwwroot, htmlHead);
        bodyHeader = config.GetValue<string>("SiteCustomize:bodyheader");
        if (bodyHeader == null) bodyHeader = "bodyheader.html";
        bodyHeader = Path.Combine(wwwroot, bodyHeader);
        bodyFooter = config.GetValue<string>("SiteCustomize:bodyfooter");
        if (bodyFooter == null) bodyFooter = "bodyfooter.html";
        bodyFooter = Path.Combine(wwwroot, bodyFooter);

        // if the files exist, we can only assume they contain valid HTML for injection into our output pages. 
        // if the files do NOT exist, set these (which are really the filenames) to null
        // note that Kestrel, the .NET web server, does not support the <!--#include virtual="filename" --> directive, 
        // so wherever we want to use these, we have to read in the files and spat them into our output.
        if (!File.Exists(htmlHead))
        {
            DBg.d(LogLevel.Debug, $"File {htmlHead} does not exist. Ignoring.");
            htmlHead = null;
        }
        if (!File.Exists(bodyHeader))
        {
            DBg.d(LogLevel.Debug, $"File {bodyHeader} does not exist. Ignoring.");
            bodyHeader = null;
        }
        if (!File.Exists(bodyFooter))
        {
            DBg.d(LogLevel.Debug, $"File {bodyFooter} does not exist. Ignoring.");
            bodyFooter = null;
        }
        // lastly get the AssemblyInformationalVersion attribute from the assembly and store it in a static variable
        var bldVersionAttribute = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        // convert it to a string and store it in a static variable
        bldVersion = bldVersionAttribute?.InformationalVersion;
        
        // get the Debugging setting from config file
        // it has to be explicitly set to false to turn off debugging (default is true)
        Debugging = config.GetValue<bool>("ServerSettings:Debugging", true);


        // get the sitename from the config file
        sitetitle = config.GetValue<string>("SiteCustomize:sitetitle");
        if (sitetitle == null) sitetitle = "GeFeSLE Lists";

        // get the owner from the config file
        owner = config.GetValue<string>("SiteCustomize:owner");

        // probably not kosher, but I'm lazy
        // get the admin user from the config file
        backdoorAdmin = config.GetSection("Users:backdooradmin").Get<GeFeSLEUser>();
        backdoorAdminPassword = config.GetValue<string>("Users:backdooradmin:Password");
        // in /LOGIN we'll make sure that a user with these credentials will always 
        // authenticate as an admin, no matter what the database says.
        
        if (backdoorAdmin != null)
        {
            DBg.d(LogLevel.Debug, $"Backdoor admin user: {backdoorAdmin.UserName}");
            DBg.d(LogLevel.Debug, $"Backdoor admin password: {backdoorAdminPassword}");
            
        }
        else
        {
            DBg.d(LogLevel.Warning, "No backdoor admin user specified in config file.");
        }

        // set the runlevel if it is specified in the config file; 
        var runlevel = config.GetValue<string>("ServerSettings:RunLevel");
        if (runlevel != null)
        {
            CURRENT_LEVEL = castRunLevel(runlevel);
        }
        else
        {
            // TODO: change this back to something sensible before release
            CURRENT_LEVEL = LogLevel.Trace;
        }

        string? apiTokenSecretKey = config.GetValue<string>("API:apiTokenSecretKey");
        if (apiTokenSecretKey != null)
        {
            GlobalConfig.apiTokenSecretKey = apiTokenSecretKey;
            DBg.d(LogLevel.Debug, $"API Token override: {apiTokenSecretKey}");
        }

        string? apiTokenDuration = config.GetValue<string>("API:apiTokenDuration");
        if (apiTokenDuration != null)
        {
            GlobalConfig.apiTokenDuration = TimeSpan.Parse(apiTokenDuration);
            DBg.d(LogLevel.Debug, $"API Token duration override: {apiTokenDuration}");
        }

        // read all of the API and OAuth2, 2nd party site settings

        googleClientID = config.GetValue<string>("OtherSites:Google:googleClientID");
        googleClientSecret = config.GetValue<string>("OtherSites:Google:googleClientSecret");
        if(googleClientID == null || googleClientSecret == null)
        {
            DBg.d(LogLevel.Warning, "Google OAuth2/Import settings not found in config file.");
        }
        microsoftClientId = config.GetValue<string>("OtherSites:Microsoft:microsoftClientId");
        microsoftClientSecret = config.GetValue<string>("OtherSites:Microsoft:microsoftClientSecret");
        microsoftTenantId = config.GetValue<string>("OtherSites:Microsoft:microsoftTenantId");
        if(microsoftClientId == null || microsoftClientSecret == null || microsoftTenantId == null)
        {
            DBg.d(LogLevel.Warning, "Microsoft OAuth2/Import settings not found in config file.");
        }
        mastoClient_Name = config.GetValue<string>("OtherSites:Mastodon:mastoClient_Name");
        mastoScopes = config.GetValue<string>("OtherSites:Mastodon:mastoScopes");
        if(mastoClient_Name == null || mastoScopes == null)
        {
            DBg.d(LogLevel.Warning, "Mastodon OAuth2/Import settings not found in config file.");
        }
        

        // lastly, the ONE thing we MUST get from the config file is the db file:
        // its an absolute path
        var dbfile = config.GetValue<string>("DatabaseSettings:DatabaseFile");
        if (dbfile == null)
        {
            DBg.d(LogLevel.Critical, "No database file specified in config file. Exiting.");
            return null;
        }
        else
        {
            DBg.d(LogLevel.Debug, $"Database file: {dbfile}");
            return dbfile;
        }



    }


    // parses the command line arguments; if --config is specified, 
    // AND it exists, RETURNS the config file
    // name, otherwise returns null
    public static string? CommandLineParse(string[] args)
    {
        DBg.d(LogLevel.Debug, "Startup");
        for (int i = 0; i < args.Length; i++)
        {
            DBg.d(LogLevel.Trace, $"Startupcommand line argument {i} is {args[i]}");
        }

        var isMigration = args.Any(a => a.Contains("database") || a.Contains("migrations"));
        if (isMigration)
        {
            DBg.d(LogLevel.Debug, "StartupDB Migration detected. CONTINUING.");
            isMigration = true;

        }

        //find if one of those args is --config
        //if it is, then load the config file


        var configArg = args.FirstOrDefault(arg => arg.StartsWith("--config"));
        string? configPath = null; ;
        if (configArg == null)
        {
            DBg.d(LogLevel.Critical, "Startup No config file specified. Exiting.");
            return null;
        }
        else
        {
            configPath = configArg.Split('=')[1];

        }
        // check that the config file exists
        if (!File.Exists(configPath))
        {
            DBg.d(LogLevel.Critical, $"Startup specified Config file {configPath} does not exist. Exiting.");
            return null;
        }
        else
        {

            return configPath;
        }

    }
 
    public static LogLevel castRunLevel(string level)
    {
        var returnLevel = LogLevel.None;
        switch (level)
        {
            case "trace":
                returnLevel = LogLevel.Trace;
                break;
            case "debug":
                returnLevel = LogLevel.Debug;
                break;
            case "info":
                returnLevel = LogLevel.Debug;
                break;
            case "warn":
                returnLevel = LogLevel.Warning;
                break;
            case "error":
                returnLevel = LogLevel.Error;
                break;
            case "critical":
                returnLevel = LogLevel.Critical;
                break;
            default:
                DBg.d(LogLevel.Critical, $"Unexpected value for runlevel: {level}");
                break;

        }
        return returnLevel;


    }
}

