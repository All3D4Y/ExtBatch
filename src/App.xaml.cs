using System;
using System.Windows;
using CmOxeProvisioner.Models;
using CmOxeProvisioner.Services;
using CmOxeProvisioner.Views;

namespace CmOxeProvisioner
{
    public partial class App : Application
    {
        private bool _isHandlingDispatcherException = false;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            this.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs args)
            {
                args.Handled = true;
                if (_isHandlingDispatcherException)
                    return;

                _isHandlingDispatcherException = true;
                try
                {
                    MessageBox.Show(
                        "예기치 않은 오류가 발생했습니다:\n" + args.Exception.Message,
                        "ExtBatch",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                finally
                {
                    _isHandlingDispatcherException = false;
                }
            };

            SettingsService settingsService = new SettingsService();
            AppSettings settings = settingsService.Load();
            ThemeService.ApplyTheme(settings.ThemeKey);

            MainWindow main = new MainWindow(settingsService, settings);
            this.MainWindow = main;
            main.Show();
        }
        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                MainWindow mw = this.MainWindow as MainWindow;
                if (mw != null) mw.ShutdownBackend();
            }
            catch
            {
            }
            base.OnExit(e);
        }
    }
}
