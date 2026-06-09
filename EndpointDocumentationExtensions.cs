using System.Text.Json;
using System.Reflection;
using Microsoft.OpenApi.Models;

namespace GeFeSLE;

public static class EndpointDocumentationExtensions
{
    private static readonly Lazy<Dictionary<string, EndpointDocConfig>> Docs =
        new(LoadDocsFromJson);

    private static readonly MethodInfo AcceptsTypedMethod =
        typeof(EndpointDocumentationExtensions)
            .GetMethod(nameof(AcceptsTyped), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ProducesTypedMethod =
        typeof(EndpointDocumentationExtensions)
            .GetMethod(nameof(ProducesTyped), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static RouteHandlerBuilder WithEndpointDocs(this RouteHandlerBuilder builder, string endpointName)
    {
        builder = builder.WithName(endpointName);

        var key = NormalizeKey(endpointName);
        if (!Docs.Value.TryGetValue(key, out var cfg))
        {
            return builder;
        }

        if (!string.IsNullOrWhiteSpace(cfg.Summary))
            builder = builder.WithSummary(cfg.Summary);

        if (!string.IsNullOrWhiteSpace(cfg.Description))
            builder = builder.WithDescription(cfg.Description);

        if (cfg.Tags.Count > 0)
            builder = builder.WithTags(cfg.Tags.ToArray());

        if (cfg.Accepts is not null)
            builder = ApplyAccepts(builder, cfg.Accepts);

        foreach (var p in cfg.Produces.OrderBy(x => x.Key))
            builder = ApplyProduces(builder, p.Key, p.Value);

        if (cfg.Produces.Count > 0)
        {
            builder = builder.WithOpenApi(op =>
            {
                foreach (var p in cfg.Produces)
                {
                    var statusCode = p.Key.ToString();
                    var desc = p.Value.Description;
                    if (string.IsNullOrWhiteSpace(desc))
                        continue;

                    if (!op.Responses.TryGetValue(statusCode, out var response))
                    {
                        response = new OpenApiResponse();
                        op.Responses[statusCode] = response;
                    }

                    response.Description = desc;
                }

                return op;
            });
        }

        return builder;
    }

    private static RouteHandlerBuilder ApplyAccepts(RouteHandlerBuilder builder, AcceptsDocConfig cfg)
    {
        var contentType = string.IsNullOrWhiteSpace(cfg.ContentType) ? "application/json" : cfg.ContentType;
        if (string.IsNullOrWhiteSpace(cfg.RequestType))
            return builder;

        var type = ResolveType(cfg.RequestType);
        if (type is null)
            return builder;

        var generic = AcceptsTypedMethod.MakeGenericMethod(type);
        return (RouteHandlerBuilder)generic.Invoke(null, new object[] { builder, contentType })!;
    }

    private static RouteHandlerBuilder ApplyProduces(RouteHandlerBuilder builder, int statusCode, ProducesDocConfig cfg)
    {
        if (cfg.NoBody || string.IsNullOrWhiteSpace(cfg.ResponseType))
            return builder.Produces(statusCode);

        var contentType = string.IsNullOrWhiteSpace(cfg.ContentType) ? "application/json" : cfg.ContentType;
        var type = ResolveType(cfg.ResponseType);
        if (type is null)
            return builder.Produces(statusCode);

        var generic = ProducesTypedMethod.MakeGenericMethod(type);
        return (RouteHandlerBuilder)generic.Invoke(null, new object[] { builder, statusCode, contentType })!;
    }

    private static RouteHandlerBuilder AcceptsTyped<T>(RouteHandlerBuilder builder, string contentType)
        where T : notnull
        => builder.Accepts<T>(contentType);

    private static RouteHandlerBuilder ProducesTyped<T>(RouteHandlerBuilder builder, int statusCode, string contentType)
        where T : notnull
        => builder.Produces<T>(statusCode, contentType);

    private static Dictionary<string, EndpointDocConfig> LoadDocsFromJson()
    {
        var result = new Dictionary<string, EndpointDocConfig>(StringComparer.OrdinalIgnoreCase);

        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "swagger-endpoints.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "swagger-endpoints.json")
        };

        var filePath = candidates.FirstOrDefault(File.Exists);
        if (string.IsNullOrEmpty(filePath))
            return result;

        using var doc = JsonDocument.Parse(File.ReadAllText(filePath));
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var endpointNode in doc.RootElement.EnumerateObject())
        {
            if (endpointNode.Value.ValueKind != JsonValueKind.Object)
                continue;

            var cfg = new EndpointDocConfig();
            var node = endpointNode.Value;

            cfg.Summary = GetString(node, "summary");
            cfg.Description = GetString(node, "description");

            // support both spellings: withtags + withthags
            cfg.Tags.AddRange(GetStringList(node, "withtags"));
            if (cfg.Tags.Count == 0)
                cfg.Tags.AddRange(GetStringList(node, "withthags"));

            var acceptsNode = GetProperty(node, "accepts");
            if (acceptsNode is JsonElement a)
            {
                if (a.ValueKind == JsonValueKind.Object)
                {
                    cfg.Accepts = new AcceptsDocConfig
                    {
                        RequestType = GetString(a, "requestType") ?? GetString(a, "type"),
                        ContentType = GetString(a, "contentType") ?? "application/json"
                    };
                }
                else if (a.ValueKind == JsonValueKind.String)
                {
                    cfg.Accepts = new AcceptsDocConfig
                    {
                        RequestType = null,
                        ContentType = a.GetString() ?? "application/json"
                    };
                }
            }

            var producesNode = GetProperty(node, "produces");
            if (producesNode is JsonElement p && p.ValueKind == JsonValueKind.Object)
            {
                foreach (var status in p.EnumerateObject())
                {
                    if (!int.TryParse(status.Name, out var statusCode))
                        continue;

                    var pcfg = new ProducesDocConfig();
                    if (status.Value.ValueKind == JsonValueKind.Object)
                    {
                        pcfg.ResponseType = GetString(status.Value, "responseType") ?? GetString(status.Value, "type");
                        pcfg.ContentType = GetString(status.Value, "contentType") ?? "application/json";
                        pcfg.Description = GetString(status.Value, "description");
                        pcfg.NoBody = GetBool(status.Value, "noBody");
                    }
                    else if (status.Value.ValueKind == JsonValueKind.String)
                    {
                        pcfg.Description = status.Value.GetString();
                        pcfg.NoBody = true;
                    }

                    cfg.Produces[statusCode] = pcfg;
                }
            }

            result[NormalizeKey(endpointNode.Name)] = cfg;
        }

        return result;
    }

    private static JsonElement? GetProperty(JsonElement node, string propertyName)
    {
        foreach (var p in node.EnumerateObject())
        {
            if (string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                return p.Value;
        }

        return null;
    }

    private static string? GetString(JsonElement node, string propertyName)
    {
        var prop = GetProperty(node, propertyName);
        return prop is JsonElement p && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    }

    private static bool GetBool(JsonElement node, string propertyName)
    {
        var prop = GetProperty(node, propertyName);
        return prop is JsonElement p && p.ValueKind == JsonValueKind.True;
    }

    private static List<string> GetStringList(JsonElement node, string propertyName)
    {
        var list = new List<string>();
        var prop = GetProperty(node, propertyName);
        if (prop is not JsonElement p)
            return list;

        if (p.ValueKind == JsonValueKind.String)
        {
            var value = p.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                list.Add(value);
            return list;
        }

        if (p.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in p.EnumerateArray())
            {
                if (e.ValueKind == JsonValueKind.String)
                {
                    var value = e.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        list.Add(value);
                }
            }
        }

        return list;
    }

    private static string NormalizeKey(string input)
        => input.Replace("_", string.Empty).Replace("-", string.Empty).Trim().ToLowerInvariant();

    private static Type? ResolveType(string rawName)
    {
        var name = rawName.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var aliases = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["string"] = typeof(string),
            ["int"] = typeof(int),
            ["integer"] = typeof(int),
            ["long"] = typeof(long),
            ["bool"] = typeof(bool),
            ["boolean"] = typeof(bool),
            ["jsonelement"] = typeof(JsonElement)
        };

        if (aliases.TryGetValue(name, out var aliasType))
            return aliasType;

        var direct = Type.GetType(name, throwOnError: false, ignoreCase: true);
        if (direct is not null)
            return direct;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var fullNameMatch = asm.GetTypes().FirstOrDefault(t =>
                string.Equals(t.FullName, name, StringComparison.OrdinalIgnoreCase));
            if (fullNameMatch is not null)
                return fullNameMatch;

            var shortNameMatch = asm.GetTypes().FirstOrDefault(t =>
                string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (shortNameMatch is not null)
                return shortNameMatch;
        }

        return null;
    }

    private sealed class EndpointDocConfig
    {
        public string? Summary { get; set; }
        public string? Description { get; set; }
        public List<string> Tags { get; } = new();
        public AcceptsDocConfig? Accepts { get; set; }
        public Dictionary<int, ProducesDocConfig> Produces { get; } = new();
    }

    private sealed class AcceptsDocConfig
    {
        public string? RequestType { get; set; }
        public string? ContentType { get; set; }
    }

    private sealed class ProducesDocConfig
    {
        public string? ResponseType { get; set; }
        public string? ContentType { get; set; }
        public string? Description { get; set; }
        public bool NoBody { get; set; }
    }
}