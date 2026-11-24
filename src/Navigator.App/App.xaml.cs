using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Navigator.App.Services;
using Navigator.App.Theming;
using Navigator.Core.Services;
using Navigator.Infrastructure;
using Navigator.Presentation.ViewModels;
using Navigator.Shell;
using Navigator.Core.Localization;

namespace Navigator.App;

public partial class App : Application
{
    private ServiceProvider? _services;
    private string _logPath = Path.Combine(Path.GetTempPath(), "Navigator-errors.log");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandledException;
        ExplorerTheme.Apply(this);
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        var services = new ServiceCollection();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<IFileSystemService, FileSystemService>();
        services.AddSingleton<IFolderWatcher, FolderWatcher>();
        services.AddSingleton<IGitStatusService, GitStatusService>();
        services.AddSingleton<IFolderSizeService, FolderSizeService>();
        services.AddSingleton<IKnownFolders, KnownFolders>();
        services.AddSingleton<IShellService, ShellService>();
        services.AddSingleton<IFileOperations, ShellFileOperations>();
        services.AddSingleton<IClipboardService, WpfClipboardService>();
        services.AddSingleton<IDialogService, WpfDialogService>();
        services.AddSingleton<IUiDispatcher, WpfDispatcher>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        _services = services.BuildServiceProvider();

        try
        {
            var store = _services.GetRequiredService<ISettingsStore>();
            _logPath = Path.Combine(store.DataDirectory, "errors.log");

            // La lingua va scelta prima di creare le finestre: i testi vengono letti alla loro creazione.
            ApplyLanguage(store.LoadSettings().Language);

            var viewModel = _services.GetRequiredService<MainViewModel>();
            var window = _services.GetRequiredService<MainWindow>();
            MainWindow = window;

            // Le cartelle passate da riga di comando si aprono in schede (es. Navigator.exe "D:\Progetti").
            await viewModel.InitializeAsync(e.Args);
            window.ApplySettings();
            window.Show();
        }
        catch (Exception ex)
        {
            Log(ex);
            MessageBox.Show(Strings.Format(Strings.StartupFailed, ex.Message), "Navigator", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    /// <summary>
    /// Lingua dei testi: quella scelta nel menu, altrimenti quella di Windows se disponibile, altrimenti l'inglese.
    /// Date e numeri restano nel formato regionale di Windows.
    /// </summary>
    private static void ApplyLanguage(string? code)
    {
        CultureInfo? culture = null;
        if (!string.IsNullOrWhiteSpace(code))
        {
            try
            {
                culture = CultureInfo.GetCultureInfo(code);
            }
            catch (CultureNotFoundException)
            {
            }
        }

        culture ??= Strings.Languages.Contains(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
            ? CultureInfo.CurrentUICulture
            : CultureInfo.GetCultureInfo("en");

        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        Strings.Culture = culture;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _services?.Dispose();
        base.OnExit(e);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // Cambio tema chiaro/scuro o colore d'accento in Impostazioni di Windows.
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
        {
            Dispatcher.InvokeAsync(() => ExplorerTheme.Apply(this));
        }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception);
        MessageBox.Show(
            Strings.Format(Strings.UnexpectedError, e.Exception.Message, _logPath),
            "Navigator",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }

    private void Log(Exception ex)
    {
        try
        {
            File.AppendAllText(_logPath, $"[{DateTime.Now:O}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
