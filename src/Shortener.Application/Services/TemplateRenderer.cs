namespace Shortener.Application.Services;

/// <summary>Substitutes {placeholder} tokens in a MessageTemplate.Body (§1's placeholder table).</summary>
public static class TemplateRenderer
{
    public static string Render(string body, IReadOnlyDictionary<string, string> values)
    {
        var result = body;
        foreach (var (key, value) in values)
        {
            result = result.Replace($"{{{key}}}", value, StringComparison.Ordinal);
        }

        return result;
    }
}
