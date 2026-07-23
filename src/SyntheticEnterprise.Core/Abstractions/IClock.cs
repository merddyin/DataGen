namespace SyntheticEnterprise.Core.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IGenerationClock : IClock
{
    IDisposable Use(DateTimeOffset instant);
}
