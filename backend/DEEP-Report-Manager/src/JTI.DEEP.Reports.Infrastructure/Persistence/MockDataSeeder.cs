using System.Globalization;
using JTI.DEEP.Reports.Domain.Entities;
using JTI.DEEP.Reports.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JTI.DEEP.Reports.Infrastructure.Persistence;

public sealed class MockDataOptions
{
    public const string SectionName = "MockData";

    public bool Enabled { get; set; }

    /// <summary>Safety guard: the seeder only runs when the connection string points at this database.</summary>
    public string DatabaseName { get; set; } = "DeepReportsMock";

    public int Customers { get; set; } = 3_000;
    public int Programs { get; set; } = 40;
    public int Transactions { get; set; } = 60_000;
    public int Seed { get; set; } = 20260101;
}

/// <summary>
/// Creates the local mock database (same table and column names as JTI-DEEP-API) and fills it with
/// generated data. Never touches any other database: it refuses to run unless the connection string's
/// database equals <see cref="MockDataOptions.DatabaseName"/>, and only seeds a database it just created.
/// Dates are Philippine local time, like the real data.
/// </summary>
public sealed class MockDataSeeder(ReportDbContext db, IOptions<MockDataOptions> options, ILogger<MockDataSeeder> logger)
{
    private static readonly (string Name, string Zone)[] ProvinceNames =
    [
        ("Metro Manila", "Metro Manila"), ("Bulacan", "North Luzon"), ("Pampanga", "North Luzon"), ("Pangasinan", "North Luzon"),
        ("Benguet", "North Luzon"), ("Ilocos Norte", "North Luzon"), ("Cavite", "South Luzon"), ("Laguna", "South Luzon"),
        ("Batangas", "South Luzon"), ("Rizal", "South Luzon"), ("Quezon", "South Luzon"), ("Cebu", "Visayas"),
        ("Iloilo", "Visayas"), ("Negros Occidental", "Visayas"), ("Bohol", "Visayas"), ("Leyte", "Visayas"),
        ("Davao del Sur", "Mindanao"), ("Misamis Oriental", "Mindanao"), ("South Cotabato", "Mindanao"), ("Zamboanga del Sur", "Mindanao"),
    ];

    private static readonly string[] Cities =
        ["City Proper", "North District", "South District", "Poblacion", "Bayside", "Uptown", "Downtown", "Riverside"];

    private static readonly string[] FirstNames =
    [
        "Juan", "Maria", "Jose", "Ana", "Mark", "Angelica", "John Paul", "Kristine", "Carlo", "Patricia", "Miguel", "Camille",
        "Rafael", "Bea", "Paolo", "Nicole", "Gabriel", "Andrea", "Joshua", "Erika", "Francis", "Joanna", "Renz", "Mika",
        "Ramon", "Liza", "Enzo", "Sofia", "Nino", "Ysabel", "Andres", "Señorita",
    ];

    private static readonly string[] LastNames =
    [
        "Dela Cruz", "Santos", "Reyes", "Garcia", "Mendoza", "Torres", "Bautista", "Villanueva", "Ramos", "Aquino",
        "Castillo", "Flores", "Gonzales", "Navarro", "Pascual", "Domingo", "Salazar", "Mercado", "Aguilar", "Peña",
        "Del Rosario", "Lim", "Tan", "Ong", "Fernandez", "Soriano",
    ];

    private static readonly string[] ProgramThemes =
        ["Ploom Trial Caravan", "Ploom X Experience", "Retail Activation", "Night Market Pop-up", "Mall Kiosk Drive",
         "Festival Booth", "Campus Outreach", "Sari-sari Store Push", "Office Tower Roadshow", "Weekend Sampling"];

    private static readonly string[] OutletKinds =
        ["Mall Kiosk", "Convenience Store", "Night Market Stall", "Event Booth", "Grocery", "Gas Station Mart", "Bar & Lounge"];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var database = new SqlConnectionStringBuilder(db.Database.GetConnectionString()).InitialCatalog;
        if (!string.Equals(database, settings.DatabaseName, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Mock data skipped: connection string targets '{Database}', not the mock database '{MockDatabase}'.",
                database, settings.DatabaseName);
            return;
        }

        if (!await db.Database.EnsureCreatedAsync(cancellationToken))
        {
            logger.LogInformation("Mock database '{Database}' already exists; not reseeding. Drop it to regenerate.", database);
            return;
        }

        logger.LogInformation("Creating mock data in '{Database}'...", database);
        var random = new Random(settings.Seed);
        var now = new DateTime(2026, 11, 1, 18, 0, 0);
        db.ChangeTracker.AutoDetectChangesEnabled = false;

        var provinces = ProvinceNames
            .Select((p, i) => new Province
            {
                PSGCCode = $"{(i + 1) * 100:D4}00000",
                Name = p.Name,
                Zone = p.Zone,
                DateCreated = new DateTime(2024, 1, 1),
            })
            .ToList();
        db.Provinces.AddRange(provinces);
        await db.SaveChangesAsync(cancellationToken);

        var programs = new List<Program>();
        for (var i = 0; i < settings.Programs; i++)
        {
            var province = provinces[random.Next(provinces.Count)];
            var start = new DateTime(2025, 1, 1).AddDays(random.Next(0, 640));
            var end = start.AddDays(random.Next(14, 180));
            var status = end < now ? ProgramStatus.Completed
                : start > now ? ProgramStatus.Draft
                : random.Next(10) == 0 ? ProgramStatus.Paused : ProgramStatus.InProgress;

            var isDeleted = i % 19 == 7; // a couple of soft-deleted programs
            var programName = $"{ProgramThemes[i % ProgramThemes.Length]} — {province.Name} {start:yyyy}-{i + 1:D2}";
            if (isDeleted)
            {
                // Same rename as JTI-DEEP-API's DeleteProgramCommand.
                var deletedAt = end.AddDays(7);
                if (deletedAt > now)
                {
                    deletedAt = now;
                }

                programName += string.Create(CultureInfo.InvariantCulture, $" - (Deleted at {deletedAt:M/d/yyyy h:mm:ss tt})");
            }

            programs.Add(new Program
            {
                ProgramName = programName,
                ProgramType = (ProgramType)random.Next(1, 4),
                Status = status,
                StartDate = start,
                EndDate = end,
                FocusZone = random.Next(12) == 0 ? null : province.Zone,
                DateCreated = start.AddDays(-random.Next(7, 30)),
                IsDeleted = isDeleted,
                Outlets = Enumerable.Range(1, random.Next(3, 7))
                    .Select(n => new ProgramOutlet
                    {
                        OutletName = $"{OutletKinds[random.Next(OutletKinds.Length)]} {province.Name} #{n}",
                        OutletCode = $"OUT-{i + 1:D3}-{n:D2}",
                        City = $"{province.Name} {Cities[random.Next(Cities.Length)]}",
                        Province = province.Name,
                        Zone = province.Zone,
                        DateCreated = start.AddDays(-3),
                        IsDeleted = random.Next(40) == 0,
                    })
                    .ToList(),
            });
        }

        db.Programs.AddRange(programs);
        await db.SaveChangesAsync(cancellationToken);

        var customers = new List<Customer>();
        for (var i = 0; i < settings.Customers; i++)
        {
            var first = FirstNames[random.Next(FirstNames.Length)];
            var last = LastNames[random.Next(LastNames.Length)];
            var emailUser = $"{first}.{last}".ToLowerInvariant().Replace(" ", "").Replace("ñ", "n");
            customers.Add(new Customer
            {
                FirstName = first,
                LastName = last,
                FullName = $"{first} {last}",
                // Not unique on purpose (it isn't in production either): a few customers share an email.
                EmailAddress = random.Next(15) == 0 ? null : $"{emailUser}{random.Next(1, i % 50 == 0 ? 2 : 999)}@example.com",
                MobileNumber = $"09{random.Next(100_000_000, 999_999_999)}",
                CustomerType = random.Next(8) == 0 ? CustomerType.Retailer : CustomerType.Consumer,
                PloomType = (PloomType)random.Next(0, 7),
                ProvincePSGCCode = random.Next(20) == 0 ? null : provinces[random.Next(provinces.Count)].PSGCCode,
                DateCreated = new DateTime(2025, 1, 1).AddMinutes(random.Next(0, 60 * 24 * 670)),
                IsDeleted = random.Next(50) == 0,
            });
        }

        db.Customers.AddRange(customers);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();

        const int batchSize = 5_000;
        for (var done = 0; done < settings.Transactions; done += batchSize)
        {
            var batch = new List<Transaction>(batchSize);
            for (var i = done; i < Math.Min(done + batchSize, settings.Transactions); i++)
            {
                var program = programs[random.Next(programs.Count)];
                var outlets = program.Outlets!.ToList();
                var customer = customers[random.Next(customers.Count)];
                var lastDay = program.EndDate < now ? program.EndDate : now;
                var days = Math.Max(1, (int)(lastDay - program.StartDate).TotalDays);
                var start = program.StartDate.Date.AddDays(random.Next(days)).AddHours(random.Next(9, 22)).AddMinutes(random.Next(60));

                batch.Add(new Transaction
                {
                    StartTransactionDate = start,
                    EndTransactionDate = start.AddMinutes(random.Next(5, 60)),
                    PloomType = (TransactionPloomType)random.Next(1, 12),
                    CustomerId = customer.Id,
                    ProgramId = program.Id,
                    ProgramOutletId = outlets[random.Next(outlets.Count)].Id,
                    DateCreated = start,
                    IsDeleted = random.Next(100) == 0,
                });
            }

            db.Transactions.AddRange(batch);
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }

        logger.LogInformation("Mock data ready: {Customers} customers, {Programs} programs, {Transactions} transactions.",
            settings.Customers, settings.Programs, settings.Transactions);
    }
}
