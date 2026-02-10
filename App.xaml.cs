using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using BaselineMode.WPF.Core.Interfaces;
using BaselineMode.WPF.Core.Interfaces.Observation;
using BaselineMode.WPF.Infrastructure.Services;
using BaselineMode.WPF.Infrastructure.Services.Observation;
using BaselineMode.WPF.Presentation.ViewModels;
using BaselineMode.WPF.Presentation.ViewModels.Observation;
using BaselineMode.WPF.Views.Shared;

namespace BaselineMode.WPF;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public IServiceProvider ServiceProvider { get; private set; }

    public App()
    {
        ServiceCollection services = new ServiceCollection();
        ConfigureServices(services);
        ServiceProvider = services.BuildServiceProvider();
    }

    private void ConfigureServices(ServiceCollection services)
    {
        // Core Services
        services.AddSingleton<IMathService, MathService>();
        services.AddSingleton<IFileService, FileService>();
        services.AddSingleton<IObservationDataProcessor, ObservationDataProcessor>();
        services.AddSingleton<IObservationExcelHelper, ObservationExcelHelper>();
        services.AddSingleton<IFileHelper, FileHelper>();  // Shared file helper for Baseline and Observation

        // ViewModels
        services.AddTransient<MainViewModel>();
        services.AddTransient<ObservationMainViewModel>();

        // Views
        services.AddTransient<UnifiedMainWindow>();
        services.AddTransient<BaselineMode.WPF.Views.Observation.ObservationMainWindow>();

        // Register Fitting Service
        services.AddSingleton<IFittingService, HemgFittingService>();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global Exception Handling
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            MessageBox.Show($"Critical Error: {ex?.Message}\n\nStack Trace:\n{ex?.StackTrace}", "Application Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        try
        {
            var mainWindow = ServiceProvider.GetRequiredService<UnifiedMainWindow>();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Startup Error: {ex.Message}\n\nInner Exception: {ex.InnerException?.Message}", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }
}
