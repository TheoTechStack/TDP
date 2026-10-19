using DisputePortal.Api.Domain;

namespace DisputePortal.Api.Features;

/// <summary>The authenticated caller, as the services need it. Built from the JWT by the controllers.</summary>
public sealed record CurrentUser(Guid Id, string Role)
{
    public bool IsAgent => Role == Roles.Agent;
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record Paging(int Page, int PageSize)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public int Skip => (Page - 1) * PageSize;

    public static Paging From(int? page, int? pageSize) =>
        new(Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize));
}
