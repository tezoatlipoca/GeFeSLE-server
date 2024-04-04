
using Newtonsoft.Json;
using Microsoft.Graph;
using Microsoft.Graph.Auth;
using Microsoft.Identity.Web;
using Microsoft.Identity.Client;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using GeFeSLE;
using Newtonsoft.Json.Linq;
using HtmlAgilityPack;
using ReverseMarkdown;
public static class MicrosoftController
{
    private static string MICROSOFT_GRAPH_STICKY_NOTES_API_URL = "https://graph.microsoft.com/v1.0/me/MailFolders/notes/messages";

    // private static ClientCredentialProvider credentialProvider()
    // {
    //     string fn = "credentialProvider";
    //     DBg.d(LogLevel.Trace, fn);
    //     // get the client id and client secret from the appsettings.json file
    //     string clientId = GlobalStatic.microsoftClientId;
    //     string clientSecret = GlobalStatic.microsoftClientSecret;
    //     string tenantId = GlobalStatic.microsoftTenantId;
    //     // create a new instance of the ClientCredentialProvider
    //     IConfidentialClientApplication confidentialClientApplication = ConfidentialClientApplicationBuilder
    //      .Create(clientId)
    //         .WithTenantId(tenantId)
    //         .WithClientSecret(clientSecret)
    //         .Build();
    //     ClientCredentialProvider authProvider = new ClientCredentialProvider(confidentialClientApplication);
    //     return authProvider;
    // }

    // get Windows "Sticky Notes"
    // surprisingly these are just stored in a special mail folder in a Microsoft
    // account's MailFolders, e.g.:
    // https://graph.microsoft.com/v1.0/me/MailFolders/notes/messages
    //
    // here we assume the user has already been OAuthenticated
    // and we have a valid access token stored in the session
    public static async Task<List<GeListItem>>? getMicrosoftOutlookTasks(HttpContext context, string? accessToken)
    {
        string fn = "getMicrosoftOutlookTasks";
        DBg.d(LogLevel.Trace, fn);
        List<GeListItem> geListItems = null;
        if (accessToken == null)
        {
            DBg.d(LogLevel.Error, $"{fn} - No access token found in session");
            return geListItems;
        }

        // using Microsoft.Graph -- couldn't get to work. 
        // Reference to type 'IAuthenticationProvider' claims it is defined in 'Microsoft.Graph.Core', 
        // but it could not be foundCS7069
        //
        // ClientCredentialProvider authProvider = credentialProvider();

        // GraphServiceClient graphClient = new GraphServiceClient(authProvider);

        // var messages = await graphClient.Me.MailFolders["notes"].Messages
        //     .Request()
        //     .GetAsync();

        // foreach (var message in messages)
        // {
        //     Console.WriteLine(message.Subject);
        // }

        // sometimes the old school is just better
        // can still make use of the Graph objects tho. 
        //create httpClient;
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + accessToken);

        var response = await client.GetAsync(MICROSOFT_GRAPH_STICKY_NOTES_API_URL);
        // handle various response.statusCodes
        switch (response.StatusCode)
        {
            case System.Net.HttpStatusCode.OK:
                var content = await response.Content.ReadAsStringAsync();
                geListItems = parseMicrosoftOutlookTasks(content);
                break;
            default:
                DBg.d(LogLevel.Error, $"{fn} - Error getting sticky notes: {response.StatusCode}");
                break;

        }
        return geListItems;
    }

    private static List<GeListItem> parseMicrosoftOutlookTasks(string content)
    {
        string fn = "parseMicrosoftOutlookTasks";
        DBg.d(LogLevel.Trace, fn);

        if (content == null)
        {
            DBg.d(LogLevel.Error, $"{fn} - content is null");
            return null;
        }

        // the return from https://graph.microsoft.com/v1.0/me/MailFolders/notes/messages
        // is a collection of Graph.Message objects wrapped in a value property
        // {
        // "@odata.context": "https://graph.microsoft.com/v1.0/$metadata#users('user-id')/MailFolders('folder-id')/messages",
        // "value": [
        // Array of message objects...
        //]
        //}
        //
        // attempt to deserialize the content into a dynamic object
        JObject json = JObject.Parse(content);
        // extract the value property
        //convert this to arry of Graph.Message objects
        //var messagesArray = JsonConvert.DeserializeObject<Microsoft.Graph.Message[]>(messages.ToString());
        // nope: The type or namespace name 'Message' does not exist in the name space 'Microsoft.Graph'
        // wtf. 
        // do it old school
        //prettyJson = JsonConvert.SerializeObject(json, Formatting.Indented);

        JArray messages = (JArray)json["value"];

        // create an array of GeListItem
        List<GeListItem> geListItems = new List<GeListItem>();
        // if there are no messages, return an empty list
        if (messages == null)
        {
            DBg.d(LogLevel.Error, $"{fn} - no messages found");
            return geListItems;
        }
        foreach (dynamic message in messages)
        {
            // create a new GeListItem
            GeListItem geListItem = new GeListItem();
            // set the name to the subject of the message
            geListItem.Name = message["subject"];
            // set the comment to the body of the message
            string htmlContent = message["body"]["content"];
            var htmlDocument = new HtmlDocument();
            htmlDocument.LoadHtml(htmlContent);
            var bodyNode = htmlDocument.DocumentNode.SelectSingleNode("//body");
            if (bodyNode != null)
                {
                geListItem.Comment = bodyNode.InnerHtml;
                }
            else
                {
                geListItem.Comment = "<i>Empty note!<i>";
                }
            // geListItem.Comment is HTML - lets convert it to markdown
            var converter = new Converter();
            geListItem.Comment = converter.Convert(geListItem.Comment);
            
            // replace any newlines with <br> tags
            //geListItem.Comment = geListItem.Comment.Replace("\r\n", "<br>");
            // set the created date to the received date of the message
            geListItem.CreatedDate = message["createdDateTime"];
            // set the modified date to the last modified date of the message
            geListItem.ModifiedDate = message["lastModifiedDateTime"];
            // add the GeListItem to the list
            geListItems.Add(geListItem);
        }


        //DBg.d(LogLevel.Information, prettyJson);
        return geListItems;

    }
}
