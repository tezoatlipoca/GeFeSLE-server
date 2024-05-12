using System.Runtime.CompilerServices;
using System.Diagnostics;




public static class DBg
{

    public static void d(LogLevel level,
            string? msg,
            [CallerFilePath] string file = "",
            [CallerLineNumber] int line = 0,
            [CallerMemberName] string member = "")

    {
        // get the current date and time in ISO 6801 format:
        // YYYY-MM-DD HH:MM:SS
        string now = DateTime.Now.ToString("s");
        var debugNfo = "";
        if(file is not null && member is not null) {
            // trick won't work if we run on a diff platform than we were compiled on:
                string normalizedFile = file.Replace('/', Path.DirectorySeparatorChar)
                                .Replace('\\', Path.DirectorySeparatorChar);

            string filename = Path.GetFileName(normalizedFile);
            debugNfo = $"[{member}//{filename}:{line}]";
        }
        
        
        if (level < GlobalConfig.CURRENT_LEVEL)
        {
            return;
        }
        switch (level)
        {
            case LogLevel.Trace:
                if(debugNfo is not null) {
                    Console.WriteLine($"{now} TRACE | {debugNfo} {msg}");
                } else {
                    Console.WriteLine($"{now} TRACE | {msg}");
                }
                return;
            case LogLevel.Debug:
                if(debugNfo is not null){
                    Console.WriteLine($"{now} DEBUG | {debugNfo} {msg}");
                } else {
                    Console.WriteLine($"{now} DEBUG | {msg}");
                }
                
                return;
            case LogLevel.Information:
                Console.WriteLine($"{now} INFO  | {msg}");
                return;
            case LogLevel.Warning:
                Console.WriteLine($"{now} WARN  | {msg}");
                return;
            case LogLevel.Error:
                Console.WriteLine($"{now} ERROR | {msg}");
                return;
            case LogLevel.Critical:
                Console.WriteLine($"{now} FATAL | {msg}");
                return;
            default:
                Console.WriteLine($"db.dump | FATAL: Unexpected value for level: {msg}");
                return;
        }
    }
}