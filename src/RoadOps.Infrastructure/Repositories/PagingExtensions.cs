using Microsoft.EntityFrameworkCore;
using RoadOps.Application.Common;

namespace RoadOps.Infrastructure.Repositories;

internal static class PagingExtensions
{
    /// <summary>Counts the filtered query, then fetches one page. The query must already have a total, stable ordering.</summary>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(this IQueryable<T> query, PageRequest page, CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);
        var items = total == 0 || page.Skip >= total
            ? []
            : await query.Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken);

        return new PagedResult<T> { Items = items, Page = page.Page, PageSize = page.PageSize, TotalCount = total };
    }
}
