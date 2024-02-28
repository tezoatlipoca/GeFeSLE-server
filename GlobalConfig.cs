using System.Reflection;

public class SuperUser
{
    public string? Username { get; set; } = null;
    public string? Password { get; set; } = null;
    public string Role { get; set; } = "SuperUser";

    public SuperUser(string username, string password)
    {

        Username = username;
        Password = password;

    }

}

public static class GlobalConfig
{
    // define get and set methods for port, bind, hostname, and hostport
    // the difference between Bind+Port and Hostname+Hostport is that Bind+Port is the address that the server listens on, 
    //while Hostname+Hostport is the address that the server tells clients to connect to
    // to handle reverse proxies, nats etc. 
    // its the latter that gets written to HTML and RSS and Federation elements; i.e. the valid back reference to this instance.
    public static int Port { get; set; }
    public static string? Bind { get; set; }
    public static string? Hostname { get; set; }
    public static int Hostport { get; set; }
    public static string? wwwroot { get; set; }

    public static LogLevel CURRENT_LEVEL { get; set; }

    // store the static HTML "injection" files for site header, top of body and page footers. 
    // note these are filenames, not the actual HTML. But when used, you should read the file and inject it into the output.
    public static string? htmlHead { get; set; }
    public static string? bodyHeader { get; set; }
    public static string? bodyFooter { get; set; }
    public static string? bldVersion { get; set; }
    public static string? sitetitle { get; set; }
    public static string? owner { get; set; }

    public static SuperUser? backdoorAdmin { get; set; }

    // the secret key for JWT bearer tokens - issued to API clients when they log in.
    // we're using only the default HS256 algorithm, so your secret key SHOULD be 
    // at least 32 characters long, the longer the better and with a mix of uc/lc/numbers/specials.  
    // these will last for the specified duration below
    public static string? apiTokenSecretKey { get; set; } = "IlikeBIGbuttsandIcannotlie!Youotherbrotherscan'tdeny!WhenagirlwalksinwithanittybittywaistandaroundthinginyourfaceyougetSPRUNG!";

    public static TimeSpan apiTokenDuration { get; set; } = TimeSpan.Parse("1.00:00:00"); // 1 day   



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
        Hostport = config.GetValue<int>("ServerSettings:Port");
        if (Hostport == 0) Hostport = 5000;
        wwwroot = config.GetValue<string>("ServerSettings:wwwroot");
        if (wwwroot == null) wwwroot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        // if wwwroot doesn't exist, create it
        if (!Directory.Exists(wwwroot))
        {
            Directory.CreateDirectory(wwwroot);
        }
        DBg.d(LogLevel.Debug, $"Port: {Port}");
        DBg.d(LogLevel.Debug, $"Bind: {Bind}");
        DBg.d(LogLevel.Debug, $"Hostname: {Hostname}");
        DBg.d(LogLevel.Debug, $"Hostport: {Hostport}");
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
        
        // get the sitename from the config file
        sitetitle = config.GetValue<string>("SiteCustomize:sitetitle");
        if (sitetitle == null) sitetitle = "GeFeSLE Lists";

        // get the owner from the config file
        owner = config.GetValue<string>("SiteCustomize:owner");

        // probably not kosher, but I'm lazy
        // get the admin user from the config file
        backdoorAdmin = config.GetSection("Users:backdooradmin").Get<SuperUser>();
        // in /LOGIN we'll make sure that a user with these credentials will always 
        // authenticate as an admin, no matter what the database says.
        if (backdoorAdmin != null)
        {
            DBg.d(LogLevel.Debug, $"Backdoor admin user: {backdoorAdmin.Username}");
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
            DBg.d(LogLevel.Critical, "StartupNo config file specified. Exiting.");
            return null;
        }
        else
        {
            configPath = configArg.Split('=')[1];

        }
        // check that the config file exists
        if (!File.Exists(configPath))
        {
            DBg.d(LogLevel.Critical, $"Startupspecified Config file {configPath} does not exist. Exiting.");
            return null;
        }
        else
        {

            return configPath;
        }

    }
    public static void changeRunLevel(LogLevel newlevel, WebApplicationBuilder builder)
    {
        DBg.d(LogLevel.Trace, "GlobalConfig.changeRunLevel() called");
        if (CURRENT_LEVEL != newlevel)
        {
            DBg.d(LogLevel.Debug, $"Changed runlevel from {CURRENT_LEVEL} to {newlevel}");
            CURRENT_LEVEL = newlevel;
            // also change the LogLevel of Microsoft.EntityFrameworkCore
            builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", newlevel + 1);
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

