using Microsoft.AspNetCore.Authentication;

public class CustomChallengeResult : IResult
{
    private readonly string _authenticationScheme;
    private readonly AuthenticationProperties _properties;

    public CustomChallengeResult(string authenticationScheme, AuthenticationProperties properties)
    {
        _authenticationScheme = authenticationScheme;
        _properties = properties;
    }

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        await httpContext.ChallengeAsync(_authenticationScheme, _properties);
    }
}