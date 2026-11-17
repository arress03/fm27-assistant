using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FM27Scout
{
    /// <summary>
    /// Photos et logos des packs graphiques (Documents\Sports Interactive\Football Manager 27\graphics).
    /// Les config.xml des packs disent quelle image va à quel identifiant FM :
    ///   from="face_123" to="graphics/pictures/person/123/portrait"
    ///   from="TCM1_456" to="graphics/pictures/team/456/logo"
    /// L'identifiant FM n'est pas l'index interne du joueur : son emplacement dans l'objet est retrouvé
    /// automatiquement (l'offset dont les valeurs tombent le plus souvent sur une photo existante).
    /// </summary>
    internal static class Gfx
    {
        public static readonly ConcurrentDictionary<uint, string> Faces = new(), Logos = new();
        public static readonly ConcurrentDictionary<ulong, uint> ClubUid = new();   // objet club -> identifiant FM
        public static bool Loaded;
        public static string Info = "";

        static readonly Regex Map = new("from=\"([^\"]+)\"\\s+to=\"graphics/pictures/(person|team|club)/(\\d+)/(portrait|logo)\"", RegexOptions.Compiled);

        public static string Root => Settings.Cur.GraphicsPath != "" ? Settings.Cur.GraphicsPath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive", "Football Manager 27", "graphics");

        public static void Load()
        {
            try
            {
                if (!Directory.Exists(Root)) { Info = "Dossier graphics introuvable : " + Root; return; }
                var files = Directory.EnumerateFiles(Root, "config.xml", SearchOption.AllDirectories)
                    .Where(f => !f.Contains(Path.DirectorySeparatorChar + "maillots" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).ToList();
                Parallel.ForEach(files, f =>
                {
                    string dir = Path.GetDirectoryName(f);
                    foreach (string line in File.ReadLines(f))
                    {
                        var m = Map.Match(line);
                        if (!m.Success || !uint.TryParse(m.Groups[3].Value, out uint uid)) continue;
                        string img = Path.Combine(dir, m.Groups[1].Value + ".png");
                        (m.Groups[2].Value == "person" ? Faces : Logos).TryAdd(uid, img);
                    }
                });
                Info = $"{Faces.Count:N0} photos, {Logos.Count:N0} logos";
            }
            catch (Exception e) { Info = "Graphismes : " + e.Message; Log.Warn(Info); }
            Loaded = true;
        }

        /// <summary>Retrouve où sont rangés les identifiants FM du joueur et du club, puis les remplit.</summary>
        public static void Calibrate(GameProcess g, List<PlayerRow> rows, List<string> diag)
        {
            if (Faces.Count == 0 && Logos.Count == 0) return;
            const int SPAN = 0x400;
            var buf = new byte[SPAN];

            int Best(IEnumerable<ulong> objs, ConcurrentDictionary<uint, string> set, int hint, string what)
            {
                // un identifiant est différent pour chaque objet : on compte les valeurs distinctes qui ont une image
                var hits = new int[SPAN / 4];
                var seen = new HashSet<uint>[SPAN / 4];
                for (int k = 0; k < seen.Length; k++) seen[k] = new();
                int n = 0;
                foreach (ulong o in objs)
                {
                    if (!g.Read(o, buf, SPAN)) continue;
                    n++;
                    for (int k = 0; k < SPAN / 4; k++)
                    {
                        uint v = BitConverter.ToUInt32(buf, k * 4);
                        if (v > 0 && set.ContainsKey(v) && seen[k].Add(v)) hits[k]++;
                    }
                }
                if (n == 0) return -1;
                int best = Array.IndexOf(hits, hits.Max());
                if (hint >= 0 && hint / 4 < hits.Length && hits[hint / 4] * 10 >= hits[best] * 9) best = hint / 4;
                diag.Add($"{what} : {n} objets testés, meilleur offset +0x{best * 4:X} ({hits[best]} correspondances), suivants : " +
                    string.Join(", ", hits.Select((h, i) => (h, i)).OrderByDescending(x => x.h).Skip(1).Take(3).Select(x => $"+0x{x.i * 4:X}={x.h}")));
                return hits[best] * 10 >= n ? best * 4 : -1;   // au moins 10 % des objets ont une image
            }

            // joueurs connus (forts) : ce sont eux qui ont une photo dans les packs
            var sample = rows.Where(r => r.CA >= 110).OrderByDescending(r => r.CA).Take(3000).Select(r => r.Addr).ToList();
            int pOff = Faces.Count == 0 ? -1 : Best(sample, Faces, Settings.Cur.PersonUidOff, "identifiant joueur");
            if (pOff >= 0)
            {
                Settings.Cur.PersonUidOff = pOff;
                Parallel.ForEach(rows, () => new byte[4], (r, _, b) => { if (g.Read(r.Addr + (ulong)pOff, b, 4)) r.Uid = BitConverter.ToUInt32(b, 0); return b; }, _ => { });
            }

            var clubs = rows.Where(r => r.ClubPtr != 0).Select(r => r.ClubPtr).Distinct().ToList();
            int cOff = Logos.Count == 0 ? -1 : Best(clubs.Take(4000), Logos, Settings.Cur.ClubUidOff, "identifiant club");
            ClubUid.Clear();
            if (cOff >= 0)
            {
                Settings.Cur.ClubUidOff = cOff;
                foreach (ulong c in clubs) ClubUid[c] = g.U32(c + (ulong)cOff);
            }
            Settings.Save();
        }

        static readonly ConcurrentDictionary<string, ImageSource> Cache = new();

        static ImageSource Image(string path, int px)
        {
            if (path == null || !File.Exists(path)) return null;
            return Cache.GetOrAdd(path + "|" + px, _ =>
            {
                try
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.UriSource = new Uri(path);
                    bi.DecodePixelWidth = px;
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.EndInit();
                    bi.Freeze();
                    return bi;
                }
                catch { return null; }
            });
        }

        public static ImageSource Face(PlayerRow r, int px = 180) => r?.Uid > 0 && Faces.TryGetValue(r.Uid, out var p) ? Image(p, px) : null;
        public static ImageSource Logo(ulong club, int px = 64) => club != 0 && ClubUid.TryGetValue(club, out uint u) && Logos.TryGetValue(u, out var p) ? Image(p, px) : null;
    }
}
