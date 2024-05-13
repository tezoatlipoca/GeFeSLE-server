using System.Text;
using Newtonsoft.Json;
//using Mastonet.Entities;
using Microsoft.AspNetCore.Authentication;
//using TootNet.Objects;


public static class MastoController
{
    // Add your static methods and properties here

    public static async Task unbookmarkMastoItems(
            string token,
            string realizedInstance,
            List<string> unbookmarkIDs)
    {

        //TODOs/ASSUMPTIONS:
        // token is valid
        // realizedInstance is valid
        // the user's token may invalidate partway through the deletion process, we don't really guard against that. 

        DBg.d(LogLevel.Trace, "unbookmarkIDs.Count: " + unbookmarkIDs.Count);
        // Mastodon API rate limits to 300 requests per 5 minutes
        // that's 1 request every second
        int mastoApiRateLimitMs = 1000;

        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
        if (unbookmarkIDs.Count <= 0)
        {
            DBg.d(LogLevel.Trace, "unbookmarkIDs.Count == 0 - NOTHING TO DO");
            return;
        }
        else
        {
            DBg.d(LogLevel.Trace, "unbookmarkIDs.Count >= 1");

            for (int i = 0; i < unbookmarkIDs.Count; i++)
            {
                string id = unbookmarkIDs[i];
                string url = realizedInstance + "/api/v1/statuses/" + id + "/unbookmark";
                DBg.d(LogLevel.Trace, "url: " + url);
                var response = await client.PostAsync(url, null);
                DBg.d(LogLevel.Trace, "response: " + response);
                if (response.IsSuccessStatusCode)
                {
                    DBg.d(LogLevel.Trace, "response.IsSuccessStatusCode");
                }
                else
                {
                    DBg.d(LogLevel.Trace, "response.IsSuccessStatusCode == false");
                }
                await Task.Delay(mastoApiRateLimitMs);
            }


        }


    }

    // takes a raw Fediverse instance from the user; makes sure its proper form
    // and then checks to see if the instance is up
    // if it IS, returned instance is good to go with further calls
    public static async Task<(bool, string)> checkInstance(string instance)
    {
        string fn = "checkInstance"; DBg.d(LogLevel.Trace, fn);
        bool checkInstance = false;
        string? ynot = null;
        if (instance is null)
        {
            ynot = "Instance is null";
            return (checkInstance, ynot);
        }
        else
        {
            // if the instance doesn't start with http:// or https://, add it
            if (!instance.StartsWith("http://") && !instance.StartsWith("https://"))
            {
                instance = "https://" + instance;
            }
            DBg.d(LogLevel.Debug, $"{fn} - checking instance: {instance}");
            var client = new HttpClient();
            

            try
            {
                var response = await client.GetAsync($"{instance}/api/v1/instance");
                if (response.IsSuccessStatusCode)
                {
                    checkInstance = true;
                    ynot = instance; // save a check for https:// in the next step
                }
                else
                {
                    ynot = "Instance is down";
                }
            }
            catch (Exception e)
            {
                ynot = e.Message;
            }
            return (checkInstance, ynot);
        }
    }

    // unlike Microsoft or Google where you pre-register your app with a singular service
    // you have to register your app with each individual Fediverse instance
    // TODO: we should cache the application registration for each instance/save for later
    //        in the database. 
    public static async Task<(ApplicationToken?, string)> registerAppWithInstance(string instance)
    {
        string? ynot = null;
        ApplicationToken? application = null;
        // construct our redirect Uri using our external hostname and port
        string redirectUri = Uri.EscapeDataString($"{GlobalConfig.Hostname}/mastocallback");

        string scopes = GlobalConfig.mastoScopes;
        if(scopes is null || GlobalConfig.mastoClient_Name is null)
        {
            ynot = "No Mastodon API scopes/Client name defined in config file - cannot register app with instance.";
            return (application, ynot);
        }


        string appRegisterUrl = $"{instance}/api/v1/apps";
        DBg.d(LogLevel.Trace, $"Registering app at: {appRegisterUrl}");

        string postData = $"client_name={GlobalConfig.mastoClient_Name}&redirect_uris={redirectUri}&scopes={scopes}&website={GlobalStatic.webSite}";
        DBg.d(LogLevel.Trace, $"Sending registration postData: {postData}");

        var content = new StringContent(postData, Encoding.UTF8, "application/x-www-form-urlencoded");
        var client = new HttpClient();
        HttpResponseMessage response = await client.PostAsync(appRegisterUrl, content);
        // response can be OK 200 or 422 Unprocessable Entity
        // if its 422, WE are going to return a bad request and itemize what could be wrong
        if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            var error = await response.Content.ReadAsStringAsync();
            ynot = $"Mastodon instance {instance} returned 422: {error} - REDIRECT URI==HOSTNAME:HOSTPORT/mastocallback - check your config file. Sent: {postData}";
        }
        // mastodon API doesn't say anything about any other status code result, 422 and 200.
        else if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            ynot = $"Mastodon instance {instance} returned {response.StatusCode}: {error} - sent: {postData}";
        }
        else
        {
            // at this point our response contains client_id and client_secret
            // TODO: we're gonna want to save the client_id and client_secret
            // on a PER instance basis, we don't want to register a new app for EVERY user 
            // from that instance. 

            var rawApplication = await response.Content.ReadAsStringAsync();
            application = JsonConvert.DeserializeObject<ApplicationToken>(rawApplication);
            // in the deserialized token object we want properties client_id and client_secret
            if (application is not null)
            {
                application.instance = instance;
            }
        }
        return (application, ynot);

    }

    
    

    public static void storeMastoToken(HttpContext context, ApplicationToken application)
    {
        string applicationStr = JsonConvert.SerializeObject(application);
        DBg.d(LogLevel.Trace, $"Storing Mastodon Application token in session: {applicationStr}");
        context.Session.SetString("masto.app_id", application.id);
        context.Session.SetString("masto.client_id", application.client_id);
        context.Session.SetString("masto.client_secret", application.client_secret);
        context.Session.SetString("masto.instance", application.instance);
    }

    public static ApplicationToken? getMastoToken(HttpContext context)
    {
        string? appId = context.Session.GetString("masto.app_id");
        string? clientId = context.Session.GetString("masto.client_id");
        string? clientSecret = context.Session.GetString("masto.client_secret");
        string? instance = context.Session.GetString("masto.instance");
        if (appId is null || clientId is null || clientSecret is null || instance is null)
        {
            return null;
        }
        else
        {
            ApplicationToken application = new ApplicationToken
            {
                id = appId,
                client_id = clientId,
                client_secret = clientSecret,
                instance = instance
            };
            return application;
        }
    }

    // unlike the google Oauth2 login, we have to manually construct the POST request to the Mastodon server to get 
    // the authorization URL
    // because we don't know what mastodon instance the user's going to specify ahead of time.
    // but we've already got these stored in the user's session cookie

    public static string? getMastodonOAuthUrl(ApplicationToken application)
    {
        string? getMastodonOAuthUrl = null;
        if(GlobalConfig.mastoScopes is null)
        {
            DBg.d(LogLevel.Error, $"No Mastodon API scopes defined in config file - cannot construct authorization URL.");
            return null;
        }
        if (!(application.id is null ||
            application.client_id is null ||
            application.client_secret is null ||
            application.instance is null))
        {
            string redirectUri = Uri.EscapeDataString($"{GlobalConfig.Hostname}/mastocallback");
            DBg.d(LogLevel.Debug, $"Constructed redirectUri: {redirectUri}");
            getMastodonOAuthUrl = $"{application.instance}/oauth/authorize?client_id={application.client_id}&response_type=code&redirect_uri={redirectUri}&scope={Uri.EscapeDataString(GlobalConfig.mastoScopes)}";
            DBg.d(LogLevel.Trace, $"Constructed authorizationUrl: {getMastodonOAuthUrl}");

        }
        return getMastodonOAuthUrl;
    }


}