using DisputePortal.Api.Domain;
using DisputePortal.Api.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DisputePortal.Api.Infrastructure.Persistence;

/// <summary>
/// Seeds demo users, transactions and a couple of disputes (with history) so the portal is usable
/// immediately. Idempotent and safe to run from several instances at once.
/// The password comes from <see cref="SeedOptions.DemoPassword"/>; on every start the demo accounts are
/// re-hashed if it changed, so rotating it is "change the secret, redeploy".
/// </summary>
public sealed class DemoDataSeeder(
    AppDbContext db,
    TimeProvider clock,
    IOptions<SeedOptions> options,
    ILogger<DemoDataSeeder> logger)
{
    public static readonly IReadOnlyList<DemoAccount> Accounts =
    [
        new("alice@demo.test", "Alice Mokoena", Roles.Customer, "has dispute history"),
        new("bongani@demo.test", "Bongani Dlamini", Roles.Customer, "no disputes yet"),
        new("agent@demo.test", "Thandi Nkosi", Roles.Agent, "reviews & resolves"),
    ];

    private static readonly (string Merchant, string Category, decimal Min, decimal Max)[] Merchants =
    [
        ("Woolworths Food", "Groceries", 85, 1450),
        ("Checkers Hyper", "Groceries", 60, 2100),
        ("Uber Trip", "Transport", 45, 420),
        ("Takealot.com", "Online shopping", 120, 6800),
        ("Netflix", "Subscriptions", 99, 229),
        ("Shell Garage", "Fuel", 300, 1400),
        ("Vodacom Airtime", "Telecoms", 29, 899),
        ("Mr D Food", "Takeaways", 95, 560),
        ("Dis-Chem Pharmacy", "Health", 70, 1250),
        ("Computicket", "Entertainment", 150, 2400),
        ("Engen Quickshop", "Fuel", 250, 1300),
        ("Steam Games", "Entertainment", 80, 1100),
    ];

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var password = options.Value.DemoPassword
            ?? throw new InvalidOperationException("Seed:DemoPassword is required when seeding is enabled.");

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7420002);", cancellationToken);

        if (await db.Users.AnyAsync(cancellationToken))
        {
            await SyncDemoPasswordsAsync(password, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return;
        }

        var now = clock.GetUtcNow();
        var alice = CreateUser("alice@demo.test", password, now);
        var bongani = CreateUser("bongani@demo.test", password, now);
        var agent = CreateUser("agent@demo.test", password, now);
        db.Users.AddRange(alice, bongani, agent);

        var aliceTxns = GenerateTransactions(alice.Id, customerNo: 1, now, seed: 1001);
        var bonganiTxns = GenerateTransactions(bongani.Id, customerNo: 2, now, seed: 2002);
        db.Transactions.AddRange(aliceTxns);
        db.Transactions.AddRange(bonganiTxns);

        // Give Alice some history: one decided dispute and one that is in review.
        var decided = aliceTxns[4];
        var upheld = Dispute.Raise(decided, null, DisputeReason.Duplicate, "I was charged twice for the same order.", decided.OccurredAt.AddDays(2));
        upheld.StartReview(agent.Id, decided.OccurredAt.AddDays(3));
        upheld.Resolve(DisputeOutcome.Upheld, "Duplicate charge confirmed with the merchant. Refund issued.", agent.Id, decided.OccurredAt.AddDays(5));

        var inReview = aliceTxns[1];
        var open = Dispute.Raise(inReview, null, DisputeReason.GoodsNotReceived, "Order never arrived.", now.AddDays(-2));
        open.StartReview(agent.Id, now.AddDays(-1));

        db.Disputes.AddRange(upheld, open);

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Seeded demo data: {Users} users, {Transactions} transactions",
            3,
            aliceTxns.Count + bonganiTxns.Count);
    }

    /// <summary>Re-hashes any demo account whose stored hash no longer matches the configured password.</summary>
    private async Task SyncDemoPasswordsAsync(string password, CancellationToken cancellationToken)
    {
        var emails = Accounts.Select(a => a.Email).ToList();
        var demoUsers = await db.Users.Where(u => emails.Contains(u.Email)).ToListAsync(cancellationToken);

        var stale = demoUsers.Where(u => !PasswordHasher.Verify(password, u.PasswordHash)).ToList();
        stale.ForEach(u => u.ChangePasswordHash(PasswordHasher.Hash(password)));

        if (stale.Count > 0)
        {
            logger.LogInformation("Demo password changed: re-hashed {Count} demo accounts", stale.Count);
        }
    }

    // Each user gets their own hash (and so their own salt), even with the same password.
    private static User CreateUser(string email, string password, DateTimeOffset now)
    {
        var account = Accounts.Single(a => a.Email == email);
        return new User(Guid.CreateVersion7(), account.Email, account.DisplayName, PasswordHasher.Hash(password), account.Role, now);
    }

    private static List<Transaction> GenerateTransactions(Guid customerId, int customerNo, DateTimeOffset now, int seed)
    {
        var random = new Random(seed);
        var result = new List<Transaction>();

        // ~45 transactions over 150 days, so a few fall outside the 120-day dispute window.
        for (var i = 0; i < 45; i++)
        {
            var (merchant, category, min, max) = Merchants[random.Next(Merchants.Length)];
            var amount = Math.Round(min + ((decimal)random.NextDouble() * (max - min)), 2);
            var occurredAt = now
                .AddDays(-(i * 3.3) - 0.5)
                .AddMinutes(-random.Next(0, 600));

            result.Add(new Transaction(
                Guid.CreateVersion7(occurredAt),
                customerId,
                reference: $"TXN-{customerNo}{i:D3}-{random.Next(100000, 999999)}",
                merchant,
                category,
                amount,
                currency: "ZAR",
                occurredAt));
        }

        return result;
    }
}

/// <summary>A seeded demo login, as advertised on the sign-in page. Never carries the password.</summary>
public sealed record DemoAccount(string Email, string DisplayName, string Role, string Hint);
