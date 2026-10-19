namespace DisputePortal.Api.Features;

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

internal static class Problems
{
    public static IResult Validation(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    public static IResult Validation(IDictionary<string, string[]> errors) => TypedResults.ValidationProblem(errors);
}
