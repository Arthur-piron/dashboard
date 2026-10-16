namespace DashboardBackEnd.Widgets;

public class WidgetRegistry
{
    private readonly Dictionary<string, IService> _services;
    private readonly Dictionary<string, IWidget> _widgets;

    public WidgetRegistry(IEnumerable<IService> services, IEnumerable<IWidget> widgets)
    {
        _services = services.ToDictionary(s => s.Key);
        _widgets = widgets.ToDictionary(w => w.Key);

        foreach (var widget in _widgets.Values)
        {
            if (!_services.ContainsKey(widget.ServiceKey))
            {
                throw new InvalidOperationException(
                    $"Widget '{widget.Key}' references unknown service '{widget.ServiceKey}'.");
            }
        }
    }

    public IReadOnlyCollection<IService> Services => _services.Values;
    public IReadOnlyCollection<IWidget> Widgets => _widgets.Values;

    public IService? FindService(string key) => _services.GetValueOrDefault(key);
    public IWidget? FindWidget(string key) => _widgets.GetValueOrDefault(key);

    public IEnumerable<IWidget> WidgetsOf(string serviceKey) =>
        _widgets.Values.Where(w => w.ServiceKey == serviceKey);
}

public static class WidgetCatalogExtensions
{
    public static IServiceCollection AddWidgetCatalog(this IServiceCollection services)
    {
        var types = typeof(WidgetRegistry).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract);

        foreach (var type in types)
        {
            if (typeof(IService).IsAssignableFrom(type)) services.AddSingleton(typeof(IService), type);
            if (typeof(IWidget).IsAssignableFrom(type)) services.AddSingleton(typeof(IWidget), type);
        }

        services.AddSingleton<WidgetRegistry>();
        return services;
    }
}
