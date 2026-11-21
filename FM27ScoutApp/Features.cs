using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FM27Scout
{
    /// <summary>Shortlist, historique de progression, profils similaires, bonnes affaires, onze type, vestiaire.</summary>
    internal static class Features
    {
        // identifiant stable d'un joueur : l'identifiant FM (photos) s'il est connu, sinon l'index interne
        public static ulong Key(PlayerRow r) => r.Uid > 0 ? r.Uid : (1UL << 40) | r.Id;

        static Dictionary<ulong, PlayerRow> s_byKey;
        static List<PlayerRow> s_byKeyFor;
        public static PlayerRow ByKey(ulong k)
        {
            if (s_byKeyFor != Game.Rows)
            {
                s_byKey = new();
                foreach (var r in Game.Rows) s_byKey.TryAdd(Key(r), r);
                s_byKeyFor = Game.Rows;
            }
            return s_byKey.GetValueOrDefault(k);
        }

        // ================================================================== shortlist

        public sealed class ShortEntry
        {
            public string Nom { get; set; } = "";
            public string Club { get; set; } = "";
            public long Valeur { get; set; } = -1;
            public int CA { get; set; }
            public string Ajoute { get; set; } = "";   // date de jeu de l'ajout
        }

        static Dictionary<string, ShortEntry> s_short;
        public static Dictionary<string, ShortEntry> Shortlist => s_short ??= Db.LoadJson("shortlist.json", new Dictionary<string, ShortEntry>());
        static void SaveShort() => Db.SaveJson("shortlist.json", Shortlist);

        public static bool InShortlist(PlayerRow r) => Shortlist.ContainsKey(Key(r).ToString());

        public static void ToggleShort(PlayerRow r)
        {
            string k = Key(r).ToString();
            if (!Shortlist.Remove(k)) Shortlist[k] = Snapshot(r);
            SaveShort();
        }

        static ShortEntry Snapshot(PlayerRow r) =>
            new() { Nom = r.Name, Club = r.Club, Valeur = r.Value, CA = r.CA, Ajoute = Game.HasDate ? Game.Date.ToString("dd/MM/yyyy") : "" };

        public static void MarkShortSeen()
        {
            foreach (var k in Shortlist.Keys.ToList())
                if (ulong.TryParse(k, out var key) && ByKey(key) is PlayerRow r) Shortlist[k] = Snapshot(r);
            SaveShort();
        }

        /// <summary>Joueurs de la shortlist (présents dans la partie) et ce qui a changé depuis l'ajout.</summary>
        public static List<(PlayerRow r, string changes, bool important)> ShortRows()
        {
            var res = new List<(PlayerRow, string, bool)>();
            foreach (var (k, e) in Shortlist)
            {
                if (!ulong.TryParse(k, out var key) || ByKey(key) is not PlayerRow r) continue;
                var ch = new List<string>();
                bool imp = false;
                if (e.Club != r.Club) { ch.Add(L.F("club : {0} → {1}", e.Club == "" ? L.T("libre") : e.Club, r.Club == "" ? L.T("libre") : r.Club)); imp = true; }
                if (e.Valeur > 0 && r.Value >= 0)
                {
                    double d = (r.Value - e.Valeur) * 100.0 / e.Valeur;
                    if (d <= -10) { ch.Add(L.F("valeur {0:F0} %", d)); imp = true; }
                    else if (d >= 10) ch.Add(L.F("valeur +{0:F0} %", d));
                }
                int m = Game.ContractMonths(r);
                if (m >= 0 && m <= 12) { ch.Add(m <= 6 ? L.F("contrat fini dans {0} mois", m) : L.T("contrat ≤ 12 mois")); imp = true; }
                if (r.Club == "") { ch.Add(L.T("libre")); imp = true; }
                if (r.CA != e.CA) ch.Add($"CA {(r.CA > e.CA ? "+" : "")}{r.CA - e.CA}");
                res.Add((r, string.Join(" · ", ch), imp));
            }
            return res.OrderByDescending(x => x.Item3).ThenByDescending(x => x.Item1.PA).ToList();
        }

        public static int ShortAlerts() => Game.Rows.Count == 0 ? 0 : ShortRows().Count(x => x.important);

        // ================================================================== historique (CA par date de jeu)

        static string HistDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FM27Scout", "historique");

        /// <summary>Un relevé par date de jeu : [clé u64][CA u8][PA u8] pour chaque joueur.</summary>
        public static void SaveHistory(List<PlayerRow> rows)
        {
            if (!Game.HasDate || rows.Count < 1000) return;
            try
            {
                Directory.CreateDirectory(HistDir);
                string path = Path.Combine(HistDir, Game.Date.ToString("yyyy-MM-dd") + ".bin");
                using (var w = new BinaryWriter(File.Create(path)))
                    foreach (var r in rows) { w.Write(Key(r)); w.Write((byte)r.CA); w.Write((byte)r.PA); }
                s_hist = null;
            }
            catch (Exception e) { Log.Warn("Historique : " + e.Message); }
        }

        static List<(DateTime date, Dictionary<ulong, (byte ca, byte pa)> v)> s_hist;

        /// <summary>Relevés passés (dates antérieures à la date de jeu actuelle, les plus récents en dernier).</summary>
        static List<(DateTime date, Dictionary<ulong, (byte ca, byte pa)> v)> History()
        {
            if (s_hist != null) return s_hist;
            s_hist = new();
            try
            {
                if (!Directory.Exists(HistDir)) return s_hist;
                foreach (var f in Directory.GetFiles(HistDir, "*.bin").OrderBy(f => f))
                {
                    if (!DateTime.TryParse(Path.GetFileNameWithoutExtension(f), out var d)) continue;
                    var bytes = File.ReadAllBytes(f);
                    var dict = new Dictionary<ulong, (byte, byte)>(bytes.Length / 10);
                    for (int i = 0; i + 10 <= bytes.Length; i += 10) dict[BitConverter.ToUInt64(bytes, i)] = (bytes[i + 8], bytes[i + 9]);
                    s_hist.Add((d, dict));
                }
                // pas plus de 40 relevés en mémoire : on garde le premier et les plus récents
                if (s_hist.Count > 40) s_hist = s_hist.Take(1).Concat(s_hist.Skip(s_hist.Count - 39)).ToList();
            }
            catch (Exception e) { Log.Warn("Historique : " + e.Message); }
            return s_hist;
        }

        /// <summary>Points (date, CA) d'un joueur, du plus ancien au plus récent.</summary>
        public static List<(DateTime date, int ca, int pa)> Points(PlayerRow r)
        {
            ulong k = Key(r);
            var pts = History().Where(h => h.v.ContainsKey(k)).Select(h => (h.date, (int)h.v[k].ca, (int)h.v[k].pa)).ToList();
            if (Game.HasDate && !pts.Any(p => p.date == Game.Date)) pts.Add((Game.Date, r.CA, r.PA));
            return pts.OrderBy(p => p.date).ToList();
        }

        /// <summary>Progression du CA depuis le premier relevé (null si aucun relevé antérieur).</summary>
        public static int? Progress(PlayerRow r)
        {
            var h = History();
            if (h.Count == 0 || !Game.HasDate || h[0].date >= Game.Date) return null;
            return h[0].v.TryGetValue(Key(r), out var x) ? r.CA - x.ca : null;
        }

        public static string FirstRecord()
        {
            var h = History();
            return h.Count == 0 ? "" : h[0].date.ToString("dd/MM/yyyy");
        }

        // ================================================================== profils similaires

        static readonly int[] OutfieldOffs = Layout.Visible.Where(v => !IsGk(v.prop)).Select(v => v.off).ToArray();
        static readonly int[] GkOffs = Layout.Visible.Select(v => v.off).ToArray();
        static bool IsGk(string p) => p is "AttributeHandling" or "AttributeAerialReach" or "AttributeCommandOfArea" or "AttributeCommunication"
            or "AttributeKicking" or "AttributeThrowing" or "AttributeOneOnOnes" or "AttributeReflexes" or "AttributeEccentricity"
            or "AttributeRushingOut" or "AttributeTendencyToPunch";

        /// <summary>Joueurs au profil d'attributs le plus proche, sur au moins un des postes principaux.</summary>
        public static List<(PlayerRow r, int sim)> Similar(PlayerRow p, bool younger, bool cheaper, int max = 20)
        {
            bool gk = p.Positions[0] >= 15;
            var offs = gk ? GkOffs : OutfieldOffs;
            var main = Enumerable.Range(0, 15).Where(i => p.Positions[i] >= 15).ToArray();
            if (main.Length == 0) main = new[] { Array.IndexOf(p.Positions, p.Positions.Max()) };
            int pAge = Game.Age(p);
            var res = new List<(PlayerRow, double)>();
            foreach (var r in Game.Rows)
            {
                if (r == p || r.Id == p.Id || !Game.Shown(r)) continue;
                if (!main.Any(i => r.Positions[i] >= 15)) continue;
                if (younger && (pAge < 0 || Game.Age(r) < 0 || Game.Age(r) >= pAge)) continue;
                if (cheaper && (p.Value <= 0 || r.Value < 0 || r.Value >= p.Value)) continue;
                double d = 0;
                foreach (int o in offs) { double x = (p.Attrs[o] - r.Attrs[o]) / 5.0; d += x * x; }
                res.Add((r, Math.Sqrt(d / offs.Length)));
            }
            return res.OrderBy(x => x.Item2).Take(max).Select(x => (x.Item1, (int)Math.Round(Math.Max(0, 100 - x.Item2 * 12)))).ToList();
        }

        // ================================================================== bonnes affaires

        public static readonly (string key, string label)[] DealKinds =
        {
            ("libres", "Joueurs libres"), ("6", "Fin de contrat ≤ 6 mois"), ("12", "Fin de contrat ≤ 12 mois"), ("prix", "Meilleur rapport qualité-prix"),
        };

        public static List<PlayerRow> Deals(string kind, int caMin)
        {
            var q = Game.Rows.Where(r => Game.Shown(r) && r.CA >= caMin);
            q = kind switch
            {
                "libres" => q.Where(r => r.Club == ""),
                "6" => q.Where(r => r.Club != "" && Game.ContractMonths(r) is >= 0 and <= 6),
                "12" => q.Where(r => r.Club != "" && Game.ContractMonths(r) is >= 0 and <= 12),
                _ => q.Where(r => r.Value > 0),
            };
            return (kind == "prix" ? q.OrderByDescending(QualityPrice) : q.OrderByDescending(r => r.PA).ThenByDescending(r => r.CA)).Take(500).ToList();
        }

        /// <summary>Points de CA par million de £ (avec un plancher pour ne pas favoriser les valeurs nulles).</summary>
        public static double QualityPrice(PlayerRow r) => r.Value < 0 ? 0 : r.CA * r.CA / 100.0 / Math.Max(0.1, r.Value / 1_000_000.0);

        // ================================================================== onze type

        public sealed record Slot(string Pos, string Role, double X, double Y);   // X 0..100 (gauche→droite), Y 0..100 (attaque→but)

        public static readonly (string name, Slot[] slots)[] Formations =
        {
            ("4-3-3", new Slot[] { new("GK","Gardien",50,93), new("DL","Latéral",12,72), new("DC","Défenseur central",37,76), new("DC","Défenseur central",63,76), new("DR","Latéral",88,72),
                new("DM","Milieu défensif",50,58), new("MC","Milieu relayeur",30,46), new("MC","Meneur de jeu",70,46),
                new("AML","Attaquant intérieur",15,22), new("ST","Buteur",50,12), new("AMR","Ailier",85,22) }),
            ("4-2-3-1", new Slot[] { new("GK","Gardien",50,93), new("DL","Latéral",12,72), new("DC","Défenseur central",37,76), new("DC","Défenseur central",63,76), new("DR","Latéral",88,72),
                new("DM","Milieu défensif",35,56), new("DM","Milieu relayeur",65,56), new("AML","Ailier",15,32), new("AMC","Milieu offensif",50,34), new("AMR","Attaquant intérieur",85,32),
                new("ST","Buteur",50,12) }),
            ("4-4-2", new Slot[] { new("GK","Gardien",50,93), new("DL","Latéral",12,72), new("DC","Défenseur central",37,76), new("DC","Défenseur central",63,76), new("DR","Latéral",88,72),
                new("ML","Ailier",12,44), new("MC","Milieu défensif",37,50), new("MC","Milieu relayeur",63,50), new("MR","Ailier",88,44),
                new("ST","Pivot",36,15), new("ST","Buteur",64,15) }),
            ("3-5-2", new Slot[] { new("GK","Gardien",50,93), new("DC","Défenseur central",25,76), new("DC","Défenseur central",50,78), new("DC","Défenseur central",75,76),
                new("WBL","Piston",10,50), new("DM","Milieu défensif",50,58), new("MC","Milieu relayeur",30,42), new("MC","Meneur de jeu",70,42), new("WBR","Piston",90,50),
                new("ST","Pivot",36,15), new("ST","Buteur",64,15) }),
            ("3-4-3", new Slot[] { new("GK","Gardien",50,93), new("DC","Défenseur central",25,76), new("DC","Défenseur central",50,78), new("DC","Défenseur central",75,76),
                new("WBL","Piston",10,48), new("MC","Milieu défensif",37,52), new("MC","Meneur de jeu",63,52), new("WBR","Piston",90,48),
                new("AML","Attaquant intérieur",18,22), new("ST","Buteur",50,12), new("AMR","Attaquant intérieur",82,22) }),
            ("4-1-4-1", new Slot[] { new("GK","Gardien",50,93), new("DL","Latéral",12,72), new("DC","Défenseur central",37,76), new("DC","Défenseur central",63,76), new("DR","Latéral",88,72),
                new("DM","Milieu défensif",50,60), new("ML","Ailier",12,38), new("MC","Milieu relayeur",37,44), new("MC","Meneur de jeu",63,44), new("MR","Ailier",88,38),
                new("ST","Buteur",50,12) }),
        };

        /// <summary>Familiarité avec le poste : 1 si naturel/accompli, puis décroissant.</summary>
        static double Familiarity(PlayerRow r, string pos)
        {
            int p = r.Positions[Array.IndexOf(Db.PositionKeys, pos)];
            return p >= 15 ? 1.0 : p >= 12 ? 0.9 : p >= 9 ? 0.75 : p >= 5 ? 0.55 : 0.3;
        }

        public static int SlotScore(PlayerRow r, Slot s)
        {
            var role = Db.RoleOf(s.Role);
            double rating = Db.Rating(r, role);
            if (Settings.Cur.TacticMeta) rating = rating * 0.6 + Meta(r, s.Pos) * 0.4;
            rating *= TacticFm.FootFactor(r, s.Pos);
            return (int)Math.Round(rating * Familiarity(r, s.Pos));
        }

        /// <summary>
        /// Note « meta » /100 : les attributs qui pèsent le plus dans le moteur de match d'après les tests de la communauté
        /// (FM-Arena, FM26 : vitesse et accélération ≈ 3× les autres ; accélération devant la vitesse en défense ;
        /// activité et détente ensuite ; réflexes et agilité pour les gardiens). Non confirmé par Sports Interactive pour FM27.
        /// </summary>
        public static double Meta(PlayerRow r, string pos)
        {
            double A(string k) => Math.Max(1, Db.Stat(r, "Attribute" + k));
            (double v, double w)[] parts = pos switch
            {
                "GK" => new[] { (A("Reflexes"), 3.0), (A("Agility"), 2.0), (A("OneOnOnes"), 1.0) },
                "DC" or "SW" => new[] { (A("Acceleration"), 3.0), (A("Pace"), 2.0), (A("JumpingReach"), 2.0), (A("WorkRate"), 1.0) },
                "DL" or "DR" or "WBL" or "WBR" => new[] { (A("Acceleration"), 3.0), (A("Pace"), 2.5), (A("WorkRate"), 1.5), (A("Stamina"), 1.0) },
                _ => new[] { (A("Pace"), 3.0), (A("Acceleration"), 2.5), (A("WorkRate"), 1.5), (A("JumpingReach"), 0.5) },
            };
            return parts.Sum(x => x.v * x.w) / parts.Sum(x => x.w) * 5;
        }

        /// <summary>Meilleur onze (affectation gloutonne des meilleurs couples joueur/poste) + 2 doublures par poste.</summary>
        public static (List<(Slot s, PlayerRow r, int score)> eleven, List<(Slot s, List<(PlayerRow r, int score)> subs)> backups) BestEleven(List<PlayerRow> squad, Slot[] slots)
        {
            var pairs = new List<(int si, PlayerRow r, int sc)>();
            for (int i = 0; i < slots.Length; i++) foreach (var r in squad) pairs.Add((i, r, SlotScore(r, slots[i])));
            var chosen = new (PlayerRow r, int sc)?[slots.Length];
            var used = new HashSet<PlayerRow>();
            foreach (var (si, r, sc) in pairs.OrderByDescending(p => p.sc))
            {
                if (chosen[si] != null || used.Contains(r)) continue;
                chosen[si] = (r, sc); used.Add(r);
                if (used.Count == slots.Length) break;
            }
            var eleven = slots.Select((s, i) => (s, chosen[i]?.r, chosen[i]?.sc ?? 0)).ToList();
            var backups = slots.Select((s, i) => (s, pairs.Where(p => p.si == i && !used.Contains(p.r)).OrderByDescending(p => p.sc).Take(2).Select(p => (p.r, p.sc)).ToList())).ToList();
            return (eleven, backups);
        }

        // ================================================================== vestiaire (stats cachées de l'effectif)

        public static readonly (string titre, bool positif, Func<PlayerRow, string> why)[] Dressing =
        {
            ("Fragiles", false, r => Db.Stat(r, "InjuryProneness") >= 14 ? L.F("fragilité {0}", Db.Stat(r, "InjuryProneness")) : null),
            ("Irréguliers", false, r => Db.Stat(r, "Consistency") is >= 1 and <= 9 ? L.F("régularité {0}", Db.Stat(r, "Consistency")) : null),
            ("Faibles en grands matchs", false, r => Db.Stat(r, "ImportantMatches") is >= 1 and <= 9 ? L.F("grands matchs {0}", Db.Stat(r, "ImportantMatches")) : null),
            ("Peu professionnels", false, r => Db.Stat(r, "AttributeProfessionalism") is >= 1 and <= 9 ? L.F("professionnalisme {0}", Db.Stat(r, "AttributeProfessionalism")) : null),
            ("Tempérament à risque", false, r =>
            {
                int t = Db.Stat(r, "AttributeTemperament"), v = Db.Stat(r, "Dirtiness"), c = Db.Stat(r, "AttributeControversy");
                var w = new List<string>();
                if (t is >= 1 and <= 7) w.Add(L.F("tempérament {0}", t));
                if (v >= 15) w.Add(L.F("vice {0}", v));
                if (c >= 15) w.Add(L.F("controverse {0}", c));
                return w.Count > 0 ? string.Join(", ", w) : null;
            }),
            ("Craquent sous la pression", false, r => Db.Stat(r, "AttributePressure") is >= 1 and <= 8 ? L.F("pression {0}", Db.Stat(r, "AttributePressure")) : null),
            ("Risque de départ (ambitieux, peu loyaux)", false, r =>
                Db.Stat(r, "AttributeAmbition") >= 15 && Db.Stat(r, "AttributeLoyalty") is >= 1 and <= 9 ? L.F("ambition {0}, loyauté {1}", Db.Stat(r, "AttributeAmbition"), Db.Stat(r, "AttributeLoyalty")) : null),
            ("Fiables pour une finale", true, r =>
                Db.Stat(r, "ImportantMatches") >= 15 && Db.Stat(r, "Consistency") >= 14 ? L.F("grands matchs {0}, régularité {1}", Db.Stat(r, "ImportantMatches"), Db.Stat(r, "Consistency")) : null),
            ("Modèles pour les jeunes", true, r =>
                Db.Stat(r, "AttributeProfessionalism") >= 16 && Db.Stat(r, "AttributeDetermination") >= 15 ? L.F("pro {0}, détermination {1}", Db.Stat(r, "AttributeProfessionalism"), Db.Stat(r, "AttributeDetermination")) : null),
            ("Polyvalents", true, r => Db.Stat(r, "Versatility") >= 15 ? L.F("polyvalence {0}", Db.Stat(r, "Versatility")) : null),
        };

        // ================================================================== axes du radar (comparateur)

        public static readonly (string label, string[] keys)[] RadarAxes =
        {
            ("Vitesse", new[] { "AttributePace", "AttributeAcceleration" }),
            ("Physique", new[] { "AttributeStrength", "AttributeStamina", "AttributeBalance", "AttributeNaturalFitness" }),
            ("Technique", new[] { "AttributeDribbling", "AttributeFirstTouch", "AttributeTechnique", "AttributeFlair" }),
            ("Création", new[] { "AttributePassing", "AttributeVision", "AttributeCrossing", "AttributeDecisions" }),
            ("Finition", new[] { "AttributeFinishing", "AttributeLongShots", "AttributeComposure", "AttributeMovement" }),
            ("Défense", new[] { "AttributeTackling", "AttributeMarking", "AttributePositioning", "AttributeAnticipation" }),
            ("Jeu aérien", new[] { "AttributeHeading", "AttributeJumpingReach", "AttributeBravery" }),
            ("Mental", new[] { "AttributeDetermination", "AttributeWorkRate", "AttributeTeamwork", "AttributeConcentration", "AttributeLeadership" }),
            ("Fiabilité", new[] { "Consistency", "ImportantMatches", "AttributeProfessionalism", "AttributePressure" }),
        };

        public static double Axis(PlayerRow r, string[] keys)
        {
            var v = keys.Select(k => Db.Stat(r, k)).Where(x => x > 0).ToList();
            return v.Count == 0 ? 0 : v.Average();
        }
    }
}
