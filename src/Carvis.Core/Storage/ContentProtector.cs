using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Carvis.Core.Storage;

/// <summary>Encrypts what Carvis stores about the user (conversations, memories).</summary>
public interface IContentProtector
{
    string Protect(string text);
    string Unprotect(string stored);
}

/// <summary>
/// Windows DPAPI: data can only be read by the same Windows user on the same PC. Elsewhere the
/// text is stored as is. Values written without encryption are still read back.
/// </summary>
public sealed class ContentProtector(bool enabled = true) : IContentProtector
{
    private const string Prefix = "dpapi:";
    private static readonly byte[] Entropy = "Carvis.v1"u8.ToArray();

    public string Protect(string text)
    {
        if (!enabled || !OperatingSystem.IsWindows() || string.IsNullOrEmpty(text))
            return text;
        return Prefix + Convert.ToBase64String(ProtectWindows(Encoding.UTF8.GetBytes(text)));
    }

    public string Unprotect(string stored)
    {
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
            return stored;
        if (!OperatingSystem.IsWindows())
            throw new CryptographicException("Estos datos se cifraron en Windows y solo se pueden leer allí.");
        return Encoding.UTF8.GetString(UnprotectWindows(Convert.FromBase64String(stored[Prefix.Length..])));
    }

    [SupportedOSPlatform("windows")]
    private static byte[] ProtectWindows(byte[] data) => ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);

    [SupportedOSPlatform("windows")]
    private static byte[] UnprotectWindows(byte[] data) => ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
}
