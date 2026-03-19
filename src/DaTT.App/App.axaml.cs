using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using DaTT.App.Infrastructure;
using DaTT.App.ViewModels;
using DaTT.App.Views;
using DaTT.Core.Interfaces;
using DaTT.Core.Services;
using DaTT.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DaTT.App;

public partial class App : Application
{
    private IServiceProvider? _services;

    public override void Initialize()
    {
        AppLog.Initialize();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppLog.Error("Unhandled domain exception", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };

        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _services = BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();
            desktop.MainWindow = new MainWindow
            {
                DataContext = _services.GetRequiredService<MainWindowViewModel>()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));

        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DaTT", "connections.json");

        services.AddSingleton<IConnectionConfigService>(sp =>
            new ConnectionConfigService(configPath, sp.GetRequiredService<ILogger<ConnectionConfigService>>()));

        services.AddTransient<MySqlProvider>();
        services.AddTransient<MariaDbProvider>();
        services.AddTransient<PostgreSqlProvider>();
        services.AddTransient<OracleProvider>();
        services.AddTransient<MongoDbProvider>();
        services.AddTransient<HiveProvider>();
        services.AddTransient<RedisProvider>();
        services.AddTransient<ElasticSearchProvider>();

        services.AddKeyedTransient<IDatabaseProvider>("MySQL",    (sp, _) => sp.GetRequiredService<MySqlProvider>());
        services.AddKeyedTransient<IDatabaseProvider>("MariaDB",  (sp, _) => sp.GetRequiredService<MariaDbProvider>());
        services.AddKeyedTransient<IDatabaseProvider>("PostgreSQL",(sp, _) => sp.GetRequiredService<PostgreSqlProvider>());
        services.AddKeyedTransient<IDatabaseProvider>("Oracle",   (sp, _) => sp.GetRequiredService<OracleProvider>());
        services.AddKeyedTransient<IDatabaseProvider>("MongoDB",  (sp, _) => sp.GetRequiredService<MongoDbProvider>());
        services.AddKeyedTransient<IDatabaseProvider>("Hive",     (sp, _) => sp.GetRequiredService<HiveProvider>());
        services.AddKeyedTransient<IDatabaseProvider>("Redis",    (sp, _) => sp.GetRequiredService<RedisProvider>());
        services.AddKeyedTransient<IDatabaseProvider>("ElasticSearch", (sp, _) => sp.GetRequiredService<ElasticSearchProvider>());

        services.AddSingleton<IProviderFactory, ProviderFactory>();

        services.AddTransient<ConnectionManagerViewModel>();
        services.AddTransient<ObjectExplorerViewModel>();
        services.AddTransient<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }

    private static void DisableAvaloniaDataAnnotationValidation()
    {
        var toRemove = BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();
        foreach (var plugin in toRemove)
            BindingPlugins.DataValidators.Remove(plugin);
    }
}
