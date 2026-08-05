using Shortener.Application.Abstractions;

namespace Shortener.Application.Services;

/// <summary>
/// Drives the admin panel's dynamic per-provider form (§M2.4). Describing a provider's required
/// fields here is independent of whether that provider's ISmsProvider is actually registered —
/// only "fake" is wired for sending this session; the others are still real, correct metadata
/// about each vendor's known auth model so the form is honest even before M5 completion.
/// </summary>
public static class SmsProviderFieldCatalog
{
    public static IReadOnlyList<ProviderField> GetFields(string providerCode) => providerCode switch
    {
        "kavenegar" => [new ProviderField("ApiKey", "کلید API", true, true)],
        "melipayamak" => [new ProviderField("Username", "نام کاربری", true, true), new ProviderField("Password", "رمز عبور", true, true)],
        "farazsms" => [new ProviderField("Username", "نام کاربری", true, true), new ProviderField("Password", "رمز عبور", true, true)],
        "fake" => [],
        _ => [],
    };
}
