using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace FM27Scout
{
    /// <summary>
    /// Client du module FM27 Bridge (chargé dans FM par BepInEx, sans interface).
    /// Le module écoute sur 127.0.0.1:27027 : une ligne = une commande, une ligne = la réponse.
    /// Il passe par le même chemin que les menus du jeu (ordres envoyés au moteur, canaux de données lus comme l'interface).
    /// </summary>
    internal static class Bridge
    {
        const int Port = 27027;
        static TcpClient s_client;
        static StreamReader s_rd;
        static StreamWriter s_wr;
        static readonly object s_lock = new();
        static DateTime s_tcpDeadUntil;          // le port répond mais ne traite plus rien : on passe par le fichier

        static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FM27Scout");

        /// <summary>Envoie une commande ; null si le module ne répond pas (FM fermé, module absent…).</summary>
        public static string Call(string cmd, int timeoutMs = 15000)
        {
            lock (s_lock)
            {
                var r = DateTime.Now < s_tcpDeadUntil ? null : CallTcp(cmd, timeoutMs);
                return r ?? CallFile(cmd, Math.Min(timeoutMs, cmd == "PING" ? 1500 : timeoutMs));
            }
        }

        /// <summary>Canal de secours : le module lit bridge_cmd.txt et ajoute « heure commande => réponse » dans bridge_out.txt.</summary>
        static string CallFile(string cmd, int timeoutMs)
        {
            try
            {
                string cmdPath = Path.Combine(Dir, "bridge_cmd.txt"), outPath = Path.Combine(Dir, "bridge_out.txt");
                if (File.Exists(cmdPath) && (DateTime.Now - File.GetLastWriteTime(cmdPath)).TotalSeconds > 5) return null;   // module absent : la commande précédente n'a jamais été lue
                long start = File.Exists(outPath) ? new FileInfo(outPath).Length : 0;
                cmd = cmd.Replace("\n", " ").Trim();
                File.WriteAllText(cmdPath, cmd + "\n", new UTF8Encoding(false));
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < timeoutMs)
                {
                    System.Threading.Thread.Sleep(40);
                    if (!File.Exists(outPath) || new FileInfo(outPath).Length <= start) continue;
                    string add;
                    using (var fs = new FileStream(outPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        fs.Seek(start, SeekOrigin.Begin);
                        add = new StreamReader(fs, Encoding.UTF8).ReadToEnd();
                    }
                    string marker = " " + cmd + " => ";
                    foreach (var line in add.Split('\n'))
                    {
                        int i = line.IndexOf(marker, StringComparison.Ordinal);
                        if (i >= 0 && line.EndsWith("\r") == false) return line[(i + marker.Length)..].TrimEnd('\r');
                        if (i >= 0) return line[(i + marker.Length)..].TrimEnd('\r');
                    }
                }
                try { if (File.Exists(cmdPath)) File.Delete(cmdPath); } catch { }
            }
            catch { }
            return null;
        }

        static string CallTcp(string cmd, int timeoutMs)
        {
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    try
                    {
                        if (s_client == null || !s_client.Connected)
                        {
                            Close();
                            s_client = new TcpClient();
                            var ar = s_client.BeginConnect("127.0.0.1", Port, null, null);
                            if (!ar.AsyncWaitHandle.WaitOne(800)) { Close(); return null; }
                            s_client.EndConnect(ar);
                            var st = s_client.GetStream();
                            s_rd = new StreamReader(st, new UTF8Encoding(false));
                            s_wr = new StreamWriter(st, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                        }
                        s_client.ReceiveTimeout = Math.Min(timeoutMs, 3000 + (cmd.StartsWith("READ") ? 6000 : 0));
                        s_wr.WriteLine(cmd.Replace("\n", " "));
                        return s_rd.ReadLine();
                    }
                    catch (IOException) when (s_client != null) { Close(); s_tcpDeadUntil = DateTime.Now.AddMinutes(10); return null; }
                    catch { Close(); }
                }
                return null;
            }
        }

        static void Close()
        {
            try { s_client?.Close(); } catch { }
            s_client = null; s_rd = null; s_wr = null;
        }

        /// <summary>« prêt » si le module tourne et a vu le moteur ; « attente » si FM charge encore ; null si absent.</summary>
        public static string Status()
        {
            var r = Call("PING", 2000);
            if (r == null) return null;
            return r.Contains("prêt") ? "prêt" : "attente";
        }

        static string Ok(string r, string what)
        {
            if (r == null) throw new Exception(L.T("FM27 Bridge ne répond pas (FM est-il lancé avec une partie chargée ?)"));
            if (!r.StartsWith("OK")) throw new Exception($"{what} : {r}");
            return r.Length > 3 ? r[3..] : "";
        }

        // ------------------------------------------------------------------ contexte (session, entraîneur principal, équipe)

        public sealed class Context
        {
            public int Manager, Team, Assistant;
            /// <summary>Contexte tiré de l'écran Entraînement (toujours ton équipe), pas du calendrier (qui peut être celui d'un autre club consulté).</summary>
            public bool FromTraining;
            public string TeamTraining => $"825307168=128;1198877028=1;1348620377={Manager};1416914273={Team}";
            public string Calendar => $"825241632=1;825307168=9;1198877028=1;1214805357={Manager};1415930221={Team}";
            public string PlayerTraining(int person) => $"825307168=77;1198877028=1;1348620377={Manager};1348825715={person};1416196963=1";
            public static string Person(int id) => $"825307168=28;1198877028=1;1685218662={id}";
        }

        public static Context Ctx()
        {
            string refs = Ok(Call("REFS"), L.T("Références"));
            var map = new Dictionary<string, Dictionary<uint, int>>();
            foreach (var part in refs.Split(" | "))
            {
                int eq = part.IndexOf('=');
                if (eq < 0) continue;
                string type = part[..eq].Trim();
                if (type.StartsWith("session=")) type = type[(type.IndexOf(' ') + 1)..];
                var d = new Dictionary<uint, int>();
                foreach (var kv in part[(eq + 1)..].Split(';'))
                {
                    var p = kv.Split('=');
                    if (p.Length == 2 && uint.TryParse(p[0], out var k) && int.TryParse(p[1], out var v)) d[k] = v;
                }
                map[type] = d;
            }
            var c = new Context();
            // l'entraînement n'existe que pour tes équipes ; le calendrier est le dernier consulté, parfois celui d'un autre club
            if (map.TryGetValue("FM.UI.TeamTrainingReference", out var tt) && tt.GetValueOrDefault(1348620377u) != 0 && tt.GetValueOrDefault(1416914273u) != 0)
            { c.Manager = tt[1348620377u]; c.Team = tt[1416914273u]; c.FromTraining = true; }
            else if (map.TryGetValue("FM.UI.CalendarReference", out var cal) && cal.ContainsKey(1214805357))
            { c.Manager = cal[1214805357]; c.Team = cal.GetValueOrDefault(1415930221u); }
            if (c.Manager == 0 || c.Team == 0) throw new Exception(L.T("partie pas encore reconnue : ouvre une fois l'écran Entraînement dans FM"));
            return c;
        }

        // ------------------------------------------------------------------ lecture

        /// <summary>Lit plusieurs propriétés d'un coup ; chaque requête = (type de référence, identifiants, propriété FourCC ou nombre).</summary>
        public static List<JsonElement?> Read(IList<(string type, string pairs, string prop)> reqs)
        {
            var res = new List<JsonElement?>();
            for (int i = 0; i < reqs.Count; i += 60)
            {
                var chunk = reqs.Skip(i).Take(60).ToList();
                string r = Ok(Call("READ " + string.Join(";;", chunk.Select(q => $"FM.UI.{q.type}|{q.pairs}|{q.prop}")), 20000), L.T("Lecture"));
                var parts = r.Split('\u001e');
                for (int j = 0; j < chunk.Count; j++)
                {
                    string p = j < parts.Length ? parts[j] : "ERR";
                    if (p.StartsWith("ERR")) { res.Add(null); continue; }
                    // FM glisse des caractères de contrôle (marqueurs de lien) dans certains textes
                    if (p.Any(ch => ch < ' ')) p = new string(p.Select(ch => ch < ' ' ? ' ' : ch).ToArray());
                    try { using var doc = JsonDocument.Parse(p); res.Add(doc.RootElement.Clone()); }
                    catch { res.Add(null); }
                }
            }
            return res;
        }

        public static JsonElement? Read1(string type, string pairs, string prop) => Read(new[] { (type, pairs, prop) })[0];

        // ------------------------------------------------------------------ écriture (ordres au moteur, comme les menus de FM)

        public static void Send(uint ev, string refType, string pairs, string json)
            => Ok(Call($"SENDJ {ev} FM.UI.{refType} {pairs} {json}"), L.F("Ordre {0}", ev));

        /// <summary>Écrit une valeur dans un canal de données (ce que fait l'interface quand on coche une case).</summary>
        public static void WriteChannel(string refType, string pairs, uint prop, string json)
            => Ok(Call($"WRITE FM.UI.{refType}|{pairs}|{prop} {json}"), L.T("Écriture"));

        public static void TeamIntensity(int row, int option) => Ok(Call($"TEAMINT {row} {option}"), L.T("Intensité équipe"));

        // ------------------------------------------------------------------ petits utilitaires JSON (format du module)

        public static string Num(long v) => $"[\"SI.Core.DynamicNumber\",\"{v}\"]";
        public static string Rec(params (uint key, string type, string val)[] entries)
            => "{\"type\":\"SI.Bindable.DynamicReference\",\"entries\":[" + string.Join(",", entries.Select(e => $"[{e.key},\"{e.type}\",{e.val}]")) + "]}";
        public static string Q(string s) => JsonSerializer.Serialize(s);

        /// <summary>Champ d'un enregistrement (DynamicReference) lu par le module : {"type":…,"entries":[[clé,type,valeur],…]}.</summary>
        public static JsonElement? Field(JsonElement? rec, uint key)
        {
            if (rec is not { ValueKind: JsonValueKind.Object } r || !r.TryGetProperty("entries", out var en)) return null;
            foreach (var e in en.EnumerateArray()) if (e[0].GetUInt32() == key) return e[2];
            return null;
        }

        public static string Str(JsonElement? v) => v is { ValueKind: JsonValueKind.String } s ? s.GetString() : null;
        public static int Int(JsonElement? v, int def = int.MinValue)
        {
            var s = Str(v);
            return s != null && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? (int)Math.Round(d) : def;
        }
        public static bool Bool(JsonElement? v) => Str(v) == "True";

        /// <summary>Identifiant d'une référence {"type":…,"pairs":[[k,v],…]} pour une clé donnée.</summary>
        public static int PairOf(JsonElement? refObj, uint key)
        {
            if (refObj is not { ValueKind: JsonValueKind.Object } r || !r.TryGetProperty("pairs", out var ps)) return 0;
            foreach (var p in ps.EnumerateArray()) if (p[0].GetUInt32() == key) return p[1].GetInt32();
            return 0;
        }

        /// <summary>Identifiants complets d'une référence lue, au format « clé=valeur;… » attendu par READ/SENDJ.</summary>
        public static string PairsOf(JsonElement? refObj)
        {
            if (refObj is not { ValueKind: JsonValueKind.Object } r || !r.TryGetProperty("pairs", out var ps)) return "";
            return string.Join(";", ps.EnumerateArray().Select(p => $"{p[0].GetUInt32()}={p[1].GetInt64()}"));
        }

        /// <summary>Éléments d'une liste [[type, valeur], …].</summary>
        public static IEnumerable<JsonElement> Items(JsonElement? list)
        {
            if (list is not { ValueKind: JsonValueKind.Array } l) yield break;
            foreach (var x in l.EnumerateArray()) if (x.ValueKind == JsonValueKind.Array && x.GetArrayLength() == 2) yield return x[1];
        }

        // mots d'un lien de profil dans les langues de FM : « Cliquez pour voir le profil de : X », « Click to view … », « Clique para ver o perfil de: X »…
        static readonly string[] ViewWords = { "pour voir ", "to view ", "para ver ", "per vedere " };
        static readonly string[] ProfileWords = { "profile", "profil", "perfil", "profilo" };

        /// <summary>Nom affiché par FM : chaîne « lien (base64) + nom » → nom complet tiré de l'infobulle, sinon le nom court. Toutes langues de FM.</summary>
        public static string DisplayName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = new string(s.Where(ch => ch >= ' ').ToArray()).Trim();
            // lien en clair : « …Cliquez pour voir <nom complet><nom court> »
            bool hasProfile = ProfileWords.Any(w => s.Contains(w, StringComparison.OrdinalIgnoreCase));
            foreach (var vw in ViewWords)
            {
                int pv = s.LastIndexOf(vw, StringComparison.OrdinalIgnoreCase);
                if (pv < 0 || hasProfile) continue;
                string rest = s[(pv + vw.Length)..].Trim();
                for (int k = (rest.Length + 1) / 2; k < rest.Length; k++)
                    if (char.IsUpper(rest[k]) && rest[..k].Contains(rest[k..])) return rest[..k].Trim();
                return rest;
            }
            if (hasProfile && FromTip(s) is { } plain) return plain;
            for (int len = s.Length - s.Length % 4; len >= 8; len -= 4)
            {
                try
                {
                    var b = Convert.FromBase64String(s[..len]);
                    foreach (var word in ProfileWords)
                        foreach (var key in new[] { Encoding.UTF8.GetBytes(word), Encoding.UTF8.GetBytes(char.ToUpperInvariant(word[0]) + word[1..]) })
                        {
                            int q = IndexOf(b, key);
                            if (q < 0) continue;
                            // l'infobulle est une chaîne précédée de sa longueur (entier 32 bits)
                            for (int k = q - 4; k >= Math.Max(0, q - 300); k--)
                            {
                                int L = BitConverter.ToInt32(b, k);
                                if (L <= 0 || L > 400 || k + 4 + L > b.Length || q >= k + 4 + L) continue;
                                string tip = Encoding.UTF8.GetString(b, k + 4, L);
                                if (tip.Any(ch => ch < ' ' && ch != ' ')) continue;
                                if (FromTip(tip) is { } name) return name;
                            }
                        }
                }
                catch { }
            }
            int eq = s.LastIndexOf('=');
            // format inconnu (autre langue de FM ?) : noter un exemple une fois pour pouvoir l'ajouter
            if (!s_nameLogged && s.Length > 40) { s_nameLogged = true; Log.Info("Nom FM sans infobulle reconnue : " + s[..Math.Min(s.Length, 400)]); }
            return eq >= 0 && eq < s.Length - 1 ? s[(eq + 1)..] : s;
        }

        static bool s_nameLogged;

        /// <summary>Nom tiré d'une infobulle de profil : « …profil de : X » (fr), « …profile: X » / « View X's profile » (en), « …perfil de: X » (pt).</summary>
        static string FromTip(string tip)
        {
            foreach (var w in ProfileWords)
            {
                int k = tip.IndexOf(w, StringComparison.OrdinalIgnoreCase);
                if (k < 0) continue;
                int c = tip.IndexOf(':', k);
                if (c >= 0 && tip[(c + 1)..].Trim() is { Length: > 1 } afterColon) return afterColon;
                // anglais : « View X's profile »
                int ap = tip.IndexOf("'s " + w, StringComparison.OrdinalIgnoreCase);
                if (ap < 0) ap = tip.IndexOf("’s " + w, StringComparison.OrdinalIgnoreCase);
                if (ap > 0)
                {
                    string before = tip[..ap];
                    int v = before.LastIndexOf("view ", StringComparison.OrdinalIgnoreCase);
                    string n = (v >= 0 ? before[(v + 5)..] : before).Trim();
                    if (n.Length > 1) return n;
                }
                // sans deux-points : « perfil de X », « profil de X », « profile of X »
                foreach (var de in new[] { " de ", " do ", " da ", " of " })
                    if (string.Compare(tip, k + w.Length, de, 0, de.Length, StringComparison.OrdinalIgnoreCase) == 0
                        && tip[(k + w.Length + de.Length)..].Trim() is { Length: > 1 } n2) return n2;
            }
            return null;
        }

        static int IndexOf(byte[] hay, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= hay.Length; i++)
            {
                int j = 0;
                while (j < needle.Length && hay[i + j] == needle[j]) j++;
                if (j == needle.Length) return i;
            }
            return -1;
        }

        /// <summary>FourCC → nombre (les propriétés de FM sont des codes de 4 lettres).</summary>
        public static uint CC(string s) => (uint)(s[0] << 24 | s[1] << 16 | s[2] << 8 | s[3]);
    }
}
