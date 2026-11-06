using System.Text.Json;
using System.Text.Json.Serialization;
using JTI.DEEP.Reports.API.Middleware;
using JTI.DEEP.Reports.Application;
using JTI.DEEP.Reports.Infrastructure;
using JTI.DEEP.Reports.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddOpenApi();
builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Local mock database only (see CLAUDE.md). Existing databases are never touched.
    if (app.Services.GetRequiredService<IOptions<MockDataOptions>>().Value.Enabled)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MockDataSeeder>().SeedAsync();
    }
}

app.UseMiddleware<ExceptionHandlerMiddleware>();
app.MapControllers();

app.Run();
