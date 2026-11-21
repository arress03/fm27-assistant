using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FM27Scout
{
    /// <summary>Affiche un montant en £ (k£ / M£).</summary>
    public sealed class MoneyConv : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c) => v is long l ? MainWindow.Money(l) : "—";
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
    }

    /// <summary>Couleur d'une stat 1..20 comme dans FM (inversée pour fragilité, vice, controverse).</summary>
    public sealed class StatBrushConv : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c) => v is int i ? MainWindow.ValueBrush(p as string ?? "", i) : MainWindow.BDim;
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
    }

    public partial class MainWindow : Window
    {
        GameProcess _g;
        bool _reading;
        DateTime _lastRead = DateTime.MinValue;
        readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
        PlayerRow _current, _cmpA, _cmpB;
        Task _gfxTask;
        DateTime? _retryAt;

        // couleurs du thème (suivent le club) + couleurs d'état fixes
        internal static Brush BText => Theme.Text;
        internal static Brush BDim => Theme.Dim;
        internal static Brush BAccent => Theme.Accent;
        internal static Brush BPanel => Theme.Surface;
        internal static Brush BPanel2 => Theme.Surface2;
        internal static Brush BLine => Theme.Line;
        internal static Brush BFaint => Theme.Faint;
        internal static readonly Brush BGood = Br("#3DD68C"), BOk = Br("#F5C451"), BMid = Br("#F59A4A"), BBad = Br("#F2555A");

        static Brush Br(string hex) { var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex); b.Freeze(); return b; }

        static readonly MoneyConv MoneyC = new();
        static readonly StatBrushConv StatC = new();

        public MainWindow()
        {
            InitializeComponent();
            L.Apply(this);
            RestoreWindow();
            Closed += (_, _) => { _timer.Stop(); if (!_reading) _g?.Dispose(); };
            SourceInitialized += (_, _) => DarkTitleBar();
            if (DateTime.TryParse(Settings.Cur.ManualDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var md)) Game.SetDate(md, true);
            _gfxTask = Task.Run(Gfx.Load);
            BuildColToggles();
            BuildGenderToggles();
            BuildLangToggles();
            BuildSearchList();
            BuildRadarFilters();
            ShowPage(NavClub);
            ShowPlayer(null);
            UpdateDateUi();
            if (Game.Rows.Count > 0) RefreshAll();   // écran recréé après un changement de langue : la base est déjà lue
            _timer.Tick += (_, _) => Tick();
            _timer.Start();
            Tick();
        }

        // ============================================================ position de la fenêtre

        void RestoreWindow()
        {
            var st = Settings.Cur;
            if (!double.IsNaN(st.WinLeft) && !double.IsNaN(st.WinTop))
            {
                // seulement si la position tombe sur un écran existant
                double vl = SystemParameters.VirtualScreenLeft, vt = SystemParameters.VirtualScreenTop;
                double vw = SystemParameters.VirtualScreenWidth, vh = SystemParameters.VirtualScreenHeight;
                if (st.WinLeft + 100 > vl && st.WinLeft < vl + vw - 100 && st.WinTop > vt - 20 && st.WinTop < vt + vh - 100)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = st.WinLeft; Top = st.WinTop;
                    if (!double.IsNaN(st.WinWidth)) Width = st.WinWidth;
                    if (!double.IsNaN(st.WinHeight)) Height = st.WinHeight;
                }
            }
            if (st.WinMax) Loaded += (_, _) => WindowState = WindowState.Maximized;
            // sauvegarde dès qu'on la déplace (la fenêtre peut être fermée de force par la compilation automatique)
            var saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            saveTimer.Tick += (_, _) =>
            {
                saveTimer.Stop();
                if (WindowState != WindowState.Normal) { st.WinMax = WindowState == WindowState.Maximized; Settings.Save(); return; }
                st.WinLeft = Left; st.WinTop = Top; st.WinWidth = Width; st.WinHeight = Height; st.WinMax = false;
                Settings.Save();
            };
            LocationChanged += (_, _) => { saveTimer.Stop(); saveTimer.Start(); };
            SizeChanged += (_, _) => { saveTimer.Stop(); saveTimer.Start(); };
            StateChanged += (_, _) => { saveTimer.Stop(); saveTimer.Start(); };
            Closing += (_, _) =>
            {
                var b = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
                st.WinLeft = b.Left; st.WinTop = b.Top; st.WinWidth = b.Width; st.WinHeight = b.Height;
                st.WinMax = WindowState == WindowState.Maximized;
                Settings.Save();
            };
        }

        // ============================================================ connexion + lecture

        void Tick()
        {
            if (_g == null || !_g.Alive)
            {
                _g?.Dispose(); _g = GameProcess.Find(out string why);
                if (_g == null) { SetStatus(false, why); return; }
            }
            if (_reading) return;
            if (Game.Rows.Count == 0 || (_retryAt != null && DateTime.Now > _retryAt)) { _retryAt = null; StartRead(); return; }
            if (AutoRefresh.IsChecked == true && DateTime.Now - _lastRead > TimeSpan.FromMinutes(5)) StartRead();
            DetectClub();
            SetStatus(true, L.F("Connecté à FM27 · {0:N0} joueurs · lus à {1:HH:mm}", Game.Rows.Count, Game.ReadAt));
        }

        void SetStatus(bool ok, string text) { StatusDot.Fill = ok ? BGood : BBad; StatusText.Text = text; }

        void ReadBtn_Click(object s, RoutedEventArgs e) { if (_g != null && !_reading) StartRead(); }

        async void StartRead()
        {
            _reading = true; ReadBtn.IsEnabled = false;
            Progress.Visibility = Visibility.Visible; Progress.Value = 0;
            var g = _g;
            ulong hint = 0;
            try { hint = g.PluginBase + System.Convert.ToUInt64(Settings.Cur.VTableRva, 16); } catch { }
            List<PlayerRow> rows = null;
            try
            {
                rows = await Task.Run(() => Game.ReadAll(g, hint,
                    (p, txt) => Dispatcher.InvokeAsync(() => { Progress.Value = p; SetStatus(true, txt); }), CancellationToken.None));
            }
            catch (Exception ex) { Log.Warn("Lecture : " + ex); Game.LastError = ex.Message; }
            _reading = false; ReadBtn.IsEnabled = true; Progress.Visibility = Visibility.Collapsed;
            _lastRead = DateTime.Now;

            if (rows == null || rows.Count == 0) { SetStatus(false, Game.LastError == "" ? L.T("Aucun joueur lu.") : Game.LastError); return; }
            Game.Rows = rows; Game.Proc = g;
            Game.ById = rows.GroupBy(r => r.Id).ToDictionary(gr => gr.Key, gr => gr.First());
            Settings.Cur.VTableRva = (Game.PlayerVTable - g.PluginBase).ToString("X");
            Settings.Cur.PluginVersion = g.PluginVersion;
            Settings.Save();
            SetStatus(true, L.T("Photos et logos…"));
            try { await _gfxTask; await Task.Run(() => { Gfx.Calibrate(g, rows, Game.Diagnostic); Gfx.UseSavedIds(g, rows); }); }
            catch (Exception ex) { Log.Warn("Graphismes : " + ex.Message); }
            Features.SaveHistory(rows);
            var probeSquad = rows.Where(r => r.Club == Settings.Cur.MyClub && Settings.Cur.MyClub != "").ToList();
            try { await Task.Run(() => Probe.Dump(g, probeSquad)); } catch (Exception ex) { Log.Warn("Sonde : " + ex.Message); }
            Game.Diagnostic.Add("graphismes : " + Gfx.Info + $" · joueurs avec photo {rows.Count(r => r.Uid > 0 && Gfx.Faces.ContainsKey(r.Uid))} · clubs avec logo {Gfx.ClubUid.Values.Count(u => Gfx.Logos.ContainsKey(u))}");
            Log.Info($"{rows.Count} joueurs lus, date {Game.FmDate(Game.GameYear, Game.GameDoy)}. " + string.Join(" | ", Game.Diagnostic.Take(3)));
            WriteDiagnostic(g);
            SetStatus(true, L.F("Connecté à FM27 · {0:N0} joueurs · lus à {1:HH:mm}", rows.Count, Game.ReadAt) + (Game.LastError != "" ? " · " + Game.LastError : ""));
            // lecture faite pendant le chargement de la partie : les noms ne sont pas encore là, on relit vite
            int named = rows.Count(r => !r.Name.StartsWith("Joueur #"));
            if (named < rows.Count / 2)
            {
                _retryAt = DateTime.Now.AddSeconds(20);
                SetStatus(true, L.F("Partie en cours de chargement ({0:N0} noms sur {1:N0}) · nouvelle lecture dans 20 s", named, rows.Count));
            }

            PlayerRow Re(PlayerRow r) => r == null ? null : Game.ById.GetValueOrDefault(r.Id, r);
            _current = Re(_current); _cmpA = Re(_cmpA); _cmpB = Re(_cmpB);

            UpdateDateUi();
            RefreshAll();
            var (_, fresh) = Db.CheckRadar(false);
            RadarBadge.Visibility = fresh.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            RadarBadgeText.Text = fresh.Count > 0 ? L.F("Radar : {0} nouveau(x) joueur(s), dont {1} (PA {2})", fresh.Count, fresh[0].Name, fresh[0].PA) : "";
        }

        void RefreshAll()
        {
            ShowClub();
            if (_current != null) ShowPlayer(_current);
            RunSelectedSearch();
            BuildRadarFilters();
            RunRadar();
            BuildClubList();
            ShowAlerts();
            FillCmpList(CmpListA, CmpSearchA, CmpInfoA);
            FillCmpList(CmpListB, CmpSearchB, CmpInfoB);
            ShowCompare();
            ShowShort();
            ShowDeals();
            ShowTactic();
            ShowDressing();
            ShowTraining();
            UpdateSide();
        }

        /// <summary>Fichier lu par le développeur pour vérifier la lecture mémoire (noms, contrats, équipes).</summary>
        void WriteDiagnostic(GameProcess g)
        {
            try
            {
                var lines = new List<string>(Game.Diagnostic);
                var named = Game.Rows.Count(r => !r.Name.StartsWith("Joueur #"));
                lines.Add($"lignes {Game.Rows.Count} · avec nom {named} · avec club {Game.Rows.Count(r => r.Club != "")} · date {Game.FmDate(Game.GameYear, Game.GameDoy)}");

                // équipes d'un club (le tien, sinon celui du meilleur joueur) : pour repérer nom et type d'équipe
                string club = Settings.Cur.MyClub != "" ? Settings.Cur.MyClub : Game.Rows.Where(r => r.Club != "").OrderByDescending(r => r.CA).Select(r => r.Club).FirstOrDefault();
                if (club != null)
                {
                    lines.Add($"--- équipes de {club}");
                    var buf = new byte[0x200];
                    foreach (var tg in Game.Rows.Where(r => r.Club == club && r.Team != 0).GroupBy(r => r.Team))
                    {
                        var ages = tg.Select(Game.Age).Where(a => a > 0).ToList();
                        lines.Add($"équipe 0x{tg.Key:X} : {tg.Count()} joueurs, âge moyen {(ages.Count > 0 ? ages.Average() : 0):F1}, CA moyen {tg.Average(r => r.CA):F0}, ex. {string.Join(", ", tg.OrderByDescending(r => r.CA).Take(3).Select(r => r.Name))}");
                        if (!g.Read(tg.Key, buf, buf.Length)) continue;
                        for (int off = 0; off < buf.Length; off += 8)
                        {
                            ulong p = BitConverter.ToUInt64(buf, off);
                            if (p < 0x10000 || p > 0x7FFF_FFFF_FFFF) continue;
                            string s1 = g.CStr(p + 4), s2 = null;
                            ulong q = g.Ptr(p); if (q > 0x10000 && q < 0x7FFF_FFFF_FFFF) s2 = g.CStr(q + 4);
                            if (Plausible(s1)) lines.Add($"   +0x{off:X} texte « {s1} »");
                            if (Plausible(s2)) lines.Add($"   +0x{off:X} texte imbriqué « {s2} »");
                        }
                        lines.Add("   octets : " + BitConverter.ToString(buf, 0, 0x100).Replace("-", " "));
                    }
                }
                var myClub = Game.Rows.FirstOrDefault(r => r.Club == Settings.Cur.MyClub && r.ClubPtr != 0);
                if (myClub != null)
                {
                    var cb = new byte[0x1000];
                    if (g.Read(myClub.ClubPtr, cb, cb.Length))
                    {
                        lines.Add($"--- objet club {Settings.Cur.MyClub} @0x{myClub.ClubPtr:X} (uid {Gfx.ClubUid.GetValueOrDefault(myClub.ClubPtr)})");
                        for (int off = 0; off < cb.Length; off += 32)
                            lines.Add($"+0x{off:X3} " + BitConverter.ToString(cb, off, 32).Replace("-", " "));
                        for (int off = 0; off + 8 <= cb.Length; off += 8)
                        {
                            ulong p = BitConverter.ToUInt64(cb, off);
                            if (p < 0x10000 || p > 0x7FFF_FFFF_FFFF) continue;
                            string s1 = g.CStr(p + 4); ulong q = g.Ptr(p); string s2 = q > 0x10000 && q < 0x7FFF_FFFF_FFFF ? g.CStr(q + 4) : null;
                            if (Plausible(s1)) lines.Add($"   +0x{off:X} texte « {s1} »");
                            if (Plausible(s2)) lines.Add($"   +0x{off:X} texte imbriqué « {s2} »");
                        }
                    }
                }
                File.WriteAllLines(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FM27Scout", "diagnostic.txt"), lines);
            }
            catch (Exception e) { Log.Warn("Diagnostic : " + e.Message); }
        }

        static bool Plausible(string s) => !string.IsNullOrEmpty(s) && s.Length >= 2 && s.Count(char.IsLetter) >= s.Length * 0.6;

        // ============================================================ date de jeu

        void UpdateDateUi()
        {
            DateBox.Text = Game.HasDate ? Game.Date.ToString("dd/MM/yyyy") : "";
            DateLabel.Text = L.T(!Game.HasDate ? "Date de jeu (inconnue)" : Game.DateIsManual ? "Date de jeu (manuelle)" : "Date de jeu (estimée)");
        }

        void DateOk_Click(object s, RoutedEventArgs e)
        {
            if (!DateTime.TryParseExact(DateBox.Text.Trim(), new[] { "d/M/yyyy", "dd/MM/yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            { MessageBox.Show(L.T("Format attendu : jj/mm/aaaa"), "FM27 Assistant"); return; }
            Game.SetDate(d, true);
            Settings.Cur.ManualDate = d.ToString("yyyy-MM-dd"); Settings.Save();
            UpdateDateUi(); RefreshAll();
        }

        void DateAuto_Click(object s, RoutedEventArgs e)
        {
            Game.DateIsManual = false; Settings.Cur.ManualDate = ""; Settings.Save();
            if (_g != null && !_reading) StartRead();
        }

        // ============================================================ navigation + recherche du haut

        void Nav_Click(object s, RoutedEventArgs e) => ShowPage((ToggleButton)s);

        void ShowPage(ToggleButton nav)
        {
            foreach (var b in new[] { NavClub, NavPlayer, NavSearch, NavRadar, NavAlerts, NavCompare, NavShort, NavDeals, NavTactic, NavDressing, NavTrain }) b.IsChecked = b == nav;
            PageTrain.Visibility = nav == NavTrain ? Visibility.Visible : Visibility.Collapsed;
            PageShort.Visibility = nav == NavShort ? Visibility.Visible : Visibility.Collapsed;
            PageDeals.Visibility = nav == NavDeals ? Visibility.Visible : Visibility.Collapsed;
            PageTactic.Visibility = nav == NavTactic ? Visibility.Visible : Visibility.Collapsed;
            PageDressing.Visibility = nav == NavDressing ? Visibility.Visible : Visibility.Collapsed;
            PageClub.Visibility = nav == NavClub ? Visibility.Visible : Visibility.Collapsed;
            PagePlayer.Visibility = nav == NavPlayer ? Visibility.Visible : Visibility.Collapsed;
            PageSearch.Visibility = nav == NavSearch ? Visibility.Visible : Visibility.Collapsed;
            PageRadar.Visibility = nav == NavRadar ? Visibility.Visible : Visibility.Collapsed;
            PageAlerts.Visibility = nav == NavAlerts ? Visibility.Visible : Visibility.Collapsed;
            PageCompare.Visibility = nav == NavCompare ? Visibility.Visible : Visibility.Collapsed;
            if (nav == NavRadar) RadarBadge.Visibility = Visibility.Collapsed;
            if (nav == NavTrain && _trainTab == "appliquer") CheckBridge();
            GuideFor(nav);
        }

        void OpenPlayer(PlayerRow r) { if (r == null) return; ShowPlayer(r); ShowPage(NavPlayer); }

        static readonly Dictionary<string, string> s_clubKeys = new();
        static string ClubKey(string club) { if (!s_clubKeys.TryGetValue(club, out var k)) s_clubKeys[club] = k = Game.Fold(club); return k; }

        static List<PlayerRow> FindByName(string text, int max)
        {
            string q = Game.Fold(text.Trim());
            if (q.Length < 2 || Game.Rows.Count == 0) return new();
            // recherche dans la base de TA partie (lue en direct dans FM) : nom du joueur, ou nom du club ;
            // ton club d'abord, puis les débuts de nom, puis le niveau
            string mine = Settings.Cur.MyClub;
            return Game.Rows.Where(r => Game.Shown(r) && (r.NameKey.Contains(q) || (q.Length >= 3 && r.Club != "" && ClubKey(r.Club).Contains(q))))
                .OrderByDescending(r => r.NameKey.Contains(q))
                .ThenByDescending(r => mine != "" && r.Club == mine)
                .ThenByDescending(r => r.NameKey.StartsWith(q) || r.NameKey.Contains(" " + q))
                .ThenByDescending(r => r.CA).Take(max).ToList();
        }

        static ListBoxItem PlayerItem(PlayerRow r)
        {
            var row = new DockPanel();
            var img = new System.Windows.Controls.Image { Width = 34, Height = 34, Margin = new Thickness(0, 0, 10, 0), Source = Gfx.Face(r, 68) ?? Gfx.Logo(r.ClubPtr, 68) };
            row.Children.Add(img);
            var sp = new StackPanel();
            row.Children.Add(sp);
            sp.Children.Add(T(r.Name, 14, BText, true));
            int age = Game.Age(r);
            sp.Children.Add(T($"{(r.Club == "" ? L.T("Sans club") : r.Club)}  ·  {(age < 0 ? "" : L.F("{0} ans", age) + "  ·  ")}{Db.BestPositions(r)}  ·  CA {r.CA} / PA {r.PA}", 12, BDim));
            return new ListBoxItem { Content = row, Tag = r };
        }

        void TopSearch_TextChanged(object s, TextChangedEventArgs e)
        {
            TopSearchHint.Visibility = TopSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            TopResults.Items.Clear();
            var hits = FindByName(TopSearch.Text, 40);
            foreach (var r in hits) TopResults.Items.Add(PlayerItem(r));
            TopPopup.IsOpen = hits.Count > 0;
        }

        void TopSearch_KeyDown(object s, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && TopResults.Items.Count > 0 && TopResults.Items[0] is ListBoxItem { Tag: PlayerRow first })
            { TopPopup.IsOpen = false; TopSearch.Text = ""; OpenPlayer(first); e.Handled = true; }
            if (e.Key == Key.Escape) TopPopup.IsOpen = false;
        }

        void TopResults_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (TopResults.SelectedItem is not ListBoxItem { Tag: PlayerRow r }) return;
            TopPopup.IsOpen = false;
            TopSearch.Text = "";
            OpenPlayer(r);
        }

        void Grid_DoubleClick(object s, MouseButtonEventArgs e)
        {
            if (((DataGrid)s).SelectedItem is DataRowView v && v["Id"] is uint id && Game.ById.TryGetValue(id, out var r)) OpenPlayer(r);
        }

        // ============================================================ petits éléments d'interface

        internal static string Money(long v) => v < 0 ? "—" : v >= 1_000_000 ? $"{v / 1_000_000.0:0.#} M£" : v >= 1000 ? $"{v / 1000} k£" : $"{v} £";

        static TextBlock T(string text, double size = 13, Brush color = null, bool bold = false) =>
            new() { Text = text, FontSize = size, Foreground = color ?? BText, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };

        internal static Brush ValueBrush(string key, int v)
        {
            if (v < 1) return BDim;
            if (key is "CA" or "PA" or "") return BText;
            if (Names.LowIsGood(key)) v = 21 - v;
            return v >= 15 ? BGood : v >= 11 ? BOk : v >= 6 ? BMid : BBad;
        }

        static Border Card(UIElement child, double pad = 18) =>
            new() { Background = BPanel, BorderBrush = BLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(pad), Margin = new Thickness(0, 0, 0, 14), Child = child };

        static Button Btn(string text, Action a, bool primary = false)
        {
            var b = new Button { Content = text };
            if (primary) b.Style = (Style)Application.Current.Resources["Primary"];
            b.Click += (_, _) => a();
            return b;
        }

        // ============================================================ tableaux (toutes les stats, triables)

        sealed record Extra(string Col, string Header, Type Type, Func<PlayerRow, object> Value, double Width, bool Accent = false);

        // en-têtes courts uniques (infobulle = nom complet), dans la langue de l'interface
        static (string lang, List<(string key, string shortName, string full)> cols) s_statCols;
        static List<(string key, string shortName, string full)> StatCols
        {
            get
            {
                if (s_statCols.cols == null || s_statCols.lang != L.Lang) s_statCols = (L.Lang, BuildStatCols());
                return s_statCols.cols;
            }
        }

        static List<(string, string, string)> BuildStatCols()
        {
            var all = Names.Hidden.Select(h => (h.k, fr: L.T(h.fr))).Concat(Names.Groups.SelectMany(g => g.attrs).Select(a => (a.k, fr: L.T(a.fr)))).ToList();
            var used = new HashSet<string>();
            var res = new List<(string, string, string)>();
            foreach (var (k, fr) in all)
            {
                var words = fr.Replace("(", "").Replace(")", "").Replace(".", "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => w.Length > 2 || w == fr).ToArray();
                string sh = words[0].Length <= 5 ? words[0] : words[0][..4] + ".";
                for (int i = 1; used.Contains(sh) && i < words.Length; i++) sh = words[0][..Math.Min(3, words[0].Length)] + ". " + words[i][..Math.Min(3, words[i].Length)] + ".";
                int n = 2; string baseSh = sh; while (used.Contains(sh)) sh = baseSh + n++;
                used.Add(sh);
                res.Add((k, sh, fr));
            }
            return res;
        }

        // catégorie de chaque stat (pour masquer / afficher des colonnes)
        static readonly Dictionary<string, string> GroupOf =
            Names.Hidden.Select(h => (h.k, g: "Cachées")).Concat(Names.Groups.SelectMany(g => g.attrs.Select(a => (a.k, g: g.cat))))
                 .GroupBy(x => x.k).ToDictionary(x => x.Key, x => x.First().g);
        static readonly string[] ColGroups = { "Cachées", "Technique", "Mental", "Physique", "Gardien" };
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DataGridColumn, string> ColGroup = new();

        void BuildGenderToggles()
        {
            GenderToggles.Children.Clear();
            foreach (var (code, label) in new[] { ("H", "Hommes"), ("F", "Femmes"), ("T", "Tous") })
            {
                var b = new ToggleButton { Content = L.T(label), IsChecked = Settings.Cur.Gender == code, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(10, 4, 10, 4), FontSize = 12 };
                b.Click += (_, _) =>
                {
                    Settings.Cur.Gender = code; Settings.Save();
                    if (_cmpA != null && !Game.Shown(_cmpA)) _cmpA = null;
                    if (_cmpB != null && !Game.Shown(_cmpB)) _cmpB = null;
                    _teamFilter = null; _alertCat = null;
                    BuildGenderToggles(); RefreshAll();
                };
                GenderToggles.Children.Add(b);
            }
        }

        /// <summary>Choix de la langue : l'écran est recréé dans la nouvelle langue (la base lue reste en mémoire).</summary>
        void BuildLangToggles()
        {
            LangToggles.Children.Clear();
            foreach (var (code, label) in L.Languages)
            {
                var b = new ToggleButton { Content = code.ToUpperInvariant(), ToolTip = label, IsChecked = L.Lang == code, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(10, 4, 10, 4), FontSize = 12 };
                b.Click += (_, _) =>
                {
                    if (L.Lang == code) { b.IsChecked = true; return; }
                    Settings.Cur.Lang = code; Settings.Save();
                    L.Init();
                    var w = new MainWindow();
                    Application.Current.MainWindow = w;
                    w.Show();
                    Close();
                };
                LangToggles.Children.Add(b);
            }
        }

        void BuildColToggles()
        {
            ColToggles.Children.Clear();
            foreach (var grp in ColGroups)
            {
                var b = new ToggleButton { Content = L.T(grp), IsChecked = !Settings.Cur.HiddenCols.Contains(grp), Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(8, 4, 8, 4), FontSize = 12 };
                b.Click += (_, _) =>
                {
                    if (!Settings.Cur.HiddenCols.Remove(grp)) Settings.Cur.HiddenCols.Add(grp);
                    Settings.Save();
                    foreach (var dg in new[] { ClubGrid, SearchGrid, RadarGrid, AlertGrid, ShortGrid, DealGrid, DressGrid, TrainGrid }) ApplyCols(dg);
                };
                ColToggles.Children.Add(b);
            }
        }

        static void ApplyCols(DataGrid g)
        {
            foreach (var c in g.Columns)
                if (ColGroup.TryGetValue(c, out var grp))
                    c.Visibility = Settings.Cur.HiddenCols.Contains(grp) ? Visibility.Collapsed : Visibility.Visible;
        }

        static DataTable MakeTable(IEnumerable<PlayerRow> rows, List<Extra> extras)
        {
            var t = new DataTable();
            t.Columns.Add("Id", typeof(uint));
            t.Columns.Add("Nom", typeof(string));
            foreach (var x in extras) t.Columns.Add(x.Col, x.Type);
            t.Columns.Add("Club", typeof(string));
            t.Columns.Add("Age", typeof(int));
            t.Columns.Add("Postes", typeof(string));
            t.Columns.Add("CA", typeof(int));
            t.Columns.Add("PA", typeof(int));
            t.Columns.Add("Valeur", typeof(long));
            t.Columns.Add("Contrat", typeof(DateTime));
            foreach (var (k, _, _) in StatCols) t.Columns.Add(k, typeof(int));

            object N(long v) => v < 0 ? DBNull.Value : v;
            foreach (var r in rows)
            {
                var vals = new List<object> { r.Id, r.Name };
                foreach (var x in extras) vals.Add(x.Value(r) ?? DBNull.Value);
                int age = Game.Age(r);
                vals.Add(r.Club); vals.Add(age < 0 ? DBNull.Value : age); vals.Add(Db.BestPositions(r)); vals.Add(r.CA); vals.Add(r.PA);
                vals.Add(N(r.Value));
                vals.Add(r.ExpYear == 0 ? DBNull.Value : new DateTime(r.ExpYear, 1, 1).AddDays(r.ExpDoy - 1));
                foreach (var (k, _, _) in StatCols) { int v = Db.Stat(r, k); vals.Add(v < 0 ? DBNull.Value : v); }
                t.Rows.Add(vals.ToArray());
            }
            return t;
        }

        static void Bind(DataGrid g, DataTable t, List<Extra> extras)
        {
            g.Columns.Clear();
            DataGridTextColumn Col(string col, object header, double w, IValueConverter conv = null, string fmt = null, bool bold = false, Brush color = null, string statKey = null)
            {
                var b = new Binding(col) { Converter = conv, StringFormat = fmt };
                var c = new DataGridTextColumn { Header = header, Binding = b, Width = new DataGridLength(w), SortMemberPath = col };
                var st = new Style(typeof(TextBlock), Application.Current.TryFindResource(typeof(TextBlock)) as Style);
                if (bold) st.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
                if (color != null) st.Setters.Add(new Setter(TextBlock.ForegroundProperty, color));
                if (statKey != null)
                {
                    st.Setters.Add(new Setter(TextBlock.ForegroundProperty, new Binding(col) { Converter = StatC, ConverterParameter = statKey }));
                    st.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
                    st.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center));
                }
                c.ElementStyle = st;
                g.Columns.Add(c);
                return c;
            }
            Col("Nom", L.T("Nom"), 200, bold: true);
            foreach (var x in extras) Col(x.Col, x.Header, x.Width, x.Type == typeof(long) ? MoneyC : null, bold: x.Accent, color: x.Accent ? BAccent : null);
            Col("Club", L.T("Club"), 160);
            Col("Age", L.T("Âge"), 48);
            Col("Postes", L.T("Postes"), 100);
            Col("CA", "CA", 48, bold: true);
            Col("PA", "PA", 48, bold: true, color: BAccent);
            Col("Valeur", L.T("Valeur"), 80, MoneyC);
            Col("Contrat", L.T("Contrat"), 90, fmt: "dd/MM/yyyy");
            foreach (var (k, sh, full) in StatCols)
            {
                var c = Col(k, new TextBlock { Text = sh, ToolTip = full + (GroupOf.TryGetValue(k, out var gr0) ? $" ({L.T(gr0)})" : "") }, 54, statKey: k);
                if (GroupOf.TryGetValue(k, out var gr)) ColGroup.AddOrUpdate(c, gr);
            }
            ApplyCols(g);
            g.FrozenColumnCount = 1;
            // clic droit / Ctrl+C sur une ligne = copier le nom
            if (g.ContextMenu == null)
            {
                var mi = new MenuItem { Header = L.T("Copier le nom") };
                mi.Click += (_, _) => { if (g.SelectedItem is DataRowView v) CopyName(v["Nom"] as string, null); };
                g.ContextMenu = new ContextMenu { Items = { mi } };
                g.PreviewKeyDown += (_, e) =>
                {
                    if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control && g.SelectedItem is DataRowView v)
                    { CopyName(v["Nom"] as string, null); e.Handled = true; }
                };
            }
            g.EnableColumnVirtualization = true;
            g.EnableRowVirtualization = true;
            g.ItemsSource = t.DefaultView;
        }

        // ============================================================ Mon club

        string _teamFilter;

        bool _pickerByUser;   // liste ouverte par « Changer de club » (choix volontaire), pas affichée faute de club connu

        void ChangeClub_Click(object s, RoutedEventArgs e) { _pickerByUser = true; ClubPicker.Visibility = Visibility.Visible; MyClubSearch.Focus(); BuildMyClubList(); }

        static readonly object AutoClubTag = new();

        void BuildMyClubList()
        {
            MyClubList.Items.Clear();
            if (Settings.Cur.MyClubManual)
                MyClubList.Items.Add(new ListBoxItem { Content = T(L.T("Détection automatique (club géré dans FM)"), 13, BAccent, true), Tag = AutoClubTag });
            string q = Game.Fold(MyClubSearch.Text.Trim());
            foreach (var c in Clubs(q, 150))
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new System.Windows.Controls.Image { Source = Gfx.Logo(c.ptr, 48), Width = 24, Height = 24, Margin = new Thickness(0, 0, 8, 0) });
                row.Children.Add(T(L.F("{0}  ({1} joueurs)", c.name, c.n), 13));
                MyClubList.Items.Add(new ListBoxItem { Content = row, Tag = c.name });
            }
            if (!MyClubList.Items.Cast<ListBoxItem>().Any(i => i.Tag is string))
                MyClubList.Items.Add(new ListBoxItem { Content = T(L.T(Game.Rows.Count == 0 ? "Base pas encore lue" : "Aucun club trouvé"), 13, BDim), IsEnabled = false });
        }

        static IEnumerable<(string name, int n, ulong ptr)> Clubs(string foldedQuery, int max) =>
            Game.Rows.Where(r => r.Club != "" && Game.Shown(r)).GroupBy(r => r.Club)
                .Select(gr => (name: gr.Key, n: gr.Count(), ca: gr.Max(x => x.CA), ptr: gr.First().ClubPtr))
                .Where(c => foldedQuery.Length == 0 || Game.Fold(c.name).Contains(foldedQuery))
                .OrderByDescending(c => c.ca).Take(max).Select(c => (c.name, c.n, c.ptr));

        void MyClubSearch_TextChanged(object s, TextChangedEventArgs e) => BuildMyClubList();

        void MyClubList_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (MyClubList.SelectedItem is not ListBoxItem { Tag: { } tag }) return;
            if (tag == AutoClubTag) { Settings.Cur.MyClubManual = false; _clubCheckedAt = DateTime.MinValue; }
            else if (tag is string name)
            {
                Settings.Cur.MyClub = name;
                if (_pickerByUser) Settings.Cur.MyClubManual = true;
                if (Settings.Cur.AlertClub == "") Settings.Cur.AlertClub = name;
            }
            else return;
            _pickerByUser = false;
            Settings.Save();
            ClubPicker.Visibility = Visibility.Collapsed;
            _teamFilter = null;
            RefreshAll();
        }

        /// <summary>
        /// Sous-catégories de l'effectif. Dans la mémoire, tous les contrats pointent vers l'équipe principale du club :
        /// on classe donc comme FM le fait d'habitude (en attendant de lire les vraies équipes) :
        /// les 25 meilleurs (CA) = équipe première, puis les autres par âge (U18, U21, réserve / équipe B).
        /// </summary>
        static Dictionary<PlayerRow, string> SquadGroups(List<PlayerRow> squad)
        {
            var res = new Dictionary<PlayerRow, string>();
            foreach (var r in squad.OrderByDescending(r => r.CA).Take(25)) res[r] = "Équipe première";
            foreach (var r in squad.Where(r => !res.ContainsKey(r)))
            {
                int a = Game.Age(r);
                res[r] = a < 0 ? "Réserve / jeunes" : a <= 18 ? "U18" : a <= 21 ? "U21" : "Réserve / équipe B";
            }
            return res;
        }

        static Border InfoTile(string label, string value, Brush color = null)
        {
            var sp = new StackPanel();
            sp.Children.Add(T(label, 11, BDim));
            sp.Children.Add(T(value, 18, color ?? BText, true));
            return new Border { Background = BPanel, CornerRadius = new CornerRadius(6), Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 8, 0), Child = sp };
        }

        void ShowClub()
        {
            TeamChips.Children.Clear();
            ClubInfo.Children.Clear();
            ClubLogo.Visibility = Visibility.Collapsed;
            if (Game.Rows.Count == 0) { ClubTitle.Text = L.T("Lance FM27 et charge ta partie"); ClubGrid.ItemsSource = null; return; }
            if (Settings.Cur.MyClub == "" || !Game.Rows.Any(r => r.Club == Settings.Cur.MyClub))
            {
                ClubTitle.Text = L.T("Mon club"); ClubPicker.Visibility = Visibility.Visible; BuildMyClubList(); return;
            }
            ClubTitle.Text = Settings.Cur.MyClub;
            var squad = Game.Rows.Where(r => r.Club == Settings.Cur.MyClub && Game.Shown(r)).ToList();
            if (squad.Count == 0)
            {
                ClubGrid.ItemsSource = null;
                ClubInfo.Children.Add(InfoTile(L.T("Aucun joueur"), L.T(Settings.Cur.Gender == "F" ? "pas d'équipe féminine dans ce club" : "pour ce filtre")));
                return;
            }
            var logo = Gfx.Logo(squad[0].ClubPtr, 80);
            if (logo != null) { ClubLogo.Source = logo; ClubLogo.Visibility = Visibility.Visible; }

            var groups = SquadGroups(squad);
            var first = squad.Where(r => groups[r] == "Équipe première").ToList();
            var ages = first.Select(Game.Age).Where(a => a > 0).ToList();
            long wages = squad.Where(r => r.Wage > 0).Sum(r => r.Wage);
            ClubInfo.Children.Add(InfoTile(L.T("Joueurs sous contrat"), squad.Count.ToString()));
            ClubInfo.Children.Add(InfoTile(L.T("Âge moyen (équipe 1)"), ages.Count > 0 ? L.F("{0:F1} ans", ages.Average()) : "—"));
            ClubInfo.Children.Add(InfoTile(L.T("CA moyen (équipe 1)"), first.Count > 0 ? first.Average(r => r.CA).ToString("F0") : "—"));
            ClubInfo.Children.Add(InfoTile(L.T("Valeur de l'effectif"), Money(squad.Where(r => r.Value > 0).Sum(r => r.Value))));
            ClubInfo.Children.Add(InfoTile(L.T("Masse salariale"), L.F("{0}/sem. · {1}/an", Money(wages), Money(wages * 52))));
            int ending = squad.Count(r => { int m = Game.ContractMonths(r); return m >= 0 && m <= 12; });
            ClubInfo.Children.Add(InfoTile(L.T("Contrats finissant ≤ 12 mois"), Game.HasDate ? ending.ToString() : L.T("date ?"), ending > 0 ? BMid : null));

            void Chip(string text, string grp)
            {
                var b = new ToggleButton { Content = text, IsChecked = _teamFilter == grp };
                b.Click += (_, _) => { _teamFilter = grp; ShowClub(); };
                TeamChips.Children.Add(b);
            }
            Chip(L.F("Tout l'effectif ({0})", squad.Count), null);
            foreach (var grp in new[] { "Équipe première", "Réserve / équipe B", "U21", "U18", "Réserve / jeunes" })
            {
                int n = groups.Values.Count(v => v == grp);
                if (n > 0) Chip($"{L.T(grp)} ({n})", grp);
            }

            var shown = squad.Where(r => _teamFilter == null || groups[r] == _teamFilter).OrderByDescending(r => r.CA);
            var extras = new List<Extra>
            {
                new("Equipe", L.T("Équipe"), typeof(string), r => L.T(groups.GetValueOrDefault(r, "")), 140),
                new("Prog", L.T("Prog. CA"), typeof(int), r => Features.Progress(r), 70),
            };
            Bind(ClubGrid, MakeTable(shown, extras), extras);
        }

        void ExportClub_Click(object s, RoutedEventArgs e)
        {
            var squad = Game.Rows.Where(r => r.Club == Settings.Cur.MyClub && Game.Shown(r)).ToList();
            var groups = SquadGroups(squad);
            Export(squad.Where(r => _teamFilter == null || groups[r] == _teamFilter).OrderByDescending(r => r.CA).ToList(), Settings.Cur.MyClub);
        }

        // ============================================================ fiche joueur

        void ShowPlayer(PlayerRow r)
        {
            _current = r;
            PlayerCard.Children.Clear();
            if (r == null)
            {
                PlayerCard.Children.Add(Card(T(L.T("Cherche un joueur dans la barre du haut, ou double-clique sur une ligne d'un tableau."), 14, BDim)));
                return;
            }

            var head = new StackPanel();
            // nom cliquable : copie dans le presse-papiers (pour le coller dans la recherche de FM)
            var nameTb = T(r.Name, 26, BText, true);
            nameTb.Cursor = Cursors.Hand;
            nameTb.ToolTip = L.T("Cliquer pour copier le nom");
            nameTb.MouseLeftButtonUp += (_, _) => CopyName(r.Name, null);
            head.Children.Add(nameTb);
            int age = Game.Age(r);
            var sub = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            var logo = Gfx.Logo(r.ClubPtr, 48);
            if (logo != null) sub.Children.Add(new System.Windows.Controls.Image { Source = logo, Width = 22, Height = 22, Margin = new Thickness(0, 0, 6, 0) });
            string clubTxt = r.Club == "" ? L.T("Sans club") : r.Club;
            string ageTxt = age < 0 ? L.T("âge inconnu") : L.F("{0} ans", age);
            string bornTxt = r.BirthYear > 0 ? " " + L.F("(né le {0})", Game.FmDate(r.BirthYear, r.BirthDoy)) : "";
            string femTxt = r.Female ? "  ·  " + L.T("football féminin") : "";
            sub.Children.Add(T($"{clubTxt}  ·  {ageTxt}{bornTxt}  ·  {Db.BestPositions(r)}{femTxt}", 14, BDim));
            head.Children.Add(sub);
            string wageTxt = r.Wage < 0 ? "—" : L.F("{0}/sem.", Money(r.Wage));
            string endTxt = r.ExpYear == 0 ? "—" : Game.FmDate(r.ExpYear, r.ExpDoy);
            head.Children.Add(T(L.F("Valeur {0}  ·  Salaire {1}  ·  Fin de contrat {2}", Money(r.Value), wageTxt, endTxt), 13, BDim));
            var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            var copyBtn = new Button { Content = L.T("Copier le nom"), Style = (Style)Application.Current.Resources["Primary"] };
            copyBtn.Click += (_, _) => CopyName(r.Name, copyBtn);
            actions.Children.Add(copyBtn);
            actions.Children.Add(ShortButton(r));
            actions.Children.Add(Btn(L.T("Comparer (joueur A)"), () => { _cmpA = r; ShowCompare(); ShowPage(NavCompare); }, true));
            actions.Children.Add(Btn(L.T("Comparer (joueur B)"), () => { _cmpB = r; ShowCompare(); ShowPage(NavCompare); }));
            if (r.Club != "") actions.Children.Add(Btn(L.F("Alertes : {0}", r.Club), () => { Settings.Cur.AlertClub = r.Club; Settings.Save(); BuildClubList(); ShowAlerts(); ShowPage(NavAlerts); }));
            head.Children.Add(actions);

            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition());
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var face = Gfx.Face(r, 240);
            if (face != null)
                top.Children.Add(new Border { CornerRadius = new CornerRadius(6), Background = BPanel2, Margin = new Thickness(0, 0, 18, 0), ClipToBounds = true,
                    Child = new System.Windows.Controls.Image { Source = face, Width = 120, Height = 120, Stretch = Stretch.UniformToFill } });
            Grid.SetColumn(head, 1);
            top.Children.Add(head);
            var abil = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            abil.Children.Add(BigTile("CA", r.CA)); abil.Children.Add(BigTile("PA", r.PA));
            Grid.SetColumn(abil, 2); top.Children.Add(abil);
            PlayerCard.Children.Add(Card(top, 18));

            var hidden = new WrapPanel();
            foreach (var (k, fr) in Names.Hidden) hidden.Children.Add(StatTile(L.T(fr), Db.Stat(r, k), k));
            var hs = new StackPanel();
            hs.Children.Add(T(L.T("Stats cachées"), 15, BText, true));
            hs.Children.Add(hidden);
            PlayerCard.Children.Add(Card(hs));

            var attrs = new Grid();
            foreach (var _ in Names.Groups) attrs.ColumnDefinitions.Add(new ColumnDefinition());
            for (int c = 0; c < Names.Groups.Length; c++)
            {
                var col = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
                col.Children.Add(T(L.T(Names.Groups[c].cat), 12, BDim, true));
                foreach (var (k, fr) in Names.Groups[c].attrs) col.Children.Add(AttrLine(L.T(fr), Db.Stat(r, k), k));
                Grid.SetColumn(col, c); attrs.Children.Add(col);
            }
            PlayerCard.Children.Add(Card(attrs));

            var roles = new StackPanel();
            roles.Children.Add(T(L.T("Notes par rôle (/100)"), 15, BText, true));
            foreach (var (nom, n) in Db.Roles.Select(x => (x.Nom, Db.Rating(r, x))).OrderByDescending(x => x.Item2))
            {
                var line = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
                var name = T(L.T(nom), 13); name.Width = 190; line.Children.Add(name);
                var val = T(n.ToString(), 13, BText, true); val.Width = 40; DockPanel.SetDock(val, Dock.Right); line.Children.Add(val);
                line.Children.Add(new Border { Height = 8, Width = Math.Max(4, n * 3.2), HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(4),
                    Background = n >= 75 ? BGood : n >= 60 ? BOk : n >= 45 ? BMid : BBad });
                roles.Children.Add(line);
            }
            roles.Children.Add(T(L.T("Pondérations modifiables : %APPDATA%\\FM27Scout\\roles.json"), 11, BDim));
            PlayerCard.Children.Add(Card(roles));
            PlayerCard.Children.Add(TrainingCard(r));
            PlayerCard.Children.Add(ProgressCard(r));
            PlayerCard.Children.Add(SimilarCard(r));
        }

        /// <summary>Copie le nom dans le presse-papiers (réessaie si un autre programme le tient).</summary>
        static void CopyName(string name, Button feedback)
        {
            if (string.IsNullOrEmpty(name)) return;
            for (int i = 0; i < 5; i++)
            {
                try { Clipboard.SetText(name); break; }
                catch (System.Runtime.InteropServices.COMException) { Thread.Sleep(50); }
            }
            if (Application.Current.MainWindow is MainWindow w) w.SetStatus(true, L.F("« {0} » copié : colle-le (Ctrl+V) dans la recherche de FM27", name));
            if (feedback == null) return;
            var old = feedback.Content;
            feedback.Content = L.T("Copié");
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            t.Tick += (_, _) => { feedback.Content = old; t.Stop(); };
            t.Start();
        }

        static Border BigTile(string label, int v)
        {
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(T(L.T(label == "CA" ? "Capacité actuelle" : "Potentiel"), 11, BDim));
            var n = T(v.ToString(), 34, label == "PA" ? BAccent : BText, true); n.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(n);
            return new Border { Background = BPanel2, CornerRadius = new CornerRadius(6), Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(10, 0, 0, 0), Child = sp, MinWidth = 120 };
        }

        static Border StatTile(string label, int v, string key)
        {
            var dp = new DockPanel { Width = 200 };
            var val = T(v < 0 ? "?" : v.ToString(), 15, ValueBrush(key, v), true);
            DockPanel.SetDock(val, Dock.Right); dp.Children.Add(val);
            dp.Children.Add(T(label, 13));
            return new Border { Background = BPanel2, CornerRadius = new CornerRadius(4), Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 8, 8, 0), Child = dp };
        }

        static DockPanel AttrLine(string label, int v, string key)
        {
            var dp = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
            var val = T(v < 0 ? "?" : v.ToString(), 13, ValueBrush(key, v), true);
            DockPanel.SetDock(val, Dock.Right); dp.Children.Add(val);
            dp.Children.Add(T(label, 13));
            return dp;
        }

        // ============================================================ recherches

        void BuildSearchList()
        {
            SearchList.Items.Clear();
            foreach (var s in Db.Searches) SearchList.Items.Add(new ListBoxItem { Content = T(L.T(s.Nom), 13), Tag = s });
            if (SearchList.Items.Count > 0) SearchList.SelectedIndex = 0;
        }

        void ReloadSearches_Click(object s, RoutedEventArgs e) { Db.ReloadSearches(); BuildSearchList(); }
        void SearchList_SelectionChanged(object s, SelectionChangedEventArgs e) => RunSelectedSearch();

        List<PlayerRow> _searchRes = new();

        void RunSelectedSearch()
        {
            if (SearchList.SelectedItem is not ListBoxItem { Tag: Db.Search s }) return;
            _searchRes = Game.Rows.Count == 0 ? new() : Db.Run(s);
            Bind(SearchGrid, MakeTable(_searchRes, new()), new());
            SearchInfo.Text = Game.Rows.Count == 0 ? L.T("Base pas encore lue") : L.F("{0} — {1} résultat(s)", L.T(s.Nom), _searchRes.Count);
        }

        void ExportSearch_Click(object s, RoutedEventArgs e) =>
            Export(_searchRes, (SearchList.SelectedItem as ListBoxItem)?.Tag is Db.Search se ? se.Nom : "recherche");

        void Export(List<PlayerRow> rows, string name)
        {
            if (rows.Count == 0) return;
            try
            {
                string path = Db.ExportCsv(rows, name);
                if (MessageBox.Show(L.F("{0} joueurs exportés :\n{1}\n\nOuvrir le fichier ?", rows.Count, path), "FM27 Assistant", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show(L.F("Export impossible : {0}", ex.Message), "FM27 Assistant"); }
        }

        // ============================================================ radar de scouting

        static readonly long[] ValueSteps = { -1, 250_000, 500_000, 1_000_000, 2_000_000, 5_000_000, 10_000_000, 20_000_000, 50_000_000, 100_000_000 };
        static readonly (string key, double x, double y)[] PitchSpots =
        {
            ("GK", 5, 50), ("DL", 20, 13), ("DC", 20, 50), ("DR", 20, 87), ("WBL", 35, 8), ("DM", 36, 50), ("WBR", 35, 92),
            ("ML", 53, 13), ("MC", 53, 50), ("MR", 53, 87), ("AML", 71, 15), ("AMC", 70, 50), ("AMR", 71, 85), ("ST", 89, 50),
        };
        static readonly (string key, string label)[] QuickSorts =
        {
            ("PA", "Potentiel"), ("CA", "Capacité"), ("Age", "Âge"), ("Valeur", "Valeur"), ("AttributeAmbition", "Ambition"),
            ("AttributeProfessionalism", "Professionnalisme"), ("Consistency", "Régularité"), ("ImportantMatches", "Grands matchs"),
            ("InjuryProneness", "Fragilité"), ("AttributeDetermination", "Détermination"), ("AttributePace", "Vitesse"),
        };
        bool _allStats;
        List<PlayerRow> _radarRes = new();

        void RadarChanged() { Db.SaveRadar(); BuildRadarFilters(); RunRadar(); }

        void BuildRadarFilters()
        {
            var f = Db.Radar;
            RadarFilters.Children.Clear();
            RadarFilters.Children.Add(new TextBlock { Text = L.T("Filtres"), Style = (Style)FindResource("H2"), Margin = new Thickness(0, 0, 0, 6) });

            void Stepper(string label, string value, Action minus, Action plus)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
                var l = T(label, 13, BText); l.Width = 110; row.Children.Add(l);
                row.Children.Add(Btn("−", () => { minus(); RadarChanged(); }));
                var v = T(value, 14, BText, true); v.Width = 95; v.TextAlignment = TextAlignment.Center; row.Children.Add(v);
                row.Children.Add(Btn("+", () => { plus(); RadarChanged(); }));
                RadarFilters.Children.Add(row);
            }
            Stepper(L.T("Âge min"), L.F("{0} ans", f.AgeMin), () => f.AgeMin = Math.Max(14, f.AgeMin - 1), () => f.AgeMin = Math.Min(f.AgeMax, f.AgeMin + 1));
            Stepper(L.T("Âge max"), L.F("{0} ans", f.AgeMax), () => f.AgeMax = Math.Max(f.AgeMin, f.AgeMax - 1), () => f.AgeMax = Math.Min(45, f.AgeMax + 1));
            Stepper("CA min", f.CaMin.ToString(), () => f.CaMin = Math.Max(0, f.CaMin - 5), () => f.CaMin = Math.Min(200, f.CaMin + 5));
            Stepper("PA min", f.PaMin.ToString(), () => f.PaMin = Math.Max(0, f.PaMin - 5), () => f.PaMin = Math.Min(200, f.PaMin + 5));
            int vi = Math.Max(0, Array.IndexOf(ValueSteps, f.ValeurMax));
            Stepper(L.T("Valeur max"), f.ValeurMax < 0 ? L.T("illimitée") : Money(f.ValeurMax),
                () => f.ValeurMax = ValueSteps[Math.Max(0, vi - 1)], () => f.ValeurMax = ValueSteps[Math.Min(ValueSteps.Length - 1, vi + 1)]);
            if (!Game.HasDate) RadarFilters.Children.Add(T(L.T("Date de jeu inconnue : filtre d'âge ignoré (saisis-la en haut)."), 11, BMid));

            var ph = new DockPanel { Margin = new Thickness(0, 12, 0, 4) };
            if (f.Postes.Count > 0) { var clr = Btn(L.T("Effacer"), () => { f.Postes.Clear(); RadarChanged(); }); DockPanel.SetDock(clr, Dock.Right); ph.Children.Add(clr); }
            ph.Children.Add(T(f.Postes.Count == 0 ? L.T("Postes : tous") : L.F("Postes : {0}", string.Join(", ", f.Postes.Select(Db.PositionLabel))), 13, BText, true));
            RadarFilters.Children.Add(ph);
            RadarFilters.Children.Add(Pitch(f));

            SortChips.Children.Clear();
            SortChips.Children.Add(new TextBlock { Text = L.T("Trier par"), Foreground = BDim, Margin = new Thickness(0, 6, 8, 0) });
            IEnumerable<(string key, string label)> sorts = QuickSorts;
            if (_allStats)
                sorts = sorts.Concat(Names.Groups.SelectMany(gr => gr.attrs).Select(x => (x.k, x.fr))).Concat(Names.Hidden)
                             .GroupBy(x => x.Item1).Select(gr => gr.First());
            foreach (var (key, label) in sorts)
            {
                bool on = f.Tri == key;
                var b = new ToggleButton { Content = on ? $"{L.T(label)} {(f.Decroissant ? "↓" : "↑")}" : L.T(label), IsChecked = on };
                var k = key;
                b.Click += (_, _) => { if (f.Tri == k) f.Decroissant = !f.Decroissant; else { f.Tri = k; f.Decroissant = k != "Age"; } RadarChanged(); };
                SortChips.Children.Add(b);
            }
            SortChips.Children.Add(Btn(L.T(_allStats ? "Moins de stats ▴" : "Toutes les stats ▾"), () => { _allStats = !_allStats; BuildRadarFilters(); }));
        }

        UIElement Pitch(Db.RadarFilter f)
        {
            const double W = 340, H = 220;
            var c = new Canvas { Width = W, Height = H, Background = Br("#1C5536"), Margin = new Thickness(0, 4, 0, 0) };
            void DrawRect(double x, double y, double w, double h)
            {
                var r = new Border { Width = w * W / 100, Height = h * H / 100, BorderBrush = Br("#5AFFFFFF"), BorderThickness = new Thickness(1) };
                Canvas.SetLeft(r, x * W / 100); Canvas.SetTop(r, y * H / 100); c.Children.Add(r);
            }
            DrawRect(2, 3, 96, 94); DrawRect(50, 3, 0.3, 94); DrawRect(2, 25, 14, 50); DrawRect(84, 25, 14, 50);
            foreach (var (key, x, y) in PitchSpots)
            {
                var b = new ToggleButton { Content = Db.PositionLabel(key), IsChecked = f.Postes.Contains(key), Width = 44, Height = 26, Padding = new Thickness(0), Margin = new Thickness(0), FontSize = 11 };
                if (b.IsChecked != true) b.Background = Br("#40FFFFFF");
                var k = key;
                b.Click += (_, _) => { if (!f.Postes.Remove(k)) f.Postes.Add(k); RadarChanged(); };
                Canvas.SetLeft(b, x * W / 100 - 22); Canvas.SetTop(b, y * H / 100 - 13);
                c.Children.Add(b);
            }
            return c;
        }

        void RunRadar()
        {
            var f = Db.Radar;
            if (Game.Rows.Count == 0) { RadarGrid.ItemsSource = null; RadarInfo.Text = L.T("Base pas encore lue"); return; }
            var (all, fresh) = Db.CheckRadar(false);
            var freshSet = fresh.Select(r => r.Id).ToHashSet();
            _radarRes = fresh.Concat(all.Where(r => !freshSet.Contains(r.Id))).ToList();
            var extras = new List<Extra>
            {
                new("Nouveau", "", typeof(string), r => freshSet.Contains(r.Id) ? L.T("NOUVEAU") : "", 80, true),
                new("Tri", L.T(Names.Of(f.Tri)), typeof(long), r => { long v = Db.SortValue(r, f.Tri); return v < 0 ? null : (object)v; }, 110, true),
            };
            // la valeur sert déjà de colonne : le tri par valeur s'affiche en £
            if (f.Tri != "Valeur") extras[1] = extras[1] with { Type = typeof(int), Value = r => { long v = Db.SortValue(r, f.Tri); return v < 0 ? null : (object)(int)v; } };
            Bind(RadarGrid, MakeTable(_radarRes, extras), extras);
            RadarInfo.Text = L.F("{0}{1} joueur(s)", all.Count, all.Count >= 500 ? "+" : "") + (fresh.Count > 0 ? " · " + L.F("{0} nouveau(x)", fresh.Count) : "");
        }

        void RadarSeen_Click(object s, RoutedEventArgs e) { Db.CheckRadar(true); RunRadar(); RadarBadge.Visibility = Visibility.Collapsed; }
        void ExportRadar_Click(object s, RoutedEventArgs e) => Export(_radarRes, "radar");

        // ============================================================ alertes d'effectif

        void BuildClubList()
        {
            ClubList.Items.Clear();
            if (Settings.Cur.AlertClub == "" && Settings.Cur.MyClub != "") Settings.Cur.AlertClub = Settings.Cur.MyClub;
            foreach (var (name, n, ptr) in Clubs(Game.Fold(ClubSearch.Text.Trim()), 150))
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new System.Windows.Controls.Image { Source = Gfx.Logo(ptr, 48), Width = 22, Height = 22, Margin = new Thickness(0, 0, 8, 0) });
                row.Children.Add(T($"{name}  ({n})", 13));
                var it = new ListBoxItem { Content = row, Tag = name };
                ClubList.Items.Add(it);
                if (name == Settings.Cur.AlertClub) it.IsSelected = true;
            }
        }

        void ClubSearch_TextChanged(object s, TextChangedEventArgs e) => BuildClubList();

        void ClubList_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (ClubList.SelectedItem is ListBoxItem { Tag: string name } && name != Settings.Cur.AlertClub)
            { Settings.Cur.AlertClub = name; Settings.Save(); _alertCat = null; ShowAlerts(); }
        }

        string _alertCat;

        void ShowAlerts()
        {
            AlertChips.Children.Clear();
            var club = Game.Rows.FirstOrDefault(r => r.Club == Settings.Cur.AlertClub && r.ClubPtr != 0);
            if (club == null) { AlertGrid.ItemsSource = null; AlertInfo.Text = L.T("Choisis un club à gauche"); return; }
            var groups = Db.Alerts(club.ClubPtr);
            AlertInfo.Text = L.F("{0} — {1} alerte(s)", Settings.Cur.AlertClub, groups.Sum(gr => gr.joueurs.Count));

            void Chip(string label, string cat)
            {
                var b = new ToggleButton { Content = label, IsChecked = _alertCat == cat };
                b.Click += (_, _) => { _alertCat = cat; ShowAlerts(); };
                AlertChips.Children.Add(b);
            }
            Chip(L.T("Toutes"), null);
            foreach (var (titre, j) in groups) Chip($"{L.T(titre)} ({j.Count})", titre);

            var items = groups.Where(gr => _alertCat == null || gr.titre == _alertCat)
                .SelectMany(gr => gr.joueurs.Select(x => (x.r, gr.titre, x.detail))).ToList();
            var alertOf = new Dictionary<PlayerRow, (string titre, string detail)>();
            // un joueur peut apparaître dans plusieurs alertes : on les garde toutes
            var rows = new List<PlayerRow>();
            foreach (var (r, titre, detail) in items) { rows.Add(r); alertOf.TryAdd(r, (titre, detail)); }
            var extras = new List<Extra>
            {
                new("Alerte", L.T("Alerte"), typeof(string), r => L.T(alertOf[r].titre), 260, true),
                new("Detail", L.T("Détail"), typeof(string), r => alertOf[r].detail, 170),
            };
            Bind(AlertGrid, MakeTable(rows.Distinct(), extras), extras);
        }

        // ============================================================ comparateur (gauche / droite)

        void CmpSearch_TextChanged(object s, TextChangedEventArgs e)
        {
            if (s == CmpSearchA) FillCmpList(CmpListA, CmpSearchA, CmpInfoA);
            else FillCmpList(CmpListB, CmpSearchB, CmpInfoB);
        }

        /// <summary>Recherche vide = ton effectif ; sinon toute la base.</summary>
        void FillCmpList(ListBox list, TextBox box, TextBlock info)
        {
            list.Items.Clear();
            List<PlayerRow> rows;
            if (box.Text.Trim().Length >= 2) { rows = FindByName(box.Text, 100); info.Text = L.F("{0} résultat(s) dans toute la base", rows.Count); }
            else
            {
                rows = Game.Rows.Where(r => r.Club == Settings.Cur.MyClub && Settings.Cur.MyClub != "" && Game.Shown(r)).OrderByDescending(r => r.CA).ToList();
                info.Text = Settings.Cur.MyClub == "" ? L.T("Tape un nom (choisis ton club dans « Mon club » pour voir ton effectif ici)") : L.F("Ton effectif ({0}) — tape un nom pour chercher partout", rows.Count);
            }
            foreach (var r in rows) list.Items.Add(PlayerItem(r));
        }

        void CmpList_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (((ListBox)s).SelectedItem is not ListBoxItem { Tag: PlayerRow r }) return;
            if (s == CmpListA) _cmpA = r; else _cmpB = r;
            ShowCompare();
        }

        void ShowCompare()
        {
            CompareBox.Children.Clear();
            var a = _cmpA; var b = _cmpB;
            if (a == null || b == null)
            {
                CompareBox.Children.Add(Card(T(L.T("Choisis un joueur à gauche et un joueur à droite."), 14, BDim)));
                if (a == null && b == null) return;
            }
            CompareBox.Children.Add(RadarCard(a, b));
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            int row = 0;
            void Add(UIElement e, int col) { Grid.SetRow(e, row); Grid.SetColumn(e, col); grid.Children.Add(e); }
            void NewRow(double h = 26) => grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(h) });

            NewRow(48);
            Add(T(a?.Name ?? "—", 16, BText, true), 1); Add(T(b?.Name ?? "—", 16, BAccent, true), 2); row++;
            NewRow(22);
            Add(T(a == null ? "" : $"{a.Club} · {Db.BestPositions(a)}", 11, BDim), 1); Add(T(b == null ? "" : $"{b.Club} · {Db.BestPositions(b)}", 11, BDim), 2); row++;

            void Section(string title) { NewRow(34); Add(T(title, 13, BDim, true), 0); row++; }
            void Line(string label, Func<PlayerRow, int> get, string key)
            {
                int va = a == null ? -1 : get(a), vb = b == null ? -1 : get(b);
                bool low = Names.LowIsGood(key);
                int sa = low ? -va : va, sb = low ? -vb : vb;
                NewRow();
                if (row % 2 == 0) { var bg = new Border { Background = BPanel }; Grid.SetColumnSpan(bg, 3); Add(bg, 0); }
                Add(T("  " + label, 13), 0);
                Add(T(va < 0 ? "—" : va.ToString(), 14, va < 0 || vb < 0 || sa == sb ? BText : sa > sb ? BGood : BBad, true), 1);
                Add(T(vb < 0 ? "—" : vb.ToString(), 14, va < 0 || vb < 0 || sa == sb ? BText : sb > sa ? BGood : BBad, true), 2);
                row++;
            }
            Section(L.T("Profil"));
            Line(L.T("Âge"), Game.Age, "AgeLow");
            Line(L.T("Capacité actuelle"), r => r.CA, "CA"); Line(L.T("Potentiel"), r => r.PA, "PA");
            Section(L.T("Stats cachées"));
            foreach (var (k, fr) in Names.Hidden) Line(L.T(fr), r => Db.Stat(r, k), k);
            foreach (var g in Names.Groups) { Section(L.T(g.cat)); foreach (var (k, fr) in g.attrs) Line(L.T(fr), r => Db.Stat(r, k), k); }
            Section(L.T("Notes par rôle"));
            foreach (var role in Db.Roles) Line(L.T(role.Nom), r => Db.Rating(r, role), "role");
            CompareBox.Children.Add(Card(grid));
        }
    }
}
