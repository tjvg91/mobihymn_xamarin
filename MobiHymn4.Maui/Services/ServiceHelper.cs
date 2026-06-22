namespace MobiHymn4.Services;

public static class ServiceHelper
{
    private static IServiceProvider _services;

    public static void Initialize(IServiceProvider services) => _services = services;

    public static T Get<T>() where T : class
    {
        if (_services == null)
            throw new InvalidOperationException("ServiceHelper has not been initialized.");

        return _services.GetRequiredService<T>();
    }
}
