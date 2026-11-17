// FM27 Bridge : module sans interface, piloté par l'app FM27 Scout (127.0.0.1:27027).
// - enregistre les ordres que l'interface de FM envoie au moteur (GameInteropSubsystem.SendEvent) ;
// - rejoue ces ordres à la demande de l'app (même chemin que les menus du jeu).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using FM.GamePlugin;
using HarmonyLib;
using Il2CppInterop.Runtime;
using SI.Core;
using SI.Interop;
using UnityEngine;

namespace FM27Bridge
{
    [BepInPlugin(Guid, "FM27 Bridge", "1.0.0")]
    public class Plugin : BasePlugin
    {
        public const string Guid = "theo.fm27bridge";
        internal static GameInteropSubsystem Interop;
        internal static readonly ConcurrentQueue<Action> MainThread = new();
        internal static readonly List<string> Events = new();          // ordres enregistrés (JSON par ligne)
        internal static readonly Dictionary<uint, InteropReference> LastRef = new();   // dernière référence vue par événement
        internal static readonly Dictionary<uint, string> LastType = new();
        internal static bool Recording = true;
        internal static readonly Dictionary<string, InteropReference> RefByType = new();   // dernier objet vu par type de référence
        internal static readonly Dictionary<string, string> PairsByType = new();          // ses identifiants (k=v;k=v)
        internal static int SessionId;                                                    // identifiant de session (clé 1198877028)
        internal static readonly Dictionary<long, TypedValue> NumCache = new();           // nombres du jeu déjà vus (copies)
        const uint KeySession = 1198877028;
        static string RefsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FM27Scout", "bridge_refs.txt");
        internal static bool Replaying;
        internal static readonly Dictionary<ulong, string> Channels = new();     // clé de canal -> "type|identifiants|propriété"
        internal static readonly HashSet<string> ChannelKinds = new();
        static string ChannelsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FM27Scout", "bridge_channels.txt");
        static string CmdPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FM27Scout", "bridge_cmd.txt");
        static string OutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FM27Scout", "bridge_out.txt");

        internal static string PairsOf(InteropReference r)
        {
            var ids = r?.m_id; if (ids == null) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < ids.Count; i++) sb.Append(i > 0 ? ";" : "").Append(ids[i].ID).Append('=').Append(ids[i].Value);
            return sb.ToString();
        }

        internal static void NoteChannel(InteropReference r, uint prop, ulong key)
        {
            try
            {
                string type = r?.GetIl2CppType().FullName ?? "null";
                lock (Channels) Channels[key] = type + "|" + PairsOf(r) + "|" + prop;
                string kind = type + "|" + prop;
                lock (ChannelKinds) if (ChannelKinds.Add(kind)) File.AppendAllText(ChannelsPath, kind + "\t" + PairsOf(r) + Environment.NewLine);
            }
            catch { }
        }

        // canal fichier : l'app (ou un script) écrit des commandes dans bridge_cmd.txt, réponses dans bridge_out.txt
        static void FileChannel()
        {
            while (true)
            {
                try
                {
                    if (File.Exists(CmdPath))
                    {
                        var lines = File.ReadAllLines(CmdPath);
                        File.Delete(CmdPath);
                        foreach (var l in lines)
                        {
                            if (string.IsNullOrWhiteSpace(l)) continue;
                            string reply;
                            try { lock (HandleLock) reply = Handle(l.Trim()); } catch (Exception e) { reply = "ERR " + e.Message; }
                            File.AppendAllText(OutPath, System.DateTime.Now.ToString("HH:mm:ss ") + l.Trim() + " => " + reply.Replace("\n", " ") + Environment.NewLine);
                        }
                    }
                }
                catch (Exception e) { Write("canal fichier : " + e.Message); }
                Thread.Sleep(300);
            }
        }
        static string LogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FM27Scout", "bridge.log");

        public override void Load()
        {
            try { if (File.Exists(RefsPath)) foreach (var l in File.ReadAllLines(RefsPath)) { var i = l.IndexOf('\t'); if (i > 0) PairsByType[l[..i]] = l[(i + 1)..]; } } catch { }
            new Harmony(Guid).PatchAll(typeof(Patches));
            try { new Harmony(Guid + ".data").PatchAll(typeof(DataPatches)); Write("lecture des données active"); }
            catch (Exception e) { Write("lecture des données indisponible : " + e.Message); }
            AddComponent<BridgeBehaviour>();
            new Thread(Server) { IsBackground = true, Name = "FM27Bridge" }.Start();
            new Thread(FileChannel) { IsBackground = true, Name = "FM27BridgeFile" }.Start();
            Write("FM27 Bridge chargé");
        }

        internal static void Write(string s)
        {
            try { File.AppendAllText(LogPath, System.DateTime.Now.ToString("HH:mm:ss ") + s + Environment.NewLine); } catch { }
        }

        /// <summary>Mémorise une référence vue passer (type, identifiants, numéro de session).</summary>
        internal static void Learn(InteropReference r)
        {
            if (r == null) return;
            try
            {
                string type = r.GetIl2CppType().FullName;
                RefByType[type] = r;
                lock (SeenRefs) { if (SeenRefs.Count > 30000) SeenRefs.Clear(); SeenRefs[type + "|" + PairsOf(r)] = r; }
                var ids = r.m_id; if (ids == null) return;
                var sb = new StringBuilder();
                for (int i = 0; i < ids.Count; i++)
                {
                    sb.Append(i > 0 ? ";" : "").Append(ids[i].ID).Append('=').Append(ids[i].Value);
                    if (ids[i].ID == KeySession) SessionId = ids[i].Value;
                }
                string pairs = sb.ToString();
                bool isNew = !PairsByType.ContainsKey(type);
                PairsByType[type] = pairs;
                if (isNew) File.WriteAllLines(RefsPath, PairsByType.Select(kv => kv.Key + "\t" + kv.Value));
            }
            catch { }
        }

        internal static void LearnNumbers(TypedValue v)
        {
            try
            {
                if (v == null || v.IsNull) return;
                var dict = (v.Get() as Il2CppSystem.Object)?.TryCast<Il2CppSystem.Collections.Generic.Dictionary<uint, TypedValue>>();
                if (dict == null) return;
                var e = dict.GetEnumerator();
                while (e.MoveNext())
                {
                    var tv = e.Current.Value;
                    if (tv == null || tv.IsNull || tv.DataType?.FullName != "SI.Core.DynamicNumber") continue;
                    if (long.TryParse(tv.AsString(), out long n) && !NumCache.ContainsKey(n)) NumCache[n] = TypedValue.CreateCopyFrom(tv);
                }
            }
            catch { }
        }

        /// <summary>Nombre au format du jeu (DynamicNumber) : création directe, sinon copie d'un nombre déjà vu.</summary>
        internal static TypedValue Number(int n)
        {
            try { var dn = new DynamicNumber(); dn.Set(n); return TypedValue.CreateValueType<DynamicNumber>(dn); }
            catch (Exception e) { Write("Nombre (méthode 1) impossible : " + e.Message); }
            try
            {
                var any = NumCache.Values.FirstOrDefault();
                if (any != null)
                {
                    var tv = TypedValue.CreateCopyFrom(any);
                    var dn = tv.Get<DynamicNumber>(); dn.Set(n); tv.SetValue<DynamicNumber>(dn);
                    if (tv.AsString() == n.ToString()) return tv;
                }
            }
            catch (Exception e) { Write("Nombre (méthode 2) impossible : " + e.Message); }
            if (NumCache.TryGetValue(n, out var c)) return TypedValue.CreateCopyFrom(c);
            throw new Exception("nombre " + n + " indisponible (change une fois ce réglage dans FM)");
        }

        static readonly Dictionary<string, System.Type> s_managed = new();
        static System.Reflection.MethodInfo s_create;

        /// <summary>Valeur typée exactement comme le jeu le fait (Create&lt;T&gt; avec le vrai type) : nécessaire pour les listes et références.</summary>
        internal static TypedValue TypedRef(Il2CppSystem.Object o)
        {
            try
            {
                string n = o.GetIl2CppType().FullName;
                if (!s_managed.TryGetValue(n, out var mt))
                {
                    if (o.TryCast<Il2CppSystem.Collections.Generic.List<TypedValue>>() != null) mt = typeof(Il2CppSystem.Collections.Generic.List<TypedValue>);
                    else foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) { try { mt = a.GetType(n); } catch { } if (mt != null) break; }
                    s_managed[n] = mt;
                }
                if (mt != null)
                {
                    s_create ??= typeof(TypedValue).GetMethods().First(m => m.Name == "Create" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
                    var typed = Activator.CreateInstance(mt, o.Pointer);
                    return (TypedValue)s_create.MakeGenericMethod(mt).Invoke(null, new[] { typed });
                }
            }
            catch (Exception e) { Write("valeur typée impossible : " + (e.InnerException?.Message ?? e.Message)); }
            return RefValue(o);
        }

        internal static TypedValue RefValue(Il2CppSystem.Object o)
        {
            try { return TypedValue.Create<Il2CppSystem.Object>(o); }
            catch (Exception e) { Write("Valeur référence (méthode 1) impossible : " + e.Message); }
            var v = TypedValue.GetReferenceTypedValue(); v.SetValue(o); return v;
        }

        /// <summary>Référence d'un type donné : dernier objet vu, sinon reconstruit à partir des identifiants mémorisés.</summary>
        internal static readonly Dictionary<string, InteropReference> SeenRefs = new();       // "type|paires" -> objet réel du jeu

        internal static InteropReference BuildRef(string type, string overridePairs = null, bool useCache = true)
        {
            if (!PairsByType.TryGetValue(type, out var pairs) && overridePairs == null) throw new Exception("référence inconnue : " + type);
            pairs = overridePairs ?? pairs;
            var list = new Il2CppSystem.Collections.Generic.List<InteropReference.Pair>();
            foreach (var kv in pairs.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var p = kv.Split('=');
                uint k = uint.Parse(p[0]); int val = int.Parse(p[1]);
                if (k == KeySession && SessionId != 0) val = SessionId;
                list.Add(new InteropReference.Pair(k, val));
            }
            if (useCache)
            {
                var kb = new StringBuilder(type).Append('|');
                for (int i = 0; i < list.Count; i++) kb.Append(i > 0 ? ";" : "").Append(list[i].ID).Append('=').Append(list[i].Value);
                string key = kb.ToString();
                lock (SeenRefs) if (SeenRefs.TryGetValue(key, out var real)) return real;
            }
            InteropReference r;
            if (RefByType.TryGetValue(type, out var model)) r = model.MemberwiseClone().Cast<InteropReference>();
            else
            {
                Il2CppSystem.Type t = null;
                try { var mt = System.Type.GetType(type + ", FM.UI"); if (mt != null) t = Il2CppType.From(mt); } catch { }
                t ??= Il2CppSystem.Type.GetType(type + ", FM.UI") ?? throw new Exception("type introuvable : " + type);
                r = Il2CppSystem.Activator.CreateInstance(t).Cast<InteropReference>();
            }
            // empreinte calculée par le constructeur du jeu (InteropReference(List<Pair>)), recopiée dans l'objet du bon type
            try
            {
                var tmp = new InteropReference(list);
                r.m_id = tmp.m_id;
                r.m_idHash = tmp.m_idHash;
            }
            catch (Exception e)
            {
                Write("empreinte : " + e.Message);
                r.m_id = list; int h = 17; foreach (var p in list) h = h * 31 + (int)p.ID * 7 + p.Value; r.m_idHash = h;
            }
            return r;
        }

        /// <summary>Intensité de l'équipe pour une ligne (0 = très mauvaise forme … 4 = très bonne forme).</summary>
        internal static string TeamIntensity(int row, int option)
        {
            if (Interop == null) return "ERR FM pas encore prêt";
            string[] labels = { "Aucun travail sur le terrain ou en salle de gym", "Semi-intensité", "Intensité normale", "Double intensité" };
            var r = BuildRef("FM.UI.TeamTrainingReference");
            var dyn = new SI.Bindable.DynamicReference();
            var dict = dyn.Cast<Il2CppSystem.Collections.Generic.Dictionary<uint, TypedValue>>();
            dict[1886680684] = Number(option);
            dict[1903577654] = TypedValue.Create(labels[Math.Clamp(option, 0, 3)]);
            dict[1668179054] = Number(row);
            var v = RefValue(dyn);
            Write("valeur construite : " + DescribeValue(v, 0));
            Replaying = true;
            try { Interop.SendEvent(1681470056, r, v); }
            finally { Replaying = false; }
            Write($"intensité équipe : ligne {row} -> option {option}");
            return "OK";
        }

        internal static string Esc(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char ch in s)
            {
                if (ch == '\\') sb.Append("\\\\");
                else if (ch == '"') sb.Append("\\\"");
                else if (ch < ' ') sb.Append("\\u").Append(((int)ch).ToString("x4"));
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        internal static string Describe(uint ev, InteropReference r, TypedValue v)
        {
            var sb = new StringBuilder();
            sb.Append("{\"ev\":").Append(ev);
            try { sb.Append(",\"ref\":\"").Append(Esc(r?.GetIl2CppType()?.FullName)).Append('"'); } catch { }
            try
            {
                sb.Append(",\"pairs\":[");
                var ids = r?.m_id;
                if (ids != null) for (int i = 0; i < ids.Count; i++) { var p = ids[i]; sb.Append(i > 0 ? "," : "").Append('[').Append(p.ID).Append(',').Append(p.Value).Append(']'); }
                sb.Append(']');
            }
            catch { sb.Append(']'); }
            try { sb.Append(",\"pid\":\"").Append(Esc(r?.GetPersistentIdentity())).Append('"'); } catch { }
            try { sb.Append(",\"vtype\":\"").Append(Esc(v == null || v.IsNull ? "null" : v.DataType?.FullName)).Append('"'); } catch { }
            try { sb.Append(",\"val\":\"").Append(Esc(v == null || v.IsNull ? "" : v.AsString())).Append('"'); } catch { }
            try { if (v != null && !v.IsNull) sb.Append(",\"rec\":").Append(DescribeValue(v, 0)); } catch (Exception e) { sb.Append(",\"recerr\":\"").Append(Esc(e.Message)).Append('"'); }
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>Détaille une valeur : dictionnaire (Record / DynamicReference), référence (paires) ou valeur simple.</summary>
        internal static string DescribeValue(TypedValue v, int depth)
        {
            if (v == null || v.IsNull) return "null";
            object o = v.Get();
            if (o is Il2CppSystem.Object io)
            {
                var dict = io.TryCast<Il2CppSystem.Collections.Generic.Dictionary<uint, TypedValue>>();
                if (dict != null && depth < 6)
                {
                    var sb = new StringBuilder("{\"type\":\"" + Esc(io.GetIl2CppType().FullName) + "\",\"entries\":[");
                    bool first = true;
                    var e = dict.GetEnumerator();
                    while (e.MoveNext())
                    {
                        var kv = e.Current;
                        sb.Append(first ? "" : ",").Append("[").Append(kv.Key).Append(",\"").Append(Esc(kv.Value == null || kv.Value.IsNull ? "null" : kv.Value.DataType?.FullName)).Append("\",").Append(DescribeValue(kv.Value, depth + 1)).Append("]");
                        first = false;
                    }
                    return sb.Append("]}").ToString();
                }
                var list = io.TryCast<Il2CppSystem.Collections.Generic.List<TypedValue>>();
                if (list != null && depth < 6)
                {
                    var sb = new StringBuilder("[");
                    for (int i = 0; i < list.Count && i < 400; i++)
                        sb.Append(i > 0 ? "," : "").Append("[\"").Append(Esc(list[i] == null || list[i].IsNull ? "null" : list[i].DataType?.FullName)).Append("\",").Append(DescribeValue(list[i], depth + 1)).Append(']');
                    return sb.Append(']').ToString();
                }
                var r = io.TryCast<InteropReference>();
                if (r != null)
                {
                    var sb = new StringBuilder("{\"type\":\"" + Esc(io.GetIl2CppType().FullName) + "\",\"pairs\":[");
                    var ids = r.m_id;
                    if (ids != null) for (int i = 0; i < ids.Count; i++) sb.Append(i > 0 ? "," : "").Append('[').Append(ids[i].ID).Append(',').Append(ids[i].Value).Append(']');
                    return sb.Append("]}").ToString();
                }
            }
            try
            {
                if (v.DataType?.FullName == "FM.GamePlugin.GameDate")
                {
                    var gd = v.Get<FM.GamePlugin.GameDate>();
                    var dt = FM.GamePlugin.GameDate.ToDateTime(gd);
                    return "\"" + dt.Year.ToString("0000") + "-" + dt.Month.ToString("00") + "-" + dt.Day.ToString("00") + "\"";
                }
            }
            catch { }
            return "\"" + Esc(v.AsString()) + "\"";
        }

        // ---------------------------------------------------------------- construction d'une valeur depuis le format du journal (JSON)
        //  objet  {"type":"SI.Bindable.DynamicReference"|"SI.Core.Record","entries":[[clé,"type",valeur],...]}
        //  simple ["System.String","texte"] / ["SI.Core.DynamicNumber","3"] / ["System.Boolean","True"] / ["FM.GamePlugin.GameDate","2026-08-10"]
        internal static TypedValue FromJson(System.Text.Json.JsonElement e)
        {
            if (e.ValueKind == System.Text.Json.JsonValueKind.Object) return ObjectValue(e);
            if (e.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                if (e.GetArrayLength() == 0 || e[0].ValueKind == System.Text.Json.JsonValueKind.Array) return ListValue(e);
                return Leaf(e[0].GetString(), e[1]);
            }
            return TypedValue.Null;
        }

        /// <summary>Objet JSON : référence ({"type","pairs"}) ou enregistrement ({"type","entries"}).</summary>
        static TypedValue ObjectValue(System.Text.Json.JsonElement e)
        {
            if (e.TryGetProperty("pairs", out var ps))
            {
                string pairs = string.Join(";", ps.EnumerateArray().Select(p => p[0].GetUInt32() + "=" + p[1].GetInt32()));
                return TypedRef(BuildRef(e.GetProperty("type").GetString(), pairs));
            }
            return RefValue(RecordFromJson(e));
        }

        /// <summary>Liste [[type, valeur], …] → List&lt;TypedValue&gt; (ex. joueurs d'un groupe d'encadrement).</summary>
        static TypedValue ListValue(System.Text.Json.JsonElement e)
        {
            var list = new Il2CppSystem.Collections.Generic.List<TypedValue>();
            foreach (var it in e.EnumerateArray())
            {
                var v = it[1];
                list.Add(v.ValueKind == System.Text.Json.JsonValueKind.Object ? ObjectValue(v) : Leaf(it[0].GetString(), v));
            }
            return TypedRef(list);
        }

        static Il2CppSystem.Object RecordFromJson(System.Text.Json.JsonElement e)
        {
            string type = e.GetProperty("type").GetString();
            Il2CppSystem.Object obj = type == "SI.Core.Record" ? new SI.Core.Record() : new SI.Bindable.DynamicReference();
            var dict = obj.Cast<Il2CppSystem.Collections.Generic.Dictionary<uint, TypedValue>>();
            foreach (var en in e.GetProperty("entries").EnumerateArray())
            {
                uint k = en[0].GetUInt32();
                string t = en[1].GetString();
                dict[k] = en[2].ValueKind == System.Text.Json.JsonValueKind.Object ? ObjectValue(en[2])
                        : en[2].ValueKind == System.Text.Json.JsonValueKind.Array ? ListValue(en[2]) : Leaf(t, en[2]);
            }
            return obj;
        }

        static TypedValue Leaf(string t, System.Text.Json.JsonElement v)
        {
            string s = v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : v.GetRawText();
            switch (t)
            {
                case "null": return TypedValue.Null;
                case "SI.Bindable.NullReference": return RefValue(SI.Bindable.NullReference.Instance);
                case "SI.Core.DynamicNumber": return Number(int.Parse(s));
                case "System.String": return TypedValue.Create(s);
                case "System.Boolean": return TypedValue.Create(s.Equals("true", StringComparison.OrdinalIgnoreCase));
                case "System.Int32": return TypedValue.Create(int.Parse(s));
                case "System.UInt32": return TypedValue.Create(uint.Parse(s));
                case "System.Int64": return TypedValue.Create(long.Parse(s));
                case "System.Single": return TypedValue.Create(float.Parse(s, System.Globalization.CultureInfo.InvariantCulture));
                case "System.Double": return TypedValue.Create(double.Parse(s, System.Globalization.CultureInfo.InvariantCulture));
                case "FM.GamePlugin.GameDate":
                    var d = System.DateTime.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
                    var gd = new FM.GamePlugin.GameDate(new Il2CppSystem.DateTime(d.Year, d.Month, d.Day));
                    return TypedValue.CreateValueType<FM.GamePlugin.GameDate>(gd);
                default: throw new Exception("type de valeur non géré : " + t);
            }
        }

        /// <summary>Envoie un ordre au moteur : SENDJ &lt;ev&gt; &lt;typeRéf&gt; &lt;paires|-&gt; &lt;json&gt;.</summary>
        internal static string SendJson(uint ev, string refType, string pairs, string json)
        {
            if (Interop == null) return "ERR FM pas encore prêt";
            var r = BuildRef(refType, pairs == "-" ? null : pairs);
            TypedValue v;
            using (var doc = System.Text.Json.JsonDocument.Parse(json)) v = FromJson(doc.RootElement);
            Replaying = true;
            try { Interop.SendEvent(ev, r, v); }
            finally { Replaying = false; }
            Write($"envoyé {ev} {refType} [{PairsOf(r)}] {json}");
            return "OK";
        }

        // ---------------------------------------------------------------- lecture : canaux de données du jeu
        internal static readonly Dictionary<ulong, string> Pending = new();          // clé -> valeur reçue (null = en attente)
        static ulong NextKey = 0xFB27000000000001UL;
        internal static readonly HashSet<string> SampledKinds = new();
        static int SampleLines;
        static string DataPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FM27Scout", "bridge_data.txt");
        internal static bool SampleAll = false;   // SAMPLE 1 pour cartographier les données affichées par FM
        internal static int ReadMode = 1;
        internal static bool Diag = false;
        internal static int SeqLines;
        static string SeqPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FM27Scout", "bridge_seq.txt");
        internal static void Seq(string s)
        {
            if (SeqLines > 400) return;
            SeqLines++;
            try { File.AppendAllText(SeqPath, System.DateTime.Now.ToString("HH:mm:ss.fff ") + s + Environment.NewLine); } catch { }
        }

        internal static void OnData(ulong key, TypedValue v)
        {
            bool mine;
            lock (Pending) mine = Pending.ContainsKey(key);
            if (Diag && (key >> 48) == 0xFB27) Write($"réponse clé={key:X} connue={mine}");
            string kind = null;
            if (!mine) { lock (Channels) Channels.TryGetValue(key, out kind); if (kind == null || !SampleAll) return; }
            string d;
            try { d = DescribeValue(v, 0); } catch (Exception e) { d = "\"ERR " + Esc(e.Message) + "\""; }
            if (mine) { lock (Pending) Pending[key] = d; return; }
            var parts = kind.Split('|');
            string k2 = parts[0] + "|" + parts[2];
            lock (SampledKinds)
            {
                if (SampleLines > 6000 || !SampledKinds.Add(k2)) return;
                SampleLines++;
            }
            try { File.AppendAllText(DataPath, kind + "\t" + (d.Length > 4000 ? d[..4000] + "…" : d) + Environment.NewLine); } catch { }
        }

        /// <summary>Lit des propriétés : chaque requête = typeRéf|paires|propriété (FourCC ou nombre). Réponses séparées par \u001e.</summary>
        internal static string Read(string[] reqs, int timeoutMs = 4000)
        {
            if (Interop == null) return "ERR FM pas encore prêt";
            var keys = new ulong[reqs.Length];
            var err = new string[reqs.Length];
            RunOnMain(() =>
            {
                for (int i = 0; i < reqs.Length; i++)
                {
                    try
                    {
                        var q = reqs[i].Split('|');
                        var r = BuildRef(q[0], q[1] == "-" ? null : q[1]);
                        uint prop = Prop(q[2]);
                        ulong key = NextKey++;
                        lock (Pending) Pending[key] = null;
                        keys[i] = key;
                        var pid = new SI.Bindable.Reference.Core.PropertyID(prop);
                        var k = new SI.Bindable.Bindings.Key(key);
                        if (ReadMode >= 1) Interop.AddNode(k, SI.Bindable.Bindings.RootKey, pid);
                        uint gen = Interop.OpenChannel(r, pid, k);
                        if (Diag) Write($"lecture {reqs[i]} clé={key:X} gen={gen} lots={Interop.m_batchedRequests?.Count}");
                    }
                    catch (Exception e) { err[i] = "ERR " + e.Message; }
                }
                return "OK";
            });
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                bool all = true;
                lock (Pending) for (int i = 0; i < keys.Length; i++) if (keys[i] != 0 && Pending[keys[i]] == null) { all = false; break; }
                if (all) break;
                Thread.Sleep(30);
            }
            var res = new string[reqs.Length];
            lock (Pending)
                for (int i = 0; i < keys.Length; i++)
                {
                    if (keys[i] == 0) { res[i] = err[i]; continue; }
                    res[i] = Pending[keys[i]] ?? "ERR pas de réponse";
                    Pending.Remove(keys[i]);
                }
            RunOnMain(() => { foreach (var k in keys) if (k != 0) try { Interop.CloseChannel(new SI.Bindable.Bindings.Key(k)); if (ReadMode >= 1) Interop.RemoveNode(new SI.Bindable.Bindings.Key(k)); } catch { } return "OK"; });
            return string.Join("\u001e", res);
        }

        internal static string WriteChannel(string type, string pairs, string prop, string json)
        {
            if (Interop == null) return "ERR FM pas encore prêt";
            var r = BuildRef(type, pairs == "-" ? null : pairs);
            var pid = new SI.Bindable.Reference.Core.PropertyID(Prop(prop));
            ulong key = NextKey++;
            var k = new SI.Bindable.Bindings.Key(key);
            Interop.AddNode(k, SI.Bindable.Bindings.RootKey, pid);
            Interop.OpenChannel(r, pid, k);
            TypedValue v;
            using (var doc = System.Text.Json.JsonDocument.Parse(json)) v = FromJson(doc.RootElement);
            Replaying = true;
            try { Interop.UpdateChannelData(k, v); }
            finally { Replaying = false; }
            // on referme le canal un peu plus tard, quand le moteur a traité l'écriture
            new Thread(() => { Thread.Sleep(3000); MainThread.Enqueue(() => { try { Interop.CloseChannel(k); Interop.RemoveNode(k); } catch { } }); }) { IsBackground = true }.Start();
            Write($"écrit {type} [{PairsOf(r)}] {prop} = {json}");
            return "OK";
        }

        static uint Prop(string s)
        {
            if (uint.TryParse(s, out var n)) return n;
            if (s.Length == 4) return (uint)(s[0] << 24 | s[1] << 16 | s[2] << 8 | s[3]);
            throw new Exception("propriété invalide : " + s);
        }

        // ---------------------------------------------------------------- serveur local (une ligne = une commande)
        static readonly object HandleLock = new();

        static void Server()
        {
            TcpListener l;
            try { l = new TcpListener(IPAddress.Loopback, 27027); l.Start(); }
            catch (Exception e) { Write("Port 27027 indisponible : " + e.Message); return; }
            while (true)
            {
                TcpClient c;
                try { c = l.AcceptTcpClient(); }
                catch (Exception e) { Write("Serveur : " + e.Message); Thread.Sleep(500); continue; }
                new Thread(() => Client(c)) { IsBackground = true, Name = "FM27BridgeClient" }.Start();
            }
        }

        /// <summary>Une connexion = un fil ; elle peut rester ouverte et inactive aussi longtemps que l'app le souhaite.</summary>
        static void Client(TcpClient c)
        {
            try
            {
                using (c)
                using (var s = c.GetStream())
                using (var rd = new StreamReader(s, Encoding.UTF8))
                using (var wr = new StreamWriter(s, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" })
                {
                    string line;
                    while ((line = rd.ReadLine()) != null)
                    {
                        string reply;
                        try { lock (HandleLock) reply = Handle(line.Trim()); } catch (Exception e) { reply = "ERR " + e.Message; }
                        wr.WriteLine(reply.Replace("\r", " ").Replace("\n", " "));
                    }
                }
            }
            catch { }   // connexion coupée par l'app : rien à faire
        }

        static string RunOnMain(Func<string> f, int timeoutMs = 10000)
        {
            string res = "ERR délai dépassé";
            using var done = new ManualResetEventSlim();
            MainThread.Enqueue(() => { try { res = f(); } catch (Exception e) { res = "ERR " + e.Message; } finally { done.Set(); } });
            done.Wait(timeoutMs);
            return res;
        }

        static string Handle(string cmd)
        {
            var parts = cmd.Split(' ', 2);
            switch (parts[0].ToUpperInvariant())
            {
                case "PING": return Interop != null ? "PONG prêt" : "PONG (attente d'une action dans FM)";
                case "EVENTS":   // ordres enregistrés depuis le dernier appel
                    lock (Events) { var s = string.Join("\u001e", Events); Events.Clear(); return "OK " + s; }
                case "TEAMINT":   // TEAMINT <ligne 0-4> <option 0-3>
                    var ti = parts[1].Split(' ');
                    return RunOnMain(() => TeamIntensity(int.Parse(ti[0]), int.Parse(ti[1])));
                case "SENDJ":   // SENDJ <ev> <typeRéf> <paires|-> <json>
                    var sj = parts[1].Split(' ', 4);
                    return RunOnMain(() => SendJson(uint.Parse(sj[0]), sj[1], sj[2], sj[3]));
                case "READ":    // READ typeRéf|paires|prop ;; typeRéf|paires|prop ...
                    return "OK " + Read(parts[1].Split(";;", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                case "READMODE": ReadMode = int.Parse(parts[1]); return "OK";
                case "SEQ": SeqLines = 0; return "OK";
                case "WRITE":   // WRITE typeRéf|paires|prop <json> : écrit une valeur dans un canal (comme une case cochée dans l'interface)
                    var wp = parts[1].Split(' ', 2);
                    var wq = wp[0].Split('|');
                    return RunOnMain(() => WriteChannel(wq[0], wq[1], wq[2], wp[1]));
                case "HASH":    // HASH <type> : empreinte reconstruite vs empreinte du dernier objet vu (diagnostic)
                    return RunOnMain(() =>
                    {
                        if (!RefByType.TryGetValue(parts[1], out var m)) return "ERR jamais vu";
                        var b = BuildRef(parts[1], PairsOf(m), false);
                        return $"OK modèle={m.m_idHash} reconstruit={b.m_idHash} paires={PairsOf(m)} / {PairsOf(b)}";
                    });
                case "BUILD":   // BUILD <json> : type de la valeur construite (diagnostic)
                    return RunOnMain(() =>
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(parts[1]);
                        var v = FromJson(doc.RootElement);
                        var sb = new StringBuilder("OK " + v.DataType?.FullName);
                        var l = (v.Get() as Il2CppSystem.Object)?.TryCast<Il2CppSystem.Collections.Generic.List<TypedValue>>();
                        if (l != null) for (int i = 0; i < l.Count; i++) sb.Append(" | ").Append(l[i].DataType?.FullName);
                        return sb.ToString();
                    });
                case "SAMPLE": SampleAll = parts.Length > 1 && parts[1] == "1"; return "OK";
                case "REFS": return "OK session=" + SessionId + " " + string.Join(" | ", PairsByType.Select(kv => kv.Key + "=" + kv.Value));
                case "RECORD": Recording = parts.Length > 1 && parts[1] == "1"; return "OK";
                case "SEND":
                    // SEND <ev> <modèle: ev dont on clone la dernière référence> <paires k=v;k=v ou -> <type> <valeur>
                    var a = parts[1].Split(' ');
                    uint ev = uint.Parse(a[0]), tpl = uint.Parse(a[1]);
                    string pairs = a[2], vtype = a[3], val = a.Length > 4 ? a[4] : "";
                    return RunOnMain(() => Send(ev, tpl, pairs, vtype, val));
                default: return "ERR commande inconnue";
            }
        }

        static TypedValue MakeValue(string vtype, string val) => vtype switch
        {
            "System.Int32" => TypedValue.Create(int.Parse(val)),
            "System.UInt32" => TypedValue.Create(uint.Parse(val)),
            "System.Int16" => TypedValue.Create(short.Parse(val)),
            "System.UInt16" => TypedValue.Create(ushort.Parse(val)),
            "System.Byte" => TypedValue.Create(byte.Parse(val)),
            "System.SByte" => TypedValue.Create(sbyte.Parse(val)),
            "System.Int64" => TypedValue.Create(long.Parse(val)),
            "System.UInt64" => TypedValue.Create(ulong.Parse(val)),
            "System.Boolean" => TypedValue.Create(val == "True" || val == "true" || val == "1"),
            "System.Single" => TypedValue.Create(float.Parse(val, System.Globalization.CultureInfo.InvariantCulture)),
            "System.String" => TypedValue.Create(val),
            "null" => TypedValue.Null,
            _ => throw new Exception("type de valeur non géré : " + vtype),
        };

        static string Send(uint ev, uint tpl, string pairs, string vtype, string val)
        {
            if (Interop == null) return "ERR FM n'a encore envoyé aucun ordre (change un réglage une fois)";
            InteropReference r = null;
            if (pairs != "noref")
            {
                if (!LastRef.TryGetValue(tpl, out var model) || model == null) return "ERR modèle inconnu pour l'événement " + tpl;
                r = model;
                if (pairs != "-")
                {
                    // clone de la référence modèle avec d'autres identifiants (ex. un autre joueur)
                    var obj = model.MemberwiseClone();
                    r = obj.Cast<InteropReference>();
                    var list = new Il2CppSystem.Collections.Generic.List<InteropReference.Pair>();
                    foreach (var kv in pairs.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var p = kv.Split('=');
                        list.Add(new InteropReference.Pair(uint.Parse(p[0]), int.Parse(p[1])));
                    }
                    r.m_id = list;
                    int h = 17; foreach (var p in list) h = h * 31 + (int)p.ID * 7 + p.Value;
                    r.m_idHash = h;
                }
            }
            var v = MakeValue(vtype, val);
            Replaying = true;
            try { Interop.SendEvent(ev, r, v); }
            finally { Replaying = false; }
            return "OK";
        }
    }

    public class BridgeBehaviour : MonoBehaviour
    {
        public BridgeBehaviour(IntPtr p) : base(p) { }
        void Update()
        {
            int n = 0;
            while (n++ < 200 && Plugin.MainThread.TryDequeue(out var a)) a();
        }
    }

    static class DataPatches
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(FM.UI.InteropDataHandler), nameof(FM.UI.InteropDataHandler.OnChannelDataChanged))]
        static void OnChannelDataChanged(Il2CppSystem.ReadOnlySpan<ulong> __0, Il2CppSystem.ReadOnlySpan<uint> __1, Il2CppSystem.Collections.Generic.List<TypedValue> __2, Il2CppSystem.ReadOnlySpan<long> __3)
        {
            try
            {
                if (__2 == null) return;
                var keys = __0.ToArray();
                int n = Math.Min(keys.Length, __2.Count);
                if (n > 0) Plugin.Seq($"Data n={n}/{keys.Length}/{__2.Count} k0={keys[0]:X} gen0={(__1.Length > 0 ? __1.ToArray()[0] : 0)}");
                for (int i = 0; i < n; i++) Plugin.OnData(keys[i], __2[i]);
            }
            catch (Exception e) { if (!s_warned) { s_warned = true; Plugin.Write("lecture des données : " + e.Message); } }
        }
        static bool s_warned;
    }

    static class Patches
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(GameInteropSubsystem), nameof(GameInteropSubsystem.OpenChannel))]
        static void OpenChannel(GameInteropSubsystem __instance, InteropReference __0, SI.Bindable.Reference.Core.PropertyID __1, SI.Bindable.Bindings.Key __2)
        {
            Plugin.Interop ??= __instance;
            Plugin.Learn(__0);
            Plugin.NoteChannel(__0, __1.id, __2.m_key);
            try { Plugin.Seq($"OpenChannel {__2.m_key:X} {__0?.GetIl2CppType().Name} prop={__1.id}"); } catch { }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GameInteropSubsystem), nameof(GameInteropSubsystem.AddNode))]
        static void AddNode(SI.Bindable.Bindings.Key __0, SI.Bindable.Bindings.Key __1, SI.Bindable.Reference.Core.PropertyID __2)
        { try { Plugin.Seq($"AddNode {__0.m_key:X} parent={__1.m_key:X} prop={__2.id}"); } catch { } }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GameInteropSubsystem), nameof(GameInteropSubsystem.UpdateChannelData))]
        static void UpdateChannelData(SI.Bindable.Bindings.Key __0, TypedValue __1)
        {
            try
            {
                if (Plugin.Replaying) return;
                string kind; lock (Plugin.Channels) Plugin.Channels.TryGetValue(__0.m_key, out kind);
                Plugin.Write($"maj canal {__0.m_key:X} {kind} = {Plugin.DescribeValue(__1, 0)}");
            }
            catch { }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GameInteropSubsystem), nameof(GameInteropSubsystem.CloseChannel))]
        static void CloseChannel(SI.Bindable.Bindings.Key __0)
        { try { Plugin.Seq($"CloseChannel {__0.m_key:X}"); } catch { } }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GameInteropSubsystem), nameof(GameInteropSubsystem.RemoveNode))]
        static void RemoveNode(SI.Bindable.Bindings.Key __0)
        { try { Plugin.Seq($"RemoveNode {__0.m_key:X}"); } catch { } }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GameInteropSubsystem), nameof(GameInteropSubsystem.SendEvent))]
        static void SendEvent(GameInteropSubsystem __instance, uint __0, InteropReference __1, TypedValue __2)
        {
            Plugin.Interop ??= __instance;
            if (Plugin.Replaying) return;
            try
            {
                Plugin.Learn(__1);
                Plugin.LearnNumbers(__2);
                if (__1 != null) Plugin.LastRef[__0] = __1;
                if (!Plugin.Recording) return;
                var d = Plugin.Describe(__0, __1, __2);
                lock (Plugin.Events) { Plugin.Events.Add(d); if (Plugin.Events.Count > 500) Plugin.Events.RemoveAt(0); }
                Plugin.Write("ordre " + d);
            }
            catch (Exception e) { Plugin.Write("erreur enregistrement : " + e.Message); }
        }
    }
}
