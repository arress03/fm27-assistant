// Logique des fonctions : stats, recherches, radar de scouting, alertes, notes par rôle, export CSV.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FM27Scout
{
    internal static class Log
    {
        static readonly string PathLog = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FM27Scout", "journal.txt");
        public static void Info(string m) => Write("INFO", m);
        public static void Warn(string m) => Write("ATTENTION", m);
        static void Write(string lvl, string m)
        {
            try { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathLog)); File.AppendAllText(PathLog, $"{DateTime.Now:HH:mm:ss} [{lvl}] {m}\n"); } catch { }
        }
    }

    /// <summary>Disposition du bloc d'attributs (offset -> nom).</summary>
    internal static class Layout
    {
        public static readonly (string prop, int off)[] Visible =
        {
            ("AttributeCrossing", 0x00), ("AttributeDribbling", 0x01), ("AttributeFinishing", 0x02),
            ("AttributeHeading", 0x03), ("AttributeLongShots", 0x04), ("AttributeMarking", 0x05),
            ("AttributeMovement", 0x06), ("AttributePassing", 0x07), ("AttributePenaltyTaking", 0x08),
            ("AttributeTackling", 0x09), ("AttributeVision", 0x0A), ("AttributeHandling", 0x0B),
            ("AttributeAerialReach", 0x0C), ("AttributeCommandOfArea", 0x0D), ("AttributeCommunication", 0x0E),
            ("AttributeKicking", 0x0F), ("AttributeThrowing", 0x10), ("AttributeAnticipation", 0x11),
            ("AttributeDecisions", 0x12), ("AttributeOneOnOnes", 0x13), ("AttributePositioning", 0x14),
            ("AttributeReflexes", 0x15), ("AttributeFirstTouch", 0x16), ("AttributeTechnique", 0x17),
            ("AttributeFlair", 0x1A), ("AttributeCorners", 0x1B), ("AttributeTeamwork", 0x1C),
            ("AttributeWorkRate", 0x1D), ("AttributeLongThrows", 0x1E), ("AttributeEccentricity", 0x1F),
            ("AttributeRushingOut", 0x20), ("AttributeTendencyToPunch", 0x21), ("AttributeAcceleration", 0x22),
            ("AttributeFreeKicks", 0x23), ("AttributeStrength", 0x24), ("AttributeStamina", 0x25),
            ("AttributePace", 0x26), ("AttributeJumpingReach", 0x27), ("AttributeLeadership", 0x28),
            ("AttributeBalance", 0x2A), ("AttributeBravery", 0x2B), ("AttributeAggression", 0x2D),
            ("AttributeAgility", 0x2E), ("AttributeNaturalFitness", 0x32), ("AttributeDetermination", 0x33),
            ("AttributeComposure", 0x34), ("AttributeConcentration", 0x35),
        };

        public static readonly (string key, int off)[] HiddenInBlock =
        {
            ("Dirtiness", 0x29), ("Consistency", 0x2C), ("ImportantMatches", 0x2F),
            ("InjuryProneness", 0x30), ("Versatility", 0x31),
        };
    }

    internal static class Db
    {
        // ------------------------------------------------------------------ clés de stats
        public static readonly string[] PositionKeys =
            { "GK", "SW", "DL", "DC", "DR", "DM", "ML", "MC", "MR", "AML", "AMC", "AMR", "ST", "WBL", "WBR" };
        public static readonly Dictionary<string, string> PositionFr = new()
        {
            ["GK"] = "GB", ["SW"] = "LIB", ["DL"] = "DG", ["DC"] = "DC", ["DR"] = "DD", ["DM"] = "MDC",
            ["ML"] = "MG", ["MC"] = "MC", ["MR"] = "MD", ["AML"] = "AG", ["AMC"] = "MOC", ["AMR"] = "AD",
            ["ST"] = "BU", ["WBL"] = "PG", ["WBR"] = "PD",
        };
        public static readonly string[] PersonalityKeys =
        {
            "AttributeAdaptability", "AttributeAmbition", "AttributeLoyalty", "AttributePressure",
            "AttributeProfessionalism", "AttributeSportsmanship", "AttributeTemperament", "AttributeControversy",
        };
        static readonly Dictionary<string, int> AttrOffsets =
            Layout.Visible.Concat(Layout.HiddenInBlock.Select(h => (h.key, h.off))).ToDictionary(x => x.Item1, x => x.Item2);

        /// <summary>Valeur 1..20 d'un attribut (visible, caché ou personnalité), CA/PA, ou -1.</summary>
        public static int Stat(PlayerRow r, string key)
        {
            if (key == "PlayerCurrentAbility" || key == "CA") return r.CA;
            if (key == "PlayerPotentialAbility" || key == "PA") return r.PA;
            if (AttrOffsets.TryGetValue(key, out int off))
            {
                int b = r.Attrs[off];
                return b < 1 || b > 100 ? -1 : Math.Max(1, (int)Math.Round(b / 5.0));
            }
            int p = Array.IndexOf(PersonalityKeys, key);
            if (p >= 0) return r.Personality[p] is >= 1 and <= 20 ? r.Personality[p] : -1;
            return -1;
        }

        /// <summary>Libellé de poste dans la langue de l'interface (clé = code interne GK, DC, ST…).</summary>
        public static string PositionLabel(string key) => L.Lang switch
        {
            "en" => key,
            "pt" => PositionPt[key],
            _ => PositionFr[key],
        };
        static readonly Dictionary<string, string> PositionPt = new()
        {
            ["GK"] = "GR", ["SW"] = "LIB", ["DL"] = "DE", ["DC"] = "DC", ["DR"] = "DD", ["DM"] = "MDC",
            ["ML"] = "ME", ["MC"] = "MC", ["MR"] = "MD", ["AML"] = "EE", ["AMC"] = "MO", ["AMR"] = "ED",
            ["ST"] = "PL", ["WBL"] = "DAE", ["WBR"] = "DAD",
        };

        public static string BestPositions(PlayerRow r)
        {
            int top = r.Positions.Max(x => (int)x);
            if (top < 1) return "";
            return string.Join("/", PositionKeys.Where((k, i) => r.Positions[i] >= Math.Max(15, top - 2))
                .Select(PositionLabel));
        }

        // ------------------------------------------------------------------ fichiers de config (JSON)

        static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FM27Scout");
        static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        internal static T LoadJson<T>(string file, T defaults)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string path = Path.Combine(Dir, file);
                if (!File.Exists(path)) { File.WriteAllText(path, JsonSerializer.Serialize(defaults, Json)); return defaults; }
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? defaults;
            }
            catch (Exception e) { Log.Warn($"{file} illisible ({e.Message}) : valeurs par défaut utilisées."); return defaults; }
        }

        internal static void SaveJson<T>(string file, T value)
        {
            try { Directory.CreateDirectory(Dir); File.WriteAllText(Path.Combine(Dir, file), JsonSerializer.Serialize(value, Json)); }
            catch (Exception e) { Log.Warn($"{file} : {e.Message}"); }
        }

        // ------------------------------------------------------------------ recherches

        public sealed class Search
        {
            public string Nom { get; set; } = "";
            public int? AgeMin { get; set; }
            public int? AgeMax { get; set; }
            public int? CaMin { get; set; }
            public int? PaMin { get; set; }
            public List<string> Postes { get; set; }            // codes FR (BU, MC, DC…) ; un seul suffit
            public Dictionary<string, int> AttributsMin { get; set; }
            public long? ValeurMax { get; set; }
            public int? ContratMoisMax { get; set; }
            public string Tri { get; set; } = "PA";              // PA, CA, Valeur, Age
            public int Limite { get; set; } = 60;
        }

        public static List<Search> Searches => s_searches ??= LoadJson("recherches.json", DefaultSearches());
        static List<Search> s_searches;
        public static void ReloadSearches() => s_searches = null;

        static List<Search> DefaultSearches() => new()
        {
            new Search { Nom = "Pépites (≤ 19 ans, PA ≥ 150)", AgeMax = 19, PaMin = 150, Tri = "PA" },
            new Search { Nom = "Jeunes prêts à jouer (≤ 23 ans, CA ≥ 130)", AgeMax = 23, CaMin = 130, Tri = "CA" },
            new Search { Nom = "Mentalité d'élite", CaMin = 100, Tri = "CA",
                AttributsMin = new() { ["AttributeProfessionalism"] = 16, ["AttributeAmbition"] = 15, ["Consistency"] = 14, ["ImportantMatches"] = 14 } },
            new Search { Nom = "Fins de contrat (≤ 6 mois, CA ≥ 120)", ContratMoisMax = 6, CaMin = 120, Tri = "CA" },
            new Search { Nom = "Gardiens d'avenir (≤ 21 ans, PA ≥ 140)", Postes = new() { "GB" }, AgeMax = 21, PaMin = 140, Tri = "PA" },
            new Search { Nom = "Buteurs (CA ≥ 140)", Postes = new() { "BU" }, CaMin = 140, Tri = "CA",
                AttributsMin = new() { ["AttributeFinishing"] = 15, ["AttributeComposure"] = 13 } },
        };

        public static List<PlayerRow> Run(Search s)
        {
            var pos = s.Postes?.Select(p => PositionFr.FirstOrDefault(kv => kv.Value == p.ToUpperInvariant()).Key)
                .Where(k => k != null).Select(k => Array.IndexOf(PositionKeys, k)).ToArray();
            IEnumerable<PlayerRow> q = Game.Rows.Where(Game.Shown);
            if (s.CaMin is int ca) q = q.Where(r => r.CA >= ca);
            if (s.PaMin is int pa) q = q.Where(r => r.PA >= pa);
            // sans date de jeu, l'âge et la durée de contrat sont inconnus : ces filtres sont ignorés
            if (Game.HasDate && s.AgeMin is int amin) q = q.Where(r => Game.Age(r) >= amin);
            if (Game.HasDate && s.AgeMax is int amax) q = q.Where(r => { int a = Game.Age(r); return a >= 0 && a <= amax; });
            if (pos is { Length: > 0 }) q = q.Where(r => pos.Any(i => r.Positions[i] >= 15));
            if (s.ValeurMax is long vmax) q = q.Where(r => r.Value >= 0 && r.Value <= vmax);
            if (Game.HasDate && s.ContratMoisMax is int cm) q = q.Where(r => { int m = Game.ContractMonths(r); return m >= 0 && m <= cm; });
            if (s.AttributsMin != null)
                foreach (var (k, v) in s.AttributsMin) { var key = k; var min = v; q = q.Where(r => Stat(r, key) >= min); }

            q = s.Tri switch
            {
                "CA" => q.OrderByDescending(r => r.CA).ThenByDescending(r => r.PA),
                "Valeur" => q.OrderByDescending(r => r.Value),
                "Age" => q.OrderBy(r => Game.Age(r)).ThenByDescending(r => r.PA),
                _ => q.OrderByDescending(r => r.PA).ThenByDescending(r => r.CA),
            };
            return q.Take(Math.Clamp(s.Limite, 1, 500)).ToList();
        }

        // ------------------------------------------------------------------ radar de scouting

        public sealed class RadarFilter
        {
            public int AgeMin { get; set; } = 15;
            public int AgeMax { get; set; } = 23;
            public int CaMin { get; set; }
            public int PaMin { get; set; } = 120;
            public long ValeurMax { get; set; } = -1;             // -1 = pas de limite
            public HashSet<string> Postes { get; set; } = new();   // codes internes (ST, MC…) ; vide = tous
            public string Tri { get; set; } = "PA";
            public bool Decroissant { get; set; } = true;
            public HashSet<uint> Vus { get; set; } = new();
        }

        static RadarFilter s_radar;
        public static RadarFilter Radar => s_radar ??= LoadJson("radar-scouting.json", new RadarFilter());
        public static void SaveRadar() => SaveJson("radar-scouting.json", Radar);

        /// <summary>Valeur servant au tri (-1 = inconnue, toujours en fin de liste).</summary>
        public static long SortValue(PlayerRow r, string key) => key switch
        {
            "Age" => Game.Age(r),
            "Valeur" => r.Value,
            _ => Stat(r, key),
        };

        public static List<PlayerRow> RunRadar()
        {
            var f = Radar;
            var pos = f.Postes.Select(k => Array.IndexOf(PositionKeys, k)).Where(i => i >= 0).ToArray();
            IEnumerable<PlayerRow> q = Game.Rows.Where(r => Game.Shown(r) && r.CA >= f.CaMin && r.PA >= f.PaMin);
            if (Game.HasDate) q = q.Where(r => { int a = Game.Age(r); return a >= f.AgeMin && a <= f.AgeMax; });
            if (f.ValeurMax >= 0) q = q.Where(r => r.Value >= 0 && r.Value <= f.ValeurMax);
            if (pos.Length > 0) q = q.Where(r => pos.Any(i => r.Positions[i] >= 15));

            string key = f.Tri;
            var known = q.Where(r => SortValue(r, key) >= 0);
            var unknown = q.Where(r => SortValue(r, key) < 0);
            var sorted = f.Decroissant ? known.OrderByDescending(r => SortValue(r, key)).ThenByDescending(r => r.PA)
                                       : known.OrderBy(r => SortValue(r, key)).ThenByDescending(r => r.PA);
            return sorted.Concat(unknown).Take(500).ToList();
        }

        /// <summary>Joueurs du radar, et ceux apparus depuis la dernière vérification.</summary>
        public static (List<PlayerRow> all, List<PlayerRow> fresh) CheckRadar(bool markSeen)
        {
            if (Game.Rows.Count == 0) return (new(), new());
            var all = RunRadar();
            var fresh = Radar.Vus.Count == 0 ? new List<PlayerRow>() : all.Where(r => !Radar.Vus.Contains(r.Id)).ToList();
            if (markSeen || Radar.Vus.Count == 0) { foreach (var r in all) Radar.Vus.Add(r.Id); SaveRadar(); }
            return (all, fresh);
        }

        // ------------------------------------------------------------------ alertes d'effectif

        public static List<(string titre, List<(PlayerRow r, string detail)> joueurs)> Alerts(ulong club)
        {
            var squad = Game.Rows.Where(r => r.ClubPtr == club && club != 0 && Game.Shown(r)).ToList();
            var res = new List<(string, List<(PlayerRow, string)>)>();
            void Add(string t, IEnumerable<(PlayerRow, string)> l) { var x = l.ToList(); if (x.Count > 0) res.Add((t, x)); }

            Add(L.T("Contrat qui se termine dans 6 mois ou moins"), squad.Where(r => { int m = Game.ContractMonths(r); return m >= 0 && m <= 6; })
                .OrderByDescending(r => r.CA).Select(r => (r, L.F("fin {0}", Game.FmDate(r.ExpYear, r.ExpDoy)))));
            Add(L.T("Contrat qui se termine dans 6 à 12 mois"), squad.Where(r => { int m = Game.ContractMonths(r); return m > 6 && m <= 12; })
                .OrderByDescending(r => r.CA).Select(r => (r, L.F("fin {0}", Game.FmDate(r.ExpYear, r.ExpDoy)))));
            Add(L.T("Très fragiles (fragilité ≥ 15)"), squad.Where(r => Stat(r, "InjuryProneness") >= 15)
                .OrderByDescending(r => Stat(r, "InjuryProneness")).Select(r => (r, L.F("fragilité {0}", Stat(r, "InjuryProneness")))));
            Add(L.T("Irréguliers (régularité ≤ 8)"), squad.Where(r => Stat(r, "Consistency") is >= 1 and <= 8)
                .OrderBy(r => Stat(r, "Consistency")).Select(r => (r, L.F("régularité {0}", Stat(r, "Consistency")))));
            Add(L.T("Fragiles dans les grands matchs (≤ 8)"), squad.Where(r => Stat(r, "ImportantMatches") is >= 1 and <= 8)
                .OrderBy(r => Stat(r, "ImportantMatches")).Select(r => (r, L.F("grands matchs {0}", Stat(r, "ImportantMatches")))));
            Add(L.T("Jeunes peu professionnels (≤ 21 ans, pro ≤ 9)"), squad.Where(r => { int a = Game.Age(r); return a >= 0 && a <= 21 && Stat(r, "AttributeProfessionalism") is >= 1 and <= 9; })
                .Select(r => (r, L.F("pro {0}, {1} ans", Stat(r, "AttributeProfessionalism"), Game.Age(r)))));
            Add(L.T("Marge de progression importante (PA - CA ≥ 25)"), squad.Where(r => r.PA - r.CA >= 25)
                .OrderByDescending(r => r.PA - r.CA).Select(r => (r, $"CA {r.CA} → PA {r.PA}")));
            return res;
        }

        // ------------------------------------------------------------------ notes par rôle

        public sealed class Role
        {
            public string Nom { get; set; } = "";
            public Dictionary<string, double> Poids { get; set; } = new();
        }

        public static List<Role> Roles => s_roles ??= WithDefaults(LoadJson("roles.json", DefaultRoles()));

        /// <summary>Ajoute les rôles par défaut absents d'un roles.json plus ancien (sans toucher aux réglages existants).</summary>
        static List<Role> WithDefaults(List<Role> list)
        {
            foreach (var d in DefaultRoles()) if (!list.Any(r => r.Nom == d.Nom)) list.Add(d);
            return list;
        }

        public static Role RoleOf(string nom) => Roles.FirstOrDefault(r => r.Nom == nom) ?? Roles.First();
        static List<Role> s_roles;

        // Pondérations de départ (attributs clés = 3, utiles = 1). Ce sont des valeurs par défaut
        // raisonnables, pas des mesures : elles se modifient dans BepInEx\config\FM27Scout\roles.json.
        static List<Role> DefaultRoles()
        {
            Role R(string nom, string cles, string utiles)
            {
                var r = new Role { Nom = nom };
                foreach (var a in cles.Split(',')) r.Poids["Attribute" + a.Trim()] = 3;
                foreach (var a in utiles.Split(',')) r.Poids.TryAdd("Attribute" + a.Trim(), 1);
                return r;
            }
            return new()
            {
                R("Gardien", "Reflexes,Handling,OneOnOnes,Positioning,AerialReach,CommandOfArea", "Communication,Kicking,Concentration,Decisions,Agility,Anticipation"),
                R("Défenseur central", "Marking,Tackling,Heading,Positioning,JumpingReach,Strength", "Anticipation,Concentration,Decisions,Composure,Bravery,Pace"),
                R("Latéral", "Tackling,Marking,Positioning,Pace,Stamina", "Crossing,Passing,Anticipation,Concentration,Teamwork,WorkRate,Acceleration"),
                R("Piston", "Crossing,Stamina,WorkRate,Pace,Acceleration", "Dribbling,Passing,Tackling,Teamwork,OffTheBall,Agility"),
                R("Milieu défensif", "Tackling,Positioning,Anticipation,Concentration,Teamwork", "Passing,Marking,Decisions,Composure,Strength,Stamina,WorkRate"),
                R("Milieu relayeur", "Passing,Stamina,WorkRate,Teamwork,Decisions", "Tackling,FirstTouch,Vision,OffTheBall,Anticipation,LongShots"),
                R("Meneur de jeu", "Passing,Vision,FirstTouch,Technique,Decisions,Composure", "Anticipation,Teamwork,Flair,OffTheBall,Agility,Balance"),
                R("Ailier", "Crossing,Dribbling,Pace,Acceleration,Agility", "Technique,FirstTouch,Flair,OffTheBall,Stamina,WorkRate"),
                R("Attaquant intérieur", "Dribbling,Finishing,FirstTouch,Acceleration,OffTheBall", "Technique,Composure,Flair,Agility,Pace,LongShots"),
                R("Milieu offensif", "Passing,Vision,FirstTouch,Technique,OffTheBall,Decisions", "Dribbling,Finishing,LongShots,Flair,Composure,Agility"),
                R("Buteur", "Finishing,OffTheBall,Composure,FirstTouch,Acceleration", "Dribbling,Heading,Technique,Anticipation,Pace,Balance"),
                R("Pivot", "Heading,Strength,JumpingReach,FirstTouch,Bravery", "Finishing,Passing,Teamwork,Composure,Balance,OffTheBall"),
                R("Gardien relanceur", "Reflexes,Passing,FirstTouch,Composure,RushingOut,OneOnOnes", "Kicking,Vision,Decisions,Anticipation,Acceleration,Handling"),
                R("Défenseur relanceur", "Passing,Composure,Marking,Tackling,Positioning", "FirstTouch,Vision,Technique,Decisions,Anticipation,Heading"),
                R("Latéral offensif", "Crossing,Stamina,Pace,Acceleration,OffTheBall", "Dribbling,Passing,Tackling,Teamwork,WorkRate,Technique"),
                R("Récupérateur", "Tackling,Aggression,WorkRate,Stamina,Anticipation", "Bravery,Strength,Teamwork,Marking,Positioning,Pace"),
                R("Milieu box-to-box", "Stamina,WorkRate,Passing,Tackling,OffTheBall", "Finishing,LongShots,Teamwork,Decisions,Strength,Pace"),
                R("Attaquant de pression", "WorkRate,Stamina,Aggression,Acceleration,Finishing", "Teamwork,Bravery,Anticipation,OffTheBall,Strength,Pace"),
                R("Faux 9", "FirstTouch,Passing,Vision,Technique,Decisions,OffTheBall", "Dribbling,Finishing,Composure,Flair,Agility,Teamwork"),
            };
        }

        /// <summary>Note /100 d'un joueur pour un rôle (moyenne pondérée des attributs 1..20).</summary>
        public static int Rating(PlayerRow r, Role role)
        {
            double sum = 0, w = 0;
            foreach (var (k, p) in role.Poids)
            {
                int v = Stat(r, k == "AttributeOffTheBall" ? "AttributeMovement" : k);
                if (v < 1) continue;
                sum += v * p; w += p;
            }
            return w == 0 ? 0 : (int)Math.Round(sum / w * 5);
        }

        // ------------------------------------------------------------------ export CSV

        public static string ExportCsv(IEnumerable<PlayerRow> rows, string nom)
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FM27Scout");
            Directory.CreateDirectory(dir);
            string safe = string.Concat(nom.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            string path = Path.Combine(dir, $"{safe}_{DateTime.Now:yyyyMMdd_HHmm}.csv");

            var keys = Layout.Visible.Select(v => v.prop)
                .Concat(Layout.HiddenInBlock.Select(h => h.key)).Concat(PersonalityKeys).ToList();
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(";", new[] { L.T("Nom"), L.T("Club"), L.T("Age"), L.T("Postes"), "CA", "PA", L.T("Valeur (£)"), L.T("Salaire (£/sem)"), L.T("Fin de contrat") }
                .Concat(keys.Select(k => L.T(Names.Of(k))))));
            foreach (var r in rows)
            {
                var cells = new List<string>
                {
                    r.Name, r.Club, Game.Age(r) < 0 ? "" : Game.Age(r).ToString(), BestPositions(r), r.CA.ToString(), r.PA.ToString(),
                    r.Value < 0 ? "" : r.Value.ToString(), r.Wage < 0 ? "" : r.Wage.ToString(), Game.FmDate(r.ExpYear, r.ExpDoy),
                };
                cells.AddRange(keys.Select(k => { int v = Stat(r, k); return v < 0 ? "" : v.ToString(); }));
                sb.AppendLine(string.Join(";", cells.Select(c => c.Contains(';') ? $"\"{c}\"" : c)));
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));   // BOM : Excel lit les accents
            return path;
        }

    }

    /// <summary>Réglages de l'app (dans %APPDATA%\FM27Scout\reglages.json).</summary>
    internal sealed class Settings
    {
        public string VTableRva { get; set; } = "4D27228";        // FM27 27.0 ; retrouvé automatiquement si besoin
        public string PluginVersion { get; set; } = "";
        public string ManualDate { get; set; } = "";               // aaaa-mm-jj si saisie à la main
        public uint PinnedId { get; set; }
        public string AlertClub { get; set; } = "";
        public string MyClub { get; set; } = "";
        public bool MyClubManual { get; set; }                     // club choisi à la main : la détection automatique ne le change plus
        public string GraphicsPath { get; set; } = "";             // vide = Documents\Sports Interactive\Football Manager 27\graphics
        public int PersonUidOff { get; set; } = -1;               // retrouvés automatiquement
        public int ClubUidOff { get; set; } = -1;
        public double WinLeft { get; set; } = double.NaN;        // position de la fenêtre (retrouvée au lancement)
        public double WinTop { get; set; } = double.NaN;
        public double WinWidth { get; set; } = double.NaN;
        public double WinHeight { get; set; } = double.NaN;
        public bool WinMax { get; set; }
        public string Gender { get; set; } = "H";                 // H = hommes, F = femmes, T = tous
        public List<string> MatchDays { get; set; } = new();          // jours de match placés dans le planning (aaaa-mm-jj)
        public List<string> HiddenCols { get; set; } = new() { "Gardien" };
        public string ThemePrimary { get; set; } = "";            // couleurs du club (lues dans FM) pour le thème
        public string ThemeSecondary { get; set; } = "";
        public string MyClubFm { get; set; } = "";                // nom du club géré tel que FM le donne
        public string TacticFormation { get; set; } = "4-3-3";
        public string TacticStyle { get; set; } = "equilibre";
        public bool TacticMeta { get; set; } = true;               // privilégier les attributs « meta » (vitesse, accélération…)
        public string TacticConfig { get; set; } = "";             // configuration FM choisie (nom interne)
        public string GamePath { get; set; } = "";                 // dossier de FM27 (renseigné par l'installation)
        public bool GuideWelcomeDone { get; set; }                 // guide de bienvenue déjà vu
        public bool GuideMuted { get; set; }                       // plus de fiches d'aide par page
        public List<string> GuideSeen { get; set; } = new();       // pages dont la fiche d'aide a été vue
        public string Lang { get; set; } = "";                     // langue de l'interface : fr, en, pt (vide = anglais)

        static Settings s_cur;
        public static Settings Cur => s_cur ??= Db.LoadJson("reglages.json", new Settings());
        public static void Save() => Db.SaveJson("reglages.json", Cur);
    }

    /// <summary>Noms français des attributs, tels qu'affichés par FM27.</summary>
    internal static class Names
    {
        public static readonly (string cat, (string k, string fr)[] attrs)[] Groups =
        {
            ("Technique", new[] { ("AttributeCorners","Corners"), ("AttributeCrossing","Centres"), ("AttributeDribbling","Dribbles"),
                ("AttributeFinishing","Finition"), ("AttributeFirstTouch","Contrôle de balle"), ("AttributeFreeKicks","Coups francs"),
                ("AttributeHeading","Jeu de tête"), ("AttributeLongShots","Tirs de loin"), ("AttributeLongThrows","Touches longues"),
                ("AttributeMarking","Marquage"), ("AttributePassing","Passes"), ("AttributePenaltyTaking","Penalty"),
                ("AttributeTackling","Tacles"), ("AttributeTechnique","Technique") }),
            ("Mental", new[] { ("AttributeAggression","Agressivité"), ("AttributeAnticipation","Anticipation"), ("AttributeBravery","Courage"),
                ("AttributeComposure","Sang-froid"), ("AttributeConcentration","Concentration"), ("AttributeDecisions","Décisions"),
                ("AttributeDetermination","Détermination"), ("AttributeFlair","Inspiration"), ("AttributeLeadership","Leadership"),
                ("AttributeMovement","Appels de balle"), ("AttributePositioning","Placement"), ("AttributeTeamwork","Jeu collectif"),
                ("AttributeVision","Vision du jeu"), ("AttributeWorkRate","Volume de jeu") }),
            ("Physique", new[] { ("AttributeAcceleration","Accélération"), ("AttributeAgility","Agilité"), ("AttributeBalance","Équilibre"),
                ("AttributeJumpingReach","Détente verticale"), ("AttributeNaturalFitness","Qualités phys. nat."), ("AttributePace","Vitesse"),
                ("AttributeStamina","Endurance"), ("AttributeStrength","Puissance") }),
            ("Gardien", new[] { ("AttributeAerialReach","Détente"), ("AttributeCommandOfArea","Sorties dans la surface"),
                ("AttributeCommunication","Communication"), ("AttributeEccentricity","Excentricité"), ("AttributeHandling","Prises de balle"),
                ("AttributeKicking","Dégagements"), ("AttributeOneOnOnes","Un contre un"), ("AttributeReflexes","Réflexes"),
                ("AttributeRushingOut","Sorties dans les pieds (tendance)"), ("AttributeTendencyToPunch","Dégagement au poing (tendance)"),
                ("AttributeThrowing","Relances à la main") }),
        };

        public static readonly (string k, string fr)[] Hidden =
        {
            ("Consistency", "Régularité"), ("ImportantMatches", "Grands matchs"), ("InjuryProneness", "Fragilité"),
            ("Dirtiness", "Vice"), ("Versatility", "Polyvalence"),
            ("AttributeProfessionalism", "Professionnalisme"), ("AttributeAmbition", "Ambition"), ("AttributePressure", "Résistance à la pression"),
            ("AttributeTemperament", "Tempérament"), ("AttributeAdaptability", "Adaptabilité"), ("AttributeLoyalty", "Loyauté"),
            ("AttributeSportsmanship", "Fair-play"), ("AttributeControversy", "Controverse"),
        };

        public static string Of(string key)
        {
            if (key == "CA" || key == "PlayerCurrentAbility") return "Capacité actuelle";
            if (key == "PA" || key == "PlayerPotentialAbility") return "Potentiel";
            if (key == "Age") return "Âge";
            if (key == "Valeur") return "Valeur";
            foreach (var g in Groups) foreach (var (k, fr) in g.attrs) if (k == key) return fr;
            foreach (var (k, fr) in Hidden) if (k == key) return fr;
            return key.Replace("Attribute", "");
        }

        /// <summary>Une valeur basse est meilleure (fragilité, vice, controverse).</summary>
        public static bool LowIsGood(string key) => key is "InjuryProneness" or "Dirtiness" or "AttributeControversy";
    }
}
