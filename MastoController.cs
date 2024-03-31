using System.Runtime.Serialization;

public static class MastoController
{
    // Add your static methods and properties here

    public static async Task unbookmarkMastoItems(
            string token, 
            string realizedInstance, 
            List<string> unbookmarkIDs) {

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
        if(unbookmarkIDs.Count <= 0) {
            DBg.d(LogLevel.Trace, "unbookmarkIDs.Count == 0 - NOTHING TO DO");
            return;
        }
        else {
            DBg.d(LogLevel.Trace, "unbookmarkIDs.Count >= 1");

            for(int i = 0; i < unbookmarkIDs.Count; i++) {
                string id = unbookmarkIDs[i];
                string url = realizedInstance + "/api/v1/statuses/" + id + "/unbookmark";
                DBg.d(LogLevel.Trace, "url: " + url);
                var response = await client.PostAsync(url, null);
                DBg.d(LogLevel.Trace, "response: " + response);
                if(response.IsSuccessStatusCode) {
                    DBg.d(LogLevel.Trace, "response.IsSuccessStatusCode");
                }
                else {
                    DBg.d(LogLevel.Trace, "response.IsSuccessStatusCode == false");
                }
                await Task.Delay(mastoApiRateLimitMs);
            }


        }
        

    }
}

