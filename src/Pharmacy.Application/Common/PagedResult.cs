namespace Pharmacy.Application.Common;

/// <summary>One page of a server-side list plus the total number of matching rows.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount);
