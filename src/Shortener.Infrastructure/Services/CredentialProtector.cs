using Microsoft.AspNetCore.DataProtection;
using Shortener.Application.Abstractions;

namespace Shortener.Infrastructure.Services;

public sealed class CredentialProtector : ICredentialProtector
{
    private readonly IDataProtector _protector;

    public CredentialProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("SmsAccountCredentials");
    }

    public string? Protect(string? plaintext) => string.IsNullOrEmpty(plaintext) ? null : _protector.Protect(plaintext);

    public string? Unprotect(string? ciphertext) => string.IsNullOrEmpty(ciphertext) ? null : _protector.Unprotect(ciphertext);
}
