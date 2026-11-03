using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Application.Authentication.Commands;
using RetailPOS.Application.Behaviors;

namespace RetailPOS.Application;

public static class DependencyInjection
{
    private sealed class RegistrationMarker
    {
    }

    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(RegistrationMarker)))
        {
            return services;
        }

        var applicationAssembly = typeof(ApplicationAssemblyMarker).Assembly;

        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(applicationAssembly);
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        services.AddValidatorsFromAssembly(applicationAssembly, ServiceLifetime.Scoped);
        services.AddScoped<ChangeUserPasswordCommandHandler>();
        services.AddScoped<RetailPOS.Application.HR.Capabilities.IHrPayrollCapabilityService,
            RetailPOS.Application.HR.Capabilities.HrPayrollCapabilityService>();
        services.AddScoped<RetailPOS.Application.Pos.Abstractions.IPosCartPriceCalculator,
            RetailPOS.Application.Pos.Queries.PosCartPriceCalculator>();
        services.AddScoped<RetailPOS.Application.Sales.Abstractions.ISaleCheckoutSettingsProvider,
            RetailPOS.Application.Sales.Checkout.SaleCheckoutSettingsProvider>();
        services.AddSingleton<RegistrationMarker>();

        return services;
    }
}

internal static class ApplicationAssemblyMarker
{
}
