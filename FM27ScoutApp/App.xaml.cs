using System.Linq;
using System.Windows;

namespace FM27Scout
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // une erreur inattendue ne doit pas fermer l'app sans explication
            DispatcherUnhandledException += (_, ev) =>
            {
                Log.Warn("Erreur : " + ev.Exception);
                MessageBox.Show(L.F("Erreur inattendue : {0}\n\nDétails dans %APPDATA%\\FM27Scout\\journal.txt", ev.Exception.Message),
                    "FM27 Assistant", MessageBoxButton.OK, MessageBoxImage.Warning);
                ev.Handled = true;
            };
            Theme.Install(Resources);
            L.Init();
            base.OnStartup(e);
            if (e.Args.Contains("--uninstall")) { Setup.Uninstall(); Shutdown(); return; }
            if (Setup.WantsSetup(e.Args)) { Setup.ShowWindow(); return; }
            var w = new MainWindow();
            MainWindow = w;
            w.Show();
        }
    }
}
