namespace RoadOps.Application.Common;

/// <summary>A 1-based page request. Validated by <see cref="Validate"/> before it reaches a repository.</summary>
public sealed record PageRequest(int Page = PageRequest.DefaultPage, int PageSize = PageRequest.DefaultPageSize)
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 500;

    public int Skip => (Page - 1) * PageSize;

    public PageRequest Validate()
    {
        if (Page < 1)
        {
            throw new ArgumentException("Page must be 1 or greater.", nameof(Page));
        }

        if (PageSize is < 1 or > MaxPageSize)
        {
            throw new ArgumentException($"Page size must be between 1 and {MaxPageSize}.", nameof(PageSize));
        }

        return this;
    }
}

public sealed class PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required int TotalCount { get; init; }

    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;

    public PagedResult<TOut> Map<TOut>(Func<T, TOut> map) => new()
    {
        Items = Items.Select(map).ToList(),
        Page = Page,
        PageSize = PageSize,
        TotalCount = TotalCount
    };
}
