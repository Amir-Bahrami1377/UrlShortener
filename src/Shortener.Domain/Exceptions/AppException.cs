namespace Shortener.Domain.Exceptions;

/// <summary>
/// Carries one of the error codes from IMPLEMENTATION_PLAN.md Appendix B. Caught by a single
/// exception-handling middleware in each web project and rendered as an RFC 7807 problem response.
/// </summary>
public class AppException(string errorCode, string message, IReadOnlyDictionary<string, object?>? extensions = null)
    : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
    public IReadOnlyDictionary<string, object?>? Extensions { get; } = extensions;
}
