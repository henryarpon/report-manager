using FluentValidation;
using JTI.DEEP.Reports.Application.Common.Behaviors;
using JTI.DEEP.Reports.Application.Reports;
using JTI.DEEP.Reports.Application.Reports.Catalog;
using JTI.DEEP.Reports.Application.Reports.Definitions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JTI.DEEP.Reports.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.Configure<ReportOptions>(configuration.GetSection(ReportOptions.SectionName));
        services.AddSingleton<ReportCatalog>();
        services.AddSingleton<ReportResolver>();
        services.AddSingleton(TimeProvider.System);

        services.AddValidatorsFromAssembly(assembly, ServiceLifetime.Singleton);
        services.AddMediatR(cfg =>
        {
            cfg.LicenseKey = configuration["MediatR:LicenseKey"];
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        return services;
    }
}
