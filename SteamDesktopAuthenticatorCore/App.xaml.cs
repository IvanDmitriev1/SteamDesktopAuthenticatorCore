using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SteamAuthCore;
using SteamAuthCore.Manifest;
using SteamAuthenticatorCore.Desktop.Services;
using SteamAuthenticatorCore.Desktop.ViewModels;
using SteamAuthenticatorCore.Desktop.Views.Pages;
using SteamAuthenticatorCore.Shared;
using WPFUI.Appearance;
using WPFUI.Common;
using WPFUI.DIControls;
using WPFUI.DIControls.Interfaces;

namespace SteamAuthenticatorCore.Desktop;

public sealed partial class App : Application
{
    public App()
    {
        this.Dispatcher.UnhandledException += DispatcherOnUnhandledException;

        _host = Host.CreateDefaultBuilder().ConfigureServices((context, collection) =>
        {
            ConfigureOptions(collection);
            ConfigureServices(collection);
        }).Build();
    }

    public delegate IManifestModelService ManifestServiceResolver();

    public const string InternalName = "SteamDesktopAuthenticatorCore";
    public const string Name = "Steam desktop authenticator core";

    private readonly IHost _host;

    #region Overrides

    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host.StartAsync();

        var services = _host.Services;

        var confirmationService = services.GetRequiredService<BaseConfirmationService>();

        var settings = services.GetRequiredService<AppSettings>();
        settings.LoadSettings();
        settings.PropertyChanged += SettingsOnPropertyChanged;

        var platformImplementations = services.GetRequiredService<IPlatformImplementations>();
        platformImplementations.SetTheme(settings.AppTheme);

        await OnStartupTask(_host.Services);

        var mainWindow = _host.Services.GetRequiredService<Views.Container>();
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        var settings = _host.Services.GetRequiredService<AppSettings>();
        settings.PropertyChanged += SettingsOnPropertyChanged;

        await _host.StopAsync();
        _host.Dispose();
    }

    #endregion

    #region PrivateMethods

    private static void ConfigureOptions(IServiceCollection service)
    {
        service.Configure<UpdateServiceOptions>(options =>
        {
            options.GitHubUrl = "https://api.github.com/repos/bduj1/StreamDesktopAuthenticatorCore/releases/latest";
        });

        service.Configure<DefaultNavigationConfiguration>(configuration =>
        {
            configuration.StartupPageTag = nameof(TokenPage);

            configuration.VisibleItems = new INavigationItem[]
            {
                new DefaultNavigationItem(typeof(TokenPage), nameof(TokenPage), "Token"),
                new DefaultNavigationItem(typeof(ConfirmationsPage), nameof(ConfirmationsPage), "Confirmations")
            };

            configuration.VisibleFooterItems = new INavigationItem[]
            {
                new DefaultNavigationItem(typeof(SettingsPage),nameof(SettingsPage), "Settings", SymbolRegular.Settings24)
            };

            configuration.HiddenItemsItems = new INavigationItem[]
            {
                new DefaultNavigationItem(typeof(LoginPage), nameof(LoginPage), "login")
            };
        });

        service.Configure<DialogConfiguration>(configuration =>
        {
            configuration.Title = App.Name;
        });

        service.Configure<SnackbarConfiguration>(configuration =>
        {
            configuration.Title = App.Name;
            configuration.Timeout = 5000;
        });
    }

    private static void ConfigureServices(IServiceCollection service)
    {
        service.AddSingleton<Views.Container>();
        service.AddScoped<IDialog, Dialog>();
        service.AddScoped<ISnackbar, Snackbar>();
        service.AddScoped<INavigation, DefaultNavigation>();

        service.AddSingleton<TokenViewModel>();
        service.AddSingleton<ConfirmationViewModel>();
        service.AddTransient<SettingsViewModel>();
        service.AddTransient<LoginViewModel>();

        service.AddTransient<TokenPage>();
        service.AddTransient<SettingsPage>();
        service.AddTransient<LoginPage>();
        service.AddTransient<ConfirmationsPage>();

        service.AddScoped<IPlatformImplementations, DesktopImplementations>();
        service.AddScoped<BaseConfirmationService, DesktopConfirmationService>();
        service.AddTransient<IPlatformTimer, DesktopTimer>();
            
        service.AddScoped<TokenService>();
        service.AddScoped<LoginService>();

        service.AddSingleton<UpdateService>();
        service.AddGoogleDriveApi(Name);
        service.AddScoped<AppSettings>();
        service.AddScoped<ISettingsService, DesktopSettingsService>();

        service.AddScoped<GoogleDriveManifestModelService>();
        service.AddScoped<IManifestDirectoryService, DesktopManifestDirectoryService>();
        service.AddScoped<LocalDriveManifestModelService>();

        service.AddSingleton<ObservableCollection<SteamGuardAccount>>();

        service.AddScoped<ManifestServiceResolver>(provider => () =>
        {
            var appSettings = provider.GetRequiredService<AppSettings>();
            return appSettings.ManifestLocation switch
            {
                AppSettings.ManifestLocationModel.LocalDrive => provider
                    .GetRequiredService<LocalDriveManifestModelService>(),
                AppSettings.ManifestLocationModel.GoogleDrive => provider
                    .GetRequiredService<GoogleDriveManifestModelService>(),
                _ => throw new ArgumentOutOfRangeException()
            };
        });
    }

    private static void DispatcherOnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Process unhandled exception

        MessageBox.Show( $"{e.Exception.Message}\n\n{e.Exception.StackTrace}", "Exception occurred", MessageBoxButton.OK, MessageBoxImage.Error);

        // Prevent default unhandled exception processing
        e.Handled = true;

        Application.Current.Shutdown();
    }

    private static async Task OnStartupTask(IServiceProvider services)
    {
        var appSettings = services.GetRequiredService<AppSettings>();
        if (appSettings.Updated)
        {
            var updateService = services.GetRequiredService<UpdateService>();
            await updateService.DeletePreviousFile(InternalName);
            appSettings.Updated = false;
        }
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var settings = (sender as AppSettings)!;

        settings.SettingsService.SaveSetting(e.PropertyName!, settings);

        if (e.PropertyName != nameof(settings.AppTheme))
            return;

        var platformImplementations = _host.Services.GetRequiredService<IPlatformImplementations>();
        Application.Current.Dispatcher.Invoke(() =>
        {
            platformImplementations.SetTheme(settings.AppTheme);
        });
    }


    #endregion
}