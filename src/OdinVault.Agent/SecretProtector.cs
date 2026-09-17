using Microsoft.AspNetCore.DataProtection;
using OdinVault.Core;

namespace OdinVault.Agent;

public sealed class SecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("OdinVault.Secrets.v1");

    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return string.Empty;
        return _protector.Protect(plaintext);
    }

    public string Unprotect(string protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue)) return string.Empty;
        return _protector.Unprotect(protectedValue);
    }
}
