using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using BaselineMode.WPF.Core.Interfaces;
using BaselineMode.WPF.Core.Interfaces.Observation;
using BaselineMode.WPF.Infrastructure.Services;
using BaselineMode.WPF.Infrastructure.Services.Baseline;
using BaselineMode.WPF.Infrastructure.Services.Observation;
using BaselineMode.WPF.Presentation.ViewModels;
using BaselineMode.WPF.Presentation.ViewModels.Baseline;
using BaselineMode.WPF.Presentation.ViewModels.Observation;
using BaselineMode.WPF.Presentation.ViewModels.Flux;

using BaselineMode.WPF.Presentation.Views.Shared;

namespace BaselineMode.WPF;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public IServiceProvider ServiceProvider { get; private set; }

    public App()
    {
        ServiceCollection services = new();
        ConfigureServices(services);
        ServiceProvider = services.BuildServiceProvider();
    }

    private static void ConfigureServices(ServiceCollection services)
    {
        // Core Services (order: Logger first, then HEMG and Math depend on it)
        services.AddSingleton<ILoggerService, LoggerService>();
        services.AddSingleton<IHemgFittingService, HemgFittingService>();
        services.AddSingleton<IMathService, MathService>();
        services.AddSingleton<IFileService, BaselineFileService>(); // Updated
        services.AddSingleton<IObservationDataProcessor, ObservationDataProcessor>();
        services.AddSingleton<IObservationExcelHelper, ObservationExcelHelper>();
        services.AddSingleton<IFileHelper, FileHelper>();  // Shared file helper for Baseline and Observation

        // ViewModels
        services.AddTransient<MainViewModel>();
        services.AddTransient<ObservationViewModel>();
        services.AddTransient<Presentation.ViewModels.Flux.FluxViewModel>();

        // Views
        services.AddTransient<UnifiedMainWindow>();
        services.AddTransient<BaselineMode.WPF.Presentation.Views.Observation.ObservationMainWindow>();
        services.AddTransient<BaselineMode.WPF.Presentation.Views.Flux.FluxMainWindow>();

        // Register Fitting Service
        services.AddSingleton<IFittingService, MathService>();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global Exception Handling
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            var logger = ServiceProvider.GetRequiredService<ILoggerService>();
            logger.LogException(ex ?? new Exception("Unknown error"), "Global Handled Exception");

            MessageBoxService.Show($"Critical Error: {ex?.Message}\n\nStack Trace:\n{ex?.StackTrace}", "Application Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        try
        {
            var mainWindow = ServiceProvider.GetRequiredService<UnifiedMainWindow>();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            var logger = ServiceProvider.GetService<ILoggerService>();
            logger?.LogException(ex, "Startup Failure");

            MessageBoxService.Show($"Startup Error: {ex.Message}\n\nInner Exception: {ex.InnerException?.Message}", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }
}
