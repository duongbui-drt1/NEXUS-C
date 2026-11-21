namespace Nexus.SharedKernel.Pagination;

public sealed record PagedRequest
{
    private const int MaxPageSize = 100;
    private int _page = 1;
    private int _pageSize = 20;

    public int Page
    {
        get => _page;
        init => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = value < 1 ? 20 : value > MaxPageSize ? MaxPageSize : value;
    }

    public string? Search { get; init; }
    public string? SortBy { get; init; }
    public bool SortDescending { get; init; }

    public int Skip => (Page - 1) * PageSize;
}

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    long TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNext => Page < TotalPages;
    public bool HasPrevious => Page > 1;
}
