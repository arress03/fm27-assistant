// Lecture de la base de joueurs de FM27 depuis la mémoire (lecture seule).
//
// Disposition des objets joueur (vérifiée sur FM27 27.0 avec le mod en jeu) :
//   +0x000 vtable (pointeur dans game_plugin.dll) — identifie les objets "joueur"
//   +0x150 postes (15 octets, 1..20)      +0x15F attributs (0x36 octets, valeurs x5)
//   +0x234 valeur (u32 £)                 +0x264 CA, +0x266 PA (u16)
//   +0x290 identifiant du joueur dans la base
//   +0x288 = bloc "personne" : +0x19 drapeau femme, +0x50/+0x58/+0x60 prénom/nom/nom usuel,
//            +0x70..+0x77 personnalité (1..20), +0x88 naissance (date FM), +0xA8 contrat
//   contrat : +0x10 équipe, +0x20 salaire (£/sem.), +0x48 fin (date FM)
//   équipe : +0x30 club (nom à +0xC0), +0xA0 calendrier (prochain match à +0x94)
// Date FM : u32 = (année << 16) | jour de l'année.
//
// Le vtable change à chaque mise à jour du jeu : on ne le suppose pas, on le RETROUVE en
// cherchant le type d'objet dont des milliers d'instances ressemblent à des joueurs
// (CA/PA, attributs, postes et personnalité tous plausibles).

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FM27Scout
{
    internal sealed class PlayerRow
    {
        public uint Id;
        public ulong Addr;
        public byte[] Attrs = new byte[0x36];
        public byte[] Positions = new byte[15];
        public byte[] Personality = new byte[8];
        public int CA, PA;
        public int BirthYear, BirthDoy;
        public long Value = -1, Wage = -1;
        public int ExpYear, ExpDoy;
        public ulong Team, ClubPtr;
        public uint Uid;
        public uint Stamp;                          // date FM à +0x200 : vaut la date du jour quand les bits 9-15 sont à 0                            // identifiant FM (photos : graphics/pictures/person/<uid>)
        public ulong ContractPtr, FirstNamePtr, LastNamePtr, CommonNamePtr;   // lus dans l'objet lors du scan
        public bool Female;
        public string Name = "", Club = "";
        public string NameKey = "";                 // nom sans accents, en minuscules (recherche)
    }

    internal static class Game
    {
        public const int OBJ_SIZE = 0x340;

        public static List<PlayerRow> Rows = new();
        public static Dictionary<uint, PlayerRow> ById = new();
        public static DateTime ReadAt;
        public static ulong PlayerVTable;
        public static string LastError = "";
        public static List<string> Diagnostic = new();

        // ---------------------------------------------------------------- date de jeu
        public static int GameYear, GameDoy;
        public static bool DateIsManual;
        public static bool HasDate => GameYear > 0;
        public static DateTime Date => HasDate ? new DateTime(GameYear, 1, 1).AddDays(GameDoy - 1) : DateTime.MinValue;

        public static void SetDate(DateTime d, bool manual) { GameYear = d.Year; GameDoy = d.DayOfYear; DateIsManual = manual; }

        /// <summary>Filtre hommes / femmes choisi dans le menu.</summary>
        public static bool Shown(PlayerRow r) => Settings.Cur.Gender switch { "F" => r.Female, "T" => true, _ => !r.Female };

        public static int Age(PlayerRow r)
        {
            if (r.BirthYear == 0 || !HasDate) return -1;
            int a = GameYear - r.BirthYear - (GameDoy < r.BirthDoy ? 1 : 0);
            return a is >= 10 and <= 60 ? a : -1;
        }

        public static int ContractMonths(PlayerRow r)
        {
            if (r.ExpYear == 0 || !HasDate) return -1;
            int days = (r.ExpYear - GameYear) * 365 + (r.ExpDoy - GameDoy);
            return days < 0 ? 0 : days / 30;
        }

        public static string FmDate(int y, int d) => y == 0 ? "" : new DateTime(y, 1, 1).AddDays(d - 1).ToString("dd/MM/yyyy");

        static (int, int) DecodeDate(uint raw)
        {
            int y = (int)(raw >> 16), d = (int)(raw & 0x1FF);
            return y is >= 1900 and <= 2150 && d is >= 1 and <= 366 ? (y, d) : (0, 0);
        }

        // ---------------------------------------------------------------- lecture complète

        /// <summary>Lit toute la base. progress(0..1, texte) est appelé depuis un thread de fond.</summary>
        public static List<PlayerRow> ReadAll(GameProcess g, ulong vtableHint, Action<double, string> progress, CancellationToken ct)
        {
            var t0 = DateTime.UtcNow;
            var regions = g.Regions();
            long total = regions.Sum(r => (long)r.size), done = 0;

            // 1) un seul passage sur la mémoire : chaque objet "plausible" est rangé par vtable
            var byVt = new ConcurrentDictionary<ulong, ConcurrentBag<PlayerRow>>();
            // dates FM (aaaa << 16 | jour) rencontrées en mémoire : la date du jour revient partout
            var dateHist = new ConcurrentDictionary<uint, int>();
            Parallel.ForEach(regions, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount - 1), CancellationToken = ct },
                () => (buf: new byte[8 << 20], hist: new Dictionary<uint, int>()),
                (reg, state, local) =>
                {
                    var (buf, hist) = local;
                    int step = buf.Length - OBJ_SIZE;
                    for (ulong pos = reg.start; pos < reg.start + reg.size; pos += (ulong)step)
                    {
                        if (ct.IsCancellationRequested) { state.Stop(); break; }
                        int len = (int)Math.Min((ulong)buf.Length, reg.start + reg.size - pos);
                        if (len > OBJ_SIZE && g.Read(pos, buf, len))
                        {
                            for (int i = 0; i + 4 <= len; i += 4)
                            {
                                uint v = BitConverter.ToUInt32(buf, i);
                                if ((v >> 16) - 2020u <= 50u && (v & 0xFE00) == 0 && (v & 0x1FF) - 1u < 366u)
                                    hist[v] = hist.GetValueOrDefault(v) + 1;
                            }
                            for (int i = 0; i + OBJ_SIZE <= len; i += 8)
                            {
                                ulong vt = BitConverter.ToUInt64(buf, i);
                                if (vt < g.PluginBase || vt >= g.PluginEnd) continue;
                                var row = TryParse(buf, i, pos + (ulong)i);
                                if (row != null) byVt.GetOrAdd(vt, _ => new ConcurrentBag<PlayerRow>()).Add(row);
                            }
                        }
                        long d = Interlocked.Add(ref done, Math.Min((long)step, (long)(reg.start + reg.size - pos)));
                        progress?.Invoke(0.75 * d / Math.Max(1, total), "Lecture de la mémoire du jeu…");
                    }
                    return local;
                },
                local => { foreach (var kv in local.hist) dateHist.AddOrUpdate(kv.Key, kv.Value, (_, c) => c + kv.Value); });

            if (byVt.IsEmpty) { LastError = "Aucun joueur trouvé : charge une partie dans FM27."; return new(); }

            // 2) le type "joueur" = le vtable qui a le plus d'instances plausibles (indice : celui déjà connu)
            var best = byVt.OrderByDescending(kv => kv.Value.Count).First();
            if (vtableHint != 0 && byVt.TryGetValue(vtableHint, out var hinted) && hinted.Count > best.Value.Count / 2) best = new(vtableHint, hinted);
            PlayerVTable = best.Key;
            // un même joueur peut exister en plusieurs exemplaires (objet vivant + copies de travail) :
            // on garde tous les exemplaires et on retient celui dont les liens (nom, contrat) sont valides
            var groups = best.Value.GroupBy(r => r.Id).ToList();
            int multi = groups.Count(gr => gr.Count() > 1);

            // 3) noms, contrats, clubs, calendrier
            progress?.Invoke(0.78, "Lecture des noms, contrats et clubs…");
            var clubNames = new ConcurrentDictionary<ulong, string>();
            var teamDates = new ConcurrentDictionary<ulong, uint>();
            var rowsBag = new ConcurrentBag<PlayerRow>();
            int n = 0, okName = 0, okContract = 0;
            var diag = new ConcurrentQueue<string>();
            Parallel.ForEach(groups, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount - 1), CancellationToken = ct },
                () => new byte[0x50],
                (gr, _, co) =>
                {
                    PlayerRow chosen = null;
                    foreach (var r in gr.OrderByDescending(x => x.ContractPtr > 0x10000))
                    {
                        string name = ReadName(g, r);
                        bool contract = r.ContractPtr > 0x10000 && g.Read(r.ContractPtr, co, co.Length);
                        if (name == null && !contract)
                        {
                            if (diag.Count < 12)
                                diag.Enqueue($"échec id {r.Id} @0x{r.Addr:X} : noms 0x{r.FirstNamePtr:X}/0x{r.LastNamePtr:X}/0x{r.CommonNamePtr:X}, contrat 0x{r.ContractPtr:X}, exemplaires {gr.Count()}");
                            continue;
                        }
                        chosen = r;
                        if (name != null) { r.Name = name; Interlocked.Increment(ref okName); }
                        if (contract)
                        {
                            Interlocked.Increment(ref okContract);
                            uint wage = BitConverter.ToUInt32(co, 0x20);
                            if (wage < 50_000_000u) r.Wage = wage;
                            (r.ExpYear, r.ExpDoy) = DecodeDate(BitConverter.ToUInt32(co, 0x48));
                            r.Team = BitConverter.ToUInt64(co, 0x10);
                            if (r.Team > 0x10000)
                            {
                                r.ClubPtr = g.Ptr(r.Team + 0x30);
                                if (r.ClubPtr > 0x10000)
                                    r.Club = clubNames.GetOrAdd(r.ClubPtr, c => { ulong p = g.Ptr(c + 0xC0); return p == 0 ? "" : g.CStr(p + 4) ?? ""; });
                                teamDates.TryAdd(r.Team, 0);
                            }
                        }
                        break;
                    }
                    chosen ??= gr.First();
                    if (string.IsNullOrEmpty(chosen.Name)) chosen.Name = $"Joueur #{chosen.Id}";
                    chosen.NameKey = Fold(chosen.Name);
                    rowsBag.Add(chosen);
                    int k = Interlocked.Increment(ref n);
                    if (k % 2000 == 0) progress?.Invoke(0.78 + 0.22 * k / groups.Count, "Lecture des noms, contrats et clubs…");
                    return co;
                },
                _ => { });
            var rows = rowsBag.ToList();
            if (rows.Count < 1000) LastError = $"Seulement {rows.Count} joueurs trouvés : la partie est-elle chargée ?";
            Diagnostic = new List<string>
            {
                $"FM {g.PluginVersion} · game_plugin 0x{g.PluginBase:X} · vtable joueur +0x{PlayerVTable - g.PluginBase:X}",
                $"objets plausibles {best.Value.Count} · joueurs distincts {groups.Count} · avec plusieurs exemplaires {multi}",
                $"noms lus {okName} · contrats lus {okContract} · clubs {clubNames.Count} · équipes {teamDates.Count}",
            };
            Diagnostic.AddRange(diag);
            Diagnostic.Add("vtables les plus fréquents : " + string.Join(", ", byVt.OrderByDescending(kv => kv.Value.Count).Take(5)
                .Select(kv => $"+0x{kv.Key - g.PluginBase:X}={kv.Value.Count}")));

            // 4) date de jeu estimée
            EstimateDate(rows, dateHist);

            int female = rows.Count(r => r.Female);
            Diagnostic.Add($"joueuses (indicateur femme) : {female} sur {rows.Count}");
            if (female > rows.Count * 6 / 10) foreach (var r in rows) r.Female = false;   // indicateur incohérent
            ReadAt = DateTime.Now;
            LastError = rows.Count < 1000 ? LastError : "";
            progress?.Invoke(1, $"{rows.Count:N0} joueurs lus en {(DateTime.UtcNow - t0).TotalSeconds:F1} s");
            return rows;
        }

        static PlayerRow TryParse(byte[] b, int i, ulong addr)
        {
            int ca = BitConverter.ToUInt16(b, i + 0x264), pa = BitConverter.ToUInt16(b, i + 0x266);
            if (ca < 1 || ca > 200 || pa < ca || pa > 200) return null;
            int top = 0;
            for (int k = 0; k < 15; k++) { byte p = b[i + 0x150 + k]; if (p > 20) return null; if (p > top) top = p; }
            if (top < 10) return null;
            int set = 0;
            for (int k = 0; k < 0x36; k++) { byte a = b[i + 0x15F + k]; if (a > 100) return null; if (a > 0) set++; }
            if (set < 45) return null;
            int pe = i + 0x288;
            for (int k = 0; k < 8; k++) { byte p = b[pe + 0x70 + k]; if (p < 1 || p > 20) return null; }

            var r = new PlayerRow { Addr = addr, CA = ca, PA = pa, Id = BitConverter.ToUInt32(b, i + 0x290), Stamp = BitConverter.ToUInt32(b, i + 0x200) };
            Buffer.BlockCopy(b, i + 0x15F, r.Attrs, 0, 0x36);
            Buffer.BlockCopy(b, i + 0x150, r.Positions, 0, 15);
            Buffer.BlockCopy(b, pe + 0x70, r.Personality, 0, 8);
            uint val = BitConverter.ToUInt32(b, i + 0x234);
            if (val != 0xFFFFFFFF && val != 300_000_000u && val < 2_000_000_000u) r.Value = val;
            (r.BirthYear, r.BirthDoy) = DecodeDate(BitConverter.ToUInt32(b, pe + 0x88));
            r.FirstNamePtr = BitConverter.ToUInt64(b, pe + 0x50);
            r.LastNamePtr = BitConverter.ToUInt64(b, pe + 0x58);
            r.CommonNamePtr = BitConverter.ToUInt64(b, pe + 0x60);
            r.ContractPtr = BitConverter.ToUInt64(b, pe + 0xA8);
            r.Female = (b[pe + 0x19] & 0x10) != 0;
            return r;
        }

        /// <summary>
        /// La date du jour est la date FM la plus fréquente en mémoire juste avant les premières fins de contrat
        /// (aucun contrat en cours n'a déjà expiré). Les candidats sont écrits dans le diagnostic.
        /// </summary>
        static void EstimateDate(List<PlayerRow> rows, ConcurrentDictionary<uint, int> hist)
        {
            DateTime D(uint v) => new DateTime((int)(v >> 16), 1, 1).AddDays((v & 0x1FF) - 1);
            var exp = rows.Where(r => r.ExpYear > 2000).GroupBy(r => new DateTime(r.ExpYear, 1, 1).AddDays(r.ExpDoy - 1))
                .Select(gr => (d: gr.Key, n: gr.Count())).OrderBy(x => x.d).ToList();
            int contracts = exp.Sum(x => x.n);
            var firstExp = exp.FirstOrDefault(x => x.n >= Math.Max(20, contracts / 500)).d;
            Diagnostic.Add("premières fins de contrat : " + string.Join(", ", exp.Take(8).Select(x => $"{x.d:dd/MM/yyyy}×{x.n}")));
            var cands = hist.Where(kv => kv.Value >= 20).Select(kv => (d: D(kv.Key), n: kv.Value))
                .Where(x => firstExp == default || (x.d <= firstExp && x.d > firstExp.AddDays(-400)))
                .OrderByDescending(x => x.n).ToList();
            Diagnostic.Add("dates candidates : " + string.Join(", ", cands.Take(15).Select(x => $"{x.d:dd/MM/yyyy}×{x.n}")));
            Diagnostic.Add("dates les plus fréquentes : " + string.Join(", ", hist.OrderByDescending(kv => kv.Value).Take(15).Select(kv => $"{D(kv.Key):dd/MM/yyyy}×{kv.Value}")));
            if (DateIsManual) return;
            // 1) source directe : la date « du jour » rangée dans chaque joueur (+0x200, sans drapeaux)
            var stamps = rows.Select(r => r.Stamp).Where(v => (v & 0xFE00) == 0 && (v >> 16) - 2020u <= 50u && (v & 0x1FF) - 1u < 366u)
                .GroupBy(v => v).Select(gr => (v: gr.Key, n: gr.Count())).OrderByDescending(x => x.n).ToList();
            Diagnostic.Add("dates joueurs (+0x200) : " + string.Join(", ", stamps.Take(6).Select(x => $"{D(x.v):dd/MM/yyyy}×{x.n}")));
            // beaucoup de joueurs gardent la date de création de la base : on prend la date fréquente LA PLUS RÉCENTE
            // qui ne dépasse pas les premières fins de contrat (aucun contrat en cours n'est déjà terminé)
            int minCount = 25;
            var recent = stamps.Where(x => x.n >= minCount).Select(x => D(x.v))
                .Where(d => firstExp == default || d <= firstExp).OrderByDescending(d => d).ToList();
            if (recent.Count > 0)
            {
                var d = recent[0];
                GameYear = d.Year; GameDoy = d.DayOfYear;
                return;
            }
            // 2) repli : estimation par fréquence
            if (cands.Count == 0) return;
            // les débuts de contrat (01/07, 01/01…) sont fréquents mais toujours antérieurs à aujourd'hui :
            // on prend la date fréquente la plus récente
            int top = cands[0].n;
            var today = cands.Where(x => x.n * 4 >= top).Max(x => x.d);
            GameYear = today.Year; GameDoy = today.DayOfYear;
        }

        static string ReadName(GameProcess g, PlayerRow r)
        {
            // chaîne imbriquée FM : [champ] -> objet -> [objet] -> texte à +4
            string Nested(ulong outer)
            {
                if (outer < 0x10000) return null;
                ulong s = g.Ptr(outer); if (s < 0x10000) return null;
                return g.CStr(s + 4);
            }
            string common = Nested(r.CommonNamePtr);
            if (!string.IsNullOrWhiteSpace(common)) return common;
            var parts = new[] { Nested(r.FirstNamePtr), Nested(r.LastNamePtr) }.Where(x => !string.IsNullOrWhiteSpace(x));
            string name = string.Join(" ", parts);
            return name.Length > 0 ? name : null;
        }

        public static string Fold(string s)
        {
            var d = s.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder(d.Length);
            foreach (char c in d)
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }
    }
}
