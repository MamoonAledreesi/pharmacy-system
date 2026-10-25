using Pharmacy.Application.Sales;

namespace Pharmacy.Web.Components.Pages.Sales;

/// <summary>A line of the sale being entered, with the texts the table shows (the request alone has only ids).</summary>
public sealed record SaleEditorLine(SaleLineRequest Request, string ProductText, string UnitText);

/// <summary>What the approval dialog collected: the below-cost reason and/or the supervisor's credentials.</summary>
public sealed record SaleApproval(string? Reason, SupervisorCredentials? Supervisor);
