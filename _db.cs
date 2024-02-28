// Created: 2020-05-17 23:05
// Modified: 2020-05-18 00:05




public static class DBg
{

    public static void d(LogLevel level,
            string msg)

    {
        if (level < GlobalConfig.CURRENT_LEVEL)
        {
            return;
        }
        switch (level)
        {
            case LogLevel.Trace:
                Console.WriteLine($"TRACE | {msg}"); 
                return;
            case LogLevel.Debug:
                Console.WriteLine($"DEBUG | {msg}");
                return;
            case LogLevel.Information:
                Console.WriteLine($"INFO | {msg}");
                return;
            case LogLevel.Warning:
                Console.WriteLine($"WARN | {msg}");
                return;
            case LogLevel.Error:
                Console.WriteLine($"ERROR | {msg}");
                return;
            case LogLevel.Critical:
                Console.WriteLine($"FATAL | {msg}");
                return;
            default:
                Console.WriteLine($"db.dump | FATAL: Unexpected value for level: {msg}");
                return;
        }
    }
}