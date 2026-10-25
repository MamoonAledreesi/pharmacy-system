namespace Pharmacy.Domain.Common;

/// <summary>How a return is settled: what comes off the debt and what moves in cash.</summary>
public sealed record RefundSplit(decimal DebtReduction, decimal CashRefund);

/// <summary>The money rules of a return (to a customer or to a supplier): how it is valued and how it is settled.</summary>
public static class ReturnPricing
{
    /// <summary>
    /// The value of returning part of an invoice line: its net price (after the invoice discount) in proportion to the
    /// quantity returned. Returning everything that is left gives exactly what is left of the line's value, so several
    /// partial returns never add up to more or less than the line was sold for.
    /// </summary>
    public static decimal LineValue(
        decimal lineNetTotal, int lineBaseQuantity, int alreadyReturnedBaseQuantity, decimal alreadyReturnedValue, int returningBaseQuantity)
    {
        var remaining = lineBaseQuantity - alreadyReturnedBaseQuantity;
        return returningBaseQuantity == remaining
            ? lineNetTotal - alreadyReturnedValue
            : decimal.Round(lineNetTotal * returningBaseQuantity / lineBaseQuantity, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The unified refund rule. The debt for this invoice is reduced first, but never by more than the credit
    /// part of the invoice that earlier returns have not already reduced, and never below zero; the rest is paid back in cash.
    /// </summary>
    public static RefundSplit Split(decimal returnAmount, decimal creditPortionRemaining, decimal customerBalance)
    {
        var debtReduction = Math.Max(0m, Math.Min(returnAmount, Math.Min(creditPortionRemaining, customerBalance)));
        return new RefundSplit(debtReduction, returnAmount - debtReduction);
    }
}
