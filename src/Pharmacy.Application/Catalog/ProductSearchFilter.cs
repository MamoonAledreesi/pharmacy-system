using Pharmacy.Domain.Catalog;

namespace Pharmacy.Application.Catalog;

public static class ProductSearchFilter
{
    /// <summary>Every word typed must appear in the product's normalized search text, in any order.</summary>
    public static IQueryable<Product> Apply(IQueryable<Product> products, string? text, bool includeInactive)
    {
        if (!includeInactive)
        {
            products = products.Where(p => p.IsActive);
        }

        foreach (var word in SearchNormalizer.Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            products = products.Where(p => p.SearchText.Contains(word));
        }

        return products;
    }
}
