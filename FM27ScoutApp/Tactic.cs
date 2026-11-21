using System;
using System.Collections.Generic;
using System.Linq;

namespace FM27Scout
{
    /// <summary>
    /// Tactiques de FM27 : les 10 styles et leurs 9 configurations (formation avec ballon / sans ballon), telles que
    /// FM les propose dans « Config. » de l'écran Tactiques. L'app note chaque configuration pour l'effectif.
    /// </summary>
    internal static class Tactic
    {
        public sealed record Instruction(string Label, string Value, string Why);

        public sealed record Style(int Index, string Key, string Name, uint Unid, string Pitch, string RoleKey, string[] Demand, Instruction[] Team);

        public sealed record Config(Style Style, int N, string Label, string Internal, string With, string Without)
        {
            public string Id => Internal;
        }

        static Instruction I(string l, string v, string w = "") => new(l, v, w);

        public static readonly Style[] Styles =
        {
            new(0, "control_possession", "Contrôle de la possession", 1129270868, "Prendre le contrôle du match, patienter et attaquer quand l'adversaire se découvre.", "possession",
                new[] { "Passing", "FirstTouch", "Composure", "Decisions" },
                new[] { I("Avec le ballon", "Passes courtes, construction patiente", "Le ballon circule pour user l'adversaire."), I("Sans le ballon", "Pressing modéré, bloc compact"), I("Clé", "Joueurs propres techniquement et calmes") }),
            new(1, "gegenpress", "Gegenpress", 1195722565, "Pressing extrêmement agressif, récupération haute et attaque immédiate.", "pressing",
                new[] { "Stamina", "WorkRate", "Pace", "Acceleration", "Aggression" },
                new[] { I("Avec le ballon", "Rythme élevé, jeu axial, attaques rapides"), I("Sans le ballon", "Ligne haute, pressing très fréquent, contre-pressing"), I("Clé", "Endurance et activité : fais tourner l'effectif", "Très fatigant pour les joueurs.") }),
            new(2, "tiki-taka", "Tiki-Taka", 1414089545, "Domination par la possession et combinaisons de passes courtes.", "possession",
                new[] { "Passing", "FirstTouch", "Technique", "Vision", "Composure" },
                new[] { I("Avec le ballon", "Passes très courtes, beaucoup de mouvement"), I("Sans le ballon", "Contre-pressing pour récupérer vite"), I("Clé", "Technique et vision partout, gardien compris") }),
            new(3, "vertical_tiki-taka", "Tiki-Taka vertical", 1447383636, "Possession tournée vers l'avant, positionnement offensif étroit.", "possession",
                new[] { "Passing", "Vision", "Movement", "Acceleration" },
                new[] { I("Avec le ballon", "Passes courtes mais verticales, jeu axial"), I("Sans le ballon", "Pressing haut"), I("Clé", "Milieux créatifs et attaquants mobiles") }),
            new(4, "wing_play", "Jeu sur les ailes", 1464421959, "Créer le surnombre sur les côtés et centrer dans la surface.", "wing",
                new[] { "Crossing", "Pace", "Dribbling", "Heading" },
                new[] { I("Avec le ballon", "Jeu large, centres fréquents"), I("Sans le ballon", "Bloc médian"), I("Clé", "Centreurs rapides et un attaquant fort de la tête") }),
            new(5, "route_one", "Longs ballons devant", 1380930900, "Envoyer rapidement le ballon devant et éviter le pressing adverse.", "direct",
                new[] { "Heading", "JumpingReach", "Strength", "Pace" },
                new[] { I("Avec le ballon", "Passes longues et directes vers les attaquants"), I("Sans le ballon", "Bloc plus bas, duels"), I("Clé", "Attaquants puissants dans les airs") }),
            new(6, "fluid_counter-attack", "Contre-attaques fluides", 1179407689, "Contenir la pression puis contre-attaquer de façon fluide et créative.", "contre",
                new[] { "Pace", "Acceleration", "Passing", "Technique" },
                new[] { I("Avec le ballon", "Transitions rapides, passes vers l'avant"), I("Sans le ballon", "Bloc médian, attendre l'erreur"), I("Clé", "Vitesse devant et relayeurs techniques") }),
            new(7, "direct_counter-attack", "Contre-attaques directes", 1145655877, "Contenir la pression puis contre-attaquer de façon directe et structurée.", "contre",
                new[] { "Pace", "Acceleration", "Finishing", "Stamina" },
                new[] { I("Avec le ballon", "Ballon vite dans la profondeur"), I("Sans le ballon", "Bloc bas à médian"), I("Clé", "Attaquants rapides et bons finisseurs") }),
            new(8, "catenaccio", "Catenaccio", 1128354885, "Neutraliser l'adversaire, défendre à 3 centraux et frapper en contre.", "bloc",
                new[] { "Positioning", "Marking", "Concentration", "Heading" },
                new[] { I("Avec le ballon", "Prudent, contre-attaques"), I("Sans le ballon", "Bloc bas à 3 centraux"), I("Clé", "Défenseurs bien placés et concentrés") }),
            new(9, "park_the_bus", "Jeu ultra-défensif", 1346458187, "Positionnement défensif très bas et structuré.", "bloc",
                new[] { "Positioning", "Concentration", "Marking", "Teamwork", "Strength" },
                new[] { I("Avec le ballon", "Très prudent, dégagements"), I("Sans le ballon", "Bloc très bas, tout le monde derrière le ballon"), I("Clé", "Discipline collective") }),
        };

        // configurations proposées par FM pour chaque style (libellés et noms internes de FM27)
        static readonly (int style, string label, string intern)[] Raw =
        {
            (0,"4-3-3 MD Jeu large / 4-3-3 MD Jeu large","1--4-3-3 DM Wide _ 4-3-3 DM Wide - control_possession"),(0,"4-3-3 MD Jeu large / 4-4-2 2MD","2--4-3-3 DM Wide _ 4-4-2 2DM - control_possession"),
            (0,"4-3-3 MD Jeu large / 4-1-4-1 MD","3--4-3-3 DM Wide _ 4-1-4-1 DM - control_possession"),(0,"4-2-3-1 MD MO Jeu large / 4-4-2 2MD","4--4-2-3-1 DM AM Wide _ 4-4-2 2DM - control_possession"),
            (0,"4-2-3-1 MD MO Jeu large / 4-4-1-1 2MD","5--4-2-3-1 DM AM Wide _ 4-4-1-1 2DM - control_possession"),(0,"4-2-3-1 MD MO Jeu large / 4-3-3 MD Jeu large","6--4-2-3-1 DM AM Wide _ 4-3-3 DM Wide - control_possession"),
            (0,"5-2-3 MD Jeu large / 5-2-3 MD Jeu large","7--5-2-3 DM Wide _ 5-2-3 DM Wide - control_possession"),(0,"5-2-3 MD Jeu large / 5-2-2-1 MD MO","8--5-2-3 DM Wide _ 5-2-2-1 DM AM - control_possession"),
            (0,"5-2-3 MD Jeu large / 4-3-3 MD Jeu large","9-- 5-2-3 DM Wide _ 4-3-3 DM Wide - control_possession"),
            (1,"4-3-3 MD Jeu large / 4-3-3 MD Jeu large","1--4-3-3 DM Wide _ 4-3-3 DM Wide - gegenpress"),(1,"4-3-3 MD Jeu large / 4-4-2 2MD","2--4-3-3 DM Wide _ 4-4-2 2DM - gegenpress"),
            (1,"4-3-3 MD Jeu large / 4-1-4-1 MD","3--4-3-3 DM Wide _ 4-1-4-1 DM - gegenpress"),(1,"4-2-3-1 MD MO Jeu large / 4-4-2 2MD","4--4-2-3-1 DM AM Wide _ 4-4-2 2DM - gegenpress"),
            (1,"4-2-3-1 MD MO Jeu large / 4-4-1-1 2MD","5--4-2-3-1 DM AM Wide _ 4-4-1-1 2DM - gegenpress"),(1,"4-2-3-1 MD MO Jeu large / 4-3-3 MD Jeu large","6--4-2-3-1 DM AM Wide _ 4-3-3 DM Wide - gegenpress"),
            (1,"4-2-4 MD Jeu large / 4-4-2 2MD","7--4-2-4 DM Wide _ 4-4-2 2DM - gegenpress"),(1,"4-2-4 MD Jeu large / 4-4-1-1 2MD","8--4-2-4 DM Wide _ 4-4-1-1 2DM - gegenpress"),
            (1,"4-2-4 MD Jeu large / 5-3-2 MD AL","9--4-2-4 DM Wide _ 5-3-2 DM WB - gegenpress"),
            (2,"4-3-3 MD Jeu large / 4-3-3 MD Jeu large","1--4-3-3 DM Wide _ 4-3-3 DM Wide - tiki-taka"),(2,"4-3-3 MD Jeu large / 4-4-2 2MD","2--4-3-3 DM Wide _ 4-4-2 2DM - tiki-taka"),
            (2,"4-3-3 MD Jeu large / 4-1-4-1 MD","3--4-3-3 DM Wide _ 4-1-4-1 DM - tiki-taka"),(2,"4-2-3-1 MD MO Jeu large / 4-4-2 2MD","4--4-2-3-1 DM AM Wide _ 4-4-2 2DM - tiki-taka"),
            (2,"4-2-3-1 MD MO Jeu large / 4-4-1-1 2MD","5--4-2-3-1 DM AM Wide _ 4-4-1-1 2DM - tiki-taka"),(2,"4-2-3-1 MD MO Jeu large / 4-3-3 MD Jeu large","6--4-2-3-1 DM AM Wide _ 4-3-3 DM Wide - tiki-taka"),
            (2,"5-2-3 MD Jeu large / 5-2-3 MD Jeu large","7--5-2-3 DM Wide _ 5-2-3 DM Wide - tiki-taka"),(2,"5-2-3 MD Jeu large / 5-2-2-1 MD MO","8--5-2-3 DM Wide _ 5-2-2-1 DM AM - tiki-taka"),
            (2,"5-2-3 MD Jeu large / 4-3-3 MD Jeu large","9-- 5-2-3 DM Wide _ 4-3-3 DM Wide - tiki-taka"),
            (3,"4-3-3 MD Jeu large / 4-3-3 MD Jeu large","1--4-3-3 DM Wide _ 4-3-3 DM Wide - vertical_tiki-taka"),(3,"4-3-3 MD Jeu large / 4-4-2 2MD","2--4-3-3 DM Wide _ 4-4-2 2DM - vertical_tiki-taka"),
            (3,"4-3-3 MD Jeu large / 4-1-4-1 MD","3--4-3-3 DM Wide _ 4-1-4-1 DM - vertical_tiki-taka"),(3,"4-4-2 Diamant Axial / 4-4-2 Diamant Axial","4--4-4-2 Diamond Narrow _ 4-4-2 Diamond Narrow - vertical_tiki-taka"),
            (3,"4-4-2 Diamant Axial / 4-3-3 MD Jeu large","5--4-4-2 Diamond Narrow _ 4-3-3 DM Wide - vertical_tiki-taka"),(3,"4-4-2 Diamant Axial / 4-4-2 2MD","6--4-4-2 Diamond Narrow _ 4-4-2 2DM - vertical_tiki-taka"),
            (3,"4-2-3-1 MD MO Jeu large / 4-4-2 2MD","7--4-2-3-1 DM AM Wide _ 4-4-2 2DM - vertical_tiki-taka"),(3,"4-2-3-1 MD MO Jeu large / 4-4-1-1 2MD","8--4-2-3-1 DM AM Wide _ 4-4-1-1 2DM - vertical_tiki-taka"),
            (3,"4-2-3-1 MD MO Jeu large / 4-3-3 MD Jeu large","9--4-2-3-1 DM AM Wide _ 4-3-3 DM Wide - vertical_tiki-taka"),
            (4,"4-2-4 MD Jeu large / 4-4-2 2MD","1--4-2-4 DM Wide _ 4-4-2 2DM - wing_play"),(4,"4-2-4 MD Jeu large / 4-4-1-1 2MD","2--4-2-4 DM Wide _ 4-4-1-1 2DM - wing_play"),
            (4,"4-2-4 MD Jeu large / 5-3-2 MD AL","3--4-2-4 DM Wide _ 5-3-2 DM WB - wing_play"),(4,"4-3-3 MD Jeu large / 4-3-3 MD Jeu large","4--4-3-3 DM Wide _ 4-3-3 DM Wide - wing_play"),
            (4,"4-3-3 MD Jeu large / 4-4-2 2MD","5--4-3-3 DM Wide _ 4-4-2 2DM - wing_play"),(4,"4-3-3 MD Jeu large / 4-1-4-1 MD","6--4-3-3 DM Wide _ 4-1-4-1 DM - wing_play"),
            (4,"4-2-3-1 MD MO Jeu large / 4-4-2 2MD","7--4-2-3-1 DM AM Wide _ 4-4-2 2DM - wing_play"),(4,"4-2-3-1 MD MO Jeu large / 4-4-1-1 2MD","8--4-2-3-1 DM AM Wide _ 4-4-1-1 2DM - wing_play"),
            (4,"4-2-3-1 MD MO Jeu large / 4-3-3 MD Jeu large","9--4-2-3-1 DM AM Wide _ 4-3-3 DM Wide - wing_play"),
            (5,"4-2-4 MD Jeu large / 4-4-2 2MD","1--4-2-4 DM Wide _ 4-4-2 2DM - route_one"),(5,"4-2-4 MD Jeu large / 4-4-1-1 2MD","2--4-2-4 DM Wide _ 4-4-1-1 2DM - route_one"),
            (5,"4-2-4 MD Jeu large / 5-3-2 MD AL","3--4-2-4 DM Wide _ 5-3-2 DM WB - route_one "),(5,"4-3-3 MD Jeu large / 4-3-3 MD Jeu large","4--4-3-3 DM Wide _ 4-3-3 DM Wide - route_one"),
            (5,"4-3-3 MD Jeu large / 4-4-2 2MD","5--4-3-3 DM Wide _ 4-4-2 2DM - route_one"),(5,"4-3-3 MD Jeu large / 4-1-4-1 MD","6--4-3-3 DM Wide _ 4-1-4-1 DM - route_one"),
            (5,"5-3-2 MD AL / 5-3-2 MD AL","7--5-3-2 DM WB _ 5-3-2 DM WB - route_one"),(5,"5-3-2 MD AL / 5-2-1-2 MD MO","8--5-3-2 DM WB _ 5-2-1-2 DM AM - route_one"),
            (5,"5-3-2 MD AL / 4-4-2 2MD","9--5-3-2 DM WB _ 4-4-2 2DM - route_one"),
            (6,"4-3-3 MD Jeu large / 4-3-3 MD Jeu large","1--4-3-3 DM Wide _ 4-3-3 DM Wide - fluid_counter-attack"),(6,"4-3-3 MD Jeu large / 4-4-2 2MD","2--4-3-3 DM Wide _ 4-4-2 2DM - fluid_counter-attack"),
            (6,"4-3-3 MD Jeu large / 4-1-4-1 MD","3--4-3-3 DM Wide _ 4-1-4-1 DM - fluid_counter-attack"),(6,"4-2-3-1 MD MO Jeu large / 4-4-2 2MD","4--4-2-3-1 DM AM Wide _ 4-4-2 2DM - fluid_counter-attack"),
            (6,"4-2-3-1 MD MO Jeu large / 4-4-1-1 2MD","5--4-2-3-1 DM AM Wide _ 4-4-1-1 2DM - fluid_counter-attack"),(6,"4-2-3-1 MD MO Jeu large / 4-3-3 MD Jeu large","6--4-2-3-1 DM AM Wide _ 4-3-3 DM Wide - fluid_counter-attack"),
            (6,"5-2-2-1 MD MO / 5-2-2-1 MD MO","7--5-2-2-1 DM AM _ 5-2-2-1 DM AM - fluid_counter-attack"),(6,"5-2-2-1 MD MO / 5-2-3 MD Jeu large","8--5-2-2-1 DM AM _ 5-2-3 DM Wide - fluid_counter-attack"),
            (6,"5-2-2-1 MD MO / 4-3-2-1 MD MO Axial","9--5-2-2-1 DM AM _ 4-3-2-1 DM AM Narrow - fluid_counter-attack"),
            (7,"4-4-2 2MD / 4-4-2 2MD","1--4-4-2 2DM _ 4-4-2 2DM - direct_counter-attack"),(7,"4-4-2 2MD / 4-4-1-1 2MD","2--4-4-2 2DM _ 4-4-1-1 2DM - direct_counter-attack"),
            (7,"4-4-2 2MD / 5-3-2 MD AL","3--4-4-2 2DM _ 5-3-2 DM WB - direct_counter-attack"),(7,"4-3-3 MD Jeu large / 4-3-3 MD Jeu large","4--4-3-3 DM Wide _ 4-3-3 DM Wide - direct_counter-attack"),
            (7,"4-3-3 MD Jeu large / 4-4-2 2MD","5--4-3-3 DM Wide _ 4-4-2 2DM - direct_counter-attack"),(7,"4-3-3 MD Jeu large / 4-1-4-1 MD","6--4-3-3 DM Wide _ 4-1-4-1 DM - direct_counter-attack"),
            (7,"5-2-3 MD Jeu large / 5-2-3 MD Jeu large","7--5-2-3 DM Wide _ 5-2-3 DM Wide - direct_counter-attack"),(7,"5-2-3 MD Jeu large / 5-2-2-1 MD MO","8--5-2-3 DM Wide _ 5-2-2-1 DM AM - direct_counter-attack"),
            (7,"5-2-3 MD Jeu large / 4-3-3 MD Jeu large","9--5-2-3 DM Wide _ 4-3-3 DM Wide - direct_counter-attack"),
            (8,"5-2-3 MD Jeu large / 5-2-3 MD Jeu large","1--5-2-3 DM Wide _ 5-2-3 DM Wide - catenaccio"),(8,"5-2-3 MD Jeu large / 5-2-2-1 MD MO","2--5-2-3 DM Wide _ 5-2-2-1 DM AM - catenaccio"),
            (8,"5-2-3 MD Jeu large / 4-3-3 MD Jeu large","3--5-2-3 DM Wide _ 4-3-3 DM Wide - catenaccio"),(8,"5-2-1-2 MD MO / 5-2-1-2 MD MO","4--5-2-1-2 DM AM _ 5-2-1-2 DM AM - catenaccio"),
            (8,"5-2-1-2 MD MO / 5-3-2 MD AL","5--5-2-1-2 DM AM _ 5-3-2 DM WB - catenaccio"),(8,"5-2-1-2 MD MO / 4-4-2 Diamant Axial","6--5-2-1-2 DM AM _ 4-4-2 Diamond Narrow - catenaccio"),
            (8,"5-2-2-1 MD MO / 5-2-2-1 MD MO","7--5-2-2-1 DM AM _ 5-2-2-1 DM AM - catenaccio"),(8,"5-2-2-1 MD MO / 5-2-3 MD Jeu large","8--5-2-2-1 DM AM _ 5-2-3 DM Wide - catenaccio"),
            (8,"5-2-2-1 MD MO / 4-3-2-1 MD MO Axial","9--5-2-2-1 DM AM _ 4-3-2-1 DM AM Narrow - catenaccio"),
            (9,"4-4-2 2MD / 4-4-2 2MD","1--4-4-2 2DM _ 4-4-2 2DM - park_the_bus"),(9,"4-4-2 2MD / 4-4-1-1 2MD","2--4-4-2 2DM _ 4-4-1-1 2DM - park_the_bus"),
            (9,"4-4-2 2MD / 5-3-2 MD AL","3--4-4-2 2DM _ 5-3-2 DM WB - park_the_bus"),(9,"5-2-3 MD Jeu large / 5-2-3 MD Jeu large","4--5-2-3 DM Wide _ 5-2-3 DM Wide - park_the_bus"),
            (9,"5-2-3 MD Jeu large / 5-2-2-1 MD MO","5--5-2-3 DM Wide _ 5-2-2-1 DM AM - park_the_bus"),(9,"5-2-3 MD Jeu large / 4-3-3 MD Jeu large","6--5-2-3 DM Wide _ 4-3-3 DM Wide - park_the_bus"),
            (9,"4-3-3 MD Jeu large / 4-3-3 MD Jeu large","7--4-3-3 DM Wide _ 4-3-3 DM Wide - park_the_bus"),(9,"4-3-3 MD Jeu large / 4-4-2 2MD","8--4-3-3 DM Wide _ 4-4-2 2DM - park_the_bus"),
            (9,"4-3-3 MD Jeu large / 4-1-4-1 MD","9--4-3-3 DM Wide _ 4-1-4-1 DM - park_the_bus"),
        };

        public static readonly Config[] Configs = Raw.Select(x =>
        {
            var parts = x.label.Split(" / ");
            int n = int.TryParse(x.intern.Split('-')[0], out var k) ? k : 1;
            return new Config(Styles[x.style], n, x.label, x.intern, parts[0], parts.Length > 1 ? parts[1] : parts[0]);
        }).ToArray();

        // ------------------------------------------------------------------ formes (X 0..100 gauche→droite, Y 0..100 attaque→but)

        static Features.Slot S(string pos, double x, double y) => new(pos, "", x, y);
        static readonly Features.Slot[] Back4 = { S("GK", 50, 93), S("DL", 12, 72), S("DC", 37, 77), S("DC", 63, 77), S("DR", 88, 72) };
        static readonly Features.Slot[] Back5 = { S("GK", 50, 93), S("WBL", 8, 62), S("DC", 27, 77), S("DC", 50, 79), S("DC", 73, 77), S("WBR", 92, 62) };

        public static readonly Dictionary<string, Features.Slot[]> Shapes = new()
        {
            ["4-3-3 MD Jeu large"] = Back4.Concat(new[] { S("DM", 50, 59), S("MC", 30, 46), S("MC", 70, 46), S("AML", 14, 22), S("AMR", 86, 22), S("ST", 50, 12) }).ToArray(),
            ["4-2-3-1 MD MO Jeu large"] = Back4.Concat(new[] { S("DM", 36, 57), S("DM", 64, 57), S("AML", 14, 30), S("AMC", 50, 33), S("AMR", 86, 30), S("ST", 50, 12) }).ToArray(),
            ["4-2-4 MD Jeu large"] = Back4.Concat(new[] { S("DM", 36, 55), S("DM", 64, 55), S("AML", 12, 22), S("AMR", 88, 22), S("ST", 38, 12), S("ST", 62, 12) }).ToArray(),
            ["4-4-2 Diamant Axial"] = Back4.Concat(new[] { S("DM", 50, 59), S("MC", 30, 46), S("MC", 70, 46), S("AMC", 50, 32), S("ST", 38, 13), S("ST", 62, 13) }).ToArray(),
            ["4-4-2 2MD"] = Back4.Concat(new[] { S("ML", 12, 44), S("DM", 37, 55), S("DM", 63, 55), S("MR", 88, 44), S("ST", 38, 15), S("ST", 62, 15) }).ToArray(),
            ["4-4-1-1 2MD"] = Back4.Concat(new[] { S("ML", 12, 44), S("DM", 37, 55), S("DM", 63, 55), S("MR", 88, 44), S("AMC", 50, 30), S("ST", 50, 12) }).ToArray(),
            ["4-1-4-1 MD"] = Back4.Concat(new[] { S("DM", 50, 60), S("ML", 12, 40), S("MC", 37, 44), S("MC", 63, 44), S("MR", 88, 40), S("ST", 50, 12) }).ToArray(),
            ["4-3-2-1 MD MO Axial"] = Back4.Concat(new[] { S("DM", 50, 59), S("MC", 30, 46), S("MC", 70, 46), S("AMC", 36, 28), S("AMC", 64, 28), S("ST", 50, 12) }).ToArray(),
            ["5-2-3 MD Jeu large"] = Back5.Concat(new[] { S("DM", 37, 52), S("DM", 63, 52), S("AML", 16, 22), S("AMR", 84, 22), S("ST", 50, 12) }).ToArray(),
            ["5-3-2 MD AL"] = Back5.Concat(new[] { S("DM", 50, 56), S("MC", 30, 44), S("MC", 70, 44), S("ST", 38, 14), S("ST", 62, 14) }).ToArray(),
            ["5-2-2-1 MD MO"] = Back5.Concat(new[] { S("DM", 37, 52), S("DM", 63, 52), S("AMC", 34, 30), S("AMC", 66, 30), S("ST", 50, 12) }).ToArray(),
            ["5-2-1-2 MD MO"] = Back5.Concat(new[] { S("DM", 37, 52), S("DM", 63, 52), S("AMC", 50, 32), S("ST", 38, 13), S("ST", 62, 13) }).ToArray(),
        };

        static string BaseRole(string pos) => pos switch
        {
            "GK" => "Gardien", "SW" or "DC" => "Défenseur central", "DL" or "DR" => "Latéral", "WBL" or "WBR" => "Piston",
            "DM" => "Milieu défensif", "MC" => "Milieu relayeur", "ML" or "MR" => "Ailier", "AML" or "AMR" => "Attaquant intérieur",
            "AMC" => "Milieu offensif", _ => "Buteur",
        };

        /// <summary>Rôle utilisé par l'app pour noter un poste selon la famille de style (FM règle ensuite ses propres rôles).</summary>
        public static string RoleFor(string roleKey, string pos, int i) => (roleKey, pos) switch
        {
            ("possession", "GK") => "Gardien relanceur",
            ("possession", "DC") when i == 0 => "Défenseur relanceur",
            ("possession", "DM") => i == 0 ? "Meneur de jeu" : "Milieu défensif",
            ("possession", "MC") => i == 0 ? "Meneur de jeu" : "Milieu relayeur",
            ("pressing", "ST") => "Attaquant de pression",
            ("pressing", "DM") => i == 0 ? "Récupérateur" : "Milieu box-to-box",
            ("pressing", "MC") => "Milieu box-to-box",
            ("pressing", "DL" or "DR") => "Latéral offensif",
            ("wing", "AML" or "AMR" or "ML" or "MR") => "Ailier",
            ("wing", "DL" or "DR") => "Latéral offensif",
            ("direct", "ST") => i == 0 ? "Pivot" : "Buteur",
            ("contre", "MC") => "Milieu box-to-box",
            ("bloc", "DM") => "Récupérateur",
            ("bloc", "MC") => "Milieu défensif",
            _ => BaseRole(pos),
        };

        public static Features.Slot[] Slots(string shape, string roleKey)
        {
            if (!Shapes.TryGetValue(shape ?? "", out var sl)) sl = Shapes["4-3-3 MD Jeu large"];
            var count = new Dictionary<string, int>();
            return sl.Select(s => { int i = count.GetValueOrDefault(s.Pos); count[s.Pos] = i + 1; return s with { Role = RoleFor(roleKey, s.Pos, i) }; }).ToArray();
        }

        public static Config ConfigOf(string id) => Configs.FirstOrDefault(c => c.Id == id) ?? Configs.First(c => c.Style.Index == 1);

        /// <summary>Postes avec ballon de la configuration choisie dans l'app (utilisés aussi par l'entraînement).</summary>
        public static Features.Slot[] CurrentSlots()
        {
            var c = ConfigOf(Settings.Cur.TacticConfig);
            return Slots(c.With, c.Style.RoleKey);
        }

        // ------------------------------------------------------------------ notation

        public sealed class Eval
        {
            public Config Config;
            public double Total, Players, Defence, Fit;
            public List<(Features.Slot s, PlayerRow r, int score)> Eleven;
            public List<(Features.Slot s, List<(PlayerRow r, int score)> subs)> Backups;
        }

        /// <summary>Adéquation /100 de l'onze avec les exigences du style (moyenne des attributs clés).</summary>
        public static double StyleFit(Style st, IEnumerable<PlayerRow> xi)
        {
            var l = xi.Where(r => r != null && r.Positions[0] < 15).ToList();
            if (l.Count == 0) return 0;
            return l.Average(r => st.Demand.Average(k => Math.Max(1, Db.Stat(r, "Attribute" + k)))) * 5;
        }

        public static Eval Evaluate(Config c, List<PlayerRow> squad)
        {
            var with = Features.BestEleven(squad, Slots(c.With, c.Style.RoleKey));
            var without = c.Without == c.With ? with : Features.BestEleven(squad, Slots(c.Without, c.Style.RoleKey));
            double p1 = with.eleven.Average(x => x.score), p2 = without.eleven.Average(x => x.score);
            double fit = StyleFit(c.Style, with.eleven.Select(x => x.r));
            return new Eval
            {
                Config = c, Players = p1, Defence = p2, Fit = fit,
                Total = p1 * 0.5 + p2 * 0.25 + fit * 0.25,
                Eleven = with.eleven, Backups = with.backups,
            };
        }

        public static List<Eval> Rank(List<PlayerRow> squad) => Configs.Select(c => Evaluate(c, squad)).OrderByDescending(e => e.Total).ToList();

        /// <summary>Points forts et faiblesses de l'onze pour ce style (attributs exigés).</summary>
        public static List<string> Fit(Style st, IEnumerable<PlayerRow> eleven)
        {
            var l = eleven.Where(r => r != null && r.Positions[0] < 15).ToList();
            var notes = new List<string>();
            if (l.Count == 0) return notes;
            foreach (var k in st.Demand)
            {
                double v = l.Average(r => Math.Max(1, Db.Stat(r, "Attribute" + k)));
                string name = L.T(Names.Of("Attribute" + k));
                if (v >= 13.5) notes.Add($"+ {name} ({v:F1}/20)");
                else if (v < 11) notes.Add($"− {name} ({v:F1}/20)");
            }
            return notes;
        }
    }
}
