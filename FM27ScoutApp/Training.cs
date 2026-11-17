using System;
using System.Collections.Generic;
using System.Linq;

namespace FM27Scout
{
    /// <summary>
    /// Planning d'entraînement sur 4 semaines + plan individuel.
    /// Tout est calculé à partir des attributs (visibles et cachés) : ce sont des règles de bon sens
    /// (récupération après match, pas de physique la veille, priorité aux points faibles), pas des formules du jeu.
    /// La fatigue est ESTIMÉE (fragilité, âge, qualités physiques naturelles, endurance) tant que
    /// l'état de forme réel n'est pas lu dans la mémoire.
    /// </summary>
    internal static class Training
    {
        // ================================================================== modèle FM27

        /// <summary>Les 9 domaines d'entraînement de FM27 (colonnes « Affectations des entraîneurs »).</summary>
        public static readonly string[] Areas =
        {
            "Tactique offensive", "Technique offensive", "Tactique défensive", "Technique défensive",
            "Tactique de possession", "Technique avec le ballon", "Gardien", "Condition physique", "Coups de pied arr.",
        };

        /// <summary>Intensités proposées par FM27 (écran « Intensité entraînement »).</summary>
        public const string NoPitch = "Aucun travail sur le terrain", Half = "Semi-intensité", Normal = "Intensité normale";
        public static readonly string[] FormStates = { "En très mauvaise forme", "En mauvaise forme", "Plutôt en forme", "En bonne forme", "En très bonne forme" };

        // famille d'attributs (axes du radar) -> domaines FM27 qui la travaillent
        static readonly Dictionary<string, string[]> AxisAreas = new()
        {
            ["Défense"] = new[] { "Tactique défensive", "Technique défensive" },
            ["Finition"] = new[] { "Technique offensive", "Tactique offensive" },
            ["Création"] = new[] { "Tactique de possession" },
            ["Technique"] = new[] { "Technique avec le ballon" },
            ["Physique"] = new[] { "Condition physique" },
            ["Vitesse"] = new[] { "Condition physique" },
            ["Jeu aérien"] = new[] { "Coups de pied arr.", "Technique défensive" },
            ["Mental"] = new[] { "Tactique de possession", "Tactique défensive" },
        };

        /// <summary>Réglages du mode expert (sauvegardés).</summary>
        public sealed class TrainSettings
        {
            public bool Expert { get; set; }
            public string Style { get; set; } = "normal";                                  // prudent / normal / pousse
            public Dictionary<string, string> AreaMode { get; set; } = new();              // domaine -> auto / force / exclu
            public Dictionary<string, string> Overrides { get; set; } = new();             // joueur -> intensité forcée
            public bool ApplyTeam { get; set; } = true;                                    // pilote : parties à régler dans FM
            public bool ApplyPlayers { get; set; } = true;
            public bool ApplyCoaches { get; set; } = true;
            public bool ApplyWeeks { get; set; } = true;
            public bool ApplyMentoring { get; set; } = true;
            public bool ApplyFocus { get; set; } = true;                                   // focus individuel sur les faiblesses
            public Dictionary<string, int> WeekProgramme { get; set; } = new();          // lundi -> priorité choisie (stabilité)
        }
        static TrainSettings s_set;
        public static TrainSettings Set => s_set ??= Db.LoadJson("entrainement.json", new TrainSettings());
        public static void SaveSet() => Db.SaveJson("entrainement.json", Set);

        /// <summary>Seuil de risque (fatigue estimée) au-delà duquel on lève le pied, selon le style.</summary>
        public static int RiskLimit => !Set.Expert ? 45 : Set.Style switch { "prudent" => 35, "pousse" => 60, _ => 45 };

        public sealed record Week(DateTime Start, List<DateTime> Matches, List<(string area, string why)> Focus, string[] TeamIntensity, string Note);

        /// <summary>
        /// Plan sur 4 semaines : pour chaque semaine, les domaines à mettre en avant (selon les points faibles de l'équipe,
        /// en variant d'une semaine à l'autre) et les réglages d'intensité par état de forme.
        /// Semaine à 2 matchs = 1 seul domaine et intensité prudente ; pas de « Condition physique » si l'effectif est fatigué
        /// ou s'il y a plus d'un match.
        /// </summary>
        public static List<Week> Plan(DateTime start, HashSet<DateTime> matches, List<(string axis, double team, double reference, double gap)> needs, int squadRisk)
        {
            var score = Areas.ToDictionary(a => a, a => 0.0);
            foreach (var n in needs.Where(n => n.gap > 0))
                if (AxisAreas.TryGetValue(n.axis, out var areas)) foreach (var a in areas) score[a] += n.gap / areas.Length;
            score["Gardien"] = -1;   // les gardiens ont leur propre préparateur : traité à part
            var why = new Dictionary<string, string>();
            foreach (var n in needs.Where(n => n.gap > 0))
                if (AxisAreas.TryGetValue(n.axis, out var areas)) foreach (var a in areas) why.TryAdd(a, $"{n.axis.ToLower()} en retard de {n.gap:F1} sur le niveau");
            if (Set.Expert)
                foreach (var (a, m) in Set.AreaMode)
                {
                    if (!score.ContainsKey(a)) continue;
                    if (m == "force") { score[a] += 100; why[a] = "imposé (mode expert)"; }
                    if (m == "exclu") score[a] = -100;
                }

            var weeks = new List<Week>();
            var lastUsed = new Dictionary<string, int>();
            for (int w = 0; w < 4; w++)
            {
                var ws = start.Date.AddDays(7 * w);
                var ms = matches.Where(d => d >= ws && d < ws.AddDays(7)).OrderBy(d => d).ToList();
                int count = ms.Count >= 2 ? 1 : ms.Count == 1 ? 2 : 3;
                bool tired = squadRisk >= RiskLimit;
                // 1er créneau : le plus gros besoin ; suivants : autres besoins nets (≥ 0,3), sinon entretien tournant
                var ranked = score.Where(kv => kv.Value > -50 && kv.Key != "Gardien")
                    .Where(kv => kv.Key != "Condition physique" || (ms.Count <= 1 && !tired) || kv.Value >= 100)
                    .OrderByDescending(kv => kv.Value).ToList();
                var pick = new List<string>();
                foreach (var kv in ranked)
                    if (pick.Count < count && (kv.Value >= 100 || (kv.Value >= 0.3 && (pick.Count == 0 || !(lastUsed.TryGetValue(kv.Key, out int lw) && lw == w - 1) || kv.Value >= 0.8))))
                        pick.Add(kv.Key);
                string[] maintenance = { "Tactique de possession", "Tactique défensive", "Tactique offensive", "Coups de pied arr.", "Technique avec le ballon" };
                for (int k = 0; pick.Count < count && k < maintenance.Length * 2; k++)
                {
                    string a = maintenance[(w + k) % maintenance.Length];
                    if (!pick.Contains(a) && !(Set.Expert && Set.AreaMode.GetValueOrDefault(a) == "exclu")) pick.Add(a);
                }
                foreach (var a in pick) lastUsed[a] = w;
                var focus = pick.Select(a => (a, why.GetValueOrDefault(a, "entretien (rotation pour garder l'équipe en place)"))).ToList();

                // intensité par état de forme (valeurs proposées par FM27)
                bool careful = ms.Count >= 2 || tired || (Set.Expert && Set.Style == "prudent");
                bool push = Set.Expert && Set.Style == "pousse" && ms.Count <= 1 && !tired;
                string[] ti = careful ? new[] { NoPitch, NoPitch, Half, Normal, Normal }
                            : push ? new[] { Half, Half, Normal, Normal, Normal }
                            : new[] { NoPitch, Half, Normal, Normal, Normal };
                string note = ms.Count >= 2 ? "semaine chargée : récupération avant tout"
                            : ms.Count == 0 ? "semaine sans match : bon moment pour travailler le fond"
                            : tired ? "effectif fatigué : on lève le pied" : "semaine normale";
                weeks.Add(new Week(ws, ms, focus, ti, note));
            }
            return weeks;
        }

        /// <summary>Écart de l'équipe (onze type) avec les joueurs de même niveau dans toute la base, par famille.</summary>
        public static List<(string axis, double team, double reference, double gap)> TeamNeeds(List<PlayerRow> eleven)
        {
            var res = new List<(string, double, double, double)>();
            if (eleven.Count == 0) return res;
            double ca = eleven.Average(r => r.CA);
            bool fem = eleven[0].Female;
            var peers = Game.Rows.Where(r => r.Female == fem && Math.Abs(r.CA - ca) <= 8 && r.Positions[0] < 15).Take(4000).ToList();
            var field = eleven.Where(r => r.Positions[0] < 15).ToList();
            if (peers.Count < 50 || field.Count == 0) return res;
            foreach (var (label, keys) in Features.RadarAxes)
            {
                if (!AxisAreas.ContainsKey(label)) continue;
                double t = field.Average(r => Features.Axis(r, keys)), refv = peers.Average(r => Features.Axis(r, keys));
                res.Add((label, t, refv, refv - t));
            }
            return res.OrderByDescending(x => x.Item4).ToList();
        }

        /// <summary>Risque de fatigue / blessure estimé (0..100) à partir des attributs.</summary>
        public static int FatigueRisk(PlayerRow r)
        {
            int inj = Db.Stat(r, "InjuryProneness"), nat = Db.Stat(r, "AttributeNaturalFitness"), sta = Db.Stat(r, "AttributeStamina");
            int age = Game.Age(r);
            double s = 0;
            if (inj > 0) s += (inj - 1) * 2.6;                 // 0..50
            if (nat > 0) s += (20 - nat) * 1.2;                // 0..23
            if (sta > 0) s += (20 - sta) * 0.6;                // 0..11
            if (age >= 30) s += (age - 29) * 3;                // +3 par an après 29 ans
            return (int)Math.Clamp(Math.Round(s), 0, 100);
        }

        public static string RiskLabel(int risk) => risk >= 55 ? "élevé" : risk >= 35 ? "moyen" : "faible";

        /// <summary>Intensité individuelle conseillée (ou forcée en mode expert).</summary>
        public static string IndividualIntensity(PlayerRow r, int risk)
        {
            if (Set.Expert && Set.Overrides.TryGetValue(Features.Key(r).ToString(), out var o)) return o;
            int lim = RiskLimit;
            return risk >= lim + 25 ? NoPitch : risk >= lim + 5 ? Half : Normal;
        }

        // ================================================================== plan individuel

        static readonly Dictionary<string, string[]> RolePositions = new()
        {
            ["Gardien"] = new[] { "GK" }, ["Défenseur central"] = new[] { "DC" }, ["Latéral"] = new[] { "DL", "DR" },
            ["Piston"] = new[] { "WBL", "WBR" }, ["Milieu défensif"] = new[] { "DM", "MC" }, ["Milieu relayeur"] = new[] { "MC", "DM" },
            ["Meneur de jeu"] = new[] { "MC", "AMC", "DM" }, ["Ailier"] = new[] { "ML", "MR", "AML", "AMR" },
            ["Attaquant intérieur"] = new[] { "AML", "AMR" }, ["Milieu offensif"] = new[] { "AMC" },
            ["Buteur"] = new[] { "ST" }, ["Pivot"] = new[] { "ST" },
        };

        public static string[] RolePositionsOf(string role) => RolePositions.TryGetValue(role, out var p) ? p : Array.Empty<string>();

        public sealed record IndividualPlan(string Role, string Focus, string Targets, string Load, int Risk, string Note, int Priority)
        {
            /// <summary>Attributs à travailler, du plus urgent au moins urgent (clés de la base).</summary>
            public List<string> GapKeys { get; init; } = new();
        }

        public static IndividualPlan Individual(PlayerRow r)
        {
            // rôle : le meilleur parmi ceux joués à un poste naturel (≥ 15)
            var roles = Db.Roles.Where(ro => RolePositions.TryGetValue(ro.Nom, out var ps) && ps.Any(p => r.Positions[Array.IndexOf(Db.PositionKeys, p)] >= 15)).ToList();
            if (roles.Count == 0) roles = Db.Roles;
            var role = roles.OrderByDescending(ro => Db.Rating(r, ro)).First();

            // attributs à travailler : les plus gros manques sur les attributs importants du rôle
            var gaps = role.Poids.Select(kv => (key: kv.Key == "AttributeOffTheBall" ? "AttributeMovement" : kv.Key, w: kv.Value))
                .Select(x => (x.key, v: Db.Stat(r, x.key), x.w)).Where(x => x.v > 0)
                .Select(x => (x.key, x.v, score: (20 - x.v) * x.w)).OrderByDescending(x => x.score).Take(3).ToList();
            string focus = gaps.Count > 0 ? Names.Of(gaps[0].key) : "—";
            string targets = string.Join(", ", gaps.Select(g => $"{Names.Of(g.key)} {g.v}"));

            int risk = FatigueRisk(r), age = Game.Age(r), margin = r.PA - r.CA;
            string load = IndividualIntensity(r, risk);

            var notes = new List<string>();
            if (margin <= 3) notes.Add("proche de son potentiel : le focus rapportera peu, privilégier le maintien");
            else if (age is >= 0 and <= 21 && margin >= 15) notes.Add("gros potentiel : du temps de jeu (prêt ou rotation) l'aidera autant que l'entraînement");
            int pro = Db.Stat(r, "AttributeProfessionalism"), det = Db.Stat(r, "AttributeDetermination");
            if (age is >= 0 and <= 21 && (pro is >= 1 and <= 11 || det is >= 1 and <= 11)) notes.Add("à mettre dans un groupe d'encadrement");
            if (Db.Stat(r, "InjuryProneness") >= 15) notes.Add("très fragile : éviter les semaines avec 2 matchs");
            var pts = Features.Points(r);
            if (pts.Count >= 3 && margin >= 10 && pts[^1].ca <= pts[0].ca && (pts[^1].date - pts[0].date).TotalDays >= 45)
                notes.Add($"stagne depuis le {pts[0].date:dd/MM} : changer de focus ou lui donner du temps de jeu");

            int prio = Math.Max(0, margin) * (age is >= 0 and <= 23 ? 2 : 1) + (notes.Any(n => n.StartsWith("stagne")) ? 20 : 0);
            return new(role.Nom, focus, targets, load, risk, string.Join(" · ", notes), prio)
            {
                GapKeys = role.Poids.Select(kv => (key: kv.Key == "AttributeOffTheBall" ? "AttributeMovement" : kv.Key, w: kv.Value))
                    .Select(x => (x.key, v: Db.Stat(r, x.key), x.w)).Where(x => x.v > 0)
                    .OrderByDescending(x => (20 - x.v) * x.w).Take(6).Select(x => x.key).ToList(),
            };
        }

        // ================================================================== tutorat

        /// <summary>Jeunes à encadrer (personnalité fragile, gros potentiel) et tuteurs possibles (pro, déterminés, expérimentés).</summary>
        public static List<(PlayerRow young, List<PlayerRow> mentors)> Mentoring(List<PlayerRow> squad)
        {
            var mentors = squad.Where(r => Game.Age(r) >= 24 && Db.Stat(r, "AttributeProfessionalism") >= 15 && Db.Stat(r, "AttributeDetermination") >= 14)
                .OrderByDescending(r => Db.Stat(r, "AttributeProfessionalism") + Db.Stat(r, "AttributeDetermination") + r.CA / 20).ToList();
            var young = squad.Where(r => Game.Age(r) is >= 0 and <= 21 && r.PA - r.CA >= 10 &&
                    (Db.Stat(r, "AttributeProfessionalism") is >= 1 and <= 12 || Db.Stat(r, "AttributeDetermination") is >= 1 and <= 12))
                .OrderByDescending(r => r.PA).ToList();
            // un tuteur du même secteur de jeu de préférence
            static int Line(PlayerRow r) => r.Positions[0] >= 15 ? 0 : new[] { 2, 3, 4, 13, 14 }.Any(i => r.Positions[i] >= 15) ? 1 : new[] { 5, 6, 7, 8 }.Any(i => r.Positions[i] >= 15) ? 2 : 3;
            return young.Select(y => (y, mentors.OrderByDescending(m => Line(m) == Line(y)).Take(3).ToList())).ToList();
        }
    }
}
