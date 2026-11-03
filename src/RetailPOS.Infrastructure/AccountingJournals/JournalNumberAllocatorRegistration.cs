using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Application.AccountingJournals.Abstractions;

namespace RetailPOS.Infrastructure.AccountingJournals;

public static class JournalNumberAllocatorRegistration
{
    public static IServiceCollection AddJournalNumberAllocator(this IServiceCollection services)
    {
        services.AddScoped<IJournalNumberAllocator, JournalNumberAllocator>();
        return services;
    }
}
