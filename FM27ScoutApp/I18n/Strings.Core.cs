// Traductions de : Logic.cs, Game.cs, Bridge.cs, Setup.cs, App.xaml.cs, Graphics.cs, Memory.cs, Probe.cs, Theme.cs

namespace FM27Scout
{
    internal static partial class L
    {
        static readonly (string fr, string en, string pt)[] CoreStrings =
        {
            // ---- groupes de colonnes / catégories d'attributs (Names.Groups.cat, "Cachées")
            // « Technique » est aussi l'attribut et « Gardien » aussi le rôle : une seule traduction valable pour les deux
            ("Technique", "Technique", "Técnica"),
            ("Mental", "Mental", "Mental"),
            ("Physique", "Physical", "Físico"),
            ("Gardien", "Goalkeeper", "Guarda-Redes"),
            ("Cachées", "Hidden", "Ocultos"),

            // ---- attributs techniques
            ("Corners", "Corners", "Cantos"),
            ("Centres", "Crossing", "Cruzamentos"),
            ("Dribbles", "Dribbling", "Finta"),
            ("Finition", "Finishing", "Finalização"),
            ("Contrôle de balle", "First Touch", "Primeiro Toque"),
            ("Coups francs", "Free Kick Taking", "Livres Diretos"),
            ("Jeu de tête", "Heading", "Cabeceamento"),
            ("Tirs de loin", "Long Shots", "Remates de Longe"),
            ("Touches longues", "Long Throws", "Lançamentos Longos"),
            ("Marquage", "Marking", "Marcação"),
            ("Passes", "Passing", "Passe"),
            ("Penalty", "Penalty Taking", "Penáltis"),
            ("Tacles", "Tackling", "Desarme"),

            // ---- attributs mentaux
            ("Agressivité", "Aggression", "Agressividade"),
            ("Anticipation", "Anticipation", "Antecipação"),
            ("Courage", "Bravery", "Bravura"),
            ("Sang-froid", "Composure", "Compostura"),
            ("Concentration", "Concentration", "Concentração"),
            ("Décisions", "Decisions", "Decisões"),
            ("Détermination", "Determination", "Determinação"),
            ("Inspiration", "Flair", "Criatividade"),
            ("Leadership", "Leadership", "Liderança"),
            ("Appels de balle", "Off The Ball", "Sem Bola"),
            ("Placement", "Positioning", "Posicionamento"),
            ("Jeu collectif", "Teamwork", "Trabalho de Equipa"),
            ("Vision du jeu", "Vision", "Visão de Jogo"),
            ("Volume de jeu", "Work Rate", "Índice de Trabalho"),

            // ---- attributs physiques
            ("Accélération", "Acceleration", "Aceleração"),
            ("Agilité", "Agility", "Agilidade"),
            ("Équilibre", "Balance", "Equilíbrio"),
            ("Détente verticale", "Jumping Reach", "Impulsão"),
            ("Qualités phys. nat.", "Natural Fitness", "Condição Física Natural"),
            ("Vitesse", "Pace", "Velocidade"),
            ("Endurance", "Stamina", "Resistência"),
            ("Puissance", "Strength", "Força"),

            // ---- attributs de gardien
            ("Détente", "Aerial Reach", "Alcançar Cruzamentos"),
            ("Sorties dans la surface", "Command Of Area", "Comando da Área"),
            ("Communication", "Communication", "Comunicação"),
            ("Excentricité", "Eccentricity", "Excentricidade"),
            ("Prises de balle", "Handling", "Segurar"),
            ("Dégagements", "Kicking", "Pontapé"),
            ("Un contre un", "One On Ones", "Um Contra Um"),
            ("Réflexes", "Reflexes", "Reflexos"),
            ("Sorties dans les pieds (tendance)", "Rushing Out (Tendency)", "Saídas (Tendência)"),
            ("Dégagement au poing (tendance)", "Punching (Tendency)", "Socos (Tendência)"),
            ("Relances à la main", "Throwing", "Reposição à Mão"),

            // ---- attributs cachés et personnalité (Names.Hidden)
            ("Régularité", "Consistency", "Consistência"),
            ("Grands matchs", "Important Matches", "Jogos Importantes"),
            ("Fragilité", "Injury Proneness", "Propensão a Lesões"),
            ("Vice", "Dirtiness", "Jogo Sujo"),
            ("Polyvalence", "Versatility", "Versatilidade"),
            ("Professionnalisme", "Professionalism", "Profissionalismo"),
            ("Ambition", "Ambition", "Ambição"),
            ("Résistance à la pression", "Pressure", "Pressão"),
            ("Tempérament", "Temperament", "Temperamento"),
            ("Adaptabilité", "Adaptability", "Adaptabilidade"),
            ("Loyauté", "Loyalty", "Lealdade"),
            ("Fair-play", "Sportsmanship", "Desportivismo"),
            ("Controverse", "Controversy", "Controvérsia"),

            // ---- libellés spéciaux de Names.Of
            ("Capacité actuelle", "Current ability", "Capacidade atual"),
            ("Potentiel", "Potential", "Potencial"),
            ("Âge", "Age", "Idade"),
            ("Valeur", "Value", "Valor"),

            // ---- rôles par défaut (Db.Role.Nom ; « Gardien » plus haut)
            ("Défenseur central", "Centre-Back", "Defesa Central"),
            ("Latéral", "Full-Back", "Defesa Lateral"),
            ("Piston", "Wing-Back", "Ala"),
            ("Milieu défensif", "Defensive Midfielder", "Médio Defensivo"),
            ("Milieu relayeur", "Central Midfielder", "Médio Centro"),
            ("Meneur de jeu", "Deep-Lying Playmaker", "Construtor de Jogo Recuado"),
            ("Ailier", "Winger", "Extremo"),
            ("Attaquant intérieur", "Inside Forward", "Avançado Interior"),
            ("Milieu offensif", "Attacking Midfielder", "Médio Ofensivo"),
            ("Buteur", "Centre Forward", "Avançado"),
            ("Pivot", "Target Forward", "Avançado de Referência"),
            ("Gardien relanceur", "Ball-Playing Goalkeeper", "Guarda-Redes de Construção"),
            ("Défenseur relanceur", "Ball-Playing Centre-Back", "Central de Construção"),
            ("Latéral offensif", "Advanced Wing-Back", "Ala Projetado"),
            ("Récupérateur", "Pressing Defensive Midfielder", "Médio Defensivo Trabalhador"),
            ("Milieu box-to-box", "Box-to-Box Midfielder", "Médio Área-a-Área"),
            ("Attaquant de pression", "Tracking Centre Forward", "Avançado Esforçado"),
            ("Faux 9", "False Nine", "Falso 9"),

            // ---- recherches par défaut (Db.Search.Nom)
            ("Pépites (≤ 19 ans, PA ≥ 150)", "Wonderkids (≤ 19 yrs, PA ≥ 150)", "Pérolas (≤ 19 anos, PA ≥ 150)"),
            ("Jeunes prêts à jouer (≤ 23 ans, CA ≥ 130)", "First-team-ready youngsters (≤ 23 yrs, CA ≥ 130)", "Jovens prontos a jogar (≤ 23 anos, CA ≥ 130)"),
            ("Mentalité d'élite", "Elite mentality", "Mentalidade de elite"),
            ("Fins de contrat (≤ 6 mois, CA ≥ 120)", "Expiring contracts (≤ 6 months, CA ≥ 120)", "Fim de contrato (≤ 6 meses, CA ≥ 120)"),
            ("Gardiens d'avenir (≤ 21 ans, PA ≥ 140)", "Goalkeepers of the future (≤ 21 yrs, PA ≥ 140)", "Guarda-redes de futuro (≤ 21 anos, PA ≥ 140)"),
            ("Buteurs (CA ≥ 140)", "Strikers (CA ≥ 140)", "Pontas de lança (CA ≥ 140)"),

            // ---- alertes d'effectif (Db.Alerts)
            ("Contrat qui se termine dans 6 mois ou moins", "Contract expiring within 6 months", "Contrato a terminar dentro de 6 meses ou menos"),
            ("Contrat qui se termine dans 6 à 12 mois", "Contract expiring in 6 to 12 months", "Contrato a terminar dentro de 6 a 12 meses"),
            ("Très fragiles (fragilité ≥ 15)", "Very injury-prone (injury proneness ≥ 15)", "Muito propensos a lesões (propensão ≥ 15)"),
            ("Irréguliers (régularité ≤ 8)", "Inconsistent (consistency ≤ 8)", "Irregulares (consistência ≤ 8)"),
            ("Fragiles dans les grands matchs (≤ 8)", "Shaky in big matches (≤ 8)", "Frágeis em jogos importantes (≤ 8)"),
            ("Jeunes peu professionnels (≤ 21 ans, pro ≤ 9)", "Unprofessional youngsters (≤ 21, prof. ≤ 9)", "Jovens pouco profissionais (≤ 21 anos, prof. ≤ 9)"),
            ("Marge de progression importante (PA - CA ≥ 25)", "Lots of room to develop (PA - CA ≥ 25)", "Grande margem de progressão (PA - CA ≥ 25)"),
            ("fin {0}", "ends {0}", "termina {0}"),
            ("fragilité {0}", "injury proneness {0}", "propensão a lesões {0}"),
            ("régularité {0}", "consistency {0}", "consistência {0}"),
            ("grands matchs {0}", "important matches {0}", "jogos importantes {0}"),
            ("pro {0}, {1} ans", "pro {0}, age {1}", "pro {0}, {1} anos"),

            // ---- export CSV (en-têtes)
            ("Nom", "Name", "Nome"),
            ("Club", "Club", "Clube"),
            ("Age", "Age", "Idade"),
            ("Postes", "Positions", "Posições"),
            ("Valeur (£)", "Value (£)", "Valor (£)"),
            ("Salaire (£/sem)", "Wage (£/wk)", "Salário (£/sem)"),
            ("Fin de contrat", "Contract expiry", "Fim de contrato"),

            // ---- lecture de la partie (Game, Memory)
            ("Lecture de la mémoire du jeu…", "Reading game memory…", "A ler a memória do jogo…"),
            ("Aucun joueur trouvé : charge une partie dans FM27.", "No players found: load a save in FM27.", "Nenhum jogador encontrado: carrega um jogo guardado no FM27."),
            ("Lecture des noms, contrats et clubs…", "Reading names, contracts and clubs…", "A ler nomes, contratos e clubes…"),
            ("Seulement {0} joueurs trouvés : la partie est-elle chargée ?", "Only {0} players found: is the save loaded?", "Apenas {0} jogadores encontrados: o jogo está carregado?"),
            ("{0:N0} joueurs lus en {1:F1} s", "{0:N0} players read in {1:F1} s", "{0:N0} jogadores lidos em {1:F1} s"),
            ("FM27 n'est pas lancé.", "FM27 is not running.", "O FM27 não está aberto."),
            ("Accès refusé à FM27 (lance l'app avec le même compte que le jeu).", "Access to FM27 denied (run the app under the same account as the game).", "Acesso ao FM27 negado (abre a app com a mesma conta do jogo)."),
            ("FM27 démarre encore…", "FM27 is still starting…", "O FM27 ainda está a arrancar…"),
            ("FM27 démarre encore… ({0})", "FM27 is still starting… ({0})", "O FM27 ainda está a arrancar… ({0})"),

            // ---- module FM27 Bridge (messages d'erreur affichés)
            ("FM27 Bridge ne répond pas (FM est-il lancé avec une partie chargée ?)", "FM27 Bridge is not responding (is FM running with a save loaded?)", "O FM27 Bridge não responde (o FM está aberto com um jogo carregado?)"),
            ("partie pas encore reconnue : ouvre une fois l'écran Entraînement dans FM", "save not recognised yet: open the Training screen in FM once", "jogo ainda não reconhecido: abre uma vez o ecrã de Treino no FM"),
            ("Références", "References", "Referências"),
            ("Lecture", "Reading", "Leitura"),
            ("Ordre {0}", "Command {0}", "Ordem {0}"),
            ("Écriture", "Writing", "Escrita"),
            ("Intensité équipe", "Team intensity", "Intensidade da equipa"),

            // ---- installation / désinstallation (Setup)
            ("Football Manager 27 est lancé : ferme-le avant d'installer.", "Football Manager 27 is running: close it before installing.", "O Football Manager 27 está aberto: fecha-o antes de instalar."),
            ("Installation de BepInEx et de FM27 Bridge dans le jeu…", "Installing BepInEx and FM27 Bridge into the game…", "A instalar o BepInEx e o FM27 Bridge no jogo…"),
            ("contenu d'installation manquant dans l'exe", "setup content missing from the exe", "conteúdo de instalação em falta no exe"),
            ("Installation de l'application…", "Installing the app…", "A instalar a aplicação…"),
            ("Raccourcis…", "Shortcuts…", "Atalhos…"),
            ("Désinstaller FM27 Assistant ?\n\nFM27 Bridge sera retiré du jeu. BepInEx reste installé mais est désactivé (winhttp.dll retiré). Tes réglages dans %APPDATA%\\FM27Scout sont conservés.",
             "Uninstall FM27 Assistant?\n\nFM27 Bridge will be removed from the game. BepInEx stays installed but is disabled (winhttp.dll removed). Your settings in %APPDATA%\\FM27Scout are kept.",
             "Desinstalar o FM27 Assistant?\n\nO FM27 Bridge será removido do jogo. O BepInEx continua instalado, mas fica desativado (winhttp.dll removido). As tuas definições em %APPDATA%\\FM27Scout são mantidas."),
            ("FM27 Assistant a été désinstallé.", "FM27 Assistant has been uninstalled.", "O FM27 Assistant foi desinstalado."),
            ("installation", "setup", "instalação"),
            ("Installe l'application et le module FM27 Bridge (BepInEx) dans Football Manager 27.", "Installs the app and the FM27 Bridge mod (BepInEx) into Football Manager 27.", "Instala a aplicação e o módulo FM27 Bridge (BepInEx) no Football Manager 27."),
            ("DOSSIER DE FOOTBALL MANAGER 27", "FOOTBALL MANAGER 27 FOLDER", "PASTA DO FOOTBALL MANAGER 27"),
            ("Parcourir…", "Browse…", "Procurar…"),
            ("Dossier de Football Manager 27 (celui qui contient fm.exe)", "Football Manager 27 folder (the one containing fm.exe)", "Pasta do Football Manager 27 (a que contém o fm.exe)"),
            ("Créer un raccourci sur le bureau", "Create a desktop shortcut", "Criar um atalho no ambiente de trabalho"),
            ("Installer", "Install", "Instalar"),
            ("Ce dossier ne contient pas fm.exe : choisis le dossier d'installation de Football Manager 27.", "This folder doesn't contain fm.exe: choose the Football Manager 27 install folder.", "Esta pasta não contém o fm.exe: escolhe a pasta de instalação do Football Manager 27."),
            ("Installation terminée. Au premier lancement de FM27, BepInEx prépare le jeu (1 à 2 minutes de plus que d'habitude), ensuite c'est instantané.",
             "Installation complete. The first time you launch FM27, BepInEx prepares the game (1 to 2 minutes longer than usual); after that it's instant.",
             "Instalação concluída. No primeiro arranque do FM27, o BepInEx prepara o jogo (1 a 2 minutos a mais do que o habitual); depois é instantâneo."),
            ("Lancer FM27 Assistant", "Launch FM27 Assistant", "Abrir o FM27 Assistant"),
            ("Échec : {0}", "Failed: {0}", "Falhou: {0}"),

            // ---- erreur inattendue (App)
            ("Erreur inattendue : {0}\n\nDétails dans %APPDATA%\\FM27Scout\\journal.txt", "Unexpected error: {0}\n\nDetails in %APPDATA%\\FM27Scout\\journal.txt", "Erro inesperado: {0}\n\nDetalhes em %APPDATA%\\FM27Scout\\journal.txt"),
        };
    }
}
