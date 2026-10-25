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
using Pharmacy.Domain.Receivables;
using Pharmacy.Domain.Sales;

namespace Pharmacy.Application.Sales;

/// <summary>
/// Posts sales. One transaction takes every line from stock (first expiring first, possibly from several batches),
/// receives the cash part into the open shift, adds the credit part to what the customer owes, and writes the audit
/// entry, so a sale is either posted completely or not at all. Paying cash for a sale never lowers an older debt.
/// </summary>
public sealed class SalesService(
    IPharmacyDbContext db,
    InventoryLedger inventoryLedger,
    CashLedger cashLedger,
    ISupervisorVerifier supervisorVerifier,
    ICurrentUser currentUser,
    IAuditLog auditLog,
    TimeProvider clock)
{
    public const string InvoicePrefix = "SI";
    public const string InvoiceReference = "SalesInvoice";

    private static readonly Error _forbidden = new(CommonErrors.Forbidden);

    /// <summary>Posts the sale and returns its id. A repeated submission of the same operation returns the first invoice.</summary>
    public async Task<Result<int>> CreateAsync(SaleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Lines);

        if (!currentUser.HasPermission(Permissions.SalesCreate))
        {
            return Result.Failure<int>(_forbidden);
        }

        // The supervisor is checked before the transaction starts: a wrong password counts toward their lockout,
        // and that count must survive the sale being refused.
        SupervisorIdentity? supervisor = null;
        if (request.Supervisor is { IsProvided: true } credentials)
        {
            var verified = await supervisorVerifier.VerifyAsync(credentials.UserName, credentials.Password, cancellationToken);
            if (verified.IsFailure)
            {
                return Result.Failure<int>(verified.Error!);
            }

            supervisor = verified.Value;
        }

        return await db.RunGuardedAsync(() => PostAsync(request, supervisor, cancellationToken), Result.Failure<int>);
    }

    private async Task<Result<int>> PostAsync(SaleRequest request, SupervisorIdentity? supervisor, CancellationToken cancellationToken)
    {
        var existing = await db.SalesInvoices.AsNoTracking()
            .Where(i => i.OperationId == request.OperationId)
            .Select(i => (int?)i.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            return Result.Success(existing.Value);
        }

        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken);
        if (customer is null)
        {
            return Result.Failure<int>(new Error(SalesErrors.UnknownCustomer));
        }

        if (!customer.IsActive)
        {
            return Result.Failure<int>(new Error(SalesErrors.InactiveCustomer));
        }

        var lines = await BuildLinesAsync(request.Lines, cancellationToken);
        if (lines.IsFailure)
        {
            return Result.Failure<int>(lines.Error!);
        }

        var priced = SalePricing.Price(
            lines.Value.Select(l => new PricedLineInput(l.Quantity, l.UnitPrice)).ToList(),
            new InvoiceDiscount(request.DiscountAmount, request.DiscountPercent));
        if (priced.IsFailure)
        {
            return Result.Failure<int>(priced.Error!);
        }

        var onCredit = priced.Value.Net - request.PaidCash;
        if (onCredit > 0 && customer.IsDefaultCash)
        {
            return Result.Failure<int>(new Error(SalesErrors.CreditNeedsRegisteredCustomer));
        }

        var discountApproval = await CheckDiscountAsync(priced.Value, supervisor, cancellationToken);
        if (discountApproval.IsFailure)
        {
            return Result.Failure<int>(discountApproval.Error!);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var userName = currentUser.UserName ?? string.Empty;
        var number = await db.NextDocumentNumberAsync(InvoicePrefix, clock.GetLocalNow().Year, cancellationToken);
        var created = SalesInvoice.Create(
            number, customer.Id, priced.Value, lines.Value, request.PaidCash, request.Note, request.OperationId,
            now, currentUser.UserId, userName);
        if (created.IsFailure)
        {
            return Result.Failure<int>(created.Error!);
        }

        var invoice = created.Value;
        db.SalesInvoices.Add(invoice);

        // The invoice id is the reference of every stock, cash, and statement entry below, so save to get it.
        await db.SaveChangesAsync(cancellationToken);

        var taken = await TakeStockAsync(invoice, lines.Value, cancellationToken);
        if (taken.IsFailure)
        {
            return Result.Failure<int>(taken.Error!);
        }

        var belowCost = CheckBelowCost(invoice, request.BelowCostReason, supervisor);
        if (belowCost.IsFailure)
        {
            return Result.Failure<int>(belowCost.Error!);
        }

        var approved = invoice.RecordApprovals(
            belowCost.Value, invoice.HasBelowCostLines ? request.BelowCostReason : null, discountApproval.Value);
        if (approved.IsFailure)
        {
            return Result.Failure<int>(approved.Error!);
        }

        await db.SaveChangesAsync(cancellationToken);

        if (invoice.PaidCash > 0)
        {
            var cash = await cashLedger.ReceiveAsync(
                CashMovementType.SalePayment, invoice.PaidCash,
                new CashMovementInfo(invoice.Number, InvoiceReference, invoice.Id), cancellationToken);
            if (cash.IsFailure)
            {
                return Result.Failure<int>(cash.Error!);
            }
        }

        if (invoice.OnCredit > 0)
        {
            var debtor = await db.LoadCustomerForUpdateAsync(customer.Id, cancellationToken);
            var entry = debtor!.Debit(
                CustomerEntryType.Sale, invoice.OnCredit,
                new CustomerAccountContext(now, currentUser.UserId, userName, invoice.Number, InvoiceReference, invoice.Id));
            if (entry.IsFailure)
            {
                return Result.Failure<int>(entry.Error!);
            }

            db.CustomerAccountEntries.Add(entry.Value);
        }

        RecordAudit(invoice, customer.Name);
        return Result.Success(invoice.Id);
    }

    private async Task<Result<List<NewSaleLine>>> BuildLinesAsync(IReadOnlyList<SaleLineRequest> requested, CancellationToken cancellationToken)
    {
        if (requested.Count == 0)
        {
            return Failure(SalesErrors.NoLines);
        }

        if (requested.Count > SalesInvoice.MaxLines)
        {
            return Result.Failure<List<NewSaleLine>>(new Error(SalesErrors.TooManyLines, SalesInvoice.MaxLines));
        }

        var productIds = requested.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Include(p => p.Units)
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var lines = new List<NewSaleLine>(requested.Count);
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
                return Result.Failure<List<NewSaleLine>>(new Error(InventoryErrors.QuantityTooLarge, Batch.MaxOperationQuantity));
            }

            if (!SalePricing.IsValidPrice(line.UnitPrice))
            {
                return Failure(SalesErrors.InvalidPrice);
            }

            lines.Add(new NewSaleLine(product.Id, unit.Id, unit.Factor, line.Quantity, (int)baseQuantity, line.UnitPrice));
        }

        return Result.Success(lines);
    }

    // A discount above the limit needs the seller's own permission or a supervisor who has it.
    private async Task<Result<string?>> CheckDiscountAsync(PricedSale priced, SupervisorIdentity? supervisor, CancellationToken cancellationToken)
    {
        var limit = await db.PharmacySettings.Select(s => s.MaxDiscountPercent).SingleAsync(cancellationToken);
        if (priced.DiscountPercent <= limit || currentUser.HasPermission(Permissions.SalesGrantHighDiscount))
        {
            return Result.Success<string?>(null);
        }

        if (supervisor is null)
        {
            return Result.Failure<string?>(new Error(SalesErrors.HighDiscountNeedsApproval, limit));
        }

        return supervisor.Has(Permissions.SalesGrantHighDiscount)
            ? Result.Success<string?>(supervisor.UserName)
            : Result.Failure<string?>(new Error(SalesErrors.SupervisorNotAccepted));
    }

    // Selling below cost needs a reason, and the seller's own permission or a supervisor who has it.
    private Result<string?> CheckBelowCost(SalesInvoice invoice, string? reason, SupervisorIdentity? supervisor)
    {
        if (!invoice.HasBelowCostLines)
        {
            return Result.Success<string?>(null);
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure<string?>(new Error(SalesErrors.BelowCostReasonRequired));
        }

        if (currentUser.HasPermission(Permissions.SalesSellBelowCost))
        {
            return Result.Success<string?>(null);
        }

        if (supervisor is null)
        {
            return Result.Failure<string?>(new Error(SalesErrors.BelowCostNeedsApproval));
        }

        return supervisor.Has(Permissions.SalesSellBelowCost)
            ? Result.Success<string?>(supervisor.UserName)
            : Result.Failure<string?>(new Error(SalesErrors.SupervisorNotAccepted));
    }

    // Each line is taken from stock in turn, so a second line for the same product sees what the first one left.
    private async Task<Result> TakeStockAsync(SalesInvoice invoice, IReadOnlyList<NewSaleLine> lines, CancellationToken cancellationToken)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var taken = await inventoryLedger.ConsumeFefoAsync(
                lines[index].ProductId, lines[index].BaseQuantity, StockMovementType.Sale,
                new MovementInfo(Note: invoice.Number, ReferenceType: InvoiceReference, ReferenceId: invoice.Id), cancellationToken);
            if (taken.IsFailure)
            {
                return Result.Failure(taken.Error!);
            }

            invoice.RecordAllocations(index, taken.Value);
        }

        return Result.Success();
    }

    private void RecordAudit(SalesInvoice invoice, string customerName)
    {
        auditLog.Record(AuditActions.SaleCreated, invoice.Number, new
        {
            Customer = customerName,
            invoice.Subtotal,
            invoice.DiscountAmount,
            invoice.Total,
            invoice.PaidCash,
            invoice.OnCredit,
            Lines = invoice.Lines.Count,
        });

        if (invoice.HasBelowCostLines)
        {
            auditLog.Record(AuditActions.SaleBelowCost, invoice.Number, new
            {
                invoice.BelowCostReason,
                ApprovedBy = invoice.BelowCostApprovedBy ?? currentUser.UserName,
                Lines = invoice.Lines
                    .Where(l => l.IsBelowCost)
                    .Select(l => new { l.ProductId, l.NetTotal, l.CostTotal })
                    .ToList(),
            });
        }

        if (invoice.DiscountApprovedBy is not null)
        {
            auditLog.Record(AuditActions.SaleHighDiscount, invoice.Number, new
            {
                invoice.DiscountPercent,
                ApprovedBy = invoice.DiscountApprovedBy,
            });
        }

    }

    private static Result<List<NewSaleLine>> Failure(string code) => Result.Failure<List<NewSaleLine>>(new Error(code));
}
