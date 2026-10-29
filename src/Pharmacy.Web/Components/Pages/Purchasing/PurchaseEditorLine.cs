using Pharmacy.Application.Purchasing;

namespace Pharmacy.Web.Components.Pages.Purchasing;

/// <summary>A line of the invoice being entered, with the texts the table shows (the request alone has only ids).</summary>
public sealed record PurchaseEditorLine(PurchaseLineRequest Request, string ProductText, string UnitText, decimal Total, string BaseText);
