using Pharmacy.Domain.Common;

namespace Pharmacy.Domain.Sales;

/// <summary>One line as priced by the seller: how many units at what price each.</summary>
public sealed record PricedLineInput(int Quantity, decimal UnitPrice);

/// <summary>The invoice discount as entered: a fixed amount or a percentage of the subtotal (never both).</summary>
public sealed record InvoiceDiscount(decimal? Amount, decimal? Percent)
{
    public static InvoiceDiscount None { get; } = new(null, null);
}

/// <summary>The result of pricing an invoice: per-line subtotal, discount share, and net, plus the invoice totals.</summary>
public sealed record PricedSale(
    IReadOnlyList<decimal> LineSubtotals,
    IReadOnlyList<decimal> DiscountShares,
    IReadOnlyList<decimal> LineNets,
    decimal Subtotal,
    decimal Discount,
    decimal Net)
{
    /// <summary>The discount as a percentage of the subtotal.</summary>
    public decimal DiscountPercent => Subtotal == 0 ? 0m : decimal.Round(Discount * 100m / Subtotal, 2, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Prices a sale. The invoice discount is spread over the lines in proportion to their subtotals, rounded to
/// the cent, and the rounding difference goes to the largest line, so the shares always add up to exactly the
/// discount. The result is what is stored on the lines: it never changes afterwards.
/// </summary>
public static class SalePricing
{
    public const int Decimals = 2;
    public const int PriceDecimals = 4;
    public const decimal MaxAmount = 1_000_000_000m;

    public static bool IsValidPrice(decimal price) =>
        price is > 0 and <= MaxAmount && decimal.Round(price, PriceDecimals) == price;

    public static decimal LineSubtotal(int quantity, decimal unitPrice) =>
        decimal.Round(quantity * unitPrice, Decimals, MidpointRounding.AwayFromZero);

    public static Result<PricedSale> Price(IReadOnlyList<PricedLineInput> lines, InvoiceDiscount discount)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(discount);

        if (lines.Count == 0)
        {
            return Result.Failure<PricedSale>(new Error(SalesErrors.NoLines));
        }

        if (lines.Any(l => l.Quantity <= 0 || !IsValidPrice(l.UnitPrice)))
        {
            return Result.Failure<PricedSale>(new Error(SalesErrors.InvalidPrice));
        }

        var subtotals = lines.Select(l => LineSubtotal(l.Quantity, l.UnitPrice)).ToList();
        var subtotal = subtotals.Sum();
        if (subtotal > MaxAmount)
        {
            return Result.Failure<PricedSale>(new Error(SalesErrors.TotalTooLarge, MaxAmount));
        }

        var amount = DiscountAmount(subtotal, discount);
        if (amount.IsFailure)
        {
            return Result.Failure<PricedSale>(amount.Error!);
        }

        var shares = Distribute(subtotals, subtotal, amount.Value);
        var nets = subtotals.Zip(shares, (s, d) => s - d).ToList();
        return Result.Success(new PricedSale(subtotals, shares, nets, subtotal, amount.Value, subtotal - amount.Value));
    }

    private static Result<decimal> DiscountAmount(decimal subtotal, InvoiceDiscount discount)
    {
        if (discount.Amount is not null && discount.Percent is not null)
        {
            return Result.Failure<decimal>(new Error(SalesErrors.InvalidDiscount));
        }

        decimal amount;
        if (discount.Percent is { } percent)
        {
            if (percent is < 0 or > 100)
            {
                return Result.Failure<decimal>(new Error(SalesErrors.InvalidDiscount));
            }

            amount = decimal.Round(subtotal * percent / 100m, Decimals, MidpointRounding.AwayFromZero);
        }
        else
        {
            amount = discount.Amount ?? 0m;
            if (amount < 0 || decimal.Round(amount, Decimals) != amount)
            {
                return Result.Failure<decimal>(new Error(SalesErrors.InvalidDiscount));
            }
        }

        // The invoice must still bring in money: a discount equal to the subtotal would be a gift, which is not a sale.
        return amount >= subtotal
            ? Result.Failure<decimal>(new Error(SalesErrors.DiscountTooLarge))
            : Result.Success(amount);
    }

    private static List<decimal> Distribute(IReadOnlyList<decimal> subtotals, decimal subtotal, decimal discount)
    {
        var shares = subtotals.Select(s => decimal.Round(discount * s / subtotal, Decimals, MidpointRounding.AwayFromZero)).ToList();
        var difference = discount - shares.Sum();
        if (difference != 0)
        {
            var largest = subtotals.Select((value, index) => (value, index)).OrderByDescending(x => x.value).ThenBy(x => x.index).First().index;
            shares[largest] += difference;
        }

        return shares;
    }
}
