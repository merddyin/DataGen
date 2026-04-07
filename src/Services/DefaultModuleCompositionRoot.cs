namespace SyntheticEnterprise.Module.Services;

public sealed class DefaultModuleCompositionRoot : IModuleCompositionRoot
{
    private readonly Dictionary<Type, object> _services = new();

    public DefaultModuleCompositionRoot Register<T>(T instance) where T : class
    {
        _services[typeof(T)] = instance;
        return this;
    }

    public T Resolve<T>() where T : class
    {
        if (_services.TryGetValue(typeof(T), out var service) && service is T typed)
        {
            return typed;
        }

        throw new InvalidOperationException($"Service not registered: {typeof(T).FullName}");
    }

    public CmdletServiceRegistry BuildRegistry()
    {
        return new CmdletServiceRegistry();
    }
}
