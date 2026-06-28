using GeFeSLE;

public static class NodeInfoEndpointRouteExtensions
{
    public static IEndpointRouteBuilder MapNodeInfoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        static void Trace(string fn)
        {
            DBg.d(LogLevel.Trace, fn);
        }

        static RouteHandlerBuilder MapNodeInfoVersion(
            IEndpointRouteBuilder routeBuilder,
            string path,
            Func<string, GeFeSLEDb, Task<IResult>> handler)
        {
            return routeBuilder.MapGet(path, async (GeFeSLEDb db) =>
            {
                string fn = $"{path} (GET)";
                Trace(fn);
                return await handler(fn, db);
            }).AllowAnonymous();
        }

        endpoints.MapGet("/.well-known/nodeinfo", () =>
            {
                string fn = "/.well-known/nodeinfo (GET)";
                Trace(fn);
                return NodeInfoEndpointService.GetDiscoveryDocument(fn);
            })
            .WithEndpointDocs("nodeinfo.discovery.get")
            .AllowAnonymous();

        endpoints.MapGet("/.well-known/x-nodeinfo2", () =>
            {
                string fn = "/.well-known/x-nodeinfo2 (GET)";
                Trace(fn);
                return NodeInfoEndpointService.GetDiscoveryDocument(fn);
            })
            .AllowAnonymous();

        MapNodeInfoVersion(endpoints, "/nodeinfo/1.0", NodeInfoEndpointService.GetNodeInfo10DocumentAsync);
        MapNodeInfoVersion(endpoints, "/nodeinfo/1.0.json", NodeInfoEndpointService.GetNodeInfo10DocumentAsync);
        MapNodeInfoVersion(endpoints, "/nodeinfo/2.0", NodeInfoEndpointService.GetNodeInfo20DocumentAsync);
        MapNodeInfoVersion(endpoints, "/nodeinfo/2.0.json", NodeInfoEndpointService.GetNodeInfo20DocumentAsync);
        MapNodeInfoVersion(endpoints, "/nodeinfo/2.1", NodeInfoEndpointService.GetNodeInfo21DocumentAsync)
            .WithEndpointDocs("nodeinfo.2_1.get");
        MapNodeInfoVersion(endpoints, "/nodeinfo/2.1.json", NodeInfoEndpointService.GetNodeInfo21DocumentAsync);

        endpoints.MapGet("/statistics.json", async (GeFeSLEDb db) =>
            {
                string fn = "/statistics.json (GET)";
                Trace(fn);
                return await NodeInfoEndpointService.GetStatisticsDocumentAsync(fn, db);
            })
            .AllowAnonymous();

        endpoints.MapGet("/status.php", () =>
            {
                string fn = "/status.php (GET)";
                Trace(fn);
                return NodeInfoEndpointService.GetStatusDocument(fn);
            })
            .AllowAnonymous();

        endpoints.MapGet("/.well-known/host-meta", () =>
            {
                string fn = "/.well-known/host-meta (GET)";
                Trace(fn);
                return NodeInfoEndpointService.GetHostMetaDocument(fn);
            })
            .AllowAnonymous();

        endpoints.MapGet("/xrpc/com.atproto.server.describeServer", () =>
            {
                string fn = "/xrpc/com.atproto.server.describeServer (GET)";
                Trace(fn);
                return NodeInfoEndpointService.GetAtProtoDescribeServerDocument(fn);
            })
            .AllowAnonymous();

        endpoints.MapGet("/xrpc/_health", () =>
            {
                string fn = "/xrpc/_health (GET)";
                Trace(fn);
                return NodeInfoEndpointService.GetAtProtoHealthDocument(fn);
            })
            .AllowAnonymous();

        return endpoints;
    }
}
