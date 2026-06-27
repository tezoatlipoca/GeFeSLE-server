using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace GeFeSLE
{
    public class GeFeSLEUser : IdentityUser
    {
        public DateTime LastAccessTime { get; set; }
        public List<JwtToken> JwtTokens { get; set; } = new List<JwtToken>();

        public string? UploadsPath { get; set; }

        public string EnsureUploadsPath()
        {
            if (string.IsNullOrWhiteSpace(UploadsPath))
            {
                UploadsPath = GetUploadsPath(UserName, Id, Email);
            }

            return UploadsPath;
        }

        public static string GetUploadsPath(string? userName, string? userId, string? email)
        {
            string source = string.IsNullOrWhiteSpace(userName)
                ? (string.IsNullOrWhiteSpace(email) ? (userId ?? "unknown-user") : email!)
                : userName;

            string sanitized = Regex.Replace(source.Trim(), "[^A-Za-z0-9._-]+", "_");
            sanitized = sanitized.Trim('.', '_');

            return string.IsNullOrWhiteSpace(sanitized) ? (userId ?? "unknown-user") : sanitized;
        }
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
