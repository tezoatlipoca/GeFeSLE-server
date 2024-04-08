using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace GeFeSLE
{
    public class GeFeSLEUser : IdentityUser
    {
        public DateTime LastAccessTime { get; set; }
        public List<JwtToken> JwtTokens { get; set; } = new List<JwtToken>();
    }

public enum GTokenSource
{
    WEB,
    FFPLUGIN,
    EDGEPLUGIN,
    CHROMEPLUGIN,
    MICROSOFT,
    GOOGLE,
}



    public class JwtToken
    {
        [Key]
        public int Id { get; set; }
        public string? Token { get; set; }
        public DateTime ExpiryDate { get; set; }

        public GTokenSource TokenSource { get; set; } 
    }

    public class UserDto {
        public string? Id { get; set; }
        public string? UserName { get; set; }
        public string? Email { get; set; }

        
        
    }
}
