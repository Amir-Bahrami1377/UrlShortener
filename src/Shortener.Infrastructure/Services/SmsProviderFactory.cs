using Microsoft.Extensions.DependencyInjection;
using Shortener.Application.Abstractions;

namespace Shortener.Infrastructure.Services;

public sealed class SmsProviderFactory(IServiceProvider serviceProvider) : ISmsProviderFactory
{
    public ISmsProvider Resolve(string providerCode) =>
        serviceProvider.GetRequiredKeyedService<ISmsProvider>(providerCode);
}
