using AgenticPayments.Application.Payments;
using AgenticPayments.Application.Webhooks;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticPayments.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(ApplicationServiceCollectionExtensions).Assembly);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<CreatePaymentUseCase>();
        services.AddScoped<ProcessProviderWebhookUseCase>();
        return services;
    }
}
