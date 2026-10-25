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
using Pharmacy.Domain.Purchasing;

namespace Pharmacy.Application.Purchasing;

/// <summary>
/// Posts returns of goods to suppliers. One transaction takes the goods out of the batches they were bought into,
/// takes the value off what the pharmacy owes the supplier and/or receives it in cash into the open shift, and writes
/// the audit entry. The split follows one rule: what is owed comes down first, but never by more than the credit part
/// of the invoice that earlier returns have not already reduced and never below zero; the rest is cash from the supplier.
/// </summary>
public sealed class PurchaseReturnService(
    IPharmacyDbContext db,
    InventoryLedger inventoryLedger,
    CashLedger cashLedger,
    ICurrentUser currentUser,
    IAuditLog auditLog,
    TimeProvider clock)
{
    public const string ReturnPrefix = "PR";
    public const string ReturnReference = "PurchaseReturn";

    private static readonly Error _forbidden = new(CommonErrors.Forbidden);

    /// <summary>Works out what the return would do (value, debt reduction, cash refund) without posting anything.</summary>
    public async Task<Result<PurchaseReturnPreview>> PreviewAsync(PurchaseReturnRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!currentUser.HasPermission(Permissions.PurchasingReturn))
        {
            return Result.Failure<PurchaseReturnPreview>(_forbidden);
        }

        var plan = await PlanAsync(request, cancellationToken);
        return plan.IsFailure
            ? Result.Failure<PurchaseReturnPreview>(plan.Error!)
            : Result.Success(new PurchaseReturnPreview(
                plan.Value.Lines.Sum(l => l.Value), plan.Value.Split.DebtReduction, plan.Value.Split.CashRefund,
                [.. plan.Value.Lines.Select(l => l.Value)]));
    }

    /// <summary>Posts the return and returns its id. A repeated submission of the same operation returns the first return.</summary>
    public async Task<Result<int>> CreateAsync(PurchaseReturnRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!currentUser.HasPermission(Permissions.PurchasingReturn))
        {
            return Result.Failure<int>(_forbidden);
        }

        return await db.RunGuardedAsync(() => PostAsync(request, cancellationToken), Result.Failure<int>);
    }

    private async Task<Result<int>> PostAsync(PurchaseReturnRequest request, CancellationToken cancellationToken)
    {
        var existing = await db.PurchaseReturns.AsNoTracking()
            .Where(r => r.OperationId == request.OperationId)
            .Select(r => (int?)r.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            return Result.Success(existing.Value);
        }

        // The invoice is locked first, then the supplier: two returns of the same invoice cannot send back the same goods.
        if (!await db.LockPurchaseInvoiceAsync(request.InvoiceId, cancellationToken))
        {
            return Result.Failure<int>(new Error(PurchasingErrors.UnknownInvoice));
        }

        var supplierId = await db.PurchaseInvoices.AsNoTracking()
            .Where(i => i.Id == request.InvoiceId)
            .Select(i => i.SupplierId)
            .SingleAsync(cancellationToken);
        var supplier = supplierId is { } id ? await db.LoadSupplierForUpdateAsync(id, cancellationToken) : null;

        var planned = await PlanAsync(request, cancellationToken);
        if (planned.IsFailure)
        {
            return Result.Failure<int>(planned.Error!);
        }

        var plan = planned.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var userName = currentUser.UserName ?? string.Empty;
        var number = await db.NextDocumentNumberAsync(ReturnPrefix, clock.GetLocalNow().Year, cancellationToken);
        var created = PurchaseReturn.Create(
            number, request.InvoiceId, supplierId, plan.Lines, plan.Split, request.Reason, request.OperationId, now,
            currentUser.UserId, userName);
        if (created.IsFailure)
        {
            return Result.Failure<int>(created.Error!);
        }

        var purchaseReturn = created.Value;
        db.PurchaseReturns.Add(purchaseReturn);

        // The return id is the reference of every stock, cash, and statement entry below, so save to get it.
        await db.SaveChangesAsync(cancellationToken);

        var stock = await TakeStockOutAsync(purchaseReturn, cancellationToken);
        if (stock.IsFailure)
        {
            return Result.Failure<int>(stock.Error!);
        }

        if (purchaseReturn.DebtReduction > 0)
        {
            var entry = supplier!.Decrease(
                SupplierEntryType.PurchaseReturn, purchaseReturn.DebtReduction,
                new AccountContext(now, currentUser.UserId, userName, purchaseReturn.Number, ReturnReference, purchaseReturn.Id));
            if (entry.IsFailure)
            {
                return Result.Failure<int>(entry.Error!);
            }

            db.SupplierAccountEntries.Add(entry.Value);
        }

        if (purchaseReturn.CashRefund > 0)
        {
            var cash = await cashLedger.ReceiveAsync(
                CashMovementType.PurchaseRefund, purchaseReturn.CashRefund,
                new CashMovementInfo(purchaseReturn.Number, ReturnReference, purchaseReturn.Id), cancellationToken);
            if (cash.IsFailure)
            {
                return Result.Failure<int>(cash.Error!);
            }
        }

        auditLog.Record(AuditActions.PurchaseReturned, purchaseReturn.Number, new
        {
            request.InvoiceId,
            purchaseReturn.Total,
            purchaseReturn.DebtReduction,
            purchaseReturn.CashRefund,
            purchaseReturn.Reason,
            Lines = purchaseReturn.Lines.Count,
        });
        return Result.Success(purchaseReturn.Id);
    }

    private sealed record Plan(List<NewPurchaseReturnLine> Lines, RefundSplit Split);

    // Reads the invoice and what earlier returns sent back, and works out the lines and the settlement.
    private async Task<Result<Plan>> PlanAsync(PurchaseReturnRequest request, CancellationToken cancellationToken)
    {
        if (request.Lines.Count == 0)
        {
            return Result.Failure<Plan>(new Error(PurchasingErrors.ReturnNoLines));
        }

        if (request.Lines.Select(l => l.InvoiceLineId).Distinct().Count() != request.Lines.Count)
        {
            return Result.Failure<Plan>(new Error(PurchasingErrors.DuplicateReturnLine));
        }

        var invoice = await db.PurchaseInvoices.AsNoTracking()
            .Include(i => i.Lines)
            .SingleOrDefaultAsync(i => i.Id == request.InvoiceId, cancellationToken);
        if (invoice is null)
        {
            return Result.Failure<Plan>(new Error(PurchasingErrors.UnknownInvoice));
        }

        var earlier = await db.PurchaseReturns.AsNoTracking()
            .Include(r => r.Lines)
            .Where(r => r.InvoiceId == invoice.Id)
            .ToListAsync(cancellationToken);
        var returnedByLine = earlier.SelectMany(r => r.Lines).GroupBy(l => l.InvoiceLineId)
            .ToDictionary(g => g.Key, g => (Base: g.Sum(l => l.BaseQuantity), Value: g.Sum(l => l.Value)));

        var productIds = invoice.Lines.Select(l => l.ProductId).Distinct().ToList();
        var units = await db.Set<ProductUnit>().AsNoTracking()
            .Where(u => productIds.Contains(u.ProductId))
            .ToDictionaryAsync(u => u.Id, cancellationToken);
        var batchIds = invoice.Lines.Where(l => l.BatchId != null).Select(l => l.BatchId!.Value).Distinct().ToList();
        var batches = await db.Batches.AsNoTracking()
            .Where(b => batchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, cancellationToken);

        var lines = new List<NewPurchaseReturnLine>(request.Lines.Count);
        foreach (var requested in request.Lines)
        {
            var line = invoice.Lines.SingleOrDefault(l => l.Id == requested.InvoiceLineId);
            if (line is null || line.BatchId is not { } batchId || !batches.TryGetValue(batchId, out var batch))
            {
                return Result.Failure<Plan>(new Error(PurchasingErrors.UnknownInvoiceLine));
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
                return Result.Failure<Plan>(new Error(PurchasingErrors.ReturnQuantityExceeds, remaining));
            }

            // Some of the goods may have been sold since: only what the batch still holds can go back.
            var held = requested.FromQuarantine ? batch.QuantityQuarantined : batch.QuantityAvailable;
            if (baseQuantity > held)
            {
                return Result.Failure<Plan>(new Error(PurchasingErrors.BatchStockTooLow, held));
            }

            var value = ReturnPricing.LineValue(line.LineTotal, line.BaseQuantity, before.Base, before.Value, (int)baseQuantity);
            lines.Add(new NewPurchaseReturnLine(
                line.Id, line.ProductId, unit.Id, unit.Factor, requested.Quantity, (int)baseQuantity, batchId, requested.FromQuarantine, value));
        }

        var total = lines.Sum(l => l.Value);
        var creditRemaining = Math.Max(0m, invoice.OnCredit - earlier.Sum(r => r.DebtReduction));
        var balance = invoice.SupplierId is { } supplierId
            ? await db.Suppliers.AsNoTracking().Where(s => s.Id == supplierId).Select(s => s.CurrentBalance).SingleAsync(cancellationToken)
            : 0m;
        return Result.Success(new Plan(lines, ReturnPricing.Split(total, creditRemaining, balance)));
    }

    // Each batch is locked together with the rest of its product's batches, then gives up its share.
    private async Task<Result> TakeStockOutAsync(PurchaseReturn purchaseReturn, CancellationToken cancellationToken)
    {
        foreach (var line in purchaseReturn.Lines)
        {
            var batch = await inventoryLedger.LockBatchAsync(line.BatchId, cancellationToken);
            if (batch is null)
            {
                return Result.Failure(new Error(InventoryErrors.UnknownBatch));
            }

            var staged = inventoryLedger.Stage(batch.ReturnToSupplier(
                line.BaseQuantity,
                line.FromQuarantine,
                inventoryLedger.ContextFor(new MovementInfo(
                    ReasonCode: null, Note: purchaseReturn.Number, ReferenceType: ReturnReference, ReferenceId: purchaseReturn.Id))));
            if (staged.IsFailure)
            {
                return staged;
            }
        }

        return Result.Success();
    }
}
