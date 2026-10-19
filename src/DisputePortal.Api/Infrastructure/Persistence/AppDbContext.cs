using System.Text;
using DisputePortal.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DisputePortal.Api.Infrastructure.Persistence;

/// <summary>
/// EF Core is used for data access only. The schema is owned by the versioned SQL scripts in
/// <c>Migrations/</c> (see <see cref="SqlMigrator"/>), so the mapping below must match them.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<Dispute> Disputes => Set<Dispute>();

    public DbSet<DisputeEvent> DisputeEvents => Set<DisputeEvent>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums are stored as readable text, which keeps SQL, logs and the history table self-explanatory.
        configurationBuilder.Properties<DisputeStatus>().HaveConversion<string>();
        configurationBuilder.Properties<DisputeReason>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(ConfigureUser);
        modelBuilder.Entity<Transaction>(ConfigureTransaction);
        modelBuilder.Entity<Dispute>(ConfigureDispute);
        modelBuilder.Entity<DisputeEvent>(ConfigureDisputeEvent);

        ApplySnakeCaseColumnNames(modelBuilder);
    }

    private static void ConfigureUser(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
    }

    private static void ConfigureTransaction(EntityTypeBuilder<Transaction> b)
    {
        b.ToTable("transactions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureDispute(EntityTypeBuilder<Dispute> b)
    {
        b.ToTable("disputes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        // Npgsql maps a uint row version onto PostgreSQL's system column xmin.
        b.Property(x => x.Version).IsRowVersion();

        b.HasOne<Transaction>().WithMany().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);

        b.HasMany(x => x.Events)
            .WithOne()
            .HasForeignKey(e => e.DisputeId);

        b.Navigation(x => x.Events)
            .HasField("_events")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureDisputeEvent(EntityTypeBuilder<DisputeEvent> b)
    {
        b.ToTable("dispute_events");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.FromStatus).HasConversion<string>();
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ApplySnakeCaseColumnNames(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                if (property.IsConcurrencyToken && property.ClrType == typeof(uint))
                {
                    continue; // xmin keeps its system name
                }

                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }
    }

    internal static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0)
            {
                sb.Append('_');
            }

            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }
}
