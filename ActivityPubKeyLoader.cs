using System.Security.Cryptography;
using System.Text;

public static class ActivityPubKeyLoader
{
    public static async Task<(RSA? SigningKey, string? PublicKeyPem)> LoadFromConfigAsync(string? configuredPrivateKeyPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPrivateKeyPath))
        {
            return (null, null);
        }

        try
        {
            var privateKeyPath = ResolveConfigPath(configuredPrivateKeyPath);
            if (!File.Exists(privateKeyPath))
            {
                DBg.d(LogLevel.Warning, $"ActivityPub signing key file not found: {privateKeyPath}");
                return (null, null);
            }

            string privateKeyPem = await File.ReadAllTextAsync(privateKeyPath);
            var rsa = RSA.Create();
            rsa.ImportFromPem(privateKeyPem);

            string publicKeyPem = PemEncode("PUBLIC KEY", rsa.ExportSubjectPublicKeyInfo());
            DBg.d(LogLevel.Information, $"ActivityPub signing key loaded from {privateKeyPath}");
            return (rsa, publicKeyPem);
        }
        catch (Exception ex)
        {
            DBg.d(LogLevel.Warning, $"Unable to load ActivityPub signing key: {ex.Message}");
            return (null, null);
        }
    }

    private static string ResolveConfigPath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            return configuredPath;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredPath));
    }

    private static string PemEncode(string label, byte[] data)
    {
        var b64 = Convert.ToBase64String(data);
        var sb = new StringBuilder();
        sb.AppendLine($"-----BEGIN {label}-----");
        for (int i = 0; i < b64.Length; i += 64)
        {
            int len = Math.Min(64, b64.Length - i);
            sb.AppendLine(b64.Substring(i, len));
        }
        sb.AppendLine($"-----END {label}-----");
        return sb.ToString();
    }
}
