using System.Text.Json;

public static class ActivityPubActivityLogStore
{
    public static void RegisterMaintenanceTasks()
    {
        if (!IsFullLoggingEnabled())
        {
            return;
        }

        if (!TryGetRetentionTimeSpan(GlobalConfig.ActivityPubActivityRetention, out var retention))
        {
            DBg.d(LogLevel.Information, "ActivityPub activity retention is 'forever'; cleanup task not scheduled.");
            return;
        }

        MaintenanceScheduler.RegisterRecurringTask(
            taskName: "ActivityPubActivityCleanup",
            action: cancellationToken => CleanupExpiredActivitiesAsync(retention, cancellationToken),
            interval: TimeSpan.FromHours(24),
            initialDelay: TimeSpan.FromMinutes(5));
    }

    public static bool IsFullLoggingEnabled()
    {
        return GlobalConfig.EnableAPActivityLogging == APActivityLoggingMode.Full;
    }

    public static bool IsPartialLoggingEnabled()
    {
        return GlobalConfig.EnableAPActivityLogging == APActivityLoggingMode.Partial;
    }

    public static bool IsNoLoggingEnabled()
    {
        return GlobalConfig.EnableAPActivityLogging == APActivityLoggingMode.None;
    }

    public static string BuildActivityEndpointUrl(Guid activityId)
    {
        string host = (GlobalConfig.Hostname ?? string.Empty).TrimEnd('/');
        return $"{host}/apv1/activities/{activityId}";
    }

    public static async Task<(bool Wrote, string? ActivityUrl, string? Error)> TryWriteActivityPayloadAsync(string payload)
    {
        if (!IsFullLoggingEnabled())
        {
            return (false, null, null);
        }

        if (string.IsNullOrWhiteSpace(GlobalConfig.ActivityPubActivitiesFolder))
        {
            return (false, null, "ActivityPub activities folder is not configured.");
        }

        Guid? activityId = TryExtractActivityGuidFromPayload(payload);
        if (!activityId.HasValue)
        {
            return (false, null, "Activity payload does not contain a valid /apv1/activities/{guid} id.");
        }

        string filePath = Path.Combine(GlobalConfig.ActivityPubActivitiesFolder, $"{activityId.Value}.json");
        try
        {
            await File.WriteAllTextAsync(filePath, payload);
            return (true, BuildActivityEndpointUrl(activityId.Value), null);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    public static async Task<(bool Found, string? Payload, string? Error)> TryReadActivityPayloadAsync(string activityIdOrGuid)
    {
        if (!IsFullLoggingEnabled())
        {
            return (false, null, null);
        }

        if (string.IsNullOrWhiteSpace(GlobalConfig.ActivityPubActivitiesFolder))
        {
            return (false, null, "ActivityPub activities folder is not configured.");
        }

        Guid? activityId = TryParseActivityId(activityIdOrGuid);
        if (!activityId.HasValue)
        {
            return (false, null, null);
        }

        string filePath = Path.Combine(GlobalConfig.ActivityPubActivitiesFolder, $"{activityId.Value}.json");
        if (!File.Exists(filePath))
        {
            return (false, null, null);
        }

        try
        {
            string payload = await File.ReadAllTextAsync(filePath);
            return (true, payload, null);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    private static Guid? TryExtractActivityGuidFromPayload(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (!doc.RootElement.TryGetProperty("id", out var idNode) || idNode.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            string? idValue = idNode.GetString();
            return TryParseActivityId(idValue);
        }
        catch
        {
            return null;
        }
    }

    private static Guid? TryParseActivityId(string? activityIdOrGuid)
    {
        if (string.IsNullOrWhiteSpace(activityIdOrGuid))
        {
            return null;
        }

        string trimmed = activityIdOrGuid.Trim();

        if (Guid.TryParse(trimmed, out var directGuid))
        {
            return directGuid;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            string lastSegment = uri.Segments.LastOrDefault()?.Trim('/').Trim() ?? string.Empty;
            if (Guid.TryParse(lastSegment, out var uriGuid))
            {
                return uriGuid;
            }
        }

        return null;
    }

    private static bool TryGetRetentionTimeSpan(string? retentionSpec, out TimeSpan retention)
    {
        retention = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(retentionSpec))
        {
            return false;
        }

        string spec = retentionSpec.Trim().ToLowerInvariant();
        if (spec == "forever")
        {
            return false;
        }

        if (spec.Length < 2)
        {
            return false;
        }

        char unit = spec[^1];
        string countPart = spec[..^1];
        if (!int.TryParse(countPart, out int count) || count <= 0)
        {
            return false;
        }

        retention = unit switch
        {
            'd' => TimeSpan.FromDays(count),
            'w' => TimeSpan.FromDays(count * 7),
            _ => TimeSpan.Zero
        };

        return retention > TimeSpan.Zero;
    }

    private static Task CleanupExpiredActivitiesAsync(TimeSpan retention, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        string? folder = GlobalConfig.ActivityPubActivitiesFolder;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return Task.CompletedTask;
        }

        DateTime cutoffUtc = DateTime.UtcNow - retention;
        int removed = 0;
        int failed = 0;

        foreach (string filePath in Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                DateTime lastWrite = File.GetLastWriteTimeUtc(filePath);
                if (lastWrite <= cutoffUtc)
                {
                    File.Delete(filePath);
                    removed++;
                }
            }
            catch (Exception ex)
            {
                failed++;
                DBg.d(LogLevel.Warning, $"ActivityPub cleanup failed for {filePath}: {ex.Message}");
            }
        }

        if (removed > 0 || failed > 0)
        {
            DBg.d(LogLevel.Information,
                $"ActivityPub cleanup completed. Removed={removed}, Failed={failed}, Retention={GlobalConfig.ActivityPubActivityRetention}");
        }

        return Task.CompletedTask;
    }
}
