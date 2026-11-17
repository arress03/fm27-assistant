using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FM27Scout
{
    /// <summary>
    /// Sonde : copie brute (lecture seule) de la mémoire autour de ton club, pour retrouver hors de l'app
    /// où FM range l'état de forme des joueurs et le calendrier des matchs.
    /// Format du .bin : suite de blocs [adresse u64][longueur u32][octets]. Le .txt donne l'index (joueurs, équipe, club).
    /// Les 6 dernières sondes sont gardées dans %APPDATA%\FM27Scout\sonde.
    /// </summary>
    internal static class Probe
    {
        static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FM27Scout", "sonde");

        static bool HeapPtr(GameProcess g, ulong p) => p > 0x10000 && p < 0x7FFF_FFFF_FFFF && (p & 7) == 0 && (p < g.PluginBase || p >= g.PluginEnd);

        public static void Dump(GameProcess g, List<PlayerRow> squad)
        {
            if (squad.Count == 0) return;
            try
            {
                Directory.CreateDirectory(Dir);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss") + (Game.HasDate ? "_jeu" + Game.Date.ToString("yyyyMMdd") : "");
                var seen = new HashSet<ulong>();
                long total = 0;
                using var bw = new BinaryWriter(File.Create(Path.Combine(Dir, stamp + ".bin")));
                bool Block(ulong addr, int len, out byte[] data)
                {
                    data = null;
                    if (total > 60_000_000 || !seen.Add(addr)) return false;
                    var b = new byte[len];
                    if (!g.Read(addr, b, len)) return false;
                    bw.Write(addr); bw.Write(len); bw.Write(b);
                    total += len + 12;
                    data = b;
                    return true;
                }

                // joueurs : objet (0x600) + chaque objet pointé (0x300), sur 2 niveaux pour l'objet joueur
                foreach (var r in squad)
                {
                    if (!Block(r.Addr, 0x600, out var pb)) continue;
                    var lvl1 = new List<ulong>();
                    for (int o = 0; o + 8 <= pb.Length; o += 8) { ulong p = BitConverter.ToUInt64(pb, o); if (HeapPtr(g, p)) lvl1.Add(p); }
                    foreach (var p in lvl1.Distinct().Take(160))
                        if (Block(p, 0x300, out var cb))
                            for (int o = 0; o + 8 <= 0x100; o += 8) { ulong q = BitConverter.ToUInt64(cb, o); if (HeapPtr(g, q)) Block(q, 0x200, out _); }
                }

                // équipe et club : parcours en largeur sur 3 niveaux (le calendrier est quelque part par là)
                var first = squad.FirstOrDefault(r => !r.Female && r.Team != 0) ?? squad[0];
                var queue = new Queue<(ulong addr, int depth)>();
                var roots = squad.Select(r => r.Team).Concat(squad.Select(r => r.ClubPtr)).Where(x => x != 0).Distinct().ToList();
                foreach (var root in roots) queue.Enqueue((root, 0));
                var visited = new HashSet<ulong>();
                int nodes = 0;
                while (queue.Count > 0 && nodes < 6000)
                {
                    var (a, d) = queue.Dequeue();
                    if (!visited.Add(a)) continue;
                    seen.Remove(a);   // déjà copié en petit depuis un joueur : on le recopie en grand
                    if (!Block(a, 0x800, out var nb)) continue;
                    nodes++;
                    if (d >= 3) continue;
                    for (int o = 0; o + 8 <= nb.Length; o += 8) { ulong p = BitConverter.ToUInt64(nb, o); if (HeapPtr(g, p) && !visited.Contains(p)) queue.Enqueue((p, d + 1)); }
                }

                var idx = new StringBuilder();
                idx.AppendLine($"date de jeu {Game.FmDate(Game.GameYear, Game.GameDoy)} · club {Settings.Cur.MyClub} · équipe 0x{first.Team:X} · club 0x{first.ClubPtr:X} · racines {string.Join(",", roots.Select(x => "0x" + x.ToString("X")))} · {total / 1024} Ko · {nodes} objets équipe/club");
                foreach (var r in squad.OrderByDescending(r => r.CA))
                    idx.AppendLine($"0x{r.Addr:X}\t{r.Id}\t{r.Uid}\t{r.Name}\tCA {r.CA}\tcontrat 0x{r.ContractPtr:X}");
                File.WriteAllText(Path.Combine(Dir, stamp + ".txt"), idx.ToString());

                foreach (var old in Directory.GetFiles(Dir, "*.bin").OrderByDescending(f => f).Skip(6))
                { File.Delete(old); File.Delete(Path.ChangeExtension(old, ".txt")); }
            }
            catch (Exception e) { Log.Warn("Sonde : " + e.Message); }
        }
    }
}
