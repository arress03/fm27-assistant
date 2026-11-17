using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FM27Scout
{
    /// <summary>Page Tactique : formation + façon de jouer → meilleur onze, rôles, consignes ; création dans FM.</summary>
    public partial class MainWindow
    {
        List<Tactic.Eval> _rank;
        string _rankKey;

        void ShowTactic()
        {
            var st = Settings.Cur;
            TacticTop.Children.Clear(); TacticPitch.Children.Clear(); TacticSide.Children.Clear();
            DrawPitch();
            var squad = Game.Rows.Where(r => r.Club == st.MyClub && st.MyClub != "" && Game.Shown(r) && !TacticFm.Unavailable.Contains(r.Uid)).ToList();
            if (squad.Count < 11)
            {
                TacticInfo.Text = st.MyClub == "" ? "Lance FM27 et charge ta carrière : ton club sera reconnu automatiquement." : "Pas assez de joueurs pour ce filtre (hommes / femmes).";
                return;
            }

            // classement des 90 configurations de FM pour cet effectif (recalculé si l'effectif ou la notation change)
            string key = $"{st.MyClub}|{squad.Count}|{squad.Sum(r => r.CA)}|{st.TacticMeta}|{TacticFm.Feet.Count}|{TacticFm.Unavailable.Count}";
            if (_rank == null || _rankKey != key) { _rank = Tactic.Rank(squad); _rankKey = key; }
            if (Tactic.Configs.All(c => c.Id != st.TacticConfig)) { st.TacticConfig = _rank[0].Config.Id; Settings.Save(); }
            var ev = _rank.First(e => e.Config.Id == st.TacticConfig);
            var cfg = ev.Config;
            var best = _rank[0];

            WrapPanel Row(string caption)
            {
                var line = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                line.Children.Add(new TextBlock { Text = caption, Style = (Style)FindResource("Caps"), Width = 96, VerticalAlignment = VerticalAlignment.Center });
                var wp = new WrapPanel();
                line.Children.Add(wp);
                TacticTop.Children.Add(line);
                return wp;
            }
            ToggleButton Chip(string text, bool on, string tip, Action click)
            {
                var b = new ToggleButton { Content = text, IsChecked = on, Margin = new Thickness(0, 0, 6, 6), ToolTip = tip };
                b.Click += (_, _) => click();
                return b;
            }

            var top3 = Row("CONSEILLÉES");
            foreach (var (e, i) in _rank.Take(3).Select((e, i) => (e, i)))
                top3.Children.Add(Chip($"{i + 1}. {e.Config.Style.Name} · {e.Config.With} · {e.Total:F0}", e.Config.Id == cfg.Id,
                    $"Avec ballon {e.Config.With}, sans ballon {e.Config.Without}", () => { st.TacticConfig = e.Config.Id; Settings.Save(); ShowTactic(); }));
            var styles = Row("STYLE");
            foreach (var sty in Tactic.Styles)
            {
                var bestOf = _rank.First(e => e.Config.Style == sty);
                styles.Children.Add(Chip($"{sty.Name} · {bestOf.Total:F0}", sty == cfg.Style, sty.Pitch, () => { st.TacticConfig = bestOf.Config.Id; Settings.Save(); ShowTactic(); }));
            }
            var forms = Row("FORMATION");
            foreach (var e in _rank.Where(e => e.Config.Style == cfg.Style).OrderBy(e => e.Config.N))
                forms.Children.Add(Chip($"{e.Config.With} / {e.Config.Without} · {e.Total:F0}", e.Config.Id == cfg.Id, "Avec le ballon / sans le ballon",
                    () => { st.TacticConfig = e.Config.Id; Settings.Save(); ShowTactic(); }));
            var mode = Row("NOTATION");
            mode.Children.Add(Chip("Meta (vitesse d'abord)", st.TacticMeta, "Pondère fort la vitesse, l'accélération et l'activité : les attributs qui font gagner dans le moteur de match selon les tests de la communauté.", () => { st.TacticMeta = true; Settings.Save(); ShowTactic(); }));
            mode.Children.Add(Chip("Classique (rôle)", !st.TacticMeta, "Note chaque joueur uniquement sur les attributs de son rôle.", () => { st.TacticMeta = false; Settings.Save(); ShowTactic(); }));

            TacticInfo.Text = $"{st.MyClub} · {cfg.Style.Name} · {cfg.With} avec ballon, {cfg.Without} sans · note {ev.Total:F0}/100"
                + (cfg.Id == best.Config.Id ? " · meilleure configuration pour ton effectif" : $" · la meilleure : {best.Config.Style.Name} {best.Config.With} ({best.Total:F0})")
                + (TacticFm.Feet.Count > 0 ? " · pieds pris en compte" : "") + (TacticFm.Unavailable.Count > 0 ? $" · {TacticFm.Unavailable.Count} blessé(s) écarté(s)" : "");

            var eleven = ev.Eleven;
            var backups = ev.Backups;
            var style = cfg.Style;
            const double W = 700, H = 900;
            foreach (var (s, r, score) in eleven)
            {
                var sp = new StackPanel { Width = 150 };
                var face = r == null ? null : Gfx.Face(r, 120);
                var top = new Border
                {
                    Width = 62, Height = 62, CornerRadius = new CornerRadius(31), HorizontalAlignment = HorizontalAlignment.Center, ClipToBounds = true,
                    Background = face != null ? new ImageBrush(face) { Stretch = Stretch.UniformToFill } : BPanel2,
                    BorderBrush = score >= 75 ? BGood : score >= 60 ? BOk : score >= 45 ? BMid : BBad, BorderThickness = new Thickness(2.5),
                };
                if (face == null) top.Child = new TextBlock { Text = Db.PositionFr[s.Pos], Foreground = BText, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                sp.Children.Add(top);
                var name = new TextBlock { Text = r?.Name ?? "—", Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 14.5, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                sp.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(200, 8, 12, 18)), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 2, 6, 3), Margin = new Thickness(0, 4, 0, 0), Child = name });
                string foot = r != null && TacticFm.Feet.TryGetValue(r.Uid, out var f) ? (f.left >= 15 && f.right >= 15 ? " · 2 pieds" : f.left > f.right ? " · gaucher" : " · droitier") : "";
                sp.Children.Add(new TextBlock { Text = $"{s.Role} · {score}{foot}", Foreground = new SolidColorBrush(Color.FromArgb(220, 230, 235, 242)), FontSize = 12, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 2, 0, 0) });
                if (r != null) { sp.Cursor = Cursors.Hand; var rr = r; sp.MouseLeftButtonUp += (_, _) => OpenPlayer(rr); }
                Canvas.SetLeft(sp, s.X / 100 * W - 75); Canvas.SetTop(sp, s.Y / 100 * H - 40);
                TacticPitch.Children.Add(sp);
            }

            // ---- colonne de droite : style, notes, efficacité, FM
            var head = new StackPanel();
            head.Children.Add(T(style.Name, 18, BText, true));
            head.Children.Add(new TextBlock { Text = style.Pitch, TextWrapping = TextWrapping.Wrap, Foreground = BDim, FontSize = 13, Margin = new Thickness(0, 4, 0, 10) });
            foreach (var ins in style.Team.Prepend(new Tactic.Instruction("Formations", $"{cfg.With} avec le ballon, {cfg.Without} sans", "Rôles et consignes d'équipe : ceux que FM associe à ce style.")))
            {
                var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                var l = T(ins.Label, 12.5, BDim); l.VerticalAlignment = VerticalAlignment.Top;
                g.Children.Add(l);
                var v = new StackPanel(); Grid.SetColumn(v, 1);
                v.Children.Add(new TextBlock { Text = ins.Value, TextWrapping = TextWrapping.Wrap, Foreground = BText, FontWeight = FontWeights.SemiBold, FontSize = 13.5 });
                if (ins.Why != "") v.Children.Add(new TextBlock { Text = ins.Why, TextWrapping = TextWrapping.Wrap, Foreground = BFaint, FontSize = 12 });
                g.Children.Add(v);
                head.Children.Add(g);
            }
            head.Children.Add(new TextBlock { Text = "NOTE DE LA CONFIGURATION", Style = (Style)FindResource("Caps"), Margin = new Thickness(0, 8, 0, 6) });
            foreach (var (k, v, w) in new[] { ("Onze avec ballon", ev.Players, "50 %"), ("Onze sans ballon", ev.Defence, "25 %"), ("Profil du style", ev.Fit, "25 %") })
            {
                var g = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                var val = T($"{v:F0}", 13, v >= 70 ? BGood : v >= 58 ? BOk : BMid, true); DockPanel.SetDock(val, Dock.Right); g.Children.Add(val);
                g.Children.Add(T($"{k}  ({w})", 12.5, BDim));
                head.Children.Add(g);
            }
            var fit = Tactic.Fit(style, eleven.Select(x => x.r));
            foreach (var f2 in fit)
                head.Children.Add(new TextBlock { Text = f2[2..], Foreground = f2[0] == '+' ? BGood : BMid, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            head.Children.Add(new TextBlock { Text = "EFFICACITÉ TACTIQUE (FM27)", Style = (Style)FindResource("Caps"), Margin = new Thickness(0, 12, 0, 6) });
            foreach (var (k, v) in new[]
            {
                ("Compréhension", "Monte en gardant la même tactique et avec l'entraînement d'équipe : évite de changer de système chaque semaine."),
                ("Capacités", "Les bons joueurs aux bons postes : c'est ce que fait « Placer le meilleur onze »."),
                ("Adhésion", "Le moral et la relation avec l'entraîneur : vois la page Vestiaire."),
                ("Prochain match", "La préparation de l'adversaire à l'entraînement : réglée par l'onglet Appliquer dans le jeu."),
            })
            {
                var g = new Grid { Margin = new Thickness(0, 0, 0, 5) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                var l = T(k, 12.5, BDim); l.VerticalAlignment = VerticalAlignment.Top; g.Children.Add(l);
                var t = new TextBlock { Text = v, TextWrapping = TextWrapping.Wrap, Foreground = BFaint, FontSize = 12 }; Grid.SetColumn(t, 1); g.Children.Add(t);
                head.Children.Add(g);
            }
            head.Children.Add(TacticApplyPanel(cfg));
            TacticSide.Children.Add(Card(head));

            // ---- coups de pied arrêtés
            var cpa = new StackPanel();
            cpa.Children.Add(T("Coups de pied arrêtés", 15, BText, true));
            cpa.Children.Add(new TextBlock { Text = "Calculé sur les attributs et les pieds de ton onze. « Appliquer dans FM » règle les tireurs de corners et de coups francs directs, la technique et la zone visée des corners ; le reste se règle dans FM, onglet Tactiques → Coups de pied arrêtés.", TextWrapping = TextWrapping.Wrap, Foreground = BFaint, FontSize = 12, Margin = new Thickness(0, 2, 0, 6) });
            if (_bridgeState == "prêt")
            {
                var spBtn = Btn(_spBusy ? "En cours…" : "Appliquer dans FM", async () => await ApplySetPieces(eleven.Where(x => x.r != null).Select(x => x.r).ToList()), true);
                spBtn.IsEnabled = !_spBusy; spBtn.HorizontalAlignment = HorizontalAlignment.Left; spBtn.Margin = new Thickness(0, 0, 0, 6);
                cpa.Children.Add(spBtn);
            }
            if (_spMsg != "") cpa.Children.Add(new TextBlock { Text = _spMsg, TextWrapping = TextWrapping.Wrap, Foreground = _spBad ? BMid : BGood, FontSize = 12.5, Margin = new Thickness(0, 0, 0, 6) });
            foreach (var sec in SetPieces.Build(eleven.Where(x => x.r != null).Select(x => x.r).ToList(), squad))
            {
                cpa.Children.Add(new TextBlock { Text = sec.Title.ToUpper(), Style = (Style)FindResource("Caps"), Margin = new Thickness(0, 12, 0, 4) });
                cpa.Children.Add(new TextBlock { Text = sec.Strategy, TextWrapping = TextWrapping.Wrap, Foreground = BText, FontWeight = FontWeights.SemiBold, FontSize = 13.5 });
                cpa.Children.Add(new TextBlock { Text = sec.Why, TextWrapping = TextWrapping.Wrap, Foreground = BDim, FontSize = 12, Margin = new Thickness(0, 1, 0, 4) });
                foreach (var (label, picks) in sec.Lists.Where(l => l.picks.Count > 0))
                {
                    var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                    g.ColumnDefinitions.Add(new ColumnDefinition());
                    var l = T(label, 12.5, BDim); l.VerticalAlignment = VerticalAlignment.Top; l.TextWrapping = TextWrapping.Wrap; g.Children.Add(l);
                    var names = new StackPanel(); Grid.SetColumn(names, 1);
                    int k = 0;
                    foreach (var pk in picks)
                    {
                        var line = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Cursor = Cursors.Hand };
                        line.Inlines.Add(new System.Windows.Documents.Run((picks.Count > 2 ? $"{++k}. " : "") + pk.Player.Name) { Foreground = BText });
                        line.Inlines.Add(new System.Windows.Documents.Run("  " + pk.Why) { Foreground = BFaint, FontSize = 11.5 });
                        var rr = pk.Player; line.MouseLeftButtonUp += (_, _) => OpenPlayer(rr);
                        names.Children.Add(line);
                    }
                    g.Children.Add(names);
                    cpa.Children.Add(g);
                }
            }
            TacticSide.Children.Add(Card(cpa));

            var subsBox = new StackPanel();
            subsBox.Children.Add(T("Doublures par poste", 15, BText, true));
            foreach (var (s, subs) in backups)
            {
                var box = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
                box.Children.Add(T($"{Db.PositionFr[s.Pos]} · {s.Role}", 12, BDim, true));
                if (subs.Count == 0) box.Children.Add(T("Personne", 13, BFaint));
                foreach (var (r, sc) in subs)
                {
                    var line = new DockPanel { Margin = new Thickness(0, 3, 0, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent };
                    var v = T(sc.ToString(), 13, sc >= 75 ? BGood : sc >= 60 ? BOk : sc >= 45 ? BMid : BBad, true);
                    DockPanel.SetDock(v, Dock.Right); line.Children.Add(v);
                    int age = Game.Age(r);
                    line.Children.Add(T($"{r.Name}  ·  {(age < 0 ? "" : age + " ans, ")}CA {r.CA}", 13));
                    var rr = r; line.MouseLeftButtonUp += (_, _) => OpenPlayer(rr);
                    box.Children.Add(line);
                }
                subsBox.Children.Add(box);
            }
            TacticSide.Children.Add(Card(subsBox));
        }

        void DrawPitch()
        {
            const double W = 700, H = 900;
            var grass = new LinearGradientBrush(Color.FromRgb(0x17, 0x4A, 0x2F), Color.FromRgb(0x10, 0x36, 0x23), 90);
            TacticPitch.Children.Add(new Rectangle { Width = W, Height = H, Fill = grass, RadiusX = 14, RadiusY = 14 });
            for (int i = 0; i < 9; i += 2)
            {
                var band = new Rectangle { Width = W - 40, Height = (H - 40) / 9.0, Fill = new SolidColorBrush(Color.FromArgb(14, 255, 255, 255)) };
                Canvas.SetLeft(band, 20); Canvas.SetTop(band, 20 + i * (H - 40) / 9.0); TacticPitch.Children.Add(band);
            }
            var line = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
            void Box(double x, double y, double w, double h)
            {
                var r = new Rectangle { Width = w, Height = h, Stroke = line, StrokeThickness = 2 };
                Canvas.SetLeft(r, x); Canvas.SetTop(r, y); TacticPitch.Children.Add(r);
            }
            Box(20, 20, W - 40, H - 40); Box(20, H / 2, W - 40, 0.5);
            Box(W / 2 - 160, H - 150, 320, 130); Box(W / 2 - 160, 20, 320, 130);
            Box(W / 2 - 70, H - 70, 140, 50); Box(W / 2 - 70, 20, 140, 50);
            var circle = new Ellipse { Width = 150, Height = 150, Stroke = line, StrokeThickness = 2 };
            Canvas.SetLeft(circle, W / 2 - 75); Canvas.SetTop(circle, H / 2 - 75); TacticPitch.Children.Add(circle);
        }

        // ============================================================ appliquer dans FM (FM27 Bridge)

        bool _tacBusy;
        string _tacMsg = "";
        bool _tacMsgBad, _tacRetry, _feetLoading;

        UIElement TacticApplyPanel(Tactic.Config cfg)
        {
            if (_bridgeState != "prêt" && !_tacRetry)
            {
                // vérifie le module directement (indépendant des autres pages), puis redessine
                _tacRetry = true;
                Task.Run(() =>
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    string r;
                    try { r = Bridge.Status() ?? "absent"; } catch (Exception e) { r = "absent"; Log.Warn("Tactique, état du module : " + e.Message); }
                    Log.Info($"Tactique : module {r} ({sw.ElapsedMilliseconds} ms)");
                    return r;
                })
                    .ContinueWith(t => Dispatcher.BeginInvoke(new Action(() =>
                    {
                        bool changed = t.Result != _bridgeState;
                        _bridgeState = t.Result;
                        if (changed) ShowTactic();
                        _tacRetry = t.Result == "prêt";   // si pas prêt : nouvel essai au prochain affichage
                    })));
            }
            if (_bridgeState == "prêt" && TacticFm.Feet.Count == 0 && !_feetLoading)
            {
                _feetLoading = true;
                Task.Run(() => { try { TacticFm.PersonsOf(Bridge.Ctx()); } catch (Exception e) { Log.Warn("Pieds : " + e.Message); } })
                    .ContinueWith(_ => Dispatcher.BeginInvoke(new Action(ShowTactic)));
            }
            var box = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            box.Children.Add(new TextBlock { Text = "DANS FM27", Style = (Style)FindResource("Caps"), Margin = new Thickness(0, 0, 0, 6) });
            if (_bridgeState != "prêt")
            {
                box.Children.Add(new TextBlock { Text = _bridgeState == null ? "Connexion à FM27…" : _bridgeState == "attente" ? "FM27 charge encore : ouvre ta carrière." : "Lance FM27 (avec FM27 Bridge) et charge ta carrière pour appliquer la tactique dans le jeu.", TextWrapping = TextWrapping.Wrap, Foreground = BDim, FontSize = 12.5 });
                return box;
            }
            var row = new WrapPanel();
            var create = Btn(_tacBusy ? "En cours…" : "Créer cette tactique dans FM", async () => await CreateFmTactic(cfg), true);
            create.IsEnabled = !_tacBusy; create.Margin = new Thickness(0, 0, 8, 8);
            create.ToolTip = "Nouvelle tactique dans FM (tes autres tactiques ne sont pas touchées) : style, formations, rôles et consignes de FM, puis ton meilleur onze.";
            row.Children.Add(create);
            var over = Btn("Appliquer à la tactique active", async () =>
            {
                if (MessageBox.Show(this, $"Remplacer le style, les formations, les rôles et les consignes de la tactique active de FM par « {cfg.Style.Name} — {cfg.With} / {cfg.Without} », puis y placer ton meilleur onze ?",
                    "FM27 Assistant", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                await ApplyToActive(cfg);
            });
            over.IsEnabled = !_tacBusy; over.Margin = new Thickness(0, 0, 8, 8);
            over.ToolTip = "Remplace la configuration de la tactique active de FM (une confirmation est demandée).";
            row.Children.Add(over);
            var place = Btn("Placer seulement le meilleur onze", async () => await PlaceEleven());
            place.IsEnabled = !_tacBusy; place.Margin = new Thickness(0, 0, 8, 8);
            place.ToolTip = "Garde la tactique active de FM telle quelle et met les meilleurs joueurs à chaque poste.";
            row.Children.Add(place);
            box.Children.Add(row);
            box.Children.Add(new TextBlock { Text = "Tout reste modifiable dans FM : rôles, consignes individuelles, mentalité.", TextWrapping = TextWrapping.Wrap, Foreground = BFaint, FontSize = 12 });
            if (_tacMsg != "") box.Children.Add(new TextBlock { Text = _tacMsg, TextWrapping = TextWrapping.Wrap, Foreground = _tacMsgBad ? BMid : BGood, FontSize = 12.5, Margin = new Thickness(0, 8, 0, 0) });
            return box;
        }

        bool _spBusy, _spBad;
        string _spMsg = "";

        async Task ApplySetPieces(List<PlayerRow> xi)
        {
            _spBusy = true; _spMsg = ""; ShowTactic();
            try
            {
                var done = await Task.Run(() => { var c = Bridge.Ctx(); return SetPiecesFm.Apply(c, xi, TacticFm.PersonsOf(c)); });
                _spMsg = "Réglé dans FM : " + string.Join(" ; ", done) + ".";
                _spBad = false;
                Log.Info("Coups de pied arrêtés appliqués : " + string.Join(" ; ", done));
            }
            catch (Exception e) { _spMsg = "Impossible d'appliquer les coups de pied arrêtés : " + e.Message; _spBad = true; }
            _spBusy = false; ShowTactic();
        }

        async Task ApplyToActive(Tactic.Config cfg)
        {
            _tacBusy = true; _tacMsg = ""; ShowTactic();
            try
            {
                await Task.Run(() => { var c = Bridge.Ctx(); TacticFm.ApplyConfig(c, cfg); System.Threading.Thread.Sleep(1500); });
                Log.Info($"Configuration appliquée à la tactique active : {cfg.Label} ({cfg.Style.Name})");
                await PlaceEleven("Configuration appliquée.");
                return;
            }
            catch (Exception e) { _tacMsg = "Impossible d'appliquer la configuration : " + e.Message; _tacMsgBad = true; }
            _tacBusy = false; ShowTactic();
        }

        async Task CreateFmTactic(Tactic.Config cfg)
        {
            _tacBusy = true; _tacMsg = ""; ShowTactic();
            try
            {
                string name = await Task.Run(() => TacticFm.CreateFull(Bridge.Ctx(), cfg));
                Log.Info($"Tactique créée dans FM : {name}");
                await PlaceEleven("Tactique créée dans FM.");
                return;
            }
            catch (Exception e) { _tacMsg = "Impossible de créer la tactique : " + e.Message; _tacMsgBad = true; }
            _tacBusy = false; ShowTactic();
        }

        async Task PlaceEleven(string created = null)
        {
            var st = Settings.Cur;
            var squad = Game.Rows.Where(r => r.Club == st.MyClub && st.MyClub != "" && Game.Shown(r)).ToList();
            _tacBusy = true; _tacMsg = ""; ShowTactic();
            try
            {
                string roleKey = Tactic.ConfigOf(st.TacticConfig).Style.RoleKey;
                var (name, plan, n, errors) = await Task.Run(() =>
                {
                    var c = Bridge.Ctx();
                    var persons = TacticFm.PersonsOf(c);
                    var state = TacticFm.Read(c, roleKey);
                    if (state.Slots.Count < 11) throw new Exception("tactique active introuvable (ouvre une fois l'écran Tactiques dans FM)");
                    var p = TacticFm.Plan(state, squad, persons);
                    var err = new List<string>();
                    int sent = TacticFm.Apply(c, state, p, persons, err, roleKey);
                    var g = TacticFm.Gauges(c);
                    return (state.Name + (g.intensity != "" ? $" · {g.intensity}, {g.complexity.ToLower()}" : ""), p, sent, err);
                });
                var moved = plan.Where(x => x.Changed).Select(x => $"{x.Player.Name} ({Db.PositionFr[x.Slot.Pos]})").ToList();
                _tacMsg = (created != null ? created + " " : "") + (n == 0 && errors.Count == 0
                    ? $"« {name} » : ton meilleur onze est déjà en place."
                    : $"« {name} » : {n} joueur(s) placé(s)" + (moved.Count > 0 ? " : " + string.Join(", ", moved) : "") + (errors.Count > 0 ? $". {errors.Count} problème(s) : {errors[0]}" : "."));
                _tacMsgBad = errors.Count > 0;
                Log.Info($"Tactique : onze placé dans FM ({n} changement(s), {errors.Count} erreur(s))");
            }
            catch (Exception e) { _tacMsg = "Impossible de placer l'onze : " + e.Message; _tacMsgBad = true; }
            _tacBusy = false; ShowTactic();
        }
    }
}
