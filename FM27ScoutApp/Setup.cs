using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace FM27Scout
{
    /// <summary>
    /// Installation en un clic : trouve FM27 (Steam), installe BepInEx + FM27 Bridge dans le jeu, installe l'app,
    /// crée les raccourcis et l'entrée « Applications installées ». Lancé quand l'exe s'appelle « …Setup… » ou avec --install.
    /// </summary>
    internal static class Setup
    {
        public const string AppName = "FM27 Assistant";
        public static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);
        public static string InstalledExe => Path.Combine(InstallDir, AppName + ".exe");
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\FM27Assistant";

        public static bool WantsSetup(string[] args) =>
            args.Contains("--install") || Path.GetFileName(Environment.ProcessPath ?? "").Contains("Setup", StringComparison.OrdinalIgnoreCase);

        // ------------------------------------------------------------------ trouver FM27

        public static string FindGame()
        {
            var roots = new List<string>();
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (k?.GetValue("SteamPath") is string sp) roots.Add(sp.Replace('/', '\\'));
            }
            catch { }
            roots.Add(@"C:\Program Files (x86)\Steam");
            foreach (var root in roots.Distinct().ToList())
            {
                string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    roots.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            }
            foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
            {
                roots.Add(Path.Combine(d.Name, "SteamLibrary"));
                roots.Add(Path.Combine(d.Name, "Program Files (x86)", "Steam"));
            }
            foreach (var r in roots.Distinct())
            {
                string g = Path.Combine(r, "steamapps", "common", "Football Manager 27");
                if (File.Exists(Path.Combine(g, "fm.exe"))) return g;
            }
            return null;
        }

        // ------------------------------------------------------------------ installer

        public static void Install(string game, bool desktop, Action<string> progress)
        {
            if (Process.GetProcessesByName("fm").Length > 0) throw new Exception("Football Manager 27 est lancé : ferme-le avant d'installer.");

            progress("Installation de BepInEx et de FM27 Bridge dans le jeu…");
            using (var s = typeof(Setup).Assembly.GetManifestResourceStream("bepinex.zip") ?? throw new Exception("contenu d'installation manquant dans l'exe"))
            using (var zip = new ZipArchive(s))
            {
                foreach (var e in zip.Entries)
                {
                    if (string.IsNullOrEmpty(e.Name)) continue;
                    string dest = Path.GetFullPath(Path.Combine(game, e.FullName));
                    if (!dest.StartsWith(Path.GetFullPath(game), StringComparison.OrdinalIgnoreCase)) continue;
                    // on garde la configuration BepInEx existante (on y coupe juste la console plus bas)
                    if (e.FullName.EndsWith("BepInEx.cfg") && File.Exists(dest)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    e.ExtractToFile(dest, true);
                }
            }
            string cfg = Path.Combine(game, "BepInEx", "config", "BepInEx.cfg");
            if (File.Exists(cfg))
            {
                var txt = File.ReadAllText(cfg);
                txt = Regex.Replace(txt, @"(\[Logging\.Console\][\s\S]*?Enabled\s*=\s*)true", "${1}false");
                File.WriteAllText(cfg, txt);
            }

            progress("Installation de l'application…");
            Directory.CreateDirectory(InstallDir);
            string self = Environment.ProcessPath;
            if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase))
                File.Copy(self, InstalledExe, true);

            progress("Raccourcis…");
            string menu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");
            Shortcut(menu, InstalledExe);
            if (desktop) Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"), InstalledExe);

            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", AppName);
                k.SetValue("DisplayIcon", InstalledExe);
                k.SetValue("Publisher", "arress03");
                k.SetValue("DisplayVersion", typeof(Setup).Assembly.GetName().Version?.ToString(3) ?? "");
                k.SetValue("InstallLocation", InstallDir);
                k.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
                k.SetValue("NoModify", 1); k.SetValue("NoRepair", 1);
                k.SetValue("FM27Path", game);
            }
            Settings.Cur.GamePath = game; Settings.Save();
        }

        static void Shortcut(string lnk, string target)
        {
            try
            {
                var t = Type.GetTypeFromProgID("WScript.Shell");
                dynamic sh = Activator.CreateInstance(t);
                dynamic sc = sh.CreateShortcut(lnk);
                sc.TargetPath = target;
                sc.WorkingDirectory = Path.GetDirectoryName(target);
                sc.IconLocation = target + ",0";
                sc.Save();
            }
            catch { }
        }

        // ------------------------------------------------------------------ désinstaller

        public static void Uninstall()
        {
            if (MessageBox.Show("Désinstaller FM27 Assistant ?\n\nFM27 Bridge sera retiré du jeu. BepInEx reste installé mais est désactivé (winhttp.dll retiré). Tes réglages dans %APPDATA%\\FM27Scout sont conservés.",
                AppName, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            string game = null;
            try { using var k = Registry.CurrentUser.OpenSubKey(UninstallKey); game = k?.GetValue("FM27Path") as string; } catch { }
            game ??= FindGame();
            if (game != null)
            {
                TryDelete(Path.Combine(game, "BepInEx", "plugins", "FM27Bridge", "FM27Bridge.dll"));
                TryDelete(Path.Combine(game, "winhttp.dll"));
            }
            TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk"));
            TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"));
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
            // l'exe en cours ne peut pas se supprimer lui-même : on laisse cmd le faire juste après la fermeture
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 2 /nobreak >nul & rmdir /s /q \"{InstallDir}\"") { CreateNoWindow = true, UseShellExecute = false });
            }
            catch { }
            MessageBox.Show("FM27 Assistant a été désinstallé.", AppName);
        }

        static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }

        // ------------------------------------------------------------------ fenêtre d'installation

        public static void ShowWindow()
        {
            var w = new Window
            {
                Title = AppName + " — installation", Width = 560, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = Theme.Bg, Foreground = Theme.Text,
            };
            var root = new StackPanel { Margin = new Thickness(28) };
            root.Children.Add(new TextBlock { Text = AppName, FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text });
            root.Children.Add(new TextBlock { Text = "Installe l'application et le module FM27 Bridge (BepInEx) dans Football Manager 27.", TextWrapping = TextWrapping.Wrap, Foreground = Theme.Dim, Margin = new Thickness(0, 6, 0, 18) });

            root.Children.Add(new TextBlock { Text = "DOSSIER DE FOOTBALL MANAGER 27", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Theme.Faint });
            var pathRow = new DockPanel { Margin = new Thickness(0, 6, 0, 14) };
            var browse = new Button { Content = "Parcourir…", Margin = new Thickness(8, 0, 0, 0) };
            DockPanel.SetDock(browse, Dock.Right); pathRow.Children.Add(browse);
            var path = new TextBox { Text = FindGame() ?? "", VerticalContentAlignment = VerticalAlignment.Center };
            pathRow.Children.Add(path);
            root.Children.Add(pathRow);
            browse.Click += (_, _) =>
            {
                var d = new OpenFolderDialog { Title = "Dossier de Football Manager 27 (celui qui contient fm.exe)" };
                if (d.ShowDialog(w) == true) path.Text = d.FolderName;
            };

            var desk = new CheckBox { Content = "Créer un raccourci sur le bureau", IsChecked = true, Foreground = Theme.Text, Margin = new Thickness(0, 0, 0, 16) };
            root.Children.Add(desk);
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Theme.Dim, Margin = new Thickness(0, 0, 0, 14) };
            root.Children.Add(status);
            var go = new Button { Content = "Installer", HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(22, 8, 22, 8) };
            if (Application.Current.Resources["Primary"] is Style ps) go.Style = ps;
            root.Children.Add(go);
            root.Children.Add(new TextBlock { Text = "dev by @arress03", Foreground = Theme.Faint, Opacity = 0.6, FontSize = 11, Margin = new Thickness(0, 14, 0, 0) });
            w.Content = root;

            bool installed = false;
            go.Click += async (_, _) =>
            {
                if (installed)
                {
                    try { Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true }); } catch { }
                    Application.Current.Shutdown();
                    return;
                }
                string game = path.Text.Trim();
                if (!File.Exists(Path.Combine(game, "fm.exe"))) { status.Text = "Ce dossier ne contient pas fm.exe : choisis le dossier d'installation de Football Manager 27."; status.Foreground = Brushes.Orange; return; }
                go.IsEnabled = false; browse.IsEnabled = false;
                bool desktop = desk.IsChecked == true;
                try
                {
                    await Task.Run(() => Install(game, desktop, m => w.Dispatcher.Invoke(() => status.Text = m)));
                    status.Foreground = Theme.Text;
                    status.Text = "Installation terminée. Au premier lancement de FM27, BepInEx prépare le jeu (1 à 2 minutes de plus que d'habitude), ensuite c'est instantané.";
                    go.Content = "Lancer FM27 Assistant"; go.IsEnabled = true; installed = true;
                }
                catch (Exception ex)
                {
                    status.Text = "Échec : " + ex.Message; status.Foreground = Brushes.Orange;
                    go.IsEnabled = true; browse.IsEnabled = true;
                }
            };
            Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
            Application.Current.MainWindow = w;
            w.Show();
        }
    }
}
