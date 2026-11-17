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
                aerialTeam ? "Technique : Corner rentrant · Cherche à trouver : Premier poteau ou 2ème poteau" : "Technique : Corner sortant · Cherche à trouver : Courte distance ou Limite de la surface",
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
}
