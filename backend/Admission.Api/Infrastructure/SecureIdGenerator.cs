using System.Security.Cryptography;

namespace Admission.Api.Infrastructure;

public static class SecureIdGenerator
{
    public static string NewId(string prefix)
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return prefix + Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
