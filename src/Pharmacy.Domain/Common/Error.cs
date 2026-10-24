namespace Pharmacy.Domain.Common;

/// <summary>
/// An expected business failure. <see cref="Code"/> doubles as the localization resource key,
/// so the UI can show a translated message without the domain knowing about languages.
/// </summary>
public sealed record Error
{
    public Error(string code, params object[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
        Arguments = arguments;
    }

    public string Code { get; }

    /// <summary>Values substituted into the localized message, in order.</summary>
    public IReadOnlyList<object> Arguments { get; }
}
