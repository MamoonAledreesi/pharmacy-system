using System.Reflection;
using NetArchTest.Rules;
using Pharmacy.Domain.Common;
using Pharmacy.Infrastructure.Persistence;
using Pharmacy.Web;

namespace Pharmacy.ArchitectureTests;

/// <summary>
/// Enforces the Clean Architecture direction: Web → Infrastructure → Application → Domain.
/// </summary>
public class LayerDependencyTests
{
    private const string Domain = "Pharmacy.Domain";
    private const string Application = "Pharmacy.Application";
    private const string Infrastructure = "Pharmacy.Infrastructure";
    private const string Web = "Pharmacy.Web";

    private static readonly Assembly _domainAssembly = typeof(Result).Assembly;
    private static readonly Assembly _applicationAssembly = Assembly.Load(Application);
    private static readonly Assembly _infrastructureAssembly = typeof(PharmacyDbContext).Assembly;
    private static readonly Assembly _webAssembly = typeof(SharedResource).Assembly;

    [Fact]
    public void Domain_DependsOnNoOtherLayerOrFramework()
    {
        AssertNoDependencies(_domainAssembly,
            Application, Infrastructure, Web,
            "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "MudBlazor");
    }

    [Fact]
    public void Application_DoesNotDependOnInfrastructureOrUi()
    {
        AssertNoDependencies(_applicationAssembly,
            Infrastructure, Web, "Microsoft.AspNetCore", "MudBlazor");
    }

    [Fact]
    public void Infrastructure_DoesNotDependOnUi()
    {
        AssertNoDependencies(_infrastructureAssembly, Web, "Microsoft.AspNetCore", "MudBlazor");
    }

    [Fact]
    public void ProjectReferences_FollowLayerDirection()
    {
        // The compiler drops unused references, so each layer may reference a subset of what it is allowed.
        ReferencedPharmacyAssemblies(_domainAssembly).ShouldBeEmpty();
        ReferencedPharmacyAssemblies(_applicationAssembly).ShouldBeSubsetOf([Domain]);
        ReferencedPharmacyAssemblies(_infrastructureAssembly).ShouldBeSubsetOf([Domain, Application]);
        ReferencedPharmacyAssemblies(_webAssembly).ShouldBeSubsetOf([Domain, Application, Infrastructure]);
    }

    private static void AssertNoDependencies(Assembly assembly, params string[] forbidden)
    {
        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbidden)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"{assembly.GetName().Name} has forbidden dependencies in: " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    private static string[] ReferencedPharmacyAssemblies(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith("Pharmacy.", StringComparison.Ordinal))];
}
