using System.Text.Json;
using Microsoft.EntityFrameworkCore;

public static class NodeInfoEndpointService
{
    private const string NodeInfoSchema10 = "http://nodeinfo.diaspora.software/ns/schema/1.0";
    private const string NodeInfoSchema20 = "http://nodeinfo.diaspora.software/ns/schema/2.0";
    private const string NodeInfoSchema21 = "http://nodeinfo.diaspora.software/ns/schema/2.1";
    private const string RepositoryUrl = "https://github.com/tezoatlipoca/GeFeSLE-server";

    public static IResult GetDiscoveryDocument(string fn)
    {
        var response = new
        {
            links = new[]
            {
                new
                {
                    rel = NodeInfoSchema21,
                    href = BuildAbsoluteUrl("/nodeinfo/2.1")
                },
                new
                {
                    rel = NodeInfoSchema20,
                    href = BuildAbsoluteUrl("/nodeinfo/2.0")
                },
                new
                {
                    rel = NodeInfoSchema10,
                    href = BuildAbsoluteUrl("/nodeinfo/1.0")
                }
            }
        };

        EndpointLoggingHelpers.LogDtoOut(fn, "NodeInfoDiscovery", response);
        return EndpointLoggingHelpers.OkPayloadWithTrace(fn, response, "nodeinfo discovery returned");
    }

    public static async Task<IResult> GetNodeInfo10DocumentAsync(string fn, GeFeSLEDb db)
    {
        var usage = await GetUsageStatsAsync(db);

        var payload = new
        {
            version = "1.0",
            software = new
            {
                name = GlobalStatic.applicationName,
                version = GlobalConfig.bldVersion ?? "unknown"
            },
            protocols = new[] { "activitypub" },
            services = new { },
            openRegistrations = false,
            usage = new
            {
                users = new
                {
                    total = usage.ActiveUsers,
                    activeHalfyear = usage.ActiveUsers,
                    activeMonth = usage.ActiveUsers
                },
                localPosts = usage.LocalComments
            },
            metadata = new { }
        };

        EndpointLoggingHelpers.LogDtoOut(fn, "NodeInfo1_0", payload);
        return EndpointLoggingHelpers.OkPayloadWithTrace(fn, payload, "nodeinfo 1.0 returned");
    }

    public static async Task<IResult> GetNodeInfo20DocumentAsync(string fn, GeFeSLEDb db)
    {
        var usage = await GetUsageStatsAsync(db);

        string instanceRoot = BuildAbsoluteUrl("/");

        var payload = new
        {
            version = "2.0",
            software = new
            {
                name = GlobalStatic.applicationName,
                version = GlobalConfig.bldVersion ?? "unknown",
                homepage = instanceRoot,
                repository = RepositoryUrl
            },
            protocols = new[] { "activitypub" },
            services = new { },
            openRegistrations = false,
            usage = new
            {
                users = new
                {
                    total = usage.ActiveUsers,
                    activeHalfyear = usage.ActiveUsers,
                    activeMonth = usage.ActiveUsers
                },
                localPosts = usage.LocalComments
            }
        };

        EndpointLoggingHelpers.LogDtoOut(fn, "NodeInfo2_0", payload);
        return EndpointLoggingHelpers.OkPayloadWithTrace(fn, payload, "nodeinfo 2.0 returned");
    }

    public static async Task<IResult> GetNodeInfo21DocumentAsync(string fn, GeFeSLEDb db)
    {
        var usage = await GetUsageStatsAsync(db);

        string instanceRoot = BuildAbsoluteUrl("/");

        var payload = new
        {
            version = "2.1",
            software = new
            {
                name = GlobalStatic.applicationName,
                version = GlobalConfig.bldVersion ?? "unknown",
                homepage = instanceRoot,
                repository = RepositoryUrl
            },
            protocols = new[] { "activitypub" },
            services = new { },
            openRegistrations = false,
            usage = new
            {
                users = new
                {
                    total = usage.ActiveUsers,
                    activeHalfyear = usage.ActiveUsers,
                    activeMonth = usage.ActiveUsers
                },
                localPosts = usage.LocalComments,
                localComments = usage.LocalComments
            }
        };

        EndpointLoggingHelpers.LogDtoOut(fn, "NodeInfo2_1", payload);
        return EndpointLoggingHelpers.OkPayloadWithTrace(fn, payload, "nodeinfo 2.1 returned");
    }

    public static async Task<IResult> GetStatisticsDocumentAsync(string fn, GeFeSLEDb db)
    {
        var usage = await GetUsageStatsAsync(db);
        var payload = new
        {
            user_count = usage.ActiveUsers,
            status_count = usage.LocalComments,
            domain_count = 1
        };

        EndpointLoggingHelpers.LogDtoOut(fn, "StatisticsJson", payload);
        return EndpointLoggingHelpers.OkPayloadWithTrace(fn, payload, "statistics returned");
    }

    public static IResult GetStatusDocument(string fn)
    {
        return EndpointLoggingHelpers.ContentWithTrace(fn, "OK", "text/plain", "status probe returned");
    }

    public static IResult GetHostMetaDocument(string fn)
    {
        string templateUrl = BuildAbsoluteUrl("/.well-known/webfinger?resource={uri}");
        string xml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<XRD xmlns=\"http://docs.oasis-open.org/ns/xri/xrd-1.0\">" +
            $"<Link rel=\"lrdd\" type=\"application/xrd+xml\" template=\"{templateUrl}\" />" +
            "</XRD>";

        return EndpointLoggingHelpers.ContentWithTrace(fn, xml, "application/xrd+xml", "host-meta returned");
    }

    public static IResult GetAtProtoDescribeServerDocument(string fn)
    {
        string hostOnly = GetHostOnly();
        var payload = new
        {
            did = $"did:web:{hostOnly}",
            availableUserDomains = new[] { hostOnly },
            inviteCodeRequired = true,
            links = new
            {
                privacyPolicy = BuildAbsoluteUrl("/"),
                termsOfService = BuildAbsoluteUrl("/")
            }
        };

        EndpointLoggingHelpers.LogDtoOut(fn, "AtProtoDescribeServer", payload);
        return EndpointLoggingHelpers.OkPayloadWithTrace(fn, payload, "atproto describeServer returned");
    }

    public static IResult GetAtProtoHealthDocument(string fn)
    {
        var payload = new { status = "ok" };
        EndpointLoggingHelpers.LogDtoOut(fn, "AtProtoHealth", payload);
        return EndpointLoggingHelpers.OkPayloadWithTrace(fn, payload, "atproto health returned");
    }

    private static async Task<(int ActiveUsers, int LocalComments)> GetUsageStatsAsync(GeFeSLEDb db)
    {
        var candidateApLists = await db.Lists
            .Where(list => !string.IsNullOrWhiteSpace(list.ActivityPubId))
            .Select(list => new { list.Id, list.ActivityPubId })
            .ToListAsync();

        var validChars = new HashSet<char>(GlobalConfig.validAPListNameChars);
        var validApListIds = candidateApLists
            .Where(list => !string.IsNullOrWhiteSpace(list.ActivityPubId)
                && list.ActivityPubId!.All(c => validChars.Contains(c)))
            .Select(list => list.Id)
            .ToList();

        int activeUsers = validApListIds.Count;
        int localComments = 0;

        if (validApListIds.Count > 0)
        {
            localComments = await db.Items
                .Where(item => validApListIds.Contains(item.ListId))
                .Where(item => item.Visible && !item.IsDeleted)
                .CountAsync();
        }

        return (activeUsers, localComments);
    }

    private static string BuildAbsoluteUrl(string path)
    {
        string host = GlobalConfig.Hostname ?? "http://localhost";
        host = host.TrimEnd('/');

        if (string.IsNullOrEmpty(path) || path == "/")
        {
            return $"{host}/";
        }

        string normalizedPath = path.StartsWith('/') ? path : $"/{path}";
        return $"{host}{normalizedPath}";
    }

    private static string GetHostOnly()
    {
        string host = GlobalConfig.Hostname ?? "http://localhost";

        if (Uri.TryCreate(host, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.Host;
        }

        host = host.Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .TrimEnd('/');

        int colonIndex = host.IndexOf(':');
        return colonIndex > 0 ? host[..colonIndex] : host;
    }
}
