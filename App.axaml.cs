using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using BaselineMode.WPF.Core.Interfaces;
using BaselineMode.WPF.Core.Interfaces.Shared;
using BaselineMode.WPF.Core.Interfaces.Observation;
using BaselineMode.WPF.Infrastructure.Services;
using BaselineMode.WPF.Infrastructure.Services.Baseline;
using BaselineMode.WPF.Infrastructure.Services.Observation;
using BaselineMode.WPF.Presentation.ViewModels.Baseline;
using BaselineMode.WPF.Presentation.ViewModels.Observation;
using BaselineMode.WPF.Presentation.ViewModels.Flux;
using BaselineMode.WPF.Views.Shared;

namespace BaselineMode.WPF;

public partial class App : Application
{
    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Build DI container
        var services = new ServiceCollection();
        ConfigureServices(services);
        ServiceProvider = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                desktop.MainWindow = ServiceProvider.GetRequiredService<UnifiedMainWindow>();
            }
            catch (Exception ex)
            {
                var logger = ServiceProvider.GetService<ILoggerService>();
                logger?.LogException(ex, "Startup Failure");
                throw;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(ServiceCollection services)
    {
        // Core Services (order: Logger first, then HEMG and Math depend on it)
        services.AddSingleton<ILoggerService, LoggerService>();
        services.AddSingleton<IHemgFittingService, HemgFittingService>();
        services.AddSingleton<IMathService, MathService>();
        services.AddSingleton<IFileService, BaselineFileService>();
        services.AddSingleton<IObservationDataProcessor, ObservationDataProcessor>();
        services.AddSingleton<IObservationExcelHelper, ObservationExcelHelper>();
        services.AddSingleton<IFileHelper, FileHelper>();
        services.AddSingleton<IDialogService, DialogService>();

        // ViewModels
        services.AddTransient<MainViewModel>();
        services.AddTransient<ObservationViewModel>();
        services.AddTransient<FluxViewModel>();

        // Views
        services.AddTransient<UnifiedMainWindow>();

        // Register Fitting Service
        services.AddSingleton<IFittingService, MathService>();
    }
}
