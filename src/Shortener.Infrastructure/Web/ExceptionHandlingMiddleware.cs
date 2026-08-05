using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Shortener.Application.Common;
using Shortener.Domain.Exceptions;

namespace Shortener.Infrastructure.Web;

public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (AppException ex)
        {
            await ProblemResponseWriter.WriteAsync(context, ex.ErrorCode, ex.Message);
        }
        catch (FileTooLargeException ex)
        {
            await ProblemResponseWriter.WriteAsync(context, ErrorCodes.FileTooLarge, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception processing {Method} {Path}", context.Request.Method, context.Request.Path);
            await ProblemResponseWriter.WriteAsync(context, ErrorCodes.Internal, "خطای غیرمنتظره سرور رخ داده است.");
        }
    }
}
