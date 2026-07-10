using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WorkRecordAssistant.Models;
using WorkRecordAssistant.Services;
using WorkRecordAssistant.ViewModels;
using WorkRecordAssistant.Views;

namespace WorkRecordAssistant;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show($"程序发生错误：\n{args.Exception.Message}", "WorkRecordAssistant",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
            Shutdown(1);
        };

        try
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            Services = services.BuildServiceProvider();

            var settingsService = Services.GetRequiredService<ISettingsService>();
            await settingsService.LoadAsync();
            ThemeService.ApplyTheme(settingsService.Current.ThemeMode);

            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
                StartMenuShortcutService.EnsureShortcut(exePath);

            var dataService = Services.GetRequiredService<IDataService>();
            await dataService.InitializeAsync();

            var mainWindow = Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
            mainWindow.Activate();
            mainWindow.Topmost = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"启动失败：\n{ex.Message}", "WorkRecordAssistant",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IDataService, SqliteDataService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddTransient<SettingsWindow>();
        services.AddTransient<LongTermArchiveViewModel>();
        services.AddTransient<LongTermArchiveDialog>();
    }
}
