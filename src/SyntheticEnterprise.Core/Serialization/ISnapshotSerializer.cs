namespace SyntheticEnterprise.Core.Serialization;

public interface ISnapshotSerializer
{
    void Save<T>(T payload, string path, bool compress);
    T Load<T>(string path);
}
