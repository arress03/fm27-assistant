using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FM27Scout
{
    /// <summary>
    /// Pilote automatique de l'entraînement : lit l'état réel dans FM27 (via FM27 Bridge), décide, puis applique
    /// avec les mêmes ordres que les menus du jeu :
    ///   1. intensité de l'équipe pour chaque état de forme ;
    ///   2. intensité individuelle de chaque joueur (condition, fraîcheur, fatigue réelles) ;
    ///   3. affectation des entraîneurs aux 9 domaines (notes réelles mesurées dans le jeu) ;
    ///   4. programme des semaines à venir (priorité tactique selon les besoins de l'équipe).
    /// </summary>
    internal static class AutoTrain
    {
        // propriétés FM (codes de 4 lettres)
        static readonly uint pUnid = Bridge.CC("unid"), pCond = Bridge.CC("cndn"), pSharp = Bridge.CC("shrp"), pFatg = Bridge.CC("fatg"),
            pShort = 1767075437, pRole = 825696289, pName = 1851878757,
            pSquad = 1432578628, pGrid = 1700935276, pTeamRows = 1164198760, pNextFix = 1347319878, pComp = 1668246896;
        const uint kPerson = 1685218662, kDate = 1717859429;

        // ordres FM
        const uint evPlayerInt = 1735948081, evAssign = 1131895105, evUnassign = 1817538425, evWeek = 1632595570;

        /// <summary>Catégorie FM (catg) → domaine de l'app (Training.Areas).</summary>
        static readonly int[] CatToArea = { 7, 6, 2, 3, 4, 5, 0, 1, 8 };
        public static string CatName(int c) => Training.Areas[CatToArea[c]];
        static int AreaToCat(string area) => Array.IndexOf(CatToArea, Array.IndexOf(Training.Areas, area));

        public static readonly string[] TeamOptions = { "Aucun travail sur le terrain ou en salle de gym", "Semi-intensité", "Intensité normale", "Double intensité" };
        public static readonly Dictionary<int, string> PlayerOptions = new() { [-1] = "Automatique", [0] = "Semi-intensité", [1] = "Intensité normale", [2] = "Double intensité" };

        public sealed class Player
        {
            public int Person; public uint Uid; public string Name = "";
            public int Cond = -1, Sharp = -1, Fatigue = int.MinValue;
            public PlayerRow Row;
            public int Want = -1; public string Why = "";
            public bool Injured;                 // FM bloque son intensité : rien à envoyer
            public bool Resting;
            public string Focus = "", FocusWhy = "", FocusBefore = "";
            public int FocusValue = int.MinValue;
            public string FocusTemplate;          // réglage individuel actuel lu dans FM (sert de modèle à l'ordre)                 // déjà au repos (« aucun travail ») : FM attend un drapeau pour l'annuler
        }

        /// <summary>Libellé FM d'un choix d'intensité individuelle (-1..2) ou d'un repos (9 + nombre de jours).</summary>
        public static string OptionLabel(int want) => want switch
        {
            >= 9 and <= 14 => want == 9 ? "1 jour" : $"{want - 8} jours",
            16 => "1 semaine",
            _ => PlayerOptions.TryGetValue(want, out var l) ? l : "?",
        };
        /// <summary>Libellé affiché (dans la langue de l'interface) d'un choix d'intensité individuelle ou d'un repos.</summary>
        public static string OptionShort(int want) => want switch
        {
            9 => L.T("Repos 1 jour"),
            >= 10 and <= 14 => L.F("Repos {0} jours", want - 8),
            16 => L.T("Repos 1 semaine"),
            _ => L.T(OptionLabel(want)),
        };
        static int Rest(int days) => days >= 7 ? 16 : 8 + Math.Clamp(days, 1, 6);     // valeurs FM : 9 = 1 jour … 14 = 6 jours, 16 = 1 semaine

        public sealed class Group { public List<Player> Members = new(); public string Name = ""; public string Why = ""; }

        public sealed class Coach
        {
            public int Index, Person; public string Name = "", Role = "";
            public bool[] Enabled = new bool[9], Checked = new bool[9], Plan = new bool[9];
            public int[] Rating = new int[9];
            public int Load => Plan.Count(x => x);
        }

        public sealed class Report
        {
            public DateTime At = DateTime.Now;
            public string Lang = L.Lang;                       // langue de ses textes (motifs, notes, erreurs)
            public bool Applied;
            public string[] TeamBefore = new string[5], TeamAfter = new string[5];
            public List<Player> Players = new();
            public List<Coach> Coaches = new();
            public List<(DateTime monday, string programme, string why)> Weeks = new();
            public List<Group> Groups = new();                 // groupes d'encadrement à créer
            public List<string> ExistingGroups = new();
            public List<string> Notes = new(), Errors = new();
            public int Orders;
        }

        static Report s_last;
        /// <summary>Dernière analyse ; oubliée si la langue de l'interface a changé depuis (ses textes sont dans l'ancienne langue).</summary>
        public static Report Last => s_last != null && s_last.Lang == L.Lang ? s_last : null;

        // ================================================================== lecture

        static List<Player> ReadPlayers(Bridge.Context ctx)
        {
            var list = Bridge.Read1("TeamTrainingReference", ctx.TeamTraining, pSquad.ToString());
            var persons = Bridge.Items(list).Select(x => Bridge.PairOf(x, kPerson)).Where(p => p != 0).Distinct().ToList();
            if (persons.Count == 0) throw new Exception(L.T("liste des joueurs vide (ouvre une fois Entraînement → Intensité dans FM)"));
            var reqs = new List<(string, string, string)>();
            foreach (var p in persons)
                foreach (var prop in new[] { pUnid, pCond, pSharp, pFatg, pShort })
                    reqs.Add(("PersonReference", Bridge.Context.Person(p), prop.ToString()));
            var vals = Bridge.Read(reqs);
            var res = new List<Player>();
            Gfx.MatchIds(Enumerable.Range(0, persons.Count).Select(i => (uint)Math.Max(0, Bridge.Int(vals[i * 5], 0))).Where(u => u != 0).ToHashSet());
            for (int i = 0; i < persons.Count; i++)
            {
                var pl = new Player
                {
                    Person = persons[i],
                    Uid = (uint)Math.Max(0, Bridge.Int(vals[i * 5], 0)),
                    Cond = Bridge.Int(vals[i * 5 + 1], -1),
                    Sharp = Bridge.Int(vals[i * 5 + 2], -1),
                    Fatigue = Bridge.Int(vals[i * 5 + 3]),
                };
                pl.Row = Game.Rows.FirstOrDefault(r => r.Uid == pl.Uid && pl.Uid != 0);
                pl.Name = pl.Row?.Name ?? Bridge.DisplayName(Bridge.Str(vals[i * 5 + 4]));
                res.Add(pl);
            }
            return res;
        }

        static List<Coach> ReadCoaches(Bridge.Context ctx, bool names)
        {
            var grid = Bridge.Read1("TeamTrainingReference", ctx.TeamTraining, pGrid.ToString());
            var rows = Bridge.Items(Bridge.Field(grid, 1383421003)).ToList();
            var res = new List<Coach>();
            foreach (var row in rows)
            {
                var c = new Coach();
                if (!row.TryGetProperty("entries", out var en)) continue;
                foreach (var e in en.EnumerateArray())
                {
                    var v = e[2];
                    if (v.ValueKind == JsonValueKind.Object && v.TryGetProperty("pairs", out _)) { c.Person = Bridge.PairOf(v, kPerson); continue; }
                    var ev = Bridge.Field(v, 1702257780);
                    if (ev == null) continue;
                    int cat = Bridge.Int(Bridge.Field(ev, 1667331175), -1);
                    c.Index = Bridge.Int(Bridge.Field(ev, 1768842360), 0);
                    if (cat is < 0 or > 8) continue;
                    c.Enabled[cat] = Bridge.Bool(Bridge.Field(v, 1299866233));
                    c.Checked[cat] = Bridge.Bool(Bridge.Field(v, 2004116555));
                    var st = Bridge.Field(v, 1464367445);
                    c.Rating[cat] = Math.Max(Bridge.Int(Bridge.Field(st, 1298233430), 0), Bridge.Int(Bridge.Field(st, 1298755158), 0));
                }
                if (c.Index != 0) res.Add(c);
            }
            if (names && res.Count > 0)
            {
                var reqs = res.SelectMany(c => new[]
                {
                    ("PersonReference", Bridge.Context.Person(c.Person), pName.ToString()),
                    ("PersonReference", Bridge.Context.Person(c.Person), pShort.ToString()),
                    ("PersonReference", Bridge.Context.Person(c.Person), pRole.ToString()),
                }).ToList();
                var vals = Bridge.Read(reqs);
                for (int i = 0; i < res.Count; i++)
                {
                    string n = Bridge.DisplayName(Bridge.Str(vals[i * 3]));
                    if (string.IsNullOrWhiteSpace(n)) n = Bridge.DisplayName(Bridge.Str(vals[i * 3 + 1]));
                    res[i].Name = string.IsNullOrWhiteSpace(n) ? L.F("Entraîneur {0}", i + 1) : n;
                    res[i].Role = Bridge.Str(vals[i * 3 + 2]) ?? "";
                }
            }
            return res;
        }

        static string Assign(int index, int cat) => Bridge.Rec((1768842360, "SI.Core.DynamicNumber", $"\"{index}\""), (1667331175, "SI.Core.DynamicNumber", $"\"{cat}\""));

        /// <summary>
        /// FM n'affiche la note d'un entraîneur dans un domaine que s'il y est affecté : on l'affecte un instant,
        /// on lit la note, puis on remet exactement comme avant.
        /// </summary>
        static void MeasureRatings(Bridge.Context ctx, List<Coach> coaches, Action<string> progress)
        {
            for (int cat = 0; cat < 9; cat++)
            {
                var probe = coaches.Where(c => c.Enabled[cat] && !c.Checked[cat]).ToList();
                if (probe.Count == 0) continue;
                progress(L.F("Mesure des notes : {0}…", L.T(CatName(cat))));
                var done = new List<Coach>();
                try
                {
                    foreach (var c in probe) { Bridge.Send(evAssign, "TeamTrainingReference", ctx.TeamTraining, Assign(c.Index, cat)); done.Add(c); }
                    var now = ReadCoaches(ctx, false);
                    foreach (var c in probe)
                    {
                        var m = now.FirstOrDefault(x => x.Index == c.Index);
                        if (m != null) c.Rating[cat] = m.Rating[cat];
                    }
                }
                finally
                {
                    foreach (var c in done)
                        try { Bridge.Send(evUnassign, "TeamTrainingReference", ctx.TeamTraining, Assign(c.Index, cat)); } catch { }
                }
            }
        }

        // ================================================================== décisions

        /// <summary>
        /// Affectations : un responsable par domaine (le mieux noté, en commençant par les domaines où il y a le moins de choix),
        /// puis des adjoints proches de son niveau pour alléger la charge. Chaque entraîneur couvre au plus 2 domaines
        /// (3 si l'effectif d'entraîneurs est réduit) pour que sa note ne baisse pas.
        /// </summary>
        /// <summary>Meilleure note déjà mesurée par entraîneur et domaine (la note lue varie d'un point selon la charge du moment).</summary>
        static Dictionary<string, int> s_bestRatings;
        static Dictionary<string, int> BestRatings => s_bestRatings ??= Db.LoadJson("notes_entraineurs.json", new Dictionary<string, int>());

        static void StabilizeRatings(List<Coach> coaches)
        {
            foreach (var c in coaches)
                for (int cat = 0; cat < 9; cat++)
                {
                    if (!c.Enabled[cat]) continue;
                    string k = $"{c.Person}:{cat}";
                    int best = BestRatings.GetValueOrDefault(k);
                    if (c.Rating[cat] > best) BestRatings[k] = c.Rating[cat];
                    else c.Rating[cat] = best;
                }
            Db.SaveJson("notes_entraineurs.json", BestRatings);
        }

        /// <summary>Qualité d'une affectation : note du responsable de chaque domaine + bonus pour les adjoints, malus si un entraîneur est surchargé.</summary>
        static double Quality(List<Coach> coaches, Func<Coach, int, bool> on, int maxLoad)
        {
            double q = 0;
            for (int cat = 0; cat < 9; cat++)
            {
                var r = coaches.Where(c => on(c, cat)).Select(c => c.Rating[cat]).OrderByDescending(x => x).ToList();
                if (r.Count == 0) { if (coaches.Any(c => c.Enabled[cat] && c.Rating[cat] > 0)) q -= 20; continue; }
                q += r[0] + 0.4 * Math.Min(2, r.Count - 1);
            }
            foreach (var c in coaches) { int load = Enumerable.Range(0, 9).Count(k => on(c, k)); if (load > maxLoad) q -= 2 * (load - maxLoad); }
            return q;
        }

        static void PlanCoaches(List<Coach> coaches, List<string> notes)
        {
            PlanCoachesGreedy(coaches, notes);
            int maxLoad = coaches.Count >= 9 ? 2 : 3;
            double now = Quality(coaches, (c, k) => c.Checked[k], maxLoad), plan = Quality(coaches, (c, k) => c.Plan[k], maxLoad);
            if (plan - now < 1.5)
            {
                foreach (var c in coaches) Array.Copy(c.Checked, c.Plan, 9);
                notes.Add(L.F("affectations actuelles déjà au niveau (écart {0:+0.0;-0.0;0}) : on n'y touche pas", plan - now));
            }
            else notes.Add(L.F("affectations améliorées : qualité {0:0.0} → {1:0.0}", now, plan));
        }

        static void PlanCoachesGreedy(List<Coach> coaches, List<string> notes)
        {
            foreach (var c in coaches) Array.Clear(c.Plan);
            int maxLoad = coaches.Count >= 9 ? 2 : 3;
            // stabilité : à note quasi égale on garde l'affectation actuelle (la note bouge d'un point selon la charge)
            double Score(Coach c, int cat) => c.Rating[cat] + (c.Checked[cat] ? 1.0 : 0);
            var cats = Enumerable.Range(0, 9).OrderBy(cat => coaches.Count(c => c.Enabled[cat] && c.Rating[cat] > 0)).ToList();
            var lead = new int[9];
            foreach (var cat in cats)
            {
                var best = coaches.Where(c => c.Enabled[cat] && c.Rating[cat] > 0 && c.Load < maxLoad)
                    .OrderByDescending(c => Score(c, cat)).ThenBy(c => c.Load).ThenBy(c => c.Enabled.Count(x => x)).FirstOrDefault();
                if (best == null)
                {
                    best = coaches.Where(c => c.Enabled[cat] && c.Rating[cat] > 0).OrderByDescending(c => c.Rating[cat]).FirstOrDefault();
                    if (best == null) { notes.Add(L.F("{0} : aucun entraîneur capable", L.T(CatName(cat)))); continue; }
                }
                best.Plan[cat] = true; lead[cat] = best.Rating[cat];
            }
            // adjoints : jusqu'à 3 par domaine, au plus 2 points sous le responsable
            for (int round = 0; round < 2; round++)
                foreach (var cat in cats)
                {
                    if (coaches.Count(c => c.Plan[cat]) >= 3) continue;
                    var sup = coaches.Where(c => !c.Plan[cat] && c.Enabled[cat] && c.Rating[cat] > 0 && c.Load < maxLoad && Score(c, cat) >= lead[cat] - 2)
                        .OrderByDescending(c => Score(c, cat)).ThenBy(c => c.Load).FirstOrDefault();
                    if (sup != null) sup.Plan[cat] = true;
                }
        }

        static void PlanPlayers(List<Player> players, List<string> notes)
        {
            var set = Training.Set;
            foreach (var p in players)
            {
                p.Want = -1; p.Why = L.T("suit le réglage de l'équipe selon sa forme");
                if (p.Cond < 0) { p.Injured = true; p.Why = L.T("blessé : FM bloque son intensité, le centre médical gère sa reprise"); continue; }
                if (set.Expert && p.Row != null && set.Overrides.TryGetValue(Features.Key(p.Row).ToString(), out var o))
                {
                    p.Want = o == Training.Normal ? 1 : o == "Double intensité" ? 2 : o == Training.NoPitch ? Rest(2) : 0;
                    p.Why = L.T("imposé (mode expert)");
                    continue;
                }
                int inj = p.Row != null ? Db.Stat(p.Row, "InjuryProneness") : 10;
                int age = p.Row != null ? Game.Age(p.Row) : 25;
                if (p.Cond < 70) { int d = p.Cond < 60 ? 3 : 2; p.Want = Rest(d); p.Why = L.F("condition {0}% : repos complet {1} jours", p.Cond, d); }
                else if (p.Fatigue != int.MinValue && p.Fatigue >= 600) { p.Want = Rest(2); p.Why = L.F("très grosse fatigue ({0}) : repos complet 2 jours", p.Fatigue); }
                else if (p.Cond < 80) { p.Want = 0; p.Why = L.F("condition {0}% : il doit récupérer", p.Cond); }
                else if (p.Fatigue != int.MinValue && p.Fatigue >= 400) { p.Want = 0; p.Why = L.F("fatigue accumulée ({0}) : on allège", p.Fatigue); }
                else if (p.Fatigue != int.MinValue && p.Fatigue >= 200) { p.Want = 1; p.Why = L.F("fatigue en hausse ({0}) : pas plus que normal", p.Fatigue); }
                else if (p.Sharp is >= 0 and < 75 && p.Cond >= 95 && (p.Fatigue == int.MinValue || p.Fatigue < 50) && inj <= 12 && age <= 31)
                { p.Want = 2; p.Why = L.F("manque de rythme ({0}%) mais frais et peu fragile : double intensité", p.Sharp); }
                else if (inj >= 16 && p.Fatigue != int.MinValue && p.Fatigue >= 100) { p.Want = 1; p.Why = L.T("très fragile : on évite la double intensité"); }
            }
        }

        static int TeamOption(string label) => label == Training.NoPitch ? 0 : label == Training.Half ? 1 : label == Training.Normal ? 2 : 3;

        static readonly (int idx, int unid, string name)[] Programmes =
        {
            (0, 100000, "Priorité tactique - Équilibrée"), (1, 100001, "Priorité tactique - Avec le ballon"), (2, 100002, "Priorité tactique - Sans le ballon"),
        };

        /// <summary>
        /// Priorité tactique de la semaine : déduite des retards de l'équipe (attaque vs défense), pas de la rotation hebdo,
        /// pour qu'elle ne change pas d'une analyse à l'autre. Semaine à 2 matchs : équilibrée. Un choix déjà fait n'est remis
        /// en cause que si l'écart a nettement bougé.
        /// </summary>
        static (int idx, int unid, string name, string why) Programme(DateTime monday, int matchCount, double att, double def)
        {
            var set = Training.Set;
            string key = monday.ToString("yyyy-MM-dd");
            double d = att - def;
            int want = matchCount >= 2 || Math.Abs(d) < 0.5 ? 0 : d > 0 ? 1 : 2;
            if (set.WeekProgramme.TryGetValue(key, out int prev) && prev != want && matchCount < 2 && Math.Abs(Math.Abs(d) - 0.5) < 0.5) want = prev;
            set.WeekProgramme[key] = want;
            string why = matchCount >= 2 ? L.T("2 matchs dans la semaine : équilibre") : L.F("retard attaque {0:0.0} / défense {1:0.0}", att, def);
            var p = Programmes[want];
            return (p.idx, p.unid, p.name, why);
        }

        static DateTime Unpack(int v) => new DateTime(v >> 17, 1, 1).AddDays(((v >> 8) & 0x1FF) - 1);
        static DateTime Monday(DateTime d) => d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7));

        /// <summary>Date du prochain match officiel (hors amicaux), lue dans FM.</summary>
        static DateTime? NextCompetitive(Bridge.Context ctx)
        {
            var fix = Bridge.Read1("PersonReference", Bridge.Context.Person(ctx.Manager), pNextFix.ToString());
            int packed = Bridge.PairOf(fix, kDate);
            if (packed <= 0 || fix is not { } f) return null;
            string pairs = string.Join(";", f.GetProperty("pairs").EnumerateArray().Select(p => $"{p[0].GetUInt32()}={p[1].GetInt32()}"));
            var comp = Bridge.Read1("FixtureReference", pairs, pComp.ToString());
            int compId = Bridge.PairOf(comp, kPerson);
            string name = compId != 0 ? Bridge.DisplayName(Bridge.Str(Bridge.Read1("CompReference", Bridge.Context.Person(compId), pName.ToString()))) : "";
            if (IsFriendly(name)) return null;
            return Unpack(packed);
        }

        /// <summary>Compétition amicale, quelle que soit la langue de FM (français, anglais, portugais…).</summary>
        static bool IsFriendly(string comp)
        {
            string f = Game.Fold(comp);
            return new[] { "amica", "friendl", "amigav", "particular", "amistos", "amichevol", "freundschaft" }.Any(f.Contains);
        }

        /// <summary>Rôle d'entraîneur adjoint, quelle que soit la langue de FM.</summary>
        static bool IsAssistant(string role)
        {
            string f = Game.Fold(role);
            return new[] { "adjoint", "assistant", "adjunto", "assistente" }.Any(f.Contains);
        }

        /// <summary>Compte rendu lisible dans %APPDATA%\FM27Scout\pilote.txt.</summary>
        static void SaveReport(Report rep)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"{(rep.Applied ? "APPLIQUÉ" : "APERÇU")} le {rep.At:dd/MM/yyyy HH:mm} — {rep.Orders} ordre(s), {rep.Errors.Count} erreur(s)");
                sb.AppendLine("\nIntensité de l'équipe");
                for (int i = 0; i < 5; i++) sb.AppendLine($"  {Training.FormStates[i]} : {rep.TeamBefore[i]} -> {rep.TeamAfter[i]}");
                sb.AppendLine("\nJoueurs");
                foreach (var p in rep.Players) sb.AppendLine($"  {p.Name,-26} cond {p.Cond,4}  rythme {p.Sharp,4}  fatigue {(p.Fatigue == int.MinValue ? "?" : p.Fatigue.ToString()),5}  -> {(p.Injured ? "Blessé" : OptionShort(p.Want)),-18} {p.Why}" + (p.Focus != "" ? $"  | focus : {p.FocusBefore} -> {p.Focus} ({p.FocusWhy})" : ""));
                sb.AppendLine("\nEntraîneurs (note sur 20 : avant -> après)");
                foreach (var c in rep.Coaches)
                    sb.AppendLine($"  {c.Name,-26} {c.Role,-28} " + string.Join("  ", Enumerable.Range(0, 9).Where(k => c.Enabled[k]).Select(k => $"{CatName(k)} {c.Rating[k]}{(c.Checked[k] ? "*" : "")}{(c.Plan[k] ? "+" : "")}")));
                sb.AppendLine("\nSemaines");
                foreach (var (mon, name, why) in rep.Weeks) sb.AppendLine($"  {mon:dd/MM} : {name} ({why})");
                sb.AppendLine("\nGroupes d'encadrement");
                foreach (var g in rep.ExistingGroups) sb.AppendLine("  existant : " + g);
                foreach (var g in rep.Groups) sb.AppendLine($"  {g.Name} : {string.Join(", ", g.Members.Select(m => m.Name))} — {g.Why}");
                foreach (var n in rep.Notes) sb.AppendLine("note : " + n);
                foreach (var e in rep.Errors) sb.AppendLine("ERREUR : " + e);
                System.IO.File.WriteAllText(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FM27Scout", "pilote.txt"), sb.ToString());
            }
            catch { }
        }

        // ================================================================== focus individuel (faiblesses)

        const uint evFocus = 1280659764, pFocusState = 1347709286, kFocusNow = 1916366156, kFocusNew = 1916366157, kFocusList = 1147235651;

        /// <summary>Attribut de la base → focus individuel de FM27 (dans l'ordre de préférence).</summary>
        static readonly Dictionary<string, string[]> FocusOf = new()
        {
            ["AttributeAcceleration"] = new[] { "Vivacité" }, ["AttributePace"] = new[] { "Vivacité" },
            ["AttributeAgility"] = new[] { "Agilité et équilibre" }, ["AttributeBalance"] = new[] { "Agilité et équilibre" },
            ["AttributeStrength"] = new[] { "Résistance" }, ["AttributeStamina"] = new[] { "Endurance" },
            ["AttributeFinishing"] = new[] { "Conversion des occasions", "Tirs" }, ["AttributeComposure"] = new[] { "Conversion des occasions" },
            ["AttributeVision"] = new[] { "Création d'occasions", "Passes" }, ["AttributeFlair"] = new[] { "Trente derniers mètres" },
            ["AttributeDribbling"] = new[] { "Progression du ballon" }, ["AttributeWorkRate"] = new[] { "Pressing" }, ["AttributeAggression"] = new[] { "Pressing" },
            ["AttributePositioning"] = new[] { "Placement défensif", "Couverture GB" }, ["AttributeConcentration"] = new[] { "Placement défensif" },
            ["AttributeAnticipation"] = new[] { "Placement défensif", "Couverture GB" }, ["AttributeBravery"] = new[] { "Contenir l'adversaire" },
            ["AttributeMovement"] = new[] { "Déplacements offensifs" }, ["AttributeOffTheBall"] = new[] { "Déplacements offensifs" },
            ["AttributeTechnique"] = new[] { "Contrôle de balle", "Trente derniers mètres" }, ["AttributeFirstTouch"] = new[] { "Contrôle de balle" },
            ["AttributeLongShots"] = new[] { "Tirs" }, ["AttributePassing"] = new[] { "Passes", "Relances (GB)" }, ["AttributeCrossing"] = new[] { "Centres" },
            ["AttributeTackling"] = new[] { "Tacles" }, ["AttributeMarking"] = new[] { "Marquage" },
            ["AttributeHeading"] = new[] { "Jeu aérien" }, ["AttributeJumpingReach"] = new[] { "Jeu aérien" },
            ["AttributeDecisions"] = new[] { "Conservation du ballon" }, ["AttributeTeamwork"] = new[] { "Conservation du ballon" },
            ["AttributeFreeKicks"] = new[] { "Coups francs" }, ["AttributeCorners"] = new[] { "Tirer les corners" },
            ["AttributePenaltyTaking"] = new[] { "Penalties" }, ["AttributeLongThrows"] = new[] { "Touches longues" },
            // gardiens
            ["AttributeReflexes"] = new[] { "Arrêts du gardien" }, ["AttributeOneOnOnes"] = new[] { "Arrêts du gardien" },
            ["AttributeHandling"] = new[] { "Prises de balle (GB)" }, ["AttributeAerialReach"] = new[] { "Prises de balle (GB)" },
            ["AttributeCommandOfArea"] = new[] { "Prises de balle (GB)", "Couverture GB" }, ["AttributeRushingOut"] = new[] { "Couverture GB" },
            ["AttributeCommunication"] = new[] { "Couverture GB" }, ["AttributeKicking"] = new[] { "Relances (GB)" }, ["AttributeThrowing"] = new[] { "Relances (GB)" },
        };

        /// <summary>
        /// Mots reconnaissant chaque focus de FM dans une autre langue que le français (FM donne ses libellés dans la langue du jeu).
        /// Chaque alias : mots (sans accents, minuscules) qui doivent tous figurer dans le libellé de FM.
        /// </summary>
        static readonly Dictionary<string, string[]> FocusAliases = new()
        {
            ["Vivacité"] = new[] { "vivacite", "quickness", "rapidez" },
            ["Agilité et équilibre"] = new[] { "agilite", "agility", "agilidade" },
            ["Résistance"] = new[] { "resistance", "strength", "forca" },
            ["Endurance"] = new[] { "endurance", "resistencia" },
            ["Conversion des occasions"] = new[] { "conversion", "concretiza", "finalizacao" },
            ["Tirs"] = new[] { "tirs", "shooting", "remate" },
            ["Création d'occasions"] = new[] { "creation", "criacao" },
            ["Passes"] = new[] { "passes", "passing", "passe" },
            ["Trente derniers mètres"] = new[] { "trente derniers", "final third", "ultimo terco" },
            ["Progression du ballon"] = new[] { "progression", "progressao" },
            ["Pressing"] = new[] { "pressing", "pressao" },
            ["Placement défensif"] = new[] { "placement defensif", "defensive positioning", "posicionamento defensivo" },
            ["Couverture GB"] = new[] { "couverture", "sweeping", "cobertura" },
            ["Contenir l'adversaire"] = new[] { "contenir", "containment", "contencao" },
            ["Déplacements offensifs"] = new[] { "deplacements offensifs", "attacking movement", "movimentacao ofensiva", "desmarcacao" },
            ["Contrôle de balle"] = new[] { "controle de balle", "ball control", "controlo de bola", "controle de bola" },
            ["Relances (GB)"] = new[] { "relances", "distribution", "reposicao", "distribuicao" },
            ["Centres"] = new[] { "centres", "crossing", "cruzamento" },
            ["Tacles"] = new[] { "tacles", "tackling", "desarme" },
            ["Marquage"] = new[] { "marquage", "marking", "marcacao" },
            ["Jeu aérien"] = new[] { "aerien", "aerial", "aereo" },
            ["Conservation du ballon"] = new[] { "conservation", "ball retention", "retencao", "conservacao" },
            ["Coups francs"] = new[] { "coups francs", "free kick", "livres" },
            ["Tirer les corners"] = new[] { "corner", "cantos" },
            ["Penalties"] = new[] { "penalt" },
            ["Touches longues"] = new[] { "touches longues", "long throw", "lancamentos" },
            ["Arrêts du gardien"] = new[] { "arrets", "shot stopping", "defesas" },
            ["Prises de balle (GB)"] = new[] { "prises de balle", "handling", "jogo de maos", "segurar" },
        };

        // focus de rééducation (blessés) : jamais choisis
        static readonly string[] RehabWords = { "reeduc", "rehab", "reabilit", "recuperacao" };

        /// <summary>Le libellé FM d'un focus correspond-il au focus voulu (nom français exact, sinon alias dans une autre langue) ?</summary>
        static bool IsFocus(string fmName, string wanted)
        {
            if (fmName == wanted) return true;
            string f = Game.Fold(fmName);
            if (RehabWords.Any(f.Contains)) return false;
            return FocusAliases.TryGetValue(wanted, out var aliases)
                && aliases.Any(a => a.Split(' ').All(f.Contains));
        }

        static bool s_focusLogged;

        static void PlanFocus(Bridge.Context ctx, List<Player> players, List<string> notes)
        {
            var who = players.Where(p => !p.Injured && p.Row != null).ToList();
            var vals = Bridge.Read(who.Select(p => ("PlayerTrainingReference", ctx.PlayerTraining(p.Person), pFocusState.ToString())).ToList());
            for (int i = 0; i < who.Count; i++)
            {
                var p = who[i]; var st = vals[i];
                if (st == null) continue;
                p.FocusTemplate = st.Value.GetRawText();
                p.FocusBefore = Bridge.Str(Bridge.Field(Bridge.Field(st, kFocusNow), 1851878757)) ?? "";
                var options = Bridge.Items(Bridge.Field(st, kFocusList))
                    .Select(o => (name: Bridge.Str(Bridge.Field(o, 1851878757)) ?? "", value: Bridge.Int(Bridge.Field(o, 1903577653), int.MinValue)))
                    .Where(o => o.value > 0).ToList();
                var plan = Training.Individual(p.Row);
                foreach (var key in plan.GapKeys)
                {
                    if (!FocusOf.TryGetValue(key, out var names)) continue;
                    var hit = names.Select(n => options.FirstOrDefault(o => o.name == n)).FirstOrDefault(o => o.name != null);
                    if (hit.name == null) hit = names.Select(n => options.FirstOrDefault(o => IsFocus(o.name, n))).FirstOrDefault(o => o.name != null);
                    if (hit.name == null)
                    {
                        // libellés inconnus (autre langue de FM) : noter la liste une fois pour pouvoir l'ajouter
                        if (!s_focusLogged && options.Count > 0) { s_focusLogged = true; Log.Info("Focus individuels proposés par FM : " + string.Join(" | ", options.Select(o => $"{o.name}={o.value}"))); }
                        continue;
                    }
                    p.Focus = hit.name; p.FocusValue = hit.value;
                    p.FocusWhy = L.F("{0} {1} (rôle {2})", L.T(Names.Of(key)), Db.Stat(p.Row, key), L.T(plan.Role).ToLower());
                    break;
                }
            }
        }

        static string FocusJson(Player p)
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(p.FocusTemplate);
            foreach (var e in node["entries"].AsArray())
                if (e[0].GetValue<uint>() == kFocusNew)
                    e[2] = System.Text.Json.Nodes.JsonNode.Parse(Bridge.Rec((1886680684, "SI.Core.DynamicNumber", $"\"{p.FocusValue}\""), (1903577654, "System.String", Bridge.Q(p.Focus))));
            return node.ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }

        // ================================================================== groupes d'encadrement

        const uint evGroupNew = 63, evGroupSelect = 1886810230, evGroupMembers = 64, evGroupName = 74;
        const uint pCurGroup = 1714908212;
        const uint GroupsInfoProp = 1734763623;        // TeamTraining : nombre de groupes + joueurs encore libres

        static string Pairs(JsonElement refObj) => string.Join(";", refObj.GetProperty("pairs").EnumerateArray().Select(p => $"{p[0].GetUInt32()}={p[1].GetInt32()}"));

        /// <summary>Joueurs déjà dans un groupe (FM n'autorise qu'un groupe par joueur) : FM donne la liste des joueurs encore libres.</summary>
        static HashSet<int> ReadGrouped(Bridge.Context ctx, Report rep, List<Player> squad)
        {
            var info = Bridge.Read1("TeamTrainingReference", ctx.TeamTraining, GroupsInfoProp.ToString());
            int count = Bridge.Int(Bridge.Field(info, 1668771444), 0);
            var free = Bridge.Items(Bridge.Field(info, 1800099193)).Select(x => Bridge.PairOf(x, kPerson)).Where(x => x != 0).ToHashSet();
            var grouped = free.Count == 0 ? new HashSet<int>() : squad.Select(p => p.Person).Where(p => !free.Contains(p)).ToHashSet();
            if (count > 0) rep.ExistingGroups.Add(L.F("{0} groupe(s) déjà en place, {1} joueur(s) déjà encadré(s) : on ne les touche pas", count, grouped.Count));
            return grouped;
        }

        /// <summary>
        /// Groupes à créer : chaque jeune à fort potentiel et au caractère fragile rejoint deux joueurs expérimentés,
        /// professionnels et déterminés, de son secteur de jeu si possible (FM limite un groupe à 3 joueurs).
        /// </summary>
        static List<Group> PlanGroups(List<Player> players, HashSet<int> grouped)
        {
            var byRow = players.Where(p => p.Row != null && !grouped.Contains(p.Person)).ToDictionary(p => p.Row, p => p);
            var plan = Training.Mentoring(byRow.Keys.ToList());
            var used = new HashSet<PlayerRow>();
            var res = new List<Group>();
            foreach (var (young, mentors) in plan)
            {
                if (used.Contains(young)) continue;
                var ms = mentors.Where(m => !used.Contains(m) && m != young).Take(2).ToList();
                if (ms.Count < 2) continue;                                       // FM exige 3 joueurs
                var g = new Group();
                g.Members.Add(byRow[young]); used.Add(young);
                // FM : exactement 3 joueurs par groupe (1 jeune + 2 tuteurs)
                foreach (var m in ms) { g.Members.Add(byRow[m]); used.Add(m); }
                string youngNames = g.Members[0].Name.Split(' ').Last();
                g.Name = L.F("Encadrement {0}", youngNames);                    // nom du groupe écrit dans FM, dans la langue de l'app
                g.Why = L.F("{0} encadré(s) par {1} et {2}", youngNames, ms[0].Name, ms[1].Name);
                res.Add(g);
            }
            return res;
        }

        static void CreateGroup(Bridge.Context ctx, Group g)
        {
            Bridge.Send(evGroupNew, "TeamTrainingReference", ctx.TeamTraining, "[\"SI.Bindable.NullReference\",\"\"]");
            var cur = Bridge.Read1("TeamTrainingReference", ctx.TeamTraining, pCurGroup.ToString());
            if (cur is not { ValueKind: JsonValueKind.Object } c || !c.TryGetProperty("pairs", out _)) throw new Exception(L.T("FM n'a pas créé le groupe"));
            string gp = Pairs(c);
            Bridge.Send(evGroupSelect, "MentoringGroupReference", gp, "[\"SI.Bindable.NullReference\",\"\"]");
            string members = "[" + string.Join(",", g.Members.Select(m =>
                $"[\"FM.UI.PersonReference\",{{\"type\":\"FM.UI.PersonReference\",\"pairs\":[[825307168,28],[1198877028,1],[{kPerson},{m.Person}]]}}]")) + "]";
            Bridge.WriteChannel("MentoringGroupReference", gp, 1919492167, members);       // sélection des joueurs (les cases cochées)
            System.Threading.Thread.Sleep(300);
            Bridge.Send(evGroupMembers, "MentoringGroupReference", gp, members);
            Bridge.Send(evGroupName, "MentoringGroupReference", gp, $"[\"System.String\",{Bridge.Q(g.Name)}]");
        }

        // ================================================================== tout faire

        /// <summary>Analyse (et applique si apply = true). progress reçoit l'étape en cours.</summary>
        public static Report Run(bool apply, List<PlayerRow> squad, Action<string> progress)
        {
            var rep = new Report();
            var set = Training.Set;
            progress(L.T("Connexion à FM27…"));
            var ctx = Bridge.Ctx();

            // --- joueurs (état réel)
            progress(L.T("Lecture de l'état de forme des joueurs…"));
            rep.Players = ReadPlayers(ctx);
            PlanPlayers(rep.Players, rep.Notes);
            int tired = rep.Players.Count(p => p.Fatigue != int.MinValue && p.Fatigue >= 200) + rep.Players.Count(p => p.Cond is >= 0 and < 85);
            bool squadTired = tired >= Math.Max(4, rep.Players.Count / 4);

            // --- plan d'équipe (besoins + calendrier) avec la fatigue réelle
            var men = squad.Where(r => !r.Female).ToList();
            if (men.Count < 11) men = squad;
            var eleven = Features.BestEleven(men, Tactic.CurrentSlots()).eleven.Where(x => x.r != null).Select(x => x.r).ToList();
            var start = Game.Date.Date;
            var matches = new HashSet<DateTime>(Settings.Cur.MatchDays.Select(s => DateTime.TryParse(s, out var d) ? d.Date : DateTime.MinValue).Where(d => d >= start && d < start.AddDays(28)));
            if (matches.Count == 0) for (int i = 0; i < 28; i++) if (start.AddDays(i).DayOfWeek == DayOfWeek.Saturday) matches.Add(start.AddDays(i));
            var needs = Training.TeamNeeds(eleven);
            var weeks = Training.Plan(start, matches, needs, squadTired ? 100 : 0);
            double attNeed = needs.Where(n => n.axis is "Finition" or "Création" or "Technique").Sum(n => Math.Max(0, n.gap));
            double defNeed = needs.Where(n => n.axis is "Défense" or "Jeu aérien").Sum(n => Math.Max(0, n.gap));
            rep.TeamAfter = weeks[0].TeamIntensity;
            if (squadTired) rep.Notes.Add(L.F("{0} joueurs fatigués ou en condition < 85 % : intensité d'équipe prudente", tired));

            progress(L.T("Lecture des réglages d'intensité actuels…"));
            var cur = Bridge.Read1("TeamTrainingReference", ctx.TeamTraining, pTeamRows.ToString());
            var rows = cur is { ValueKind: JsonValueKind.Object } one ? new List<JsonElement> { one } : Bridge.Items(cur).ToList();
            foreach (var r in rows)
            {
                int idx = Bridge.Int(Bridge.Field(r, pCond), -1);
                if (idx is >= 0 and < 5) rep.TeamBefore[idx] = Bridge.Str(Bridge.Field(Bridge.Field(r, 1247966831), 1903577654)) ?? "?";
            }

            // --- entraîneurs (notes réelles)
            progress(L.T("Lecture des entraîneurs…"));
            rep.Coaches = ReadCoaches(ctx, true);
            if (set.ApplyCoaches && rep.Coaches.Count > 0)
            {
                MeasureRatings(ctx, rep.Coaches, progress);
                StabilizeRatings(rep.Coaches);
                PlanCoaches(rep.Coaches, rep.Notes);
            }
            else foreach (var c in rep.Coaches) Array.Copy(c.Checked, c.Plan, 9);

            // --- programmes des semaines (uniquement en saison : FM gère lui-même la pré-saison)
            if (set.ApplyWeeks)
            {
                progress(L.T("Calendrier…"));
                DateTime? first = null;
                try { first = NextCompetitive(ctx); } catch (Exception e) { rep.Notes.Add(L.T("prochain match non lu : ") + e.Message); }
                if (first == null) rep.Notes.Add(L.T("prochain match amical ou inconnu : programmes de pré-saison laissés à FM"));
                else
                    for (int k = 0; k < 4; k++)
                    {
                        var mon = Monday(start).AddDays(7 * (k + 1));
                        if (mon < Monday(first.Value)) continue;
                        int mc = matches.Count(m => m >= mon && m < mon.AddDays(7));
                        var (_, _, name, why) = Programme(mon, mc, attNeed, defNeed);
                        rep.Weeks.Add((mon, name, why));
                    }
            }

            if (set.ApplyFocus)
            {
                progress(L.T("Focus individuel (faiblesses)…"));
                try { PlanFocus(ctx, rep.Players, rep.Notes); }
                catch (Exception e) { rep.Notes.Add(L.T("focus individuel non lu : ") + e.Message); }
            }

            if (set.ApplyMentoring)
            {
                progress(L.T("Groupes d'encadrement…"));
                try { rep.Groups = PlanGroups(rep.Players, ReadGrouped(ctx, rep, rep.Players)); }
                catch (Exception e) { rep.Notes.Add(L.T("groupes d'encadrement non lus : ") + e.Message); }
            }

            if (!apply) { s_last = rep; SaveReport(rep); return rep; }

            // ================================================================== application
            rep.Applied = true;
            if (set.ApplyTeam)
            {
                progress(L.T("Intensité de l'équipe…"));
                for (int i = 0; i < 5; i++)
                {
                    if (rep.TeamBefore[i] == TeamOptions[TeamOption(rep.TeamAfter[i])]) continue;
                    try { Bridge.TeamIntensity(i, TeamOption(rep.TeamAfter[i])); rep.Orders++; }
                    catch (Exception e) { rep.Errors.Add(L.F("intensité « {0} » : {1}", L.T(Training.FormStates[i]), e.Message)); }
                }
            }
            if (set.ApplyPlayers)
            {
                progress(L.T("Intensité individuelle…"));
                // l'adjoint (référence attendue par FM dans l'ordre) : l'entraîneur adjoint, sinon le premier entraîneur
                int assistant = rep.Coaches.FirstOrDefault(c => IsAssistant(c.Role))?.Person
                              ?? rep.Coaches.FirstOrDefault(c => c.Person != ctx.Manager)?.Person ?? ctx.Manager;
                foreach (var p in rep.Players.Where(p => !p.Injured))
                {
                    string json = Bridge.Rec(
                        (1903577654, "System.String", Bridge.Q(OptionLabel(p.Want))),
                        (1702257780, "SI.Bindable.DynamicReference", Bridge.Rec(
                            (1416914273, "SI.Bindable.DynamicReference", Bridge.Rec((kPerson, "SI.Core.DynamicNumber", $"\"{ctx.Team}\""))),
                            (1313885274, "SI.Bindable.DynamicReference", Bridge.Rec((kPerson, "SI.Core.DynamicNumber", $"\"{assistant}\""))))),
                        (1215644527, "System.Boolean", "\"True\""),
                        (840966176, "System.Boolean", p.Resting ? "\"True\"" : "\"False\""),
                        (1886680684, "SI.Core.DynamicNumber", $"\"{p.Want}\""));
                    try { Bridge.Send(evPlayerInt, "PlayerTrainingReference", ctx.PlayerTraining(p.Person), json); rep.Orders++; }
                    catch (Exception e) { rep.Errors.Add($"{p.Name} : {e.Message}"); }
                }
            }
            if (set.ApplyCoaches)
            {
                progress(L.T("Affectation des entraîneurs…"));
                // d'abord les ajouts, ensuite les retraits (un domaine n'est jamais vide en cours de route)
                foreach (var c in rep.Coaches) for (int cat = 0; cat < 9; cat++)
                        if (c.Plan[cat] && !c.Checked[cat])
                            try { Bridge.Send(evAssign, "TeamTrainingReference", ctx.TeamTraining, Assign(c.Index, cat)); rep.Orders++; }
                            catch (Exception e) { rep.Errors.Add($"{c.Name} → {L.T(CatName(cat))} : {e.Message}"); }
                foreach (var c in rep.Coaches) for (int cat = 0; cat < 9; cat++)
                        if (!c.Plan[cat] && c.Checked[cat])
                            try { Bridge.Send(evUnassign, "TeamTrainingReference", ctx.TeamTraining, Assign(c.Index, cat)); rep.Orders++; }
                            catch (Exception e) { rep.Errors.Add(L.F("{0} retiré de {1} : {2}", c.Name, L.T(CatName(cat)), e.Message)); }
                // vérification
                var after = ReadCoaches(ctx, false);
                int bad = after.Sum(a => { var c = rep.Coaches.FirstOrDefault(x => x.Index == a.Index); return c == null ? 0 : Enumerable.Range(0, 9).Count(k => a.Checked[k] != c.Plan[k]); });
                if (bad > 0) rep.Errors.Add(L.F("{0} affectation(s) refusée(s) par FM", bad));
            }
            if (set.ApplyWeeks)
            {
                progress(L.T("Programmes des semaines…"));
                foreach (var (mon, name, _) in rep.Weeks)
                {
                    var (idx, unid, _) = Programmes.First(p => p.name == name);
                    string json = Bridge.Rec(
                        (1131498817, "FM.GamePlugin.GameDate", Bridge.Q(mon.ToString("yyyy-MM-dd"))),
                        (1702257780, "SI.Bindable.DynamicReference", Bridge.Rec(
                            (1954115685, "SI.Core.DynamicNumber", "\"0\""), (1886745411, "System.Boolean", "\"False\""),
                            (1953723504, "SI.Core.DynamicNumber", $"\"{idx}\""), (1953713495, "SI.Core.DynamicNumber", "\"1\""),
                            (1953715785, "SI.Core.DynamicNumber", "\"0\""), (1970170212, "SI.Core.DynamicNumber", $"\"{unid}\""),
                            (1953719397, "System.String", "\"\""))),
                        (1832409396, "SI.Bindable.DynamicReference", Bridge.Rec(
                            (1986096245, "System.Boolean", "\"True\""),
                            (1313885274, "SI.Bindable.DynamicReference", Bridge.Rec((kPerson, "SI.Core.DynamicNumber", "\"-8388609\""))))));
                    try { Bridge.Send(evWeek, "CalendarReference", ctx.Calendar, json); rep.Orders++; }
                    catch (Exception e) { rep.Errors.Add(L.F("semaine du {0:dd/MM} : {1}", mon, e.Message)); }
                }
            }
            if (set.ApplyFocus)
            {
                progress(L.T("Focus individuel…"));
                foreach (var p in rep.Players.Where(p => p.FocusValue > 0 && p.FocusTemplate != null && p.FocusBefore != p.Focus))
                    try { Bridge.Send(evFocus, "PlayerTrainingReference", ctx.PlayerTraining(p.Person), FocusJson(p)); rep.Orders++; }
                    catch (Exception e) { rep.Errors.Add(L.F("focus de {0} : {1}", p.Name, e.Message)); }
            }
            if (set.ApplyMentoring && rep.Groups.Count > 0)
            {
                progress(L.T("Création des groupes d'encadrement…"));
                foreach (var g in rep.Groups)
                    try { CreateGroup(ctx, g); rep.Orders += 4; }
                    catch (Exception e) { rep.Errors.Add(L.F("groupe « {0} » : {1}", g.Name, e.Message)); }
            }
            Training.SaveSet();
            SaveReport(rep);
            Log.Info($"Entraînement appliqué via FM27 Bridge : {rep.Orders} ordre(s), {rep.Errors.Count} erreur(s)" + (rep.Errors.Count > 0 ? " — " + string.Join(" | ", rep.Errors) : ""));
            s_last = rep;
            return rep;
        }
    }
}
