using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Application.Auditing;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Cash;
using Pharmacy.Application.Common;
using Pharmacy.Application.Inventory;
using Pharmacy.Domain.Cash;
using Pharmacy.Domain.Catalog;
using Pharmacy.Domain.Common;
using Pharmacy.Domain.Inventory;
using Pharmacy.Domain.Receivables;
using Pharmacy.Domain.Sales;

namespace Pharmacy.Application.Sales;

/// <summary>
/// Posts sales returns. One transaction gives the goods back to the batches they were sold from (at the cost they had),
/// takes the value off the customer's debt and/or pays it back in cash from the open shift, and writes the audit entry.
/// The split follows one rule: debt first, but never more than the credit part of the invoice that earlier returns have
/// not already reduced and never more than the customer owes; the rest is cash.
/// </summary>
public sealed class SalesReturnService(
    IPharmacyDbContext db,
    InventoryLedger inventoryLedger,
    CashLedger cashLedger,
    ICurrentUser currentUser,
    IAuditLog auditLog,
    TimeProvider clock)
{
    public const string ReturnPrefix = "SR";
    public const string ReturnReference = "SalesReturn";

    private static readonly Error _forbidden = new(CommonErrors.Forbidden);

    /// <summary>Works out what the return would do (value, debt reduction, cash refund) without posting anything.</summary>
    public async Task<Result<ReturnPreview>> PreviewAsync(SalesReturnRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!currentUser.HasPermission(Permissions.SalesReturn))
        {
            return Result.Failure<ReturnPreview>(_forbidden);
        }

        var plan = await PlanAsync(request, cancellationToken);
        return plan.IsFailure
            ? Result.Failure<ReturnPreview>(plan.Error!)
            : Result.Success(new ReturnPreview(
                plan.Value.Lines.Sum(l => l.Value), plan.Value.Split.DebtReduction, plan.Value.Split.CashRefund,
                plan.Value.IsLate, plan.Value.WindowDays, [.. plan.Value.Lines.Select(l => l.Value)]));
    }

    /// <summary>Posts the return and returns its id. A repeated submission of the same operation returns the first return.</summary>
    public async Task<Result<int>> CreateAsync(SalesReturnRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!currentUser.HasPermission(Permissions.SalesReturn))
        {
            return Result.Failure<int>(_forbidden);
        }

        return await db.RunGuardedAsync(() => PostAsync(request, cancellationToken), Result.Failure<int>);
    }

    private async Task<Result<int>> PostAsync(SalesReturnRequest request, CancellationToken cancellationToken)
    {
        var existing = await db.SalesReturns.AsNoTracking()
            .Where(r => r.OperationId == request.OperationId)
            .Select(r => (int?)r.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            return Result.Success(existing.Value);
        }

        // The invoice is locked first, then the customer: two returns of the same invoice cannot give back the same goods.
        if (!await db.LockSalesInvoiceAsync(request.InvoiceId, cancellationToken))
        {
            return Result.Failure<int>(new Error(SalesErrors.UnknownInvoice));
        }

        var customerId = await db.SalesInvoices.AsNoTracking()
            .Where(i => i.Id == request.InvoiceId)
            .Select(i => i.CustomerId)
            .SingleAsync(cancellationToken);
        var customer = await db.LoadCustomerForUpdateAsync(customerId, cancellationToken);

        var planned = await PlanAsync(request, cancellationToken);
        if (planned.IsFailure)
        {
            return Result.Failure<int>(planned.Error!);
        }

        var plan = planned.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var userName = currentUser.UserName ?? string.Empty;
        var number = await db.NextDocumentNumberAsync(ReturnPrefix, clock.GetLocalNow().Year, cancellationToken);
        var created = SalesReturn.Create(
            number, request.InvoiceId, customerId, plan.Lines, plan.Split, request.Reason, plan.IsLate,
            request.OperationId, now, currentUser.UserId, userName);
        if (created.IsFailure)
        {
            return Result.Failure<int>(created.Error!);
        }

        var salesReturn = created.Value;
        db.SalesReturns.Add(salesReturn);

        // The return id is the reference of every stock, cash, and statement entry below, so save to get it.
        await db.SaveChangesAsync(cancellationToken);

        var stock = await GiveBackStockAsync(salesReturn, cancellationToken);
        if (stock.IsFailure)
        {
            return Result.Failure<int>(stock.Error!);
        }

        if (salesReturn.DebtReduction > 0)
        {
            var entry = customer!.Credit(
                CustomerEntryType.SaleReturn, salesReturn.DebtReduction,
                new CustomerAccountContext(now, currentUser.UserId, userName, salesReturn.Number, ReturnReference, salesReturn.Id));
            if (entry.IsFailure)
            {
                return Result.Failure<int>(entry.Error!);
            }

            db.CustomerAccountEntries.Add(entry.Value);
        }

        if (salesReturn.CashRefund > 0)
        {
            var cash = await cashLedger.PayAsync(
                CashMovementType.SaleRefund, salesReturn.CashRefund,
                new CashMovementInfo(salesReturn.Number, ReturnReference, salesReturn.Id), cancellationToken);
            if (cash.IsFailure)
            {
                return Result.Failure<int>(cash.Error!);
            }
        }

        auditLog.Record(AuditActions.SaleReturned, salesReturn.Number, new
        {
            request.InvoiceId,
            salesReturn.Total,
            salesReturn.DebtReduction,
            salesReturn.CashRefund,
            Lines = salesReturn.Lines.Count,
        });
        if (salesReturn.IsLate)
        {
            auditLog.Record(AuditActions.SaleLateReturn, salesReturn.Number, new { request.InvoiceId, salesReturn.Reason });
        }

        return Result.Success(salesReturn.Id);
    }

    private sealed record Plan(List<NewReturnLine> Lines, RefundSplit Split, bool IsLate, int WindowDays);

    // Reads the invoice and what earlier returns took back, and works out the lines, the allocations, and the settlement.
    private async Task<Result<Plan>> PlanAsync(SalesReturnRequest request, CancellationToken cancellationToken)
    {
        if (request.Lines.Count == 0)
        {
            return Result.Failure<Plan>(new Error(SalesErrors.ReturnNoLines));
        }

        if (request.Lines.Select(l => l.InvoiceLineId).Distinct().Count() != request.Lines.Count)
        {
            return Result.Failure<Plan>(new Error(SalesErrors.DuplicateReturnLine));
        }

        var invoice = await db.SalesInvoices.AsNoTracking()
            .Include(i => i.Lines).ThenInclude(l => l.Allocations)
            .SingleOrDefaultAsync(i => i.Id == request.InvoiceId, cancellationToken);
        if (invoice is null)
        {
            return Result.Failure<Plan>(new Error(SalesErrors.UnknownInvoice));
        }

        var window = await db.PharmacySettings.Select(s => s.ReturnWindowDays).SingleAsync(cancellationToken);
        var isLate = DaysSinceSale(invoice.CreatedAtUtc) > window;
        if (isLate && !currentUser.HasPermission(Permissions.SalesLateReturn))
        {
            return Result.Failure<Plan>(new Error(SalesErrors.ReturnWindowPassed, window));
        }

        var earlier = await db.SalesReturns.AsNoTracking()
            .Include(r => r.Lines).ThenInclude(l => l.Allocations)
            .Where(r => r.InvoiceId == invoice.Id)
            .ToListAsync(cancellationToken);
        var returnedByLine = earlier.SelectMany(r => r.Lines).GroupBy(l => l.InvoiceLineId)
            .ToDictionary(g => g.Key, g => (Base: g.Sum(l => l.BaseQuantity), Value: g.Sum(l => l.Value)));
        var returnedByAllocation = earlier.SelectMany(r => r.Lines).SelectMany(l => l.Allocations).GroupBy(a => a.InvoiceAllocationId)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.BaseQuantity));

        var productIds = invoice.Lines.Select(l => l.ProductId).Distinct().ToList();
        var units = await db.Set<ProductUnit>().AsNoTracking()
            .Where(u => productIds.Contains(u.ProductId))
            .ToDictionaryAsync(u => u.Id, cancellationToken);
        var batchIds = invoice.Lines.SelectMany(l => l.Allocations).Select(a => a.BatchId).Distinct().ToList();
        var expiries = await db.Batches.AsNoTracking()
            .Where(b => batchIds.Contains(b.Id))
            .Select(b => new { b.Id, b.ExpiryDate })
            .ToDictionaryAsync(b => b.Id, b => b.ExpiryDate, cancellationToken);

        var today = inventoryLedger.Today;
        var lines = new List<NewReturnLine>(request.Lines.Count);
        foreach (var requested in request.Lines)
        {
            var line = invoice.Lines.SingleOrDefault(l => l.Id == requested.InvoiceLineId);
            if (line is null)
            {
                return Result.Failure<Plan>(new Error(SalesErrors.UnknownInvoiceLine));
            }

            if (!units.TryGetValue(requested.ProductUnitId, out var unit) || unit.ProductId != line.ProductId)
            {
                return Result.Failure<Plan>(new Error(InventoryErrors.UnknownUnit));
            }

            if (requested.Quantity <= 0)
            {
                return Result.Failure<Plan>(new Error(InventoryErrors.InvalidQuantity));
            }

            var baseQuantity = (long)requested.Quantity * unit.Factor;
            var before = returnedByLine.GetValueOrDefault(line.Id);
            var remaining = line.BaseQuantity - before.Base;
            if (baseQuantity > remaining)
            {
                return Result.Failure<Plan>(new Error(SalesErrors.ReturnQuantityExceeds, remaining));
            }

            var value = ReturnPricing.LineValue(line.NetTotal, line.BaseQuantity, before.Base, before.Value, (int)baseQuantity);

            // The goods go back to the batches they came from, the last one taken first.
            var allocations = new List<NewReturnAllocation>();
            var left = (int)baseQuantity;
            foreach (var allocation in line.Allocations.OrderByDescending(a => a.Id))
            {
                var available = allocation.BaseQuantity - returnedByAllocation.GetValueOrDefault(allocation.Id);
                var taken = Math.Min(available, left);
                if (taken <= 0)
                {
                    continue;
                }

                // Goods that have expired meanwhile are never put back on the shelf.
                var quarantined = !requested.Sellable || ExpiryRules.IsExpired(expiries[allocation.BatchId], today);
                allocations.Add(new NewReturnAllocation(allocation.Id, allocation.BatchId, taken, allocation.UnitCost, quarantined));
                left -= taken;
            }

            if (left != 0)
            {
                return Result.Failure<Plan>(new Error(SalesErrors.ReturnQuantityExceeds, remaining));
            }

            lines.Add(new NewReturnLine(line.Id, line.ProductId, unit.Id, unit.Factor, requested.Quantity, (int)baseQuantity, value, allocations));
        }

        var total = lines.Sum(l => l.Value);
        var creditRemaining = Math.Max(0m, invoice.OnCredit - earlier.Sum(r => r.DebtReduction));
        var balance = await db.Customers.AsNoTracking()
            .Where(c => c.Id == invoice.CustomerId)
            .Select(c => c.CurrentBalance)
            .SingleAsync(cancellationToken);
        return Result.Success(new Plan(lines, ReturnPricing.Split(total, creditRemaining, balance), isLate, window));
    }

    // Each batch is locked together with the rest of its product's batches, then takes its share back.
    private async Task<Result> GiveBackStockAsync(SalesReturn salesReturn, CancellationToken cancellationToken)
    {
        foreach (var allocation in salesReturn.Lines.SelectMany(l => l.Allocations))
        {
            var batch = await inventoryLedger.LockBatchAsync(allocation.BatchId, cancellationToken);
            if (batch is null)
            {
                return Result.Failure(new Error(InventoryErrors.UnknownBatch));
            }

            var staged = inventoryLedger.Stage(batch.ReturnFromCustomer(
                allocation.BaseQuantity,
                allocation.Quarantined,
                inventoryLedger.ContextFor(new MovementInfo(Note: salesReturn.Number, ReferenceType: ReturnReference, ReferenceId: salesReturn.Id))));
            if (staged.IsFailure)
            {
                return staged;
            }
        }

        return Result.Success();
    }

    // Whole local calendar days between the sale and today, in the pharmacy's time zone.
    private int DaysSinceSale(DateTime soldAtUtc)
    {
        var soldLocal = TimeZoneInfo.ConvertTimeFromUtc(soldAtUtc, clock.LocalTimeZone).Date;
        return (clock.GetLocalNow().Date - soldLocal).Days;
    }
}
