using System.Runtime.CompilerServices;

public static class EndpointLoggingHelpers
{
    public static string TrimDtoLogPayload(string payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return payload;
        }

        int trimTo = GlobalConfig.LogDTOsTrimCharacters;
        if (trimTo <= 0 || payload.Length <= trimTo)
        {
            return payload;
        }

        return payload.Substring(0, trimTo) + "...(trimmed)";
    }

    public static string SerializeForDtoLog(object? dto)
    {
        if (dto is null)
        {
            return "null";
        }

        try
        {
            return TrimDtoLogPayload(System.Text.Json.JsonSerializer.Serialize(dto));
        }
        catch (Exception ex)
        {
            return $"<serialization failed: {ex.Message}>";
        }
    }

    public static void LogDtoIn(
        string fn,
        string dtoName,
        object? dto,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = "")
    {
        if (!GlobalConfig.LogDTOsIn)
        {
            return;
        }

        DBg.d(LogLevel.Trace, $"{fn} <-- {dtoName} {SerializeForDtoLog(dto)}", file, line, member);
    }

    public static void LogDtoOut(
        string fn,
        string dtoName,
        object? dto,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = "")
    {
        if (!GlobalConfig.LogDTOsOut)
        {
            return;
        }

        DBg.d(LogLevel.Trace, $"{fn} --> {dtoName} {SerializeForDtoLog(dto)}", file, line, member);
    }

    public static IResult BadRequestWithTrace(
        string fn,
        string message,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = "")
    {
        DBg.d(LogLevel.Error, $"{fn} --> 400: {message}", file, line, member);
        return Results.BadRequest(message);
    }

    public static IResult BadRequestObjectWithTrace(
        string fn,
        object payload,
        string message,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = "")
    {
        DBg.d(LogLevel.Error, $"{fn} --> 400: {message}", file, line, member);
        return Results.BadRequest(payload);
    }

    public static IResult OkWithTrace(
        string fn,
        string message,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = "")
    {
        DBg.d(LogLevel.Information, $"{fn} --> 200: {message}", file, line, member);
        return Results.Ok();
    }

    public static IResult OkPayloadWithTrace<T>(
        string fn,
        T payload,
        string message,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = "")
    {
        DBg.d(LogLevel.Information, $"{fn} --> 200: {message}", file, line, member);
        return Results.Ok(payload);
    }
    public static IResult NotFoundWithTrace(
        string fn,
        string message,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = "")
    {
        DBg.d(LogLevel.Error, $"{fn} --> 404: {message}", file, line, member);
        return Results.NotFound(message);
    }

    public static IResult NotFoundNoMessageWithTrace(
        string fn,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = "")
    {
        DBg.d(LogLevel.Error, $"{fn} --> 404", file, line, member);
        return Results.NotFound();
    }

    public static IResult NoContentWithTrace(
        string fn,
        string message,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = "")
    {
        DBg.d(LogLevel.Information, $"{fn} --> 204: {message}", file, line, member);
        return Results.NoContent();
    }

    public static IResult RedirectWithTrace(
        string fn,
        string location,
        string message,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = "")
    {
        DBg.d(LogLevel.Information, $"{fn} --> 302: {message}", file, line, member);
        return Results.Redirect(location);
    }

    public static IResult ContentWithTrace(
        string fn,
        string content,
        string contentType,
        string message,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = "")
    {
        DBg.d(LogLevel.Information, $"{fn} --> 200: {message}", file, line, member);
        return Results.Content(content, contentType);
    }

    public static IResult NotFoundObjectWithTrace(
        string fn,
        object payload,
        string message,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = "")
    {
        DBg.d(LogLevel.Error, $"{fn} --> 404: {message}", file, line, member);
        return Results.NotFound(payload);
    }

    public static IResult UnauthorizedWithTrace(
        string fn,
        string? message = null,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = "")
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            DBg.d(LogLevel.Error, $"{fn} --> 401", file, line, member);
            return Results.Unauthorized();
        }

        DBg.d(LogLevel.Error, $"{fn} --> 401: {message}", file, line, member);
        return Results.Json(new { error = message }, statusCode: StatusCodes.Status401Unauthorized);
    }

    public static IResult ProblemWithTrace(
        string fn,
        string message,
        int statusCode,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = "")
    {
        DBg.d(LogLevel.Error, $"{fn} --> {statusCode}: {message}", file, line, member);
        return Results.Problem(message, statusCode: statusCode);
    }

    public static IResult StatusCodeWithTrace(
        string fn,
        int statusCode,
        string? message = null,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = "")
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            DBg.d(LogLevel.Error, $"{fn} --> {statusCode}", file, line, member);
        }
        else
        {
            DBg.d(LogLevel.Error, $"{fn} --> {statusCode}: {message}", file, line, member);
        }

        return Results.StatusCode(statusCode);
    }
}
