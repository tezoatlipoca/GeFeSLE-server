using System.Collections.Concurrent;

public static class ProcessTracker
{
    private static ConcurrentDictionary<string, string> _processes = new ConcurrentDictionary<string, string>();
    private static ConcurrentDictionary<string, string> _descriptions = new ConcurrentDictionary<string, string>();
    public static void StartProcess(string token, string? description)
    {
        _processes.TryAdd(token, "Started");
        if(description != null)
        {
            _descriptions.TryAdd(token, description);
        } else {
            _descriptions.TryAdd(token, "<unknown process>");
        }
    }

    public static void UpdateProcess(string token, string status)
    {
        _processes[token] = status;
    }

    public static string? GetProcessStatus(string token)
    {
        // check that token is a valid key
        if(string.IsNullOrEmpty(token))
        {
            return $"Invalid process token {token}";
        }
        else if(!_processes.ContainsKey(token))
        {
            return $"Unrecognized process token {token}";
        }
        else {
            return _processes.TryGetValue(token, out var status) ? status : null;
        }
    }
    public static List<string> ShitsGoingOn() {
        return _processes.Select(x => $"{x.Key} - {_descriptions[x.Key]} - {x.Value}").ToList();
    }
}