using Pharmacy.Domain.Receivables;

namespace Pharmacy.Application.Receivables;

public sealed record CustomerInput(string? Name, string? Phone, string? Address, string? Notes, bool IsActive);

public sealed record CustomerQuery(string? Text, bool IncludeInactive, int PageIndex, int PageSize);

public sealed record CustomerListItem(int Id, string Name, string? Phone, bool IsActive, bool IsDefaultCash, decimal Balance);

/// <summary>A customer for the details page and for editing. <c>RowVersion</c> detects two people editing at once.</summary>
public sealed record CustomerDetail(
    int Id,
    string Name,
    string? Phone,
    string? Address,
    string? Notes,
    bool IsActive,
    bool IsDefaultCash,
    decimal Balance,
    bool HasOpeningBalance,
    byte[] RowVersion);

public sealed record StatementQuery(int CustomerId, int PageIndex, int PageSize);

/// <summary>
/// One statement line. <c>Amount</c> is positive when the customer owes more and negative when they owe less.
/// <c>CollectionId</c> and <c>CollectionReversed</c> are set for collection lines so the page can offer a reversal.
/// </summary>
public sealed record CustomerEntryRow(
    long Id,
    DateTime OccurredAtUtc,
    CustomerEntryType Type,
    decimal Amount,
    decimal BalanceAfter,
    string? Note,
    string UserName,
    string? ReferenceType,
    long? ReferenceId,
    string? ReferenceNumber,
    bool CollectionReversed);

public sealed record CollectDebtRequest(Guid OperationId, int CustomerId, decimal Amount, string? Note);

public sealed record ReverseCollectionRequest(Guid OperationId, int CollectionId, string? Reason);

public sealed record CustomerOpeningBalanceRequest(Guid OperationId, int CustomerId, decimal Amount, string? Note);
