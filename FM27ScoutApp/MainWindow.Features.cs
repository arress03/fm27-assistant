using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FM27Scout
{
    /// <summary>Shortlist, bonnes affaires, vestiaire, et les ajouts de la fiche joueur / du comparateur.</summary>
    public partial class MainWindow
    {
        // ============================================================ shortlist

        List<PlayerRow> _shortRes = new();

        void ShowShort()
        {
            var rows = Game.Rows.Count == 0 ? new() : Features.ShortRows().Where(x => Game.Shown(x.r)).ToList();
            _shortRes = rows.Select(x => x.r).ToList();
            var ch = rows.ToDictionary(x => x.r, x => x);
            var extras = new List<Extra>
            {
                new("Changements", L.T("Changements depuis l'ajout"), typeof(string), r => ch[r].changes, 300, true),
                new("Prog", L.T("Prog. CA"), typeof(int), r => Features.Progress(r), 70),
            };
            Bind(ShortGrid, MakeTable(_shortRes, extras), extras);
            int alerts = rows.Count(x => x.important);
            ShortInfo.Text = Features.Shortlist.Count == 0
                ? L.T("Vide : ajoute des joueurs depuis leur fiche.")
                : L.F("{0} joueur(s)", rows.Count) + (alerts > 0 ? " · " + L.F("{0} à surveiller (club, valeur en baisse, fin de contrat)", alerts) : "");
            NavShortLabel.Text = alerts > 0 ? L.F("Shortlist · {0} à voir", alerts) : Features.Shortlist.Count > 0 ? $"{L.T("Shortlist")} · {Features.Shortlist.Count}" : L.T("Shortlist");
        }

        void ShortSeen_Click(object s, RoutedEventArgs e) { Features.MarkShortSeen(); ShowShort(); }
        void ExportShort_Click(object s, RoutedEventArgs e) => Export(_shortRes, "shortlist");

        // ============================================================ bonnes affaires

        string _dealKind = "libres";
        int _dealCaMin = 100;
        List<PlayerRow> _dealRes = new();

        void ShowDeals()
        {
            DealChips.Children.Clear();
            foreach (var (key, label) in Features.DealKinds)
            {
                var b = new ToggleButton { Content = L.T(label), IsChecked = _dealKind == key };
                var k = key;
                b.Click += (_, _) => { _dealKind = k; ShowDeals(); };
                DealChips.Children.Add(b);
            }
            DealChips.Children.Add(new TextBlock { Text = "   CA min", Foreground = BDim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) });
            DealChips.Children.Add(Btn("−", () => { _dealCaMin = Math.Max(0, _dealCaMin - 10); ShowDeals(); }));
            DealChips.Children.Add(new TextBlock { Text = _dealCaMin.ToString(), Foreground = BText, FontWeight = FontWeights.SemiBold, Width = 40, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            DealChips.Children.Add(Btn("+", () => { _dealCaMin = Math.Min(200, _dealCaMin + 10); ShowDeals(); }));

            if (Game.Rows.Count == 0) { DealGrid.ItemsSource = null; DealInfo.Text = L.T("Base pas encore lue"); return; }
            _dealRes = Features.Deals(_dealKind, _dealCaMin);
            var extras = new List<Extra>
            {
                new("QP", L.T("Qualité/prix"), typeof(int), r => r.Value > 0 ? (object)(int)Math.Round(Features.QualityPrice(r)) : null, 90, true),
                new("Salaire", L.T("Salaire/sem."), typeof(long), r => r.Wage >= 0 ? r.Wage : null, 90),
            };
            Bind(DealGrid, MakeTable(_dealRes, extras), extras);
            DealInfo.Text = L.F("{0} joueur(s)", $"{_dealRes.Count}{(_dealRes.Count >= 500 ? "+" : "")}") +
                (!Game.HasDate && _dealKind is "6" or "12" ? " · " + L.T("date de jeu inconnue : saisis-la en haut") : "") +
                (_dealKind == "prix" ? " · " + L.T("qualité/prix = CA² par million de £ (plus c'est haut, mieux c'est)") : "");
        }

        void ExportDeals_Click(object s, RoutedEventArgs e) => Export(_dealRes, "bonnes-affaires");

        // ============================================================ vestiaire

        string _dressCat;

        void ShowDressing()
        {
            DressChips.Children.Clear();
            var squad = Game.Rows.Where(r => r.Club == Settings.Cur.MyClub && Settings.Cur.MyClub != "" && Game.Shown(r)).ToList();
            if (squad.Count == 0) { DressGrid.ItemsSource = null; DressInfo.Text = L.T("Choisis ton club dans « Mon club »."); return; }
            var cats = Features.Dressing.Select(c => (c.titre, c.positif, list: squad.Select(r => (r, why: c.why(r))).Where(x => x.why != null).OrderByDescending(x => x.r.CA).ToList())).ToList();

            void Chip(string label, string cat, bool positive)
            {
                var b = new ToggleButton { Content = label, IsChecked = _dressCat == cat };
                if (cat != null && b.IsChecked != true) b.Foreground = positive ? BGood : BMid;
                b.Click += (_, _) => { _dressCat = cat; ShowDressing(); };
                DressChips.Children.Add(b);
            }
            Chip(L.T("Tout le vestiaire"), null, true);
            foreach (var (titre, positif, list) in cats) if (list.Count > 0) Chip($"{L.T(titre)} · {list.Count}", titre, positif);

            var shown = new List<PlayerRow>();
            var why = new Dictionary<PlayerRow, List<string>>();
            foreach (var (titre, _, list) in cats.Where(c => _dressCat == null || c.titre == _dressCat))
                foreach (var (r, w) in list)
                {
                    if (!why.TryGetValue(r, out var l)) { why[r] = l = new(); shown.Add(r); }
                    l.Add(_dressCat == null ? $"{L.T(titre)} ({w})" : w);
                }
            if (_dressCat == null) foreach (var r in squad.Where(r => !why.ContainsKey(r))) { why[r] = new() { "" }; shown.Add(r); }
            var extras = new List<Extra> { new("Pourquoi", L.T("Pourquoi"), typeof(string), r => string.Join(" · ", why[r]), 360, true) };
            Bind(DressGrid, MakeTable(shown.OrderByDescending(r => r.CA), extras), extras);
            DressInfo.Text = $"{Settings.Cur.MyClub} · " + L.F("{0} joueurs · points forts en vert, risques en orange (stats cachées)", squad.Count);
        }

        // ============================================================ fiche joueur : shortlist, progression, profils similaires

        bool _simYounger, _simCheaper;

        Button ShortButton(PlayerRow r)
        {
            var b = new Button();
            void Refresh() => b.Content = Features.InShortlist(r) ? L.T("Dans la shortlist") : L.T("Ajouter à la shortlist");
            Refresh();
            b.Click += (_, _) => { Features.ToggleShort(r); Refresh(); ShowShort(); };
            return b;
        }

        UIElement ProgressCard(PlayerRow r)
        {
            var pts = Features.Points(r);
            var sp = new StackPanel();
            sp.Children.Add(T(L.T("Progression"), 15, BText, true));
            if (pts.Count < 2)
            {
                sp.Children.Add(T(L.T("Un relevé est enregistré à chaque date de jeu : la courbe apparaîtra quand tu auras avancé dans ta partie."), 12, BDim));
                return Card(sp);
            }
            int first = pts[0].ca, last = pts[^1].ca;
            sp.Children.Add(T(L.F("CA {0} → {1} ({2}) depuis le {3:dd/MM/yyyy}  ·  PA {4}", first, last, $"{(last >= first ? "+" : "")}{last - first}", pts[0].date, pts[^1].pa), 12, last > first ? BGood : last < first ? BBad : BDim));
            const double W = 640, H = 140;
            var c = new Canvas { Width = W, Height = H + 20, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            int lo = Math.Max(0, pts.Min(p => p.ca) - 5), hi = Math.Min(200, Math.Max(pts.Max(p => p.ca), pts[^1].pa) + 5);
            double t0 = pts[0].date.Ticks, t1 = Math.Max(t0 + 1, pts[^1].date.Ticks);
            double X(DateTime d) => (d.Ticks - t0) / (t1 - t0) * (W - 40) + 30;
            double Y(int v) => H - (v - lo) / (double)Math.Max(1, hi - lo) * H;
            var pa = new Line { X1 = 30, X2 = W - 10, Y1 = Y(pts[^1].pa), Y2 = Y(pts[^1].pa), Stroke = BAccent, StrokeDashArray = new DoubleCollection { 4, 4 }, StrokeThickness = 1 };
            c.Children.Add(pa);
            var lbl = T($"PA {pts[^1].pa}", 10, BAccent); Canvas.SetLeft(lbl, W - 60); Canvas.SetTop(lbl, Y(pts[^1].pa) - 14); c.Children.Add(lbl);
            var poly = new Polyline { Stroke = BGood, StrokeThickness = 2.5 };
            foreach (var p in pts) poly.Points.Add(new Point(X(p.date), Y(p.ca)));
            c.Children.Add(poly);
            foreach (var p in pts)
            {
                var dot = new Ellipse { Width = 7, Height = 7, Fill = BGood, ToolTip = L.F("{0:dd/MM/yyyy} : CA {1}", p.date, p.ca) };
                Canvas.SetLeft(dot, X(p.date) - 3.5); Canvas.SetTop(dot, Y(p.ca) - 3.5); c.Children.Add(dot);
            }
            foreach (var (d, al) in new[] { (pts[0].date, 0.0), (pts[^1].date, 1.0) })
            {
                var t = T(d.ToString("dd/MM/yy"), 10, BDim); Canvas.SetLeft(t, X(d) - 20 - al * 10); Canvas.SetTop(t, H + 4); c.Children.Add(t);
            }
            sp.Children.Add(c);
            return Card(sp);
        }

        UIElement SimilarCard(PlayerRow r)
        {
            var sp = new StackPanel();
            var head = new DockPanel();
            var opts = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(opts, Dock.Right);
            ToggleButton Opt(string text, bool on, Action a)
            {
                var b = new ToggleButton { Content = text, IsChecked = on, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(4, 0, 0, 0) };
                b.Click += (_, _) => { a(); ShowPlayer(r); };
                return b;
            }
            opts.Children.Add(Opt(L.T("Plus jeunes"), _simYounger, () => _simYounger = !_simYounger));
            opts.Children.Add(Opt(L.T("Moins chers"), _simCheaper, () => _simCheaper = !_simCheaper));
            head.Children.Add(opts);
            head.Children.Add(T(L.T("Profils similaires (remplaçants possibles)"), 15, BText, true));
            sp.Children.Add(head);
            sp.Children.Add(T(L.T("Attributs les plus proches, sur au moins un de ses postes. Clic = fiche."), 11, BDim));

            var list = Game.Rows.Count == 0 ? new() : Features.Similar(r, _simYounger, _simCheaper, 15);
            if (list.Count == 0) sp.Children.Add(T(L.T("Aucun joueur trouvé avec ces filtres."), 13, BDim));
            var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var w in new[] { 34.0, 3.0, 2.2, 0.9, 1.4, 0.8, 0.8, 1.2 })
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = w > 10 ? new GridLength(w) : new GridLength(w, GridUnitType.Star) });
            int row = 0;
            void Cell(UIElement e, int col) { Grid.SetRow(e, row); Grid.SetColumn(e, col); grid.Children.Add(e); }
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
            string[] heads = { "", L.T("Joueur"), L.T("Club"), L.T("Âge"), L.T("Postes"), "CA", "PA", L.T("Valeur") };
            for (int i = 0; i < heads.Length; i++) Cell(T(heads[i], 11, BDim, true), i);
            foreach (var (o, sim) in list)
            {
                row++;
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
                var bg = new Border { Background = row % 2 == 0 ? BPanel2 : Brushes.Transparent, Cursor = Cursors.Hand, CornerRadius = new CornerRadius(4) };
                var oo = o; bg.MouseLeftButtonUp += (_, _) => OpenPlayer(oo);
                Grid.SetColumnSpan(bg, 8); Cell(bg, 0);
                var img = new System.Windows.Controls.Image { Source = Gfx.Face(o, 64) ?? Gfx.Logo(o.ClubPtr, 64), Width = 30, Height = 30, IsHitTestVisible = false };
                Cell(img, 0);
                var nm = new StackPanel { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
                nm.Children.Add(T(o.Name, 13, BText, true));
                nm.Children.Add(T(L.F("ressemblance {0} %", sim), 10, sim >= 85 ? BGood : sim >= 70 ? BOk : BMid));
                Cell(nm, 1);
                int age = Game.Age(o);
                var cells = new[] { o.Club == "" ? L.T("Libre") : o.Club, age < 0 ? "—" : age.ToString(), Db.BestPositions(o), o.CA.ToString(), o.PA.ToString(), Money(o.Value) };
                for (int i = 0; i < cells.Length; i++)
                {
                    var t = T(cells[i], 13, i == 4 ? BAccent : BText, i is 3 or 4);
                    t.IsHitTestVisible = false; t.TextTrimming = TextTrimming.CharacterEllipsis;
                    Cell(t, i + 2);
                }
            }
            sp.Children.Add(grid);
            return Card(sp);
        }

        // ============================================================ comparateur : radar

        UIElement RadarCard(PlayerRow a, PlayerRow b)
        {
            const double S = 420, R = 150;
            var c = new Canvas { Width = S, Height = S, HorizontalAlignment = HorizontalAlignment.Center };
            var axes = Features.RadarAxes;
            int n = axes.Length;
            Point P(int i, double v) { double ang = -Math.PI / 2 + 2 * Math.PI * i / n; return new Point(S / 2 + Math.Cos(ang) * R * v / 20, S / 2 + Math.Sin(ang) * R * v / 20); }
            foreach (double ring in new[] { 5.0, 10, 15, 20 })
            {
                var pg = new Polygon { Stroke = Br("#33FFFFFF"), StrokeThickness = 1 };
                for (int i = 0; i < n; i++) pg.Points.Add(P(i, ring));
                c.Children.Add(pg);
            }
            for (int i = 0; i < n; i++)
            {
                var o = P(i, 0); var e = P(i, 20);
                c.Children.Add(new Line { X1 = o.X, Y1 = o.Y, X2 = e.X, Y2 = e.Y, Stroke = Br("#26FFFFFF") });
                var lp = P(i, 23.5);
                var t = T(L.T(axes[i].label), 12, BDim, true);
                t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(t, lp.X - t.DesiredSize.Width / 2); Canvas.SetTop(t, lp.Y - 8);
                c.Children.Add(t);
            }
            void DrawShape(PlayerRow r, Brush stroke, Color fill)
            {
                if (r == null) return;
                var pg = new Polygon { Stroke = stroke, StrokeThickness = 2.5, Fill = new SolidColorBrush(fill) };
                for (int i = 0; i < n; i++) pg.Points.Add(P(i, Features.Axis(r, axes[i].keys)));
                c.Children.Add(pg);
            }
            DrawShape(a, BText, Color.FromArgb(0x40, 0xE6, 0xE9, 0xF2));
            DrawShape(b, BAccent, Color.FromArgb(0x40, 0xD8, 0x1B, 0x87));
            var sp = new StackPanel();
            var legend = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            if (a != null) legend.Children.Add(T($"{a.Name}    ", 13, BText, true));
            if (b != null) legend.Children.Add(T(b.Name, 13, BAccent, true));
            sp.Children.Add(legend);
            sp.Children.Add(c);
            sp.Children.Add(T(L.T("Moyenne des attributs de chaque famille (sur 20). « Fiabilité » = régularité, grands matchs, professionnalisme, pression."), 11, BDim));
            return Card(sp);
        }
    }
}
