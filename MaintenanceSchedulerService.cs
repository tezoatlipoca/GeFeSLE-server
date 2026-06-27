public static class MaintenanceScheduler
{
    private sealed class ScheduledTask
    {
        public string Name { get; set; } = string.Empty;
        public TimeSpan Interval { get; set; }
        public DateTimeOffset NextRunUtc { get; set; }
        public Func<CancellationToken, Task> Action { get; set; } = _ => Task.CompletedTask;
        public bool IsRunning { get; set; }
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<string, ScheduledTask> Tasks = new(StringComparer.OrdinalIgnoreCase);

    public static void RegisterRecurringTask(
        string taskName,
        Func<CancellationToken, Task> action,
        TimeSpan interval,
        TimeSpan? initialDelay = null)
    {
        if (string.IsNullOrWhiteSpace(taskName))
        {
            throw new ArgumentException("Task name is required.", nameof(taskName));
        }

        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be greater than zero.");
        }

        DateTimeOffset firstRun = DateTimeOffset.UtcNow + (initialDelay ?? interval);
        lock (Gate)
        {
            Tasks[taskName] = new ScheduledTask
            {
                Name = taskName,
                Action = action,
                Interval = interval,
                NextRunUtc = firstRun,
                IsRunning = false
            };
        }

        DBg.d(LogLevel.Information,
            $"Maintenance task registered: {taskName}; every {interval}; first run at {firstRun:O}");
    }

    public static async Task RunDueTasksAsync(CancellationToken cancellationToken)
    {
        List<ScheduledTask> dueTasks;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        lock (Gate)
        {
            dueTasks = Tasks.Values
                .Where(t => !t.IsRunning && t.NextRunUtc <= now)
                .ToList();

            foreach (var task in dueTasks)
            {
                task.IsRunning = true;
            }
        }

        foreach (var task in dueTasks)
        {
            try
            {
                await task.Action(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // normal shutdown cancellation
            }
            catch (Exception ex)
            {
                DBg.d(LogLevel.Error, $"Maintenance task '{task.Name}' failed: {ex.Message}");
            }
            finally
            {
                lock (Gate)
                {
                    task.IsRunning = false;
                    task.NextRunUtc = DateTimeOffset.UtcNow + task.Interval;
                }
            }
        }
    }
}

public sealed class MaintenanceSchedulerService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MaintenanceScheduler.RunDueTasksAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                DBg.d(LogLevel.Error, $"Maintenance scheduler loop error: {ex.Message}");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
