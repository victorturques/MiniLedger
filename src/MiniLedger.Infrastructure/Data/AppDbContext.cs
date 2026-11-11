using Microsoft.EntityFrameworkCore;
using MiniLedger.Application.Interfaces;
using MiniLedger.Domain.Entities;

namespace MiniLedger.Infrastructure.Data;

public class AppDbContext : DbContext, IAppDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        IncrementAccountVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        IncrementAccountVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void IncrementAccountVersions()
    {
        foreach (var entry in ChangeTracker.Entries<Account>())
        {
            if (entry.State == EntityState.Modified)
                entry.Entity.Version++;
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Aplicado a toda propriedade DateTime, inclusive as que forem criadas depois:
        // sem isso o fuso se perde no round-trip pelo SQLite.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Account>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.HasIndex(a => a.AccountNumber).IsUnique();
            entity.Property(a => a.HolderName).IsRequired().HasMaxLength(150);
            entity.Property(a => a.AccountNumber).IsRequired().HasMaxLength(8);
            entity.Property(a => a.PasswordHash).IsRequired();
            entity.Property(a => a.Balance).HasPrecision(18, 2);
            entity.Property(a => a.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.HasOne(t => t.Account)
                  .WithMany(a => a.Transactions)
                  .HasForeignKey(t => t.AccountId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.Property(t => t.Amount).HasPrecision(18, 2);
            entity.Property(t => t.CounterpartyAccountNumber).HasMaxLength(8);
        });
    }
}
