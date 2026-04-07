namespace SyntheticEnterprise.Core.Abstractions;

public interface IRandomSource
{
    int Next();
    int Next(int maxValue);
    int Next(int minValue, int maxValue);
    double NextDouble();
}
