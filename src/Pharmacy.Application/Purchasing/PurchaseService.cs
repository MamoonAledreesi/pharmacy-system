using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Application.Auditing;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Cash;
using Pharmacy.Application.Common;
using Pharmacy.Application.Inventory;
using Pharmacy.Domain.Cash;
using Pharmacy.Domain.Common;
using Pharmacy.Domain.Inventory;
using Pharmacy.Domain.Purchasing;

namespace Pharmacy.Application.Purchasing;

/// <summary>
/// Posts purchase invoices. One transaction receives every line into stock (creating or topping up batches), pays
/// the cash part from the open shift, adds the credit part to what the supplier is owed, and writes the audit entry,
/// so an invoice is either posted completely or not at all.
/// </summary>
public sealed class PurchaseService(
    IPharmacyDbContext db,
    InventoryLedger inventoryLedger,
    CashLedger cashLedger,
    ICurrentUser currentUser,
    IAuditLog auditLog,
    TimeProvider clock)
{
    public const string InvoicePrefix = "PI";
    public const string InvoiceReference = "PurchaseInvoice";

    private static readonly Error _forbidden = new(CommonErrors.Forbidden);

    /// <summary>Posts the invoice and returns its id. A repeated submission of the same operation returns the first invoice.</summary>
    public async Task<Result<int>> CreateAsync(PurchaseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Lines);

        if (!currentUser.HasPermission(Permissions.PurchasingCreate))
        {
            return Result.Failure<int>(_forbidden);
        }

        return await db.RunGuardedAsync(async () =>
        {
            var existing = await db.PurchaseInvoices.AsNoTracking()
                .Where(i => i.OperationId == request.OperationId)
                .Select(i => (int?)i.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (existing is not null)
            {
                return Result.Success(existing.Value);
            }

            Supplier? supplier = null;
            if (request.SupplierId is { } supplierId)
            {
                supplier = await db.LoadSupplierForUpdateAsync(supplierId, cancellationToken);
                if (supplier is null)
                {
                    return Result.Failure<int>(new Error(PurchasingErrors.UnknownSupplier));
                }

                if (!supplier.IsActive)
                {
                    return Result.Failure<int>(new Error(PurchasingErrors.InactiveSupplier));
                }
            }

            var lines = await BuildLinesAsync(request.Lines, cancellationToken);
            if (lines.IsFailure)
            {
                return Result.Failure<int>(lines.Error!);
            }

            var now = clock.GetUtcNow().UtcDateTime;
            var userName = currentUser.UserName ?? string.Empty;
            var number = await db.NextDocumentNumberAsync(InvoicePrefix, request.InvoiceDate.Year, cancellationToken);
            var created = PurchaseInvoice.Create(
                number, supplier?.Id, request.SupplierName, request.InvoiceDate, inventoryLedger.Today,
                request.SupplierInvoiceNumber, request.PaidCash, request.Note, request.OperationId, now,
                currentUser.UserId, userName, lines.Value);
            if (created.IsFailure)
            {
                return Result.Failure<int>(created.Error!);
            }

            var invoice = created.Value;
            db.PurchaseInvoices.Add(invoice);

            // The invoice id is the reference of every stock, cash, and statement entry below, so save to get it.
            await db.SaveChangesAsync(cancellationToken);

            var received = await ReceiveLinesAsync(invoice, lines.Value, cancellationToken);
            if (received.IsFailure)
            {
                return Result.Failure<int>(received.Error!);
            }

            if (invoice.PaidCash > 0)
            {
                var cash = await cashLedger.PayAsync(
                    CashMovementType.PurchasePayment, invoice.PaidCash,
                    new CashMovementInfo(invoice.Number, InvoiceReference, invoice.Id), cancellationToken);
                if (cash.IsFailure)
                {
                    return Result.Failure<int>(cash.Error!);
                }
            }

            if (invoice.OnCredit > 0)
            {
                var entry = supplier!.Increase(
                    SupplierEntryType.Invoice, invoice.OnCredit,
                    new AccountContext(now, currentUser.UserId, userName, invoice.Number, InvoiceReference, invoice.Id));
                if (entry.IsFailure)
                {
                    return Result.Failure<int>(entry.Error!);
                }

                db.SupplierAccountEntries.Add(entry.Value);
            }

            auditLog.Record(AuditActions.PurchaseCreated, invoice.Number, new
            {
                Supplier = supplier?.Name ?? invoice.SupplierName,
                invoice.Total,
                invoice.PaidCash,
                invoice.OnCredit,
                Lines = invoice.Lines.Count,
            });
            return Result.Success(invoice.Id);
        }, Result.Failure<int>);
    }

    private async Task<Result<List<NewPurchaseLine>>> BuildLinesAsync(
        IReadOnlyList<PurchaseLineRequest> requested, CancellationToken cancellationToken)
    {
        var productIds = requested.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Include(p => p.Units)
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var lines = new List<NewPurchaseLine>(requested.Count);
        foreach (var line in requested)
        {
            if (!products.TryGetValue(line.ProductId, out var product))
            {
                return Failure(InventoryErrors.UnknownProduct);
            }

            var unit = product.Units.SingleOrDefault(u => u.Id == line.ProductUnitId);
            if (unit is null)
            {
                return Failure(InventoryErrors.UnknownUnit);
            }

            if (!product.IsActive)
            {
                return Failure(InventoryErrors.InactiveProduct);
            }

            if (line.Quantity <= 0)
            {
                return Failure(InventoryErrors.InvalidQuantity);
            }

            var baseQuantity = (long)line.Quantity * unit.Factor;
            if (baseQuantity > Batch.MaxOperationQuantity)
            {
                return Result.Failure<List<NewPurchaseLine>>(new Error(InventoryErrors.QuantityTooLarge, Batch.MaxOperationQuantity));
            }

            var baseCost = Batch.UnitCostFromPackCost(line.CostPerUnit, unit.Factor);
            if (line.CostPerUnit < 0 || decimal.Round(line.CostPerUnit, Batch.UnitCostDecimals) != line.CostPerUnit
                || !Batch.IsValidUnitCost(baseCost))
            {
                return Failure(InventoryErrors.InvalidUnitCost);
            }

            var expiry = product.TracksExpiry ? line.ExpiryDate : null;
            if (product.TracksExpiry)
            {
                if (expiry is null)
                {
                    return Failure(InventoryErrors.ExpiryRequired);
                }

                if (ExpiryRules.IsExpired(expiry, inventoryLedger.Today))
                {
                    return Failure(PurchasingErrors.ExpiryInPast);
                }
            }

            lines.Add(new NewPurchaseLine(
                product.Id, unit.Id, line.Quantity, unit.Factor, (int)baseQuantity, line.CostPerUnit, baseCost, line.BatchNumber, expiry));
        }

        return Result.Success(lines);
    }

    // Each line is saved on its own so a second line for the same batch finds the batch created by the first one.
    private async Task<Result> ReceiveLinesAsync(
        PurchaseInvoice invoice, IReadOnlyList<NewPurchaseLine> lines, CancellationToken cancellationToken)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var received = await inventoryLedger.ReceiveAsync(
                StockMovementType.Purchase, line.ProductId, line.BatchNumber, line.ExpiryDate, line.BaseUnitCost, line.BaseQuantity,
                new MovementInfo(Note: invoice.Number, ReferenceType: InvoiceReference, ReferenceId: invoice.Id), cancellationToken);
            if (received.IsFailure)
            {
                return Result.Failure(received.Error!);
            }

            await db.SaveChangesAsync(cancellationToken);
            invoice.AssignBatch(index, received.Value);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static Result<List<NewPurchaseLine>> Failure(string code) =>
        Result.Failure<List<NewPurchaseLine>>(new Error(code));
}
