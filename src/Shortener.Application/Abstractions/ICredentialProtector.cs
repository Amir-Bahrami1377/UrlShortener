namespace Shortener.Application.Abstractions;

/// <summary>Encrypts/decrypts SmsAccount credential fields (§M2.4) via ASP.NET Core Data Protection.</summary>
public interface ICredentialProtector
{
    string? Protect(string? plaintext);

    string? Unprotect(string? ciphertext);
}
