namespace SyntheticEnterprise.Core.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
