namespace SyntheticEnterprise.Core.Plugins;

using System.Security.Cryptography;
using System.Text;
using SyntheticEnterprise.Contracts.Plugins;

public interface IGenerationPluginInstallationService
{
    string ManagedRootPath { get; }
    GenerationPluginInstallationResult Install(IEnumerable<string> rootPaths, bool allowAssemblyPlugins);
}

public sealed class GenerationPluginInstallationService : IGenerationPluginInstallationService
{
    private readonly IGenerationPluginPackageValidator _validator;
    private readonly IGenerationPluginRegistrationService _registrationService;

    public GenerationPluginInstallationService(
        IGenerationPluginPackageValidator validator,
        IGenerationPluginRegistrationService registrationService)
    {
        _validator = validator;
        _registrationService = registrationService;
        var overridePath = Environment.GetEnvironmentVariable("SYNTHETIC_ENTERPRISE_MANAGED_PLUGIN_ROOT");
        ManagedRootPath = string.IsNullOrWhiteSpace(overridePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SyntheticEnterprise", "plugins")
            : Path.GetFullPath(overridePath);
    }

    public string ManagedRootPath { get; }

    public GenerationPluginInstallationResult Install(IEnumerable<string> rootPaths, bool allowAssemblyPlugins)
    {
        var normalizedRoots = rootPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var validationSettings = new ExternalPluginExecutionSettings
        {
            Enabled = true,
            PluginRootPaths = normalizedRoots,
            AllowAssemblyPlugins = allowAssemblyPlugins,
            RequireAssemblyHashApproval = false
        };
        var reports = _validator.Validate(normalizedRoots, validationSettings);
        var messages = reports.SelectMany(report => report.Messages).ToList();
        var installable = reports.Where(report => !report.HasErrors && report.PluginCount > 0).ToList();
        var installedPaths = new List<string>();

        Directory.CreateDirectory(ManagedRootPath);

        foreach (var report in installable)
        {
            var destinationPath = Path.Combine(ManagedRootPath, BuildInstallFolderName(report));
            EnsureManagedPath(destinationPath);

            if (Directory.Exists(destinationPath))
            {
                Directory.Delete(destinationPath, recursive: true);
            }

            CopyDirectory(report.RootPath, destinationPath);
            installedPaths.Add(destinationPath);
        }

        var registration = _registrationService.Register(installedPaths, allowAssemblyPlugins);
        messages.AddRange(registration.Messages);

        return new GenerationPluginInstallationResult
        {
            ManagedRootPath = ManagedRootPath,
            InstalledPaths = installedPaths,
            Registered = registration.Registered,
            Messages = messages
        };
    }

    private string BuildInstallFolderName(GenerationPluginPackageValidationReport report)
    {
        var rootName = Path.GetFileName(report.RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(rootName))
        {
            rootName = "plugin-package";
        }

        var combinedMaterial = string.Join("|", report.Plugins
            .Where(plugin => !string.IsNullOrWhiteSpace(plugin.ContentHash))
            .OrderBy(plugin => plugin.Capability, StringComparer.OrdinalIgnoreCase)
            .ThenBy(plugin => plugin.ContentHash, StringComparer.OrdinalIgnoreCase)
            .Select(plugin => $"{plugin.Capability}:{plugin.ContentHash}"));
        var suffix = ComputeHash(combinedMaterial).Substring(0, 12);

        return $"{SanitizePathSegment(rootName)}-{suffix}";
    }

    private void EnsureManagedPath(string destinationPath)
    {
        var fullManagedRoot = Path.GetFullPath(ManagedRootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullDestination = Path.GetFullPath(destinationPath);
        if (!fullDestination.StartsWith(fullManagedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fullDestination, fullManagedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Managed plugin install path '{fullDestination}' is outside '{fullManagedRoot}'.");
        }
    }

    private static void CopyDirectory(string sourceRoot, string destinationRoot)
    {
        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, directory);
            Directory.CreateDirectory(Path.Combine(destinationRoot, relative));
        }

        Directory.CreateDirectory(destinationRoot);
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, file);
            var destination = Path.Combine(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static string SanitizePathSegment(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(invalidChars.Contains(character) ? '-' : character);
        }

        return builder.ToString();
    }

    private static string ComputeHash(string value)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }
}
