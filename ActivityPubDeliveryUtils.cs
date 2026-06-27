using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public static class ActivityPubDeliveryUtils
{
    public static string ActivityPubKeyIdForActor(string actorUrl)
    {
        return $"{actorUrl}#main-key";
    }

    public static string ComputeBodyDigestSha256(string payload)
    {
        byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);
        byte[] digestBytes = SHA256.HashData(payloadBytes);
        return $"SHA-256={Convert.ToBase64String(digestBytes)}";
    }

    public static string BuildActivityPubSignatureHeader(HttpMethod method, Uri requestUri, string dateHeaderValue, string digestHeaderValue, string contentType, string actorUrl, RSA signingKey)
    {
        string requestTarget = $"{method.Method.ToLowerInvariant()} {requestUri.PathAndQuery}";
        string hostHeader = requestUri.IsDefaultPort ? requestUri.Host : requestUri.Authority;
        string signingString =
            $"(request-target): {requestTarget}\n" +
            $"host: {hostHeader}\n" +
            $"date: {dateHeaderValue}\n" +
            $"digest: {digestHeaderValue}\n" +
            $"content-type: {contentType}";

        byte[] signature = signingKey.SignData(
            Encoding.UTF8.GetBytes(signingString),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        string signatureB64 = Convert.ToBase64String(signature);
        string keyId = ActivityPubKeyIdForActor(actorUrl);

        return $"keyId=\"{keyId}\",algorithm=\"rsa-sha256\",headers=\"(request-target) host date digest content-type\",signature=\"{signatureB64}\"";
    }

    public static async Task<bool> SendSignedActivityPubMessageAsync(string inboxUrl, string actorUrl, object activityPayload, string successLogMessage, RSA? signingKey)
    {
        if (signingKey is null)
        {
            DBg.d(LogLevel.Warning, $"Cannot send ActivityPub message to {inboxUrl}: signing key is not configured.");
            return false;
        }

        if (!Uri.TryCreate(inboxUrl, UriKind.Absolute, out var inboxUri))
        {
            DBg.d(LogLevel.Warning, $"ActivityPub POST aborted: invalid inbox URL {inboxUrl}");
            return false;
        }

        string payload = JsonSerializer.Serialize(activityPayload);
        if (ActivityPubActivityLogStore.IsPartialLoggingEnabled())
        {
            DBg.d(LogLevel.Trace,
                $"ActivityPub outbound message to follower inbox {inboxUrl} from actor {actorUrl}:\n" +
                JsonSerializer.Serialize(activityPayload, new JsonSerializerOptions { WriteIndented = true }));
        }
        string digestHeader = ComputeBodyDigestSha256(payload);
        string dateHeader = DateTimeOffset.UtcNow.ToString("r");

        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, inboxUri);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/activity+json");
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/activity+json");

        string contentTypeHeader = request.Content.Headers.ContentType?.ToString() ?? "application/activity+json";

        string signatureHeader;
        try
        {
            signatureHeader = BuildActivityPubSignatureHeader(HttpMethod.Post, inboxUri, dateHeader, digestHeader, contentTypeHeader, actorUrl, signingKey);
        }
        catch (Exception ex)
        {
            DBg.d(LogLevel.Warning, $"ActivityPub POST aborted: could not sign request for {inboxUrl}. {ex.Message}");
            return false;
        }

        request.Headers.Host = inboxUri.IsDefaultPort ? inboxUri.Host : inboxUri.Authority;
        request.Headers.TryAddWithoutValidation("Date", dateHeader);
        request.Headers.TryAddWithoutValidation("Digest", digestHeader);
        request.Headers.TryAddWithoutValidation("Signature", signatureHeader);
        request.Headers.TryAddWithoutValidation("Authorization", $"Signature {signatureHeader}");

        var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var responseText = await response.Content.ReadAsStringAsync();
            DBg.d(LogLevel.Warning, $"ActivityPub POST failed to {inboxUrl} ({(int)response.StatusCode} {response.StatusCode}): {responseText}");
            return false;
        }

        if (ActivityPubActivityLogStore.IsFullLoggingEnabled())
        {
            var writeResult = await ActivityPubActivityLogStore.TryWriteActivityPayloadAsync(payload);
            if (writeResult.Wrote && !string.IsNullOrWhiteSpace(writeResult.ActivityUrl))
            {
                DBg.d(LogLevel.Information, $"{successLogMessage} | Activity: {writeResult.ActivityUrl}");
            }
            else
            {
                DBg.d(LogLevel.Warning,
                    $"{successLogMessage} | Failed to persist ActivityPub activity payload: {writeResult.Error ?? "unknown error"}");
            }
        }
        else
        {
            DBg.d(LogLevel.Information, successLogMessage);
        }

        return true;
    }

    public static async Task<string?> ResolveActorInboxAsync(string actorIri, GeListFollower? knownFollower = null)
    {
        if (knownFollower is not null && !string.IsNullOrWhiteSpace(knownFollower.Inbox))
        {
            return knownFollower.Inbox;
        }

        if (knownFollower is not null)
        {
            var guessedInbox = GeListFollower.GuessInboxFromActorIri(actorIri);
            if (!string.IsNullOrWhiteSpace(guessedInbox))
            {
                knownFollower.Inbox = guessedInbox;
                return guessedInbox;
            }

            return null;
        }

        GeListFollower actorDetails = knownFollower ?? new GeListFollower
        {
            Id = actorIri,
            Type = "Person"
        };

        await actorDetails.FetchActorInfoFromIriAsync();
        if (!string.IsNullOrWhiteSpace(actorDetails.Inbox))
        {
            return actorDetails.Inbox;
        }

        var fallbackInbox = GeListFollower.GuessInboxFromActorIri(actorIri);
        if (!string.IsNullOrWhiteSpace(fallbackInbox))
        {
            return fallbackInbox;
        }

        return null;
    }
}
