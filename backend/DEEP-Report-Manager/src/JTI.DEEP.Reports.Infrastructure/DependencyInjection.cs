using JTI.DEEP.Reports.Application.Reports.Abstractions;
using JTI.DEEP.Reports.Infrastructure.Export;
using JTI.DEEP.Reports.Infrastructure.Persistence;
using JTI.DEEP.Reports.Infrastructure.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JTI.DEEP.Reports.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");

        services.AddDbContext<ReportDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.CommandTimeout(120)));

        services.AddScoped<IReportReader, EfReportReader>();
        services.AddScoped<ILookupReader, EfLookupReader>();
        services.AddSingleton<IReportFileWriter, CsvReportFileWriter>();

        services.Configure<MockDataOptions>(configuration.GetSection(MockDataOptions.SectionName));
        services.AddScoped<MockDataSeeder>();

        return services;
    }
}
