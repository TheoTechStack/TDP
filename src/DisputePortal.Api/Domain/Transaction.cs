namespace DisputePortal.Api.Domain;

/// <summary>A settled card/account transaction. Read-only from the portal's point of view.</summary>
public sealed class Transaction
{
    private Transaction()
    {
    }

    public Transaction(
        Guid id,
        Guid customerId,
        string reference,
        string merchant,
        string category,
        decimal amount,
        string currency,
        DateTimeOffset occurredAt)
    {
        Id = id;
        CustomerId = customerId;
        Reference = reference;
        Merchant = merchant;
        Category = category;
        Amount = amount;
        Currency = currency;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }

    public Guid CustomerId { get; private set; }

    public string Reference { get; private set; } = string.Empty;

    public string Merchant { get; private set; } = string.Empty;

    public string Category { get; private set; } = string.Empty;

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = "ZAR";

    public DateTimeOffset OccurredAt { get; private set; }
}
