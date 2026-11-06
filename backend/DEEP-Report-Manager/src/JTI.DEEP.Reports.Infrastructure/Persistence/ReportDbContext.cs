using System.Linq.Expressions;
using JTI.DEEP.Reports.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace JTI.DEEP.Reports.Infrastructure.Persistence;

/// <summary>
/// Read-only model over the JTI-DEEP-API tables that reports need. Table and column names match
/// JTI-DEEP-API. This context never owns the schema: no migrations. The only time it creates tables
/// is for the local mock database (see <see cref="MockDataSeeder"/>).
/// </summary>
public sealed class ReportDbContext(DbContextOptions<ReportDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Program> Programs => Set<Program>();
    public DbSet<ProgramOutlet> ProgramOutlets => Set<ProgramOutlet>();
    public DbSet<Province> Provinces => Set<Province>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Province>(b =>
        {
            b.ToTable("Provinces");
            b.Property(p => p.PSGCCode).HasMaxLength(20);
            b.Property(p => p.Name).HasMaxLength(150);
        });

        modelBuilder.Entity<Customer>(b =>
        {
            b.ToTable("Customers");
            b.Property(c => c.FirstName).HasMaxLength(150);
            b.Property(c => c.LastName).HasMaxLength(150);
            b.Property(c => c.FullName).HasMaxLength(300);
            b.Property(c => c.EmailAddress).HasMaxLength(256);
            b.Property(c => c.MobileNumber).HasMaxLength(20);
            b.Property(c => c.ProvincePSGCCode).HasMaxLength(20);
            b.HasIndex(c => c.FullName).HasDatabaseName("IX_Customer_FullName");

            // Same alternate-key link as JTI-DEEP-API's CustomerConfiguration.
            b.HasOne(c => c.Province)
                .WithMany()
                .HasForeignKey(c => c.ProvincePSGCCode)
                .HasPrincipalKey(p => p.PSGCCode)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);
        });

        modelBuilder.Entity<Program>(b =>
        {
            b.ToTable("Programs");
            b.Property(p => p.ProgramName).HasMaxLength(200);
            b.Property(p => p.FocusZone).HasMaxLength(100);
            b.HasMany(p => p.Outlets).WithOne(o => o.Program).HasForeignKey(o => o.ProgramId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<ProgramOutlet>(b =>
        {
            b.ToTable("ProgramOutlets");
            b.Property(o => o.OutletName).HasMaxLength(200);
            b.Property(o => o.OutletCode).HasMaxLength(50);
        });

        modelBuilder.Entity<Transaction>(b =>
        {
            b.ToTable("Transactions");
            b.HasOne(t => t.Customer).WithMany(c => c.Transactions).HasForeignKey(t => t.CustomerId).OnDelete(DeleteBehavior.NoAction);
            b.HasOne(t => t.Program).WithMany(p => p.Transactions).HasForeignKey(t => t.ProgramId).OnDelete(DeleteBehavior.NoAction);
            b.HasOne(t => t.Outlet).WithMany().HasForeignKey(t => t.ProgramOutletId).OnDelete(DeleteBehavior.NoAction);
        });

        // Same global soft-delete filter as JTI-DEEP-API's ApplicationDbContext, except for the parents a
        // transaction points to through a required navigation (Customer, Program, ProgramOutlet): a filter
        // there turns the join into an INNER JOIN that silently drops the transaction (rows ≠ count).
        // A transaction stays visible when its customer, program or outlet is deleted; those entities are
        // excluded explicitly where they are listed themselves (their roots, lookups, outlet count).
        Type[] unfiltered = [typeof(Customer), typeof(Program), typeof(ProgramOutlet)];
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(t => typeof(BaseEntity).IsAssignableFrom(t.ClrType) && !unfiltered.Contains(t.ClrType)))
        {
            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var filter = Expression.Lambda(
                Expression.Equal(Expression.Property(parameter, nameof(BaseEntity.IsDeleted)), Expression.Constant(false)),
                parameter);
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
        }
    }
}
