public class LoginDto
{
    public string? OAuthProvider { get; set; }
    public string? Instance { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }

    public bool IsValid()
    {
        string fn = "LoginDto.IsValid"; DBg.d(LogLevel.Trace, fn);
        if(string.IsNullOrWhiteSpace(OAuthProvider) && 
           string.IsNullOrWhiteSpace(Instance) && 
           string.IsNullOrWhiteSpace(Username) && 
           string.IsNullOrWhiteSpace(Password))
        {
            return false;
        }
        else
        {
            return true;
        }
        
    }

}