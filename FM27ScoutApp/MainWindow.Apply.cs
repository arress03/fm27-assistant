using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FM27Scout
{
    /// <summary>Entraînement → « Appliquer dans le jeu » : un clic, l'app lit FM27, décide et règle tout (via FM27 Bridge).</summary>
    public partial class MainWindow
    {
        string _bridgeState;                     // null = inconnu, "absent", "attente", "prêt"
        bool _autoBusy;
        string _autoStep = "";
        DateTime _bridgeCheckedAt;

        void CheckBridge()
        {
            if ((DateTime.Now - _bridgeCheckedAt).TotalSeconds < 3) return;
            _bridgeCheckedAt = DateTime.Now;
            Task.Run(() => Bridge.Status() ?? "absent").ContinueWith(t =>
            {
                string s = t.Result;
                if (s != _bridgeState) { _bridgeState = s; if (_trainTab == "appliquer" && PageTrain.Visibility == Visibility.Visible) ShowTraining(); if (PageTactic.Visibility == Visibility.Visible) ShowTactic(); }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void ShowApply(List<PlayerRow> squad, Training.Week week)
        {
            var set = Training.Set;
            CheckBridge();

            // --- état du lien avec FM
            var head = new DockPanel();
            var dot = new System.Windows.Shapes.Ellipse { Width = 10, Height = 10, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                Fill = _bridgeState == "prêt" ? BGood : _bridgeState == "attente" ? BOk : _bridgeState == null ? BDim : BBad };
            head.Children.Add(dot);
            head.Children.Add(T(_bridgeState switch
            {
                "prêt" => "FM27 est connecté : l'app peut lire ta partie et régler l'entraînement.",
                "attente" => "FM27 est lancé : charge ta partie (ou clique n'importe où dans le jeu).",
                null => "Recherche de FM27…",
                _ => "FM27 n'est pas lancé (ou le module FM27 Bridge n'est pas installé).",
            }, 13, BText, true));
            var top = new StackPanel();
            top.Children.Add(head);
            top.Children.Add(T("Un clic : l'app lit l'état réel de ta partie (condition, fraîcheur et fatigue de chaque joueur, notes de tes entraîneurs, prochain match), " +
                "décide du meilleur réglage puis l'applique directement dans FM, exactement comme si tu cliquais dans les menus.", 12, BDim));

            // --- ce qu'on règle (choix en mode expert, tout en mode auto)
            var parts = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            foreach (var (label, get, setter) in new (string, Func<bool>, Action<bool>)[]
            {
                ("Intensité de l'équipe", () => set.ApplyTeam, v => set.ApplyTeam = v),
                ("Intensité de chaque joueur", () => set.ApplyPlayers, v => set.ApplyPlayers = v),
                ("Affectation des entraîneurs", () => set.ApplyCoaches, v => set.ApplyCoaches = v),
                ("Programme des semaines", () => set.ApplyWeeks, v => set.ApplyWeeks = v),
                ("Focus individuel (faiblesses)", () => set.ApplyFocus, v => set.ApplyFocus = v),
                ("Groupes d'encadrement", () => set.ApplyMentoring, v => set.ApplyMentoring = v),
            })
            {
                var cb = new CheckBox { Content = label, IsChecked = get(), IsEnabled = !_autoBusy, Foreground = BText, Margin = new Thickness(0, 0, 16, 4) };
                cb.Click += (_, _) => { setter(cb.IsChecked == true); Training.SaveSet(); };
                parts.Children.Add(cb);
            }
            top.Children.Add(parts);

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var go = Btn(_autoBusy ? "En cours…" : "Tout analyser et appliquer dans FM27", async () => await RunAuto(squad, true), true);
            go.IsEnabled = !_autoBusy && _bridgeState == "prêt";
            row.Children.Add(go);
            var prev = Btn("Aperçu sans rien changer", async () => await RunAuto(squad, false));
            prev.IsEnabled = !_autoBusy && _bridgeState == "prêt";
            prev.Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(prev);
            top.Children.Add(row);
            if (_autoStep != "") top.Children.Add(T(_autoStep, 12, _autoBusy ? BOk : BDim));
            top.Children.Add(T("Conseil : sauvegarde ta partie (Ctrl+S dans FM) avant la première utilisation.", 11, BDim));
            TrainBox.Children.Add(Card(top));

            var rep = AutoTrain.Last;
            if (rep != null) ShowAutoReport(rep);
        }

        async Task RunAuto(List<PlayerRow> squad, bool apply)
        {
            if (_autoBusy) return;
            _autoBusy = true; _autoStep = "Démarrage…"; ShowTraining();
            var progress = new Progress<string>(s => { _autoStep = s; ShowTraining(); });
            IProgress<string> pr = progress;
            try
            {
                var rep = await Task.Run(() => AutoTrain.Run(apply, squad, s => pr.Report(s)));
                _autoStep = apply
                    ? $"Appliqué à {rep.At:HH:mm} : {rep.Orders} réglage(s) envoyé(s) à FM" + (rep.Errors.Count > 0 ? $", {rep.Errors.Count} problème(s)" : ", sans erreur")
                    : $"Aperçu du {rep.At:HH:mm} (rien n'a été changé dans FM)";
            }
            catch (Exception ex) { _autoStep = "Erreur : " + ex.Message; Log.Warn("Pilote entraînement : " + ex); }
            _autoBusy = false;
            _bridgeCheckedAt = DateTime.MinValue;
            ShowTraining();
        }

        void ShowAutoReport(AutoTrain.Report rep)
        {
            string verb = rep.Applied ? "Appliqué" : "Proposé";

            // --- intensité équipe
            var team = new StackPanel();
            team.Children.Add(T($"Intensité de l'équipe — {verb.ToLower()}", 15, BText, true));
            for (int i = 0; i < 5; i++)
            {
                string after = AutoTrain.TeamOptions[rep.TeamAfter[i] == Training.NoPitch ? 0 : rep.TeamAfter[i] == Training.Half ? 1 : rep.TeamAfter[i] == Training.Normal ? 2 : 3];
                bool same = rep.TeamBefore[i] == after;
                team.Children.Add(T($"   {Training.FormStates[i]} : " + (same || rep.TeamBefore[i] == null ? after : $"{rep.TeamBefore[i]} → {after}"), 12, same ? BDim : BText));
            }
            foreach (var n in rep.Notes) team.Children.Add(T(n, 11, BDim));
            TrainBox.Children.Add(Card(team));

            // --- joueurs
            var pl = new StackPanel();
            pl.Children.Add(T($"Joueurs ({rep.Players.Count}) — état lu dans FM", 15, BText, true));
            var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            foreach (var w in new[] { 190, 75, 70, 70, 140, 170, 0 })
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = w == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
            void Cell(int r, int c, string s, Brush b, bool bold = false)
            {
                var t = T(s, 12, b, bold); t.Margin = new Thickness(0, 1, 8, 1); t.TextTrimming = TextTrimming.CharacterEllipsis;
                Grid.SetRow(t, r); Grid.SetColumn(t, c); grid.Children.Add(t);
            }
            grid.RowDefinitions.Add(new RowDefinition());
            string[] hdr = { "Joueur", "Condition", "Rythme", "Fatigue", "Intensité", "Focus individuel", "Pourquoi" };
            for (int c = 0; c < hdr.Length; c++) Cell(0, c, hdr[c], BDim, true);
            int rr = 1;
            foreach (var p in rep.Players.OrderBy(p => p.Want == -1).ThenBy(p => p.Cond))
            {
                grid.RowDefinitions.Add(new RowDefinition());
                Cell(rr, 0, p.Name, BText);
                Cell(rr, 1, p.Cond >= 0 ? p.Cond + " %" : "?", p.Cond >= 90 ? BGood : p.Cond >= 80 ? BOk : BBad);
                Cell(rr, 2, p.Sharp >= 0 ? p.Sharp + " %" : "?", p.Sharp >= 85 ? BGood : p.Sharp >= 70 ? BOk : BMid);
                Cell(rr, 3, p.Fatigue == int.MinValue ? "?" : p.Fatigue <= 0 ? "frais" : p.Fatigue.ToString(), p.Fatigue >= 400 ? BBad : p.Fatigue >= 200 ? BMid : BGood);
                Cell(rr, 4, p.Injured ? "Blessé" : AutoTrain.OptionShort(p.Want), p.Injured ? BBad : p.Want == -1 ? BDim : p.Want == 2 ? BAccent : p.Want == 0 || p.Want >= 9 ? BMid : BText, p.Want != -1);
                Cell(rr, 5, p.Focus == "" ? "—" : p.Focus, p.Focus != "" && p.Focus != p.FocusBefore ? BText : BDim);
                var why = T(p.Why + (p.FocusWhy != "" ? " · focus : " + p.FocusWhy : ""), 12, BDim);
                why.Margin = new Thickness(0, 1, 8, 1); why.TextTrimming = TextTrimming.CharacterEllipsis; why.ToolTip = why.Text;
                Grid.SetRow(why, rr); Grid.SetColumn(why, 6); grid.Children.Add(why);
                rr++;
            }
            pl.Children.Add(grid);
            TrainBox.Children.Add(Card(pl));

            // --- entraîneurs
            if (rep.Coaches.Count > 0)
            {
                var co = new StackPanel();
                co.Children.Add(T($"Affectation des entraîneurs — {verb.ToLower()} (notes mesurées dans FM, sur 20)", 15, BText, true));
                for (int cat = 0; cat < 9; cat++)
                {
                    var on = rep.Coaches.Where(c => c.Plan[cat]).OrderByDescending(c => c.Rating[cat]).ToList();
                    var was = rep.Coaches.Where(c => c.Checked[cat]).ToList();
                    bool changed = !on.Select(c => c.Index).ToHashSet().SetEquals(was.Select(c => c.Index));
                    string txt = on.Count == 0 ? "personne" : string.Join(", ", on.Select(c => $"{c.Name} ({c.Rating[cat]})"));
                    var line = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
                    var lab = T(AutoTrain.CatName(cat), 12, BDim); lab.Width = 190; line.Children.Add(lab);
                    line.Children.Add(T(txt + (changed ? "   (modifié)" : ""), 12, changed ? BText : BDim, changed));
                    co.Children.Add(line);
                }
                var load = rep.Coaches.Where(c => c.Load > 0).OrderByDescending(c => c.Load).Select(c => $"{c.Name} {c.Load}");
                co.Children.Add(T("Charge (domaines par entraîneur) : " + string.Join(" · ", load), 11, BDim));
                TrainBox.Children.Add(Card(co));
            }

            // --- semaines
            if (rep.Weeks.Count > 0)
            {
                var wk = new StackPanel();
                wk.Children.Add(T($"Programme des semaines — {verb.ToLower()}", 15, BText, true));
                foreach (var (mon, name, why) in rep.Weeks)
                    wk.Children.Add(T($"   Semaine du {mon:dd/MM} : {name}  ·  {why}", 12, BText));
                TrainBox.Children.Add(Card(wk));
            }

            if (rep.Groups.Count > 0 || rep.ExistingGroups.Count > 0)
            {
                var gr = new StackPanel();
                gr.Children.Add(T($"Groupes d'encadrement — {(rep.Applied ? "créés" : "proposés")}", 15, BText, true));
                foreach (var g in rep.ExistingGroups) gr.Children.Add(T("   déjà en place : " + g, 12, BDim));
                foreach (var g in rep.Groups) gr.Children.Add(T($"   {g.Name} : {string.Join(", ", g.Members.Select(m => m.Name))}", 12, BText));
                if (rep.Groups.Count == 0) gr.Children.Add(T("   aucun jeune n'a besoin d'un nouveau groupe", 12, BDim));
                TrainBox.Children.Add(Card(gr));
            }

            if (rep.Errors.Count > 0)
            {
                var er = new StackPanel();
                er.Children.Add(T("Problèmes", 15, BBad, true));
                foreach (var e in rep.Errors.Take(20)) er.Children.Add(T(e, 12, BText));
                TrainBox.Children.Add(Card(er));
            }
        }
    }
}
