using AgenticPayments.Application.Payments;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticPayments.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(ApplicationServiceCollectionExtensions).Assembly);
        services.AddScoped<CreatePaymentUseCase>();
        return services;
    }
}
