namespace SyntheticEnterprise.Core.Serialization;

public interface ISnapshotSerializer
{
    void Save(object payload, string path, bool compress);
    T Load<T>(string path);
}
