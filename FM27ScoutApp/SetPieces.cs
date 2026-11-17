using System;
using System.Collections.Generic;
using System.Linq;

namespace FM27Scout
{
    /// <summary>Coups de pied arrêtés : tireurs, cibles, marqueurs et stratégie conseillée, calculés sur les attributs.</summary>
    internal static class SetPieces
    {
        public sealed record Pick(PlayerRow Player, double Score, string Why);
        public sealed record Section(string Title, string Strategy, string Why, List<(string label, List<Pick> picks)> Lists);

        static double A(PlayerRow r, string k) => Math.Max(0, Db.Stat(r, "Attribute" + k));

        static List<Pick> Top(IEnumerable<PlayerRow> pool, Func<PlayerRow, double> score, Func<PlayerRow, string> why, int n) =>
            pool.Select(r => new Pick(r, score(r), why(r))).OrderByDescending(p => p.Score).Take(n).ToList();

        /// <summary>xi = titulaires (prioritaires pour les rôles sur le terrain), squad = tout l'effectif.</summary>
        public static List<Section> Build(List<PlayerRow> xi, List<PlayerRow> squad)
        {
            var field = xi.Where(r => r.Positions[0] < 15).ToList();       // sans le gardien
            if (field.Count == 0) field = xi;
            var res = new List<Section>();

            // ---- corners offensifs (options de FM27 : technique rentrant/sortant, « Cherche à trouver »)
            Func<PlayerRow, double> aerial = r => A(r, "Heading") * 1.2 + A(r, "JumpingReach") * 1.2 + A(r, "Strength") * 0.6 + A(r, "Bravery") * 0.5 + A(r, "Movement") * 0.5;
            Func<PlayerRow, double> delivery = r => A(r, "Corners") * 2 + A(r, "Technique") + A(r, "Crossing") * 0.5;
            var takers = Top(field, delivery, r => $"corners {A(r, "Corners")}, technique {A(r, "Technique")}", 3);
            var targets = Top(field.Where(r => !takers.Take(1).Any(t => t.Player == r)), aerial, r => $"jeu de tête {A(r, "Heading")}, détente {A(r, "JumpingReach")}, force {A(r, "Strength")}", 4);
            double targetAvg = targets.Count == 0 ? 0 : targets.Average(t => (A(t.Player, "Heading") + A(t.Player, "JumpingReach")) / 2);
            bool aerialTeam = targetAvg >= 14;
            // corner rentrant : côté gauche = pied droit, côté droit = pied gauche (et l'inverse pour le sortant)
            PlayerRow BestFoot(bool leftFoot) => field.Where(r => TacticFm.Feet.TryGetValue(r.Uid, out var f) && (leftFoot ? f.left : f.right) >= 14)
                .OrderByDescending(delivery).FirstOrDefault();
            var rightFooted = BestFoot(false); var leftFooted = BestFoot(true);
            var sideLists = new List<(string, List<Pick>)> { ("Tireurs", takers) };
            if (rightFooted != null || leftFooted != null)
                sideLists.Add(("Rentrant : côté gauche / droit", new[] { rightFooted, leftFooted }.Where(x => x != null).Distinct()
                    .Select(r => new Pick(r, delivery(r), r == rightFooted && r == leftFooted ? "deux pieds" : r == rightFooted ? "pied droit, corners à gauche" : "pied gauche, corners à droite")).ToList()));
            sideLists.Add(("Cibles (joueurs aériens)", targets));
            res.Add(new Section("Corners offensifs",
                aerialTeam ? "Technique : Corner rentrant · Cherche à trouver : 2ème poteau" : "Technique : Corner sortant · Cherche à trouver : Courte distance",
                aerialTeam ? $"Tes cibles sont fortes de la tête (moyenne {targetAvg:F1}/20) : sers-les directement dans les 5m50."
                           : $"Tes cibles manquent de jeu aérien (moyenne {targetAvg:F1}/20) : un centre direct profite surtout à la défense adverse.",
                sideLists));

            // ---- corners défensifs
            Func<PlayerRow, double> defAir = r => A(r, "Marking") + A(r, "Heading") + A(r, "JumpingReach") + A(r, "Positioning") * 0.7 + A(r, "Strength") * 0.5;
            var markers = Top(field, defAir, r => $"marquage {A(r, "Marking")}, tête {A(r, "Heading")}, détente {A(r, "JumpingReach")}", 4);
            double markAvg = field.Average(r => A(r, "Marking"));
            var runners = Top(field.Where(r => !markers.Any(m => m.Player == r)), r => A(r, "Pace") + A(r, "Acceleration") + A(r, "Movement") * 0.5, r => $"vitesse {A(r, "Pace")}, accélération {A(r, "Acceleration")}", 2);
            res.Add(new Section("Corners défensifs",
                markAvg >= 12.5 ? "Défenseurs aériens : « Marquage individuel » pour les meilleurs marqueurs, un « Marquage au premier poteau » · Contre-attaque : « Rester devant »"
                                : "Défenseurs aériens : « Marquage de zone dans les 5m50 » (central, proche, éloigné, 1er poteau) · Contre-attaque : « Rester devant »",
                markAvg >= 12.5 ? $"Ton effectif marque bien (marquage moyen {markAvg:F1}/20)." : $"Marquage moyen de l'effectif {markAvg:F1}/20 : la zone limite les erreurs individuelles.",
                new() { ("Défenseurs aériens", markers), ("Joueurs de contre-attaque", runners) }));

            // ---- coups francs
            var fkDirect = Top(field, r => A(r, "FreeKicks") * 2 + A(r, "Technique") + A(r, "LongShots") * 0.7 + A(r, "Composure") * 0.3, r => $"coups francs {A(r, "FreeKicks")}, frappe de loin {A(r, "LongShots")}", 2);
            var fkCross = Top(field, r => A(r, "FreeKicks") + A(r, "Crossing") * 1.5 + A(r, "Technique"), r => $"centres {A(r, "Crossing")}, coups francs {A(r, "FreeKicks")}", 2);
            bool shooter = fkDirect.Count > 0 && A(fkDirect[0].Player, "FreeKicks") >= 15;
            res.Add(new Section("Coups francs",
                shooter ? "Frappe directe dans les 25-30 m, centre au-delà" : "Centre dans la surface plutôt que frappe directe",
                shooter ? $"{fkDirect[0].Player.Name} a {A(fkDirect[0].Player, "FreeKicks")} en coups francs : il peut tenter sa chance."
                        : "Personne n'a au moins 15 en coups francs : la frappe directe rapporte peu.",
                new() { ("Frappe directe", fkDirect), ("Centres", fkCross), ("Cibles", targets) }));

            // ---- penaltys et touches
            var pens = Top(field, r => A(r, "PenaltyTaking") * 2 + A(r, "Composure") * 1.2 + A(r, "Finishing") * 0.5, r => $"penaltys {A(r, "PenaltyTaking")}, sang-froid {A(r, "Composure")}", 5);
            var throws = Top(field, r => A(r, "LongThrows") * 2 + A(r, "Strength") * 0.5, r => $"touches longues {A(r, "LongThrows")}", 1);
            bool longThrow = throws.Count > 0 && A(throws[0].Player, "LongThrows") >= 15;
            res.Add(new Section("Penaltys et touches",
                longThrow ? $"Touches longues dans la surface près du but adverse avec {throws[0].Player.Name}" : "Touches courtes pour garder le ballon",
                longThrow ? $"{throws[0].Player.Name} a {A(throws[0].Player, "LongThrows")} en touches longues : presque un corner de plus."
                          : "Personne n'a au moins 15 en touches longues.",
                new() { ("Ordre des tireurs de penalty", pens), ("Touches longues", throws) }));
            return res;
        }
    }

    /// <summary>Applique dans FM (via FM27 Bridge) : tireurs de corners et de coups francs directs par pied, technique et zone visée des corners.</summary>
    internal static class SetPiecesFm
    {
        const uint evRemove = 910380099, evAddLeft = 1213227378, evAddRight = 1416066355, evTechnique = 927877996, evTarget = 1249537108;
        const uint pType = 1668375608;
        const int kindCorner = 5, kindFreeKick = 6;

        static string Pairs(Bridge.Context c) => $"825307168=93;1198877028=1;1348620377={c.Manager};1685218662={c.Team}";
        static double A(PlayerRow r, string k) => Math.Max(0, Db.Stat(r, "Attribute" + k));
        static string PersonRef(int person) => $"{{\"type\":\"FM.UI.PersonReference\",\"pairs\":[[825307168,28],[1198877028,1],[1685218662,{person}]]}}";

        static string Entry(int kind, int list, int index, int person) => Bridge.Rec(
            (1398883404, "SI.Core.DynamicNumber", $"\"{kind}\""), (961376122, "SI.Core.DynamicNumber", $"\"{list}\""),
            (1883858041, "SI.Core.DynamicNumber", $"\"{index}\""), (1886157170, "FM.UI.PersonReference", PersonRef(person)));

        /// <summary>Remplace les deux listes de tireurs (pied gauche, pied droit) d'un type de coup de pied arrêté.</summary>
        static void SetTakers(Bridge.Context c, int kind, List<int> left, List<int> right)
        {
            string p = Pairs(c);
            int any = left.Concat(right).First();
            // FM retire par rang : on vide chaque liste en retirant le premier jusqu'à 8 fois (sans effet sur une liste vide)
            for (int list = 0; list < 2; list++)
                for (int i = 0; i < 8; i++) Bridge.Send(evRemove, "SetPieceReference", p, Entry(kind, list, 0, any));
            string List(List<int> ps) => "[" + string.Join(",", ps.Select(x => $"[\"SI.Bindable.DynamicReference\",{Entry(kind, 2, x & 0xFFFFFF, x)}]")) + "]";
            if (left.Count > 0) Bridge.Send(evAddLeft, "SetPieceReference", p, List(left));
            if (right.Count > 0) Bridge.Send(evAddRight, "SetPieceReference", p, List(right));
        }

        /// <summary>Meilleurs tireurs d'un pied (si les pieds ne sont pas connus : les meilleurs tout court).</summary>
        static List<PlayerRow> Best(IEnumerable<PlayerRow> pool, Func<PlayerRow, double> score, bool leftFoot, int n)
        {
            var l = pool.ToList();
            var withFoot = l.Where(r => TacticFm.Feet.TryGetValue(r.Uid, out var f) && (leftFoot ? f.left : f.right) >= 14).ToList();
            return (withFoot.Count > 0 ? withFoot : l).OrderByDescending(score).Take(n).ToList();
        }

        public static List<string> Apply(Bridge.Context c, List<PlayerRow> xi, Dictionary<uint, int> persons)
        {
            var done = new List<string>();
            var field = xi.Where(r => r.Positions[0] < 15 && persons.ContainsKey(r.Uid)).ToList();
            if (field.Count == 0) throw new Exception("aucun joueur de l'onze reconnu dans FM");
            string p = Pairs(c);

            // tireurs de corners
            Func<PlayerRow, double> corner = r => A(r, "Corners") * 2 + A(r, "Technique") + A(r, "Crossing") * 0.5;
            var cl = Best(field, corner, true, 2); var cr = Best(field, corner, false, 2);
            SetTakers(c, kindCorner, cl.Select(r => persons[r.Uid]).ToList(), cr.Select(r => persons[r.Uid]).ToList());
            done.Add($"corners : {string.Join(", ", cl.Select(r => r.Name))} (gauchers) · {string.Join(", ", cr.Select(r => r.Name))} (droitiers)");

            // tireurs de coups francs directs
            Func<PlayerRow, double> fk = r => A(r, "FreeKicks") * 2 + A(r, "Technique") + A(r, "LongShots") * 0.7 + A(r, "Composure") * 0.3;
            var fl = Best(field, fk, true, 2); var fr = Best(field, fk, false, 2);
            SetTakers(c, kindFreeKick, fl.Select(r => persons[r.Uid]).ToList(), fr.Select(r => persons[r.Uid]).ToList());
            done.Add($"coups francs directs : {string.Join(", ", fl.Select(r => r.Name))} (gauchers) · {string.Join(", ", fr.Select(r => r.Name))} (droitiers)");

            // technique et zone visée des corners offensifs, selon le jeu de tête des cibles
            Func<PlayerRow, double> aerial = r => A(r, "Heading") * 1.2 + A(r, "JumpingReach") * 1.2 + A(r, "Strength") * 0.6 + A(r, "Bravery") * 0.5 + A(r, "Movement") * 0.5;
            var targets = field.OrderByDescending(aerial).Take(4).ToList();
            bool air = targets.Average(r => (A(r, "Heading") + A(r, "JumpingReach")) / 2) >= 14;
            Bridge.WriteChannel("SetPieceReference", p, pType, Bridge.Rec((1886680684, "SI.Core.DynamicNumber", "\"0\""), (1903577654, "System.String", Bridge.Q("Corners"))));
            System.Threading.Thread.Sleep(500);
            Bridge.Send(evTechnique, "SetPieceReference", p, Bridge.Num(air ? 0 : 1));
            Bridge.Send(evTarget, "SetPieceReference", p, Bridge.Rec((1903577654, "System.String", Bridge.Q(air ? "2ème poteau" : "Courte distance")), (1886680684, "SI.Core.DynamicNumber", air ? "\"3\"" : "\"1\"")));
            done.Add(air ? "corners : rentrants vers le 2ème poteau" : "corners : sortants en courte distance");
            return done;
        }
    }
}
