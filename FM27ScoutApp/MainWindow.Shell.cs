using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;

namespace FM27Scout
{
    /// <summary>Coque de l'app : barre de titre, club géré détecté dans FM (nom + couleurs), guide interactif.</summary>
    public partial class MainWindow
    {
        // ============================================================ barre de titre sombre (Windows 10/11)

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        void DarkTitleBar()
        {
            try
            {
                var h = new WindowInteropHelper(this).Handle;
                int on = 1;
                DwmSetWindowAttribute(h, 20, ref on, 4);                      // mode sombre
                UpdateSide();                                                  // club mémorisé affiché dès l'ouverture
                ColorTitleBar();
                Theme.Changed += ColorTitleBar;
            }
            catch { }
        }

        void ColorTitleBar()
        {
            try
            {
                var h = new WindowInteropHelper(this).Handle;
                var c = Theme.Bg.Color;
                int bgr = c.R | (c.G << 8) | (c.B << 16);
                DwmSetWindowAttribute(h, 35, ref bgr, 4);                     // couleur de la barre (Windows 11)
            }
            catch { }
        }

        // ============================================================ club géré

        void UpdateSide()
        {
            string club = Settings.Cur.MyClub;
            SideClubName.Text = club == "" ? L.T("Aucun club") : club;
            var any = club == "" ? null : Game.Rows.FirstOrDefault(r => r.Club == club && r.ClubPtr != 0);
            var logo = any == null ? null : Gfx.Logo(any.ClubPtr, 52);
            SideClubLogo.Source = logo;
            SideClubLogo.Visibility = logo != null ? Visibility.Visible : Visibility.Collapsed;
        }

        DateTime _clubCheckedAt;
        (Color, Color) _clubColors;
        bool _clubBusy;

        /// <summary>Demande à FM (via FM27 Bridge) quel club tu diriges et ses couleurs : « Mon club » et le thème suivent tout seuls.</summary>
        void DetectClub()
        {
            if (_clubBusy || Settings.Cur.MyClubManual || (DateTime.Now - _clubCheckedAt).TotalSeconds < 45) return;
            bool haveClub = Settings.Cur.MyClub != "";
            _clubCheckedAt = DateTime.Now; _clubBusy = true;
            Task.Run(() =>
            {
                try
                {
                    if (Bridge.Status() != "prêt") return ((string, Color?, Color?, string)?)null;
                    var ctx = Bridge.Ctx();
                    // contexte tiré du calendrier : peut être celui d'un autre club consulté dans FM ; ne sert qu'à défaut de club connu
                    if (!ctx.FromTraining && haveClub) return ((string, Color?, Color?, string)?)null;
                    var clubRef = Bridge.Read1("PersonReference", Bridge.Context.Person(ctx.Manager), "825630752");
                    string pairs = Bridge.PairsOf(clubRef);
                    if (pairs == "") return ((string, Color?, Color?, string)?)null;
                    var v = Bridge.Read(new[] { ("ClubReference", pairs, "1851878757"), ("ClubReference", pairs, "1835218292"), ("ClubReference", pairs, "943267874"),
                                                ("ClubReference", pairs, Bridge.CC("unid").ToString()) });
                    // club identifié par des identifiants (indépendant de la langue de FM) : effectif géré, sinon identifiant du club
                    string byId = null;
                    if (ctx.FromTraining) try { byId = SquadClub(ctx); } catch (Exception e) { Log.Warn("Club géré (effectif) : " + e.Message); }
                    byId ??= ClubByUid((uint)Math.Max(0, Bridge.Int(v[3], 0)));
                    return ((string, Color?, Color?, string)?)(Bridge.DisplayName(Bridge.Str(v[0])), Theme.ParseFm(Bridge.Str(v[1])), Theme.ParseFm(Bridge.Str(v[2])), byId);
                }
                catch (Exception e) { Log.Warn("Club géré : " + e.Message); return ((string, Color?, Color?, string)?)null; }
            }).ContinueWith(t =>
            {
                _clubBusy = false;
                if (t.Result is not { } res) return;
                var (name, c1n, c2n, byId) = res;
                if (byId == null && string.IsNullOrWhiteSpace(name)) return;
                bool changed = false;
                if (c1n is Color c1)
                {
                    var c2 = c2n ?? Colors.White;
                    if ((c1, c2) != _clubColors) { bool first = _clubColors == default; _clubColors = (c1, c2); if (!first || (c1 != Theme.Primary && c2 != Theme.Primary)) { Theme.Apply(c1, c2); changed = true; } }
                }
                if (!string.IsNullOrWhiteSpace(name)) Settings.Cur.MyClubFm = name;
                string match = byId ?? ClubByName(name);
                if (match != null && match != Settings.Cur.MyClub)
                {
                    Settings.Cur.MyClub = match; changed = true;
                    Log.Info($"Club géré détecté dans FM : {match} (par {(byId != null ? "identifiants" : "nom « " + name + " »")})");
                }
                Settings.Save();
                if (changed) RefreshAll(); else UpdateSide();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>Club des joueurs que tu entraînes (effectif lu dans FM, reliés à la base par leur identifiant FM) : le club le plus représenté.</summary>
        static string SquadClub(Bridge.Context ctx)
        {
            const uint pSquad = 1432578628, kPerson = 1685218662;
            var list = Bridge.Read1("TeamTrainingReference", ctx.TeamTraining, pSquad.ToString());
            var persons = Bridge.Items(list).Select(x => Bridge.PairOf(x, kPerson)).Where(p => p != 0).Distinct().Take(40).ToList();
            if (persons.Count == 0) return null;
            string unid = Bridge.CC("unid").ToString();
            var uids = Bridge.Read(persons.Select(p => ("PersonReference", Bridge.Context.Person(p), unid)).ToList())
                .Select(v => (uint)Math.Max(0, Bridge.Int(v, 0))).Where(u => u != 0).ToHashSet();
            Gfx.MatchIds(uids);   // sans pack de photos : relie la base aux identifiants FM de ton effectif
            var clubs = Game.Rows.Where(r => r.Uid != 0 && uids.Contains(r.Uid) && r.Club != "").GroupBy(r => r.Club)
                .Select(g => (club: g.Key, n: g.Count())).OrderByDescending(x => x.n).ToList();
            // au moins la moitié des joueurs reconnus dans le même club (prêts et transferts récents mis à part)
            return clubs.Count > 0 && clubs[0].n * 2 >= clubs.Sum(x => x.n) ? clubs[0].club : null;
        }

        /// <summary>Club dont l'identifiant FM (celui des logos) est donné.</summary>
        static string ClubByUid(uint uid) =>
            uid == 0 ? null : Game.Rows.FirstOrDefault(r => r.Club != "" && r.ClubPtr != 0 && Gfx.ClubUid.TryGetValue(r.ClubPtr, out var u) && u == uid)?.Club;

        /// <summary>Club de la base au nom donné par FM (dernier recours) : nom identique, sinon l'un contient l'autre (noms courts exclus).</summary>
        static string ClubByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 60) return null;
            string fold = Game.Fold(name);
            var clubs = Game.Rows.Where(r => r.Club != "").Select(r => r.Club).Distinct().ToList();
            return clubs.FirstOrDefault(c => Game.Fold(c) == fold)
                ?? clubs.Where(c => { string fc = Game.Fold(c); return Math.Min(fc.Length, fold.Length) >= 4 && (fc.Contains(fold) || fold.Contains(fc)); })
                        .OrderBy(c => Math.Abs(c.Length - name.Length)).FirstOrDefault();
        }

        // ============================================================ guide

        sealed record Guide(string Title, string Text, string[] Points);

        static readonly Guide[] Welcome =
        {
            new("Bienvenue dans FM27 Assistant",
                "L'app lit ta partie de Football Manager 27 pendant que tu joues : tout ce que FM cache (potentiel, personnalité, fragilité) et ce qu'il montre, réuni au même endroit.",
                new[] { "Lance FM27 et charge ta carrière : l'app se connecte toute seule.", "Ton club est reconnu automatiquement, et l'app prend ses couleurs." }),
            new("Tout se trouve à gauche",
                "Le menu est rangé en deux parties : ce qui concerne ton club, et ce qui sert à recruter.",
                new[] { "Mon club : effectif, tactique, entraînement, vestiaire.", "Recrutement : recherches, radar, bonnes affaires, shortlist, alertes, comparateur.", "La barre du haut cherche n'importe quel joueur de la base." }),
            new("L'app peut régler FM à ta place",
                "Grâce au module FM27 Bridge installé dans le jeu, l'app ne fait pas que conseiller : elle applique.",
                new[] { "Entraînement : intensités, entraîneurs, programmes, focus individuels et encadrement en un clic.", "Tactique : ton meilleur onze placé d'un clic dans ta tactique FM.", "Tu gardes la main : tout reste modifiable dans le jeu." }),
            new("Une aide sur chaque page",
                "La première fois que tu ouvres une page, une fiche comme celle-ci t'explique à quoi elle sert. Tu peux revoir ce guide depuis le bas du menu.",
                Array.Empty<string>()),
        };

        Dictionary<ToggleButton, Guide> _pageGuides;

        Dictionary<ToggleButton, Guide> PageGuides => _pageGuides ??= new()
        {
            [NavClub] = new("Effectif", "Ton club au complet, avec les attributs visibles et cachés de chaque joueur.",
                new[] { "Les onglets séparent équipe première, réserve et jeunes.", "Clique sur un en-tête de colonne pour trier.", "Double-clic sur un joueur pour ouvrir sa fiche." }),
            [NavTactic] = new("Tactique", "L'app note les 90 configurations tactiques de FM27 (10 styles × 9 formations) pour ton effectif et te conseille les meilleures.",
                new[] { "La note combine l'onze avec ballon, l'onze sans ballon et le profil demandé par le style.", "Pieds forts et attributs « meta » (vitesse, accélération) pris en compte.", "« Créer cette tactique dans FM » règle style, formations et onze en un clic ; les coups de pied arrêtés sont juste en dessous." }),
            [NavTrain] = new("Entraînement", "Le plan d'entraînement des 4 prochaines semaines, et le pilote qui l'applique dans FM.",
                new[] { "Plan 4 semaines : ce qu'il faut travailler et quand lever le pied.", "Entraînement individuel : rôle, faiblesses et focus de chaque joueur.", "Appliquer dans le jeu : l'app lit la forme réelle de tes joueurs et règle tout dans FM." }),
            [NavDressing] = new("Vestiaire", "Les traits de caractère cachés de ton effectif : leaders, joueurs à risque, profils à encadrer.",
                new[] { "Les filtres en haut isolent chaque catégorie.", "Pratique pour choisir un capitaine ou former des groupes d'encadrement." }),
            [NavSearch] = new("Recherches", "Des recherches prêtes à l'emploi sur toute la base : jeunes pépites, joueurs libres, profils précis.",
                new[] { "Choisis une recherche à gauche, les résultats s'affichent à droite.", "Les recherches sont modifiables dans recherches.json." }),
            [NavRadar] = new("Radar de scouting", "Règle tes critères une fois : le radar te signale chaque nouveau joueur qui y correspond.",
                new[] { "Âge, poste, potentiel, valeur, contrat : tout se combine.", "Un badge dans le menu prévient quand de nouveaux joueurs apparaissent." }),
            [NavDeals] = new("Bonnes affaires", "Les joueurs qui valent le coup : libres, fin de contrat proche, ou meilleur rapport qualité-prix.", Array.Empty<string>()),
            [NavShort] = new("Shortlist", "Les joueurs que tu suis. L'app note ce qui a changé depuis que tu les as ajoutés : club, valeur, progression.",
                new[] { "Ajoute un joueur depuis sa fiche." }),
            [NavAlerts] = new("Alertes d'effectif", "Ce qui cloche dans un effectif : contrats qui expirent, postes trop courts, joueurs en déclin.", Array.Empty<string>()),
            [NavCompare] = new("Comparateur", "Deux joueurs côte à côte, attribut par attribut, avec leur profil en radar.", Array.Empty<string>()),
            [NavPlayer] = new("Fiche joueur", "Tout sur un joueur : potentiel, personnalité, fragilité, meilleurs rôles, progression et joueurs similaires.",
                new[] { "« Copier le nom » pour le retrouver vite dans FM.", "Ajoute-le à ta shortlist pour suivre son évolution." }),
        };

        readonly Queue<Guide> _guideQueue = new();
        bool _welcomeShown;

        void GuideFor(ToggleButton nav)
        {
            var st = Settings.Cur;
            if (!st.GuideWelcomeDone && !_welcomeShown)
            {
                _welcomeShown = true;
                foreach (var g in Welcome) _guideQueue.Enqueue(g);
                ShowNextGuide();
                return;
            }
            if (st.GuideMuted || GuideLayer.Visibility == Visibility.Visible) return;
            if (!PageGuides.TryGetValue(nav, out var guide)) return;
            string key = nav.Name;
            if (st.GuideSeen.Contains(key)) return;
            st.GuideSeen.Add(key); Settings.Save();
            _guideQueue.Clear(); _guideQueue.Enqueue(guide);
            ShowNextGuide();
        }

        int _guideTotal, _guideIndex;

        void ShowNextGuide()
        {
            if (_guideQueue.Count == 0) { GuideLayer.Visibility = Visibility.Collapsed; return; }
            if (_guideIndex == 0) _guideTotal = _guideQueue.Count;
            var g = _guideQueue.Dequeue();
            _guideIndex++;
            GuideStep.Text = _guideTotal > 1 ? L.F("ÉTAPE {0} SUR {1}", _guideIndex, _guideTotal) : L.T("CONSEIL");
            GuideTitle.Text = L.T(g.Title);
            GuideText.Text = L.T(g.Text);
            GuidePoints.Children.Clear();
            foreach (var p in g.Points)
            {
                var line = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
                var dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = BAccent, Margin = new Thickness(2, 7, 12, 0), VerticalAlignment = VerticalAlignment.Top };
                line.Children.Add(dot);
                line.Children.Add(new TextBlock { Text = L.T(p), TextWrapping = TextWrapping.Wrap, FontSize = 13.5, Foreground = BText });
                GuidePoints.Children.Add(line);
            }
            GuideNext.Content = _guideQueue.Count > 0 ? L.T("Suivant") : L.T("Compris");
            GuideSkip.Visibility = _guideQueue.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            GuideMute.Visibility = _guideTotal > 1 ? Visibility.Collapsed : Visibility.Visible;
            GuideMute.IsChecked = false;
            GuideLayer.Visibility = Visibility.Visible;
        }

        void CloseGuide()
        {
            _guideQueue.Clear(); _guideIndex = 0;
            GuideLayer.Visibility = Visibility.Collapsed;
            if (!Settings.Cur.GuideWelcomeDone) { Settings.Cur.GuideWelcomeDone = true; Settings.Save(); }
        }

        void GuideNext_Click(object s, RoutedEventArgs e)
        {
            if (GuideMute.IsChecked == true) { Settings.Cur.GuideMuted = true; Settings.Save(); }
            if (_guideQueue.Count == 0) CloseGuide(); else ShowNextGuide();
        }

        void GuideSkip_Click(object s, RoutedEventArgs e) => CloseGuide();

        void GuideAgain_Click(object s, RoutedEventArgs e)
        {
            Settings.Cur.GuideSeen.Clear(); Settings.Cur.GuideMuted = false; Settings.Save();
            _guideQueue.Clear(); _guideIndex = 0;
            foreach (var g in Welcome) _guideQueue.Enqueue(g);
            ShowNextGuide();
        }
    }
}
