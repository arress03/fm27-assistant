using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace FM27Scout
{
    /// <summary>
    /// Tactique côté FM (via FM27 Bridge) : lire la tactique active, y placer le meilleur onze, lancer la création
    /// d'une tactique avec le style de FM qui correspond.
    /// </summary>
    internal static class TacticFm
    {
        const uint pName = 1851878757, pSlots = 1919628150, pSelection = 1098208873, pStyle = 1381059181, pMentality = 1131965302;
        const uint evNew = 1412787285, evNext = 1515149153, evPlace = 826434931;
        const uint kSlot = 1414752632, kPos = 1835167823, kPerson = 1886157170, kSel = 1883858041, kPersonId = 1685218662;

        static string Pairs(Bridge.Context c) => $"825307168=126;1198877028=1;1651271022=1;1685218662={c.Team}";

        /// <summary>Poste de FM (bits) → poste de l'app. Bits 20-22 = côté (gauche, droite, axe).</summary>
        public static string Position(long bits)
        {
            long b = bits & 0xFFFFF;
            foreach (var (bit, pos) in new (long, string)[]
            {
                (1, "GK"), (2, "SW"), (4, "DR"), (8, "DL"), (16, "DC"), (32, "WBR"), (64, "WBL"), (128, "DM"),
                (256, "MR"), (512, "ML"), (1024, "MC"), (2048, "AMR"), (4096, "AML"), (8192, "AMC"), (16384, "ST"),
            })
                if ((b & bit) != 0) return pos;
            return "MC";
        }

        static string BaseRole(string pos) => pos switch
        {
            "GK" => "Gardien", "SW" or "DC" => "Défenseur central", "DL" or "DR" => "Latéral", "WBL" or "WBR" => "Piston",
            "DM" => "Milieu défensif", "MC" => "Milieu relayeur", "ML" or "MR" => "Ailier", "AML" or "AMR" => "Attaquant intérieur",
            "AMC" => "Milieu offensif", _ => "Buteur",
        };

        public sealed class Slot { public int Index; public string Pos; public int Person; public Features.Slot App; }

        public sealed class State
        {
            public string Name = "";
            public List<Slot> Slots = new();
            public Dictionary<int, int> SelIndex = new();     // personne → place dans la feuille de match (0-10 titulaires, puis remplaçants)
        }

        public static State Read(Bridge.Context c, string roleKey)
        {
            string p = Pairs(c);
            var v = Bridge.Read(new[] { ("TeamTacticReference", p, pName.ToString()), ("TeamTacticReference", p, pSlots.ToString()), ("TeamTacticReference", p, pSelection.ToString()) });
            var st = new State { Name = Bridge.Str(v[0]) ?? "" };
            var count = new Dictionary<string, int>();
            foreach (var row in Bridge.Items(v[1]))
            {
                var r = (JsonElement?)row;
                long bits = long.TryParse(Bridge.Str(Bridge.Field(r, kPos)), out var bb) ? bb : 0;
                var s = new Slot { Index = Bridge.Int(Bridge.Field(r, kSlot), -1), Pos = Position(bits), Person = Bridge.PairOf(Bridge.Field(r, kPerson), kPersonId) };
                if (s.Index < 0) continue;
                int i = count.GetValueOrDefault(s.Pos); count[s.Pos] = i + 1;
                s.App = new Features.Slot(s.Pos, Tactic.RoleFor(roleKey, s.Pos, i), 50, 50);
                st.Slots.Add(s);
            }
            foreach (var row in Bridge.Items(v[2]))
            {
                var r = (JsonElement?)row;
                int person = Bridge.PairOf(Bridge.Field(r, kPerson), kPersonId), sel = Bridge.Int(Bridge.Field(r, kSel), -1);
                if (person != 0 && sel >= 0) st.SelIndex[person] = sel;
            }
            return st;
        }

        public sealed class Placement { public Slot Slot; public PlayerRow Player; public int Score; public bool Changed; }

        /// <summary>Meilleur onze pour les postes de la tactique active de FM.</summary>
        public static List<Placement> Plan(State st, List<PlayerRow> squad, Dictionary<uint, int> personOfUid)
        {
            var usable = squad.Where(r => personOfUid.ContainsKey(r.Uid) && !Unavailable.Contains(r.Uid)).ToList();
            var slots = st.Slots.OrderBy(s => s.Index).ToList();
            var (eleven, _) = Features.BestEleven(usable, slots.Select(s => s.App).ToArray());
            var res = new List<Placement>();
            for (int i = 0; i < slots.Count; i++)
            {
                var (_, r, score) = eleven[i];
                res.Add(new Placement { Slot = slots[i], Player = r, Score = score, Changed = r != null && personOfUid[r.Uid] != slots[i].Person });
            }
            return res;
        }

        /// <summary>
        /// Place les joueurs dans FM (comme la liste déroulante « Nom » de l'écran Tactiques). FM échange les deux joueurs :
        /// la feuille de match est relue avant chaque ordre pour envoyer les bonnes places.
        /// </summary>
        public static int Apply(Bridge.Context c, State st, List<Placement> plan, Dictionary<uint, int> personOfUid, List<string> errors, string roleKey)
        {
            string p = Pairs(c);
            int n = 0;
            foreach (var pl in plan.Where(x => x.Player != null))
            {
                int person = personOfUid[pl.Player.Uid];
                var now = n == 0 ? st : Read(c, roleKey);
                var slot = now.Slots.FirstOrDefault(x => x.Index == pl.Slot.Index);
                if (slot == null || slot.Person == person) continue;
                int sel = now.SelIndex.TryGetValue(person, out var s) ? s : -1;
                string json = Bridge.Rec((1886680684, "SI.Core.DynamicNumber", "\"2\""),
                    (1903577654, "SI.Bindable.DynamicReference", Bridge.Rec(
                        (kPerson, "FM.UI.PersonReference", $"{{\"type\":\"FM.UI.PersonReference\",\"pairs\":[[825307168,28],[1198877028,1],[1685218662,{person}]]}}"),
                        (1635149634, "SI.Core.DynamicNumber", $"\"{sel}\""),
                        (kSel, "SI.Core.DynamicNumber", $"\"{person & 0xFFFFFF}\""),
                        (kSlot, "SI.Core.DynamicNumber", $"\"{pl.Slot.Index}\""))));
                try { Bridge.Send(evPlace, "TeamTacticReference", p, json); n++; Thread.Sleep(250); }
                catch (Exception e) { errors.Add(L.F("{0} : {1}", pl.Player.Name, e.Message)); }
            }
            return n;
        }

        /// <summary>Jauges de FM pour la tactique active (libellés tels que FM les affiche).</summary>
        public static (string intensity, string complexity) Gauges(Bridge.Context c)
        {
            string p = Pairs(c);
            var v = Bridge.Read(new[] { ("TeamTacticReference", p, "1697728600"), ("TeamTacticReference", p, "930311012") });
            return (Bridge.Str(v[0]) ?? "", Bridge.Str(v[1]) ?? "");
        }

        /// <summary>Correspondance personne FM ↔ joueur de la base (identifiant unique).</summary>
        public static Dictionary<uint, int> PersonsOf(Bridge.Context c)
        {
            var list = Bridge.Read1("TeamTrainingReference", c.TeamTraining, "1432578628");
            var persons = Bridge.Items(list).Select(x => Bridge.PairOf(x, kPersonId)).Where(x => x != 0).Distinct().ToList();
            var reqs = new List<(string, string, string)>();
            foreach (var x in persons) { reqs.Add(("PersonReference", Bridge.Context.Person(x), "1970170212")); reqs.Add(("PersonReference", Bridge.Context.Person(x), Bridge.CC("cndn").ToString())); }
            var vals = Bridge.Read(reqs);
            var map = new Dictionary<uint, int>();
            lock (Unavailable) Unavailable.Clear();
            for (int i = 0; i < persons.Count; i++)
            {
                int uid = Bridge.Int(vals[i * 2], 0);
                if (uid <= 0) continue;
                map[(uint)uid] = persons[i];
                if (Bridge.Int(vals[i * 2 + 1], -1) < 0) lock (Unavailable) Unavailable.Add((uint)uid);   // condition illisible = blessé
            }
            Gfx.MatchIds(map.Keys);   // sans pack de photos : relie la base aux identifiants FM de ton effectif
            try { ReadFeet(map); } catch { }
            return map;
        }

        // ------------------------------------------------------------------ pieds (fort / faible)

        /// <summary>Pied gauche et pied droit (1-20) par identifiant de joueur, lus dans FM.</summary>
        public static readonly Dictionary<uint, (int left, int right)> Feet = new();

        /// <summary>Joueurs blessés (indisponibles), lus dans FM.</summary>
        public static readonly HashSet<uint> Unavailable = new();

        static void ReadFeet(Dictionary<uint, int> persons)
        {
            var todo = persons.Where(kv => !Feet.ContainsKey(kv.Key)).ToList();
            if (todo.Count == 0) return;
            var vals = Bridge.Read(todo.Select(kv => ("PersonReference", Bridge.Context.Person(kv.Value), "1244885353")).ToList());
            for (int i = 0; i < todo.Count; i++)
            {
                int l = Bridge.Int(Bridge.Field(vals[i], 842276896), 0), r = Bridge.Int(Bridge.Field(vals[i], 842276897), 0);
                if (l > 0 && r > 0) Feet[todo[i].Key] = (l, r);
            }
        }

        /// <summary>
        /// Bonus/malus de pied pour un poste de côté : latéraux, pistons et milieux de côté jouent mieux avec le pied du côté
        /// (centres) ; ailiers offensifs avec le pied opposé (repiquer et frapper). 1 = neutre.
        /// </summary>
        public static double FootFactor(PlayerRow r, string pos)
        {
            if (r == null || !Feet.TryGetValue(r.Uid, out var f)) return 1;
            bool leftSide = pos is "DL" or "WBL" or "ML" or "AML", rightSide = pos is "DR" or "WBR" or "MR" or "AMR";
            if (!leftSide && !rightSide) return Math.Min(f.left, f.right) >= 14 ? 1.02 : 1;   // ambidextre : léger plus partout
            bool inverted = pos is "AML" or "AMR";
            int same = leftSide ? f.left : f.right, other = leftSide ? f.right : f.left;
            int wanted = inverted ? other : same, weak = inverted ? same : other;
            double fWanted = wanted >= 15 ? 1.0 : wanted >= 11 ? 0.96 : 0.9;
            return Math.Min(1.04, fWanted + (weak >= 14 ? 0.03 : 0));
        }

        // ------------------------------------------------------------------ configurations de FM (style + formations)

        const uint evConfig = 89, evConfirm = 879515246;

        /// <summary>Applique une configuration de FM (style, formation avec et sans ballon, rôles et consignes du style) à la tactique active.</summary>
        public static void ApplyConfig(Bridge.Context c, Tactic.Config cfg)
        {
            string json = Bridge.Rec((1886680684, "SI.Core.DynamicNumber", "\"5\""), (1903577654, "System.String", Bridge.Q(cfg.Label)),
                (1970170212, "SI.Core.DynamicNumber", $"\"{cfg.Style.Unid}\""), (942940192, "System.String", Bridge.Q(cfg.Internal)));
            Bridge.Send(evConfig, "TeamTacticReference", Pairs(c), json);
        }

        /// <summary>Crée une nouvelle tactique dans FM (les autres ne sont pas touchées), avec la configuration choisie.</summary>
        public static string CreateFull(Bridge.Context c, Tactic.Config cfg)
        {
            string p = Pairs(c);
            var menu = Bridge.Read1("TeamTacticReference", p, "1949648203")?.GetRawText() ?? "";
            int tactics = System.Text.RegularExpressions.Regex.Matches(menu, "\\[1937011303,").Count;
            if (tactics >= 3) throw new Exception(L.T("FM est limité à 3 tactiques et elles sont toutes prises : supprime-en une dans FM, ou utilise « Appliquer à la tactique active »"));
            Bridge.Send(evNew, "TeamTacticReference", p, "[\"null\",\"\"]");
            Thread.Sleep(600);
            Bridge.WriteChannel("TeamTacticReference", p, pStyle, Bridge.Num(cfg.Style.Index));
            Thread.Sleep(3500);
            Bridge.Send(evNext, "TeamTacticReference", p, "[\"null\",\"\"]");
            Thread.Sleep(400);
            Bridge.Send(evNext, "TeamTacticReference", p, "[\"null\",\"\"]");
            Thread.Sleep(400);
            Bridge.Send(evConfirm, "TeamTacticReference", p, "[\"null\",\"\"]");
            Thread.Sleep(1500);
            var after = Bridge.Read1("TeamTacticReference", p, "1949648203")?.GetRawText() ?? "";
            if (System.Text.RegularExpressions.Regex.Matches(after, "\\[1937011303,").Count <= tactics)
                throw new Exception(L.T("FM n'a pas créé la nouvelle tactique (rien n'a été modifié)"));
            ApplyConfig(c, cfg);
            Thread.Sleep(1500);
            return Bridge.Str(Bridge.Read1("TeamTacticReference", p, pName.ToString())) ?? "";
        }
    }
}
