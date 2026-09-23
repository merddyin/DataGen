namespace SyntheticEnterprise.Contracts.Scenarios;

/// <summary>
/// Declares the DataGen version that introduced a scenario option, so the option can later be
/// retired through <see cref="ScenarioOptionRetirement"/> as a data change rather than new code.
/// </summary>
/// <param name="Path">The option's <c>$.</c>-style JSON path within a scenario document.</param>
/// <param name="IntroducedInVersion">The DataGen version that first read the option.</param>
/// <param name="Summary">What the option controls, in one line.</param>
public sealed record ScenarioOptionIntroduction(
    string Path,
    string IntroducedInVersion,
    string Summary);

/// <summary>
/// Declares a scenario option that DataGen no longer reads. A scenario document that still sets
/// the option would otherwise deserialize cleanly and lose the setting without any report.
/// </summary>
/// <param name="Path">The option's <c>$.</c>-style JSON path within a scenario document.</param>
/// <param name="RetiredInVersion">The DataGen version that stopped reading the option.</param>
/// <param name="Replacement">
/// What an author should use instead, or <see langword="null"/> when nothing replaces the option.
/// </param>
public sealed record ScenarioOptionRetirement(
    string Path,
    string RetiredInVersion,
    string? Replacement)
{
    /// <summary>
    /// Builds the author-facing text, which always names the option, the version that retired it,
    /// and the replacement, so the text is identical wherever the retirement is reported.
    /// </summary>
    public string Describe()
    {
        var replacement = Replacement is null
            ? "Nothing replaces it."
            : $"It is replaced by {Replacement}.";

        return $"Scenario option '{Path}' was retired in DataGen {RetiredInVersion} and is no longer read. "
            + $"{replacement} Remove the retired option from the scenario.";
    }
}

/// <summary>
/// The registry of scenario option versions. Introducing or retiring an option is a change to the
/// tables below and to nothing else.
/// </summary>
public sealed class ScenarioOptionLifecycleRegistry
{
    private static readonly ScenarioOptionIntroduction[] DefaultIntroductions =
    {
        new(
            "$.infrastructure.effectiveSecurityConfigurationEndpointCount",
            "0.13.0",
            "Number of Windows endpoints that receive an effective local security policy object."),
        new(
            "$.identity.accountOwnershipConditionCount",
            "0.13.0",
            "Number of times each documented directory account ownership condition is emitted per company.")
    };

    private static readonly ScenarioOptionRetirement[] DefaultRetirements =
    {
        new(
            "$.identity.legacyDirectoryIdentifierVariantCount",
            "0.13.0",
            "the explicit versioned directory account cases added to the identity profile in 0.13.0, "
                + "which describe real directory conditions instead of copying one person's employee "
                + "identifier onto another person's account")
    };

    private readonly Dictionary<string, ScenarioOptionIntroduction> _introductionsByPath;
    private readonly Dictionary<string, ScenarioOptionRetirement> _retirementsByPath;

    public ScenarioOptionLifecycleRegistry(
        IEnumerable<ScenarioOptionIntroduction> introductions,
        IEnumerable<ScenarioOptionRetirement> retirements)
    {
        Introductions = introductions.ToList();
        Retirements = retirements.ToList();

        // Scenario deserialization is case-insensitive, so option matching must be as well.
        _introductionsByPath = Introductions.ToDictionary(
            introduction => introduction.Path,
            StringComparer.OrdinalIgnoreCase);
        _retirementsByPath = Retirements.ToDictionary(
            retirement => retirement.Path,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The registry DataGen itself uses.</summary>
    public static ScenarioOptionLifecycleRegistry Default { get; } =
        new(DefaultIntroductions, DefaultRetirements);

    /// <summary>Options that declare the version which introduced them.</summary>
    public IReadOnlyList<ScenarioOptionIntroduction> Introductions { get; }

    /// <summary>Options DataGen no longer reads.</summary>
    public IReadOnlyList<ScenarioOptionRetirement> Retirements { get; }

    public bool TryGetIntroduction(string path, out ScenarioOptionIntroduction introduction)
        => _introductionsByPath.TryGetValue(path, out introduction!);

    public bool TryGetRetirement(string path, out ScenarioOptionRetirement retirement)
        => _retirementsByPath.TryGetValue(path, out retirement!);
}
