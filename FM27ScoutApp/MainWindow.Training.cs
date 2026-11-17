using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace FM27Scout
{
    /// <summary>Onglet Entraînement : planning 4 semaines, plans individuels, tutorat ; carte sur la fiche joueur.</summary>
    public partial class MainWindow
    {
        string _trainTab = "planning";


        HashSet<DateTime> MatchDays(DateTime start, out bool isDefault)
        {
            var set = Settings.Cur.MatchDays.Select(s => DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.Date : DateTime.MinValue)
                .Where(d => d >= start && d < start.AddDays(28)).ToHashSet();
            isDefault = set.Count == 0;
            if (isDefault)   // rien de placé : un match chaque samedi
                for (int i = 0; i < 28; i++) if (start.AddDays(i).DayOfWeek == DayOfWeek.Saturday) set.Add(start.AddDays(i));
            return set;
        }

        void ToggleMatch(DateTime d, HashSet<DateTime> current, bool wasDefault)
        {
            // la première fois, on part des samedis proposés pour que le clic garde les autres
            if (wasDefault) foreach (var m in current) Settings.Cur.MatchDays.Add(m.ToString("yyyy-MM-dd"));
            string k = d.ToString("yyyy-MM-dd");
            if (!Settings.Cur.MatchDays.Remove(k)) Settings.Cur.MatchDays.Add(k);
            Settings.Save();
            ShowTraining();
        }

        void ShowTraining()
        {
            var set = Training.Set;
            TrainChips.Children.Clear();
            foreach (var (key, label) in new[] { ("planning", "Plan 4 semaines"), ("individuel", "Entraînement individuel"), ("tutorat", "Encadrement"), ("appliquer", "Appliquer dans le jeu") })
            {
                var b = new ToggleButton { Content = label, IsChecked = _trainTab == key };
                var k = key;
                b.Click += (_, _) => { _trainTab = k; ShowTraining(); };
                TrainChips.Children.Add(b);
            }
            TrainChips.Children.Add(new TextBlock { Text = "      Mode", Foreground = BDim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            foreach (var (expert, label) in new[] { (false, "Auto"), (true, "Expert") })
            {
                var b = new ToggleButton { Content = label, IsChecked = set.Expert == expert, ToolTip = expert ? "Tu choisis les domaines, le style et les intensités" : "L'app décide de tout" };
                var ex = expert;
                b.Click += (_, _) => { set.Expert = ex; Training.SaveSet(); ShowTraining(); };
                TrainChips.Children.Add(b);
            }

            TrainBox.Children.Clear();
            TrainScroll.Visibility = _trainTab == "individuel" ? Visibility.Collapsed : Visibility.Visible;
            TrainGrid.Visibility = _trainTab == "individuel" ? Visibility.Visible : Visibility.Collapsed;

            var squad = Game.Rows.Where(r => r.Club == Settings.Cur.MyClub && Settings.Cur.MyClub != "" && Game.Shown(r)).ToList();
            if (squad.Count < 11) { TrainInfo.Text = Settings.Cur.MyClub == "" ? "Choisis ton club dans « Mon club »." : "Pas assez de joueurs pour ce filtre."; TrainGrid.ItemsSource = null; return; }
            if (!Game.HasDate) { TrainInfo.Text = "Date de jeu inconnue : saisis-la en haut pour construire le plan."; return; }

            var slots = Tactic.CurrentSlots();
            var eleven = Features.BestEleven(squad, slots).eleven.Where(x => x.r != null).Select(x => x.r).ToList();
            int squadRisk = (int)eleven.Average(r => Training.FatigueRisk(r));
            TrainInfo.Text = $"{Settings.Cur.MyClub} · onze type en {Tactic.ConfigOf(Settings.Cur.TacticConfig).With} · " + (set.Expert ? "mode expert : tes réglages priment" : "mode auto : l'app décide de tout") +
                (AutoTrain.Last != null ? $" · état réel lu dans FM à {AutoTrain.Last.At:HH:mm}" : " · fatigue estimée (onglet « Appliquer dans le jeu » pour lire l'état réel)");

            if (_trainTab == "individuel") { ShowTrainGrid(squad); return; }
            if (_trainTab == "tutorat") { ShowMentoring(squad); return; }

            var needs = Training.TeamNeeds(eleven);
            var start = Game.Date.Date;
            var matches = MatchDays(start, out bool isDefault);
            if (_trainTab == "appliquer") { ShowApply(squad, Training.Plan(start, matches, needs, squadRisk)[0]); return; }

            // --- mode expert : réglages
            if (set.Expert)
            {
                var exBox = new StackPanel();
                exBox.Children.Add(T("Réglages experts", 15, BText, true));
                exBox.Children.Add(T("Domaines : clic pour passer de Auto à Imposé puis Exclu. Style : jusqu'où on pousse les joueurs.", 11, BDim));
                var areas = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
                foreach (var a in Training.Areas.Where(a => a != "Gardien"))
                {
                    string m = set.AreaMode.GetValueOrDefault(a, "auto");
                    var b = new Button
                    {
                        Content = m == "force" ? $"{a} · prioritaire" : m == "exclu" ? $"{a} · exclu" : a,
                        Foreground = m == "force" ? BGood : m == "exclu" ? BBad : BText,
                    };
                    var aa = a;
                    b.Click += (_, _) => { set.AreaMode[aa] = m == "auto" ? "force" : m == "force" ? "exclu" : "auto"; Training.SaveSet(); ShowTraining(); };
                    areas.Children.Add(b);
                }
                exBox.Children.Add(areas);
                var styles = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                styles.Children.Add(T("Style   ", 13, BDim));
                foreach (var (k, l) in new[] { ("prudent", "Prudent (moins de blessures)"), ("normal", "Équilibré"), ("pousse", "Poussé (progression max)") })
                {
                    var b = new ToggleButton { Content = l, IsChecked = set.Style == k };
                    var kk = k;
                    b.Click += (_, _) => { set.Style = kk; Training.SaveSet(); ShowTraining(); };
                    styles.Children.Add(b);
                }
                exBox.Children.Add(styles);
                exBox.Children.Add(T("Intensité d'un joueur précis : onglet « Entraînement individuel », clic droit sur sa ligne.", 11, BDim));
                TrainBox.Children.Add(Card(exBox));
            }

            // --- besoins de l'équipe
            var needBox = new StackPanel();
            needBox.Children.Add(T("Ce qu'il faut travailler (onze type comparé aux joueurs de même niveau dans toute la base)", 15, BText, true));
            var wrap = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var (axis, team, refv, gap) in needs)
            {
                var sp = new StackPanel();
                sp.Children.Add(T(axis, 11, BDim));
                sp.Children.Add(T($"{team:F1} / 20", 17, gap >= 0.6 ? BBad : gap >= 0.2 ? BMid : BGood, true));
                sp.Children.Add(T(gap >= 0 ? $"−{gap:F1} vs niveau" : $"+{-gap:F1} vs niveau", 11, BDim));
                wrap.Children.Add(new Border { Background = BPanel2, CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 8), Child = sp, MinWidth = 110 });
            }
            needBox.Children.Add(wrap);
            var risky = squad.Select(r => (r, risk: Training.FatigueRisk(r))).Where(x => x.risk >= Training.RiskLimit + 10).OrderByDescending(x => x.risk).ToList();
            needBox.Children.Add(T($"Fatigue estimée de l'effectif : {Training.RiskLabel(squadRisk)} ({squadRisk}/100)" +
                (risky.Count > 0 ? $" · à ménager : {string.Join(", ", risky.Take(6).Select(x => x.r.Name))}" : ""), 12, squadRisk >= Training.RiskLimit ? BMid : BDim));
            TrainBox.Children.Add(Card(needBox));

            // --- les 4 semaines
            var weeks = Training.Plan(start, matches, needs, squadRisk);
            var plan = new StackPanel();
            plan.Children.Add(T($"Plan du {start:dd/MM} au {start.AddDays(27):dd/MM}", 15, BText, true));
            plan.Children.Add(T("À régler dans FM27 : Entraînement, onglets « Affectations des entraîneurs » (domaines à charger) et « Intensité entraînement » (tableau par état de forme).", 11, BDim));
            var wgrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 8, 0, 0) };
            var fr = new CultureInfo("fr-FR");
            int wi = 0;
            foreach (var w in weeks)
            {
                wi++;
                var sp = new StackPanel();
                sp.Children.Add(T($"Semaine {wi} · {w.Start:dd/MM} → {w.Start.AddDays(6):dd/MM}", 14, BText, true));
                sp.Children.Add(T(w.Matches.Count == 0 ? "Pas de match" : "Match " + string.Join(", ", w.Matches.Select(m => m.ToString("ddd dd/MM", fr))), 12, w.Matches.Count >= 2 ? BMid : BAccent, true));
                sp.Children.Add(T(w.Note, 11, BDim));
                sp.Children.Add(new Border { Height = 8 });
                sp.Children.Add(T("Domaines à mettre en avant", 11, BDim, true));
                foreach (var (area, why) in w.Focus)
                {
                    sp.Children.Add(T(area, 13, BGood, true));
                    sp.Children.Add(T($"   {why}", 11, BDim));
                }
                sp.Children.Add(new Border { Height = 8 });
                sp.Children.Add(T("Intensité par état de forme", 11, BDim, true));
                for (int i = 0; i < 5; i++)
                {
                    var row = new DockPanel();
                    string val = w.TeamIntensity[i];
                    var v = T(val, 12, val == Training.Normal ? BText : val == Training.Half ? BOk : BMid, true);
                    DockPanel.SetDock(v, Dock.Right); row.Children.Add(v);
                    row.Children.Add(T(Training.FormStates[i], 12, BDim));
                    sp.Children.Add(row);
                }
                wgrid.Children.Add(new Border { Background = BPanel2, CornerRadius = new CornerRadius(6), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 8, 8), Child = sp });
            }
            plan.Children.Add(wgrid);
            TrainBox.Children.Add(Card(plan));

            // --- matchs (cliquables)
            var cal = new StackPanel();
            cal.Children.Add(T("Tes matchs", 15, BText, true));
            cal.Children.Add(T(isDefault ? "Aucun match placé : un match par samedi est supposé. Clique sur un jour pour placer ou retirer un match (en attendant la lecture automatique du calendrier)."
                                         : "Clique sur un jour pour placer ou retirer un match.", 11, isDefault ? BMid : BDim));
            var grid = new UniformGrid { Columns = 7, Margin = new Thickness(0, 8, 0, 0) };
            int offset = ((int)start.DayOfWeek + 6) % 7;
            foreach (var dn in new[] { "Lun", "Mar", "Mer", "Jeu", "Ven", "Sam", "Dim" }) grid.Children.Add(T(dn, 11, BDim, true));
            for (int i = 0; i < offset; i++) grid.Children.Add(new Border());
            for (int i = 0; i < 28; i++)
            {
                var d = start.AddDays(i);
                bool m = matches.Contains(d);
                var t = new TextBlock { Text = d.ToString("dd/MM"), Foreground = m ? Brushes.White : BText, FontWeight = m ? FontWeights.SemiBold : FontWeights.Normal, HorizontalAlignment = HorizontalAlignment.Center };
                var cell = new Border { Background = m ? BAccent : BPanel2, CornerRadius = new CornerRadius(5), Padding = new Thickness(4, 6, 4, 6), Margin = new Thickness(0, 0, 4, 4), Child = t, Cursor = Cursors.Hand };
                var dd = d;
                cell.MouseLeftButtonUp += (_, _) => ToggleMatch(dd, matches, isDefault);
                grid.Children.Add(cell);
            }
            cal.Children.Add(grid);
            TrainBox.Children.Add(Card(cal));
        }

        void ShowTrainGrid(List<PlayerRow> squad)
        {
            var plans = squad.ToDictionary(r => r, r => Training.Individual(r));
            var extras = new List<Extra>
            {
                new("Intensite", "Intensité", typeof(string), r => plans[r].Load, 170, true),
                new("Focus", "Priorité individuelle", typeof(string), r => plans[r].Focus, 150, true),
                new("Cibles", "Attributs à travailler", typeof(string), r => plans[r].Targets, 240),
                new("Role", "Profil de rôle", typeof(string), r => plans[r].Role, 140),
                new("Fatigue", "Fatigue estimée", typeof(int), r => plans[r].Risk, 100),
                new("Remarque", "Remarque", typeof(string), r => plans[r].Note, 360),
            };
            // clic droit : copier le nom + (mode expert) forcer l'intensité du joueur
            var menu = new ContextMenu();
            var copy = new MenuItem { Header = "Copier le nom" };
            copy.Click += (_, _) => { if (TrainGrid.SelectedItem is System.Data.DataRowView v) CopyName(v["Nom"] as string, null); };
            menu.Items.Add(copy);
            if (Training.Set.Expert)
            {
                menu.Items.Add(new Separator());
                foreach (var opt in new[] { "Auto", Training.NoPitch, Training.Half, Training.Normal })
                {
                    var mi = new MenuItem { Header = opt == "Auto" ? "Intensité : auto" : "Intensité : " + opt };
                    var o = opt;
                    mi.Click += (_, _) =>
                    {
                        if (TrainGrid.SelectedItem is not System.Data.DataRowView v || v["Id"] is not uint id || !Game.ById.TryGetValue(id, out var r)) return;
                        string k = Features.Key(r).ToString();
                        if (o == "Auto") Training.Set.Overrides.Remove(k); else Training.Set.Overrides[k] = o;
                        Training.SaveSet(); ShowTraining();
                    };
                    menu.Items.Add(mi);
                }
            }
            TrainGrid.ContextMenu = menu;
            Bind(TrainGrid, MakeTable(squad.OrderByDescending(r => plans[r].Priority), extras), extras);
        }

        void ShowMentoring(List<PlayerRow> squad)
        {
            var pairs = Training.Mentoring(squad);
            var box = new StackPanel();
            box.Children.Add(T("Encadrement : jeunes à encadrer", 15, BText, true));
            box.Children.Add(T("Jeunes (≤ 21 ans) à fort potentiel mais à la personnalité fragile (professionnalisme ou détermination ≤ 12), et les cadres les plus professionnels pour les encadrer, du même secteur de jeu si possible. Dans FM27 : Entraînement, puis Encadrement, puis Créer nouveau groupe d'encadrement.", 11, BDim));
            if (pairs.Count == 0) box.Children.Add(T("Aucun jeune à encadrer en priorité dans cet effectif.", 13, BGood));
            foreach (var (y, mentors) in pairs)
            {
                var line = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
                var nm = T($"{y.Name}  ({Game.Age(y)} ans, CA {y.CA} → PA {y.PA}) · pro {Db.Stat(y, "AttributeProfessionalism")}, détermination {Db.Stat(y, "AttributeDetermination")}", 13, BText, true);
                nm.Cursor = Cursors.Hand; var yy = y; nm.MouseLeftButtonUp += (_, _) => OpenPlayer(yy);
                line.Children.Add(nm);
                line.Children.Add(T(mentors.Count == 0 ? "   Pas de tuteur assez professionnel dans l'effectif."
                    : "   Tuteurs : " + string.Join("  ·  ", mentors.Select(m => $"{m.Name} (pro {Db.Stat(m, "AttributeProfessionalism")}, dét. {Db.Stat(m, "AttributeDetermination")})")), 12, mentors.Count == 0 ? BMid : BGood));
                box.Children.Add(line);
            }
            TrainBox.Children.Add(Card(box));
        }

        UIElement TrainingCard(PlayerRow r)
        {
            var p = Training.Individual(r);
            var sp = new StackPanel();
            sp.Children.Add(T("Entraînement conseillé", 15, BText, true));
            var wrap = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            void Tile(string label, string value, Brush color = null)
            {
                var t = new StackPanel();
                t.Children.Add(T(label, 11, BDim));
                t.Children.Add(T(value, 15, color ?? BText, true));
                wrap.Children.Add(new Border { Background = BPanel2, CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 8), Child = t });
            }
            Tile("Profil de rôle", p.Role, BAccent);
            Tile("Priorité individuelle", p.Focus, BAccent);
            Tile("Intensité", p.Load, p.Load == Training.Normal ? BText : p.Load == Training.Half ? BOk : BMid);
            Tile("Risque de fatigue (estimé)", $"{Training.RiskLabel(p.Risk)} ({p.Risk})", p.Risk >= 55 ? BBad : p.Risk >= 35 ? BMid : BGood);
            sp.Children.Add(wrap);
            if (p.Targets != "") sp.Children.Add(T("À travailler en priorité pour ce rôle : " + p.Targets, 12, BText));
            if (p.Note != "") sp.Children.Add(T(p.Note, 12, BMid));
            return Card(sp);
        }
    }
}
