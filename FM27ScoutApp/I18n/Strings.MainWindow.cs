// Traductions de : MainWindow.xaml.cs

namespace FM27Scout
{
    internal static partial class L
    {
        static readonly (string fr, string en, string pt)[] MainWindowStrings =
        {
            // barre d'état, lecture, date
            ("Connecté à FM27 · {0:N0} joueurs · lus à {1:HH:mm}", "Connected to FM27 · {0:N0} players · read at {1:HH:mm}", "Ligado ao FM27 · {0:N0} jogadores · lidos às {1:HH:mm}"),
            ("Aucun joueur lu.", "No players read.", "Nenhum jogador lido."),
            ("Photos et logos…", "Photos and badges…", "Fotografias e emblemas…"),
            ("Partie en cours de chargement ({0:N0} noms sur {1:N0}) · nouvelle lecture dans 20 s", "Save still loading ({0:N0} of {1:N0} names) · re-reading in 20 s", "Jogo ainda a carregar ({0:N0} nomes de {1:N0}) · nova leitura dentro de 20 s"),
            ("Radar : {0} nouveau(x) joueur(s), dont {1} (PA {2})", "Radar: {0} new player(s), including {1} (PA {2})", "Radar: {0} jogador(es) novo(s), incluindo {1} (PA {2})"),
            ("Date de jeu (inconnue)", "Game date (unknown)", "Data do jogo (desconhecida)"),
            ("Date de jeu (manuelle)", "Game date (manual)", "Data do jogo (manual)"),
            ("Date de jeu (estimée)", "Game date (estimated)", "Data do jogo (estimada)"),
            ("Format attendu : jj/mm/aaaa", "Expected format: dd/mm/yyyy", "Formato esperado: dd/mm/aaaa"),

            // listes de joueurs / clubs
            ("Sans club", "No club", "Sem clube"),
            ("{0} ans", "{0} yrs", "{0} anos"),
            ("{0}  ({1} joueurs)", "{0}  ({1} players)", "{0}  ({1} jogadores)"),
            ("Base pas encore lue", "Database not read yet", "Base de dados ainda não lida"),
            ("Aucun club trouvé", "No club found", "Nenhum clube encontrado"),
            ("Détection automatique (club géré dans FM)", "Automatic detection (club managed in FM)", "Deteção automática (clube orientado no FM)"),

            // affichage : sexe
            ("Hommes", "Men", "Masculino"),
            ("Femmes", "Women", "Feminino"),
            ("Tous", "All", "Todos"),

            // en-têtes des tableaux
            ("Nom", "Name", "Nome"),
            ("Club", "Club", "Clube"),
            ("Âge", "Age", "Idade"),
            ("Postes", "Positions", "Posições"),
            ("Valeur", "Value", "Valor"),
            ("Contrat", "Contract", "Contrato"),
            ("Équipe", "Team", "Equipa"),
            ("Prog. CA", "CA prog.", "Prog. CA"),
            ("Copier le nom", "Copy name", "Copiar nome"),

            // mon club
            ("Lance FM27 et charge ta partie", "Start FM27 and load your save", "Abre o FM27 e carrega o teu jogo"),
            ("Mon club", "My club", "O meu clube"),
            ("Aucun joueur", "No players", "Nenhum jogador"),
            ("pas d'équipe féminine dans ce club", "no women's team at this club", "sem equipa feminina neste clube"),
            ("pour ce filtre", "for this filter", "para este filtro"),
            ("Joueurs sous contrat", "Players under contract", "Jogadores sob contrato"),
            ("Âge moyen (équipe 1)", "Average age (first team)", "Idade média (equipa A)"),
            ("{0:F1} ans", "{0:F1} yrs", "{0:F1} anos"),
            ("CA moyen (équipe 1)", "Average CA (first team)", "CA média (equipa A)"),
            ("Valeur de l'effectif", "Squad value", "Valor do plantel"),
            ("Masse salariale", "Wage bill", "Massa salarial"),
            ("{0}/sem. · {1}/an", "{0}/wk · {1}/yr", "{0}/sem. · {1}/ano"),
            ("Contrats finissant ≤ 12 mois", "Contracts ending ≤ 12 months", "Contratos a terminar ≤ 12 meses"),
            ("date ?", "date?", "data?"),
            ("Tout l'effectif ({0})", "Whole squad ({0})", "Plantel completo ({0})"),
            ("Équipe première", "First team", "Equipa principal"),
            ("Réserve / équipe B", "Reserves / B team", "Reservas / equipa B"),
            ("U21", "U21", "Sub-21"),
            ("U18", "U18", "Sub-18"),
            ("Réserve / jeunes", "Reserves / youth", "Reservas / juniores"),

            // fiche joueur
            ("Cherche un joueur dans la barre du haut, ou double-clique sur une ligne d'un tableau.", "Search for a player in the top bar, or double-click a row in any table.", "Pesquisa um jogador na barra de cima, ou faz duplo clique numa linha de uma tabela."),
            ("Cliquer pour copier le nom", "Click to copy the name", "Clica para copiar o nome"),
            ("âge inconnu", "age unknown", "idade desconhecida"),
            ("(né le {0})", "(born {0})", "(nascido a {0})"),
            ("football féminin", "women's football", "futebol feminino"),
            ("{0}/sem.", "{0}/wk", "{0}/sem."),
            ("Valeur {0}  ·  Salaire {1}  ·  Fin de contrat {2}", "Value {0}  ·  Wage {1}  ·  Contract expires {2}", "Valor {0}  ·  Salário {1}  ·  Fim de contrato {2}"),
            ("Comparer (joueur A)", "Compare (player A)", "Comparar (jogador A)"),
            ("Comparer (joueur B)", "Compare (player B)", "Comparar (jogador B)"),
            ("Alertes : {0}", "Alerts: {0}", "Alertas: {0}"),
            ("Stats cachées", "Hidden attributes", "Atributos ocultos"),
            ("Notes par rôle (/100)", "Role ratings (/100)", "Notas por função (/100)"),
            ("Pondérations modifiables : %APPDATA%\\FM27Scout\\roles.json", "Weightings can be edited in: %APPDATA%\\FM27Scout\\roles.json", "Ponderações editáveis em: %APPDATA%\\FM27Scout\\roles.json"),
            ("« {0} » copié : colle-le (Ctrl+V) dans la recherche de FM27", "“{0}” copied: paste it (Ctrl+V) into the FM27 search", "“{0}” copiado: cola-o (Ctrl+V) na pesquisa do FM27"),
            ("Copié", "Copied", "Copiado"),
            ("Capacité actuelle", "Current ability", "Capacidade atual"),
            ("Potentiel", "Potential", "Potencial"),

            // recherches, export
            ("{0} — {1} résultat(s)", "{0} — {1} result(s)", "{0} — {1} resultado(s)"),
            ("{0} joueurs exportés :\n{1}\n\nOuvrir le fichier ?", "{0} players exported:\n{1}\n\nOpen the file?", "{0} jogadores exportados:\n{1}\n\nAbrir o ficheiro?"),
            ("Export impossible : {0}", "Export failed: {0}", "Não foi possível exportar: {0}"),

            // radar
            ("Filtres", "Filters", "Filtros"),
            ("Âge min", "Min age", "Idade mín."),
            ("Âge max", "Max age", "Idade máx."),
            ("Valeur max", "Max value", "Valor máx."),
            ("illimitée", "unlimited", "ilimitado"),
            ("Date de jeu inconnue : filtre d'âge ignoré (saisis-la en haut).", "Game date unknown: age filter ignored (enter it at the top).", "Data do jogo desconhecida: filtro de idade ignorado (introduz a data em cima)."),
            ("Effacer", "Clear", "Limpar"),
            ("Postes : tous", "Positions: all", "Posições: todas"),
            ("Postes : {0}", "Positions: {0}", "Posições: {0}"),
            ("Trier par", "Sort by", "Ordenar por"),
            ("Capacité", "Ability", "Capacidade"),
            ("Moins de stats ▴", "Fewer attributes ▴", "Menos atributos ▴"),
            ("Toutes les stats ▾", "All attributes ▾", "Todos os atributos ▾"),
            ("NOUVEAU", "NEW", "NOVO"),
            ("{0}{1} joueur(s)", "{0}{1} player(s)", "{0}{1} jogador(es)"),
            ("{0} nouveau(x)", "{0} new", "{0} novo(s)"),

            // alertes d'effectif (titres produits par Db.Alerts, gardés en français comme clés)
            ("Choisis un club à gauche", "Pick a club on the left", "Escolhe um clube à esquerda"),
            ("{0} — {1} alerte(s)", "{0} — {1} alert(s)", "{0} — {1} alerta(s)"),
            ("Toutes", "All", "Todas"),
            ("Alerte", "Alert", "Alerta"),
            ("Détail", "Detail", "Detalhe"),
            ("Contrat qui se termine dans 6 mois ou moins", "Contract expiring within 6 months", "Contrato a terminar dentro de 6 meses ou menos"),
            ("Contrat qui se termine dans 6 à 12 mois", "Contract expiring in 6 to 12 months", "Contrato a terminar dentro de 6 a 12 meses"),
            ("Très fragiles (fragilité ≥ 15)", "Very injury-prone (injury proneness ≥ 15)", "Muito propensos a lesões (propensão ≥ 15)"),
            ("Irréguliers (régularité ≤ 8)", "Inconsistent (consistency ≤ 8)", "Irregulares (consistência ≤ 8)"),
            ("Fragiles dans les grands matchs (≤ 8)", "Shaky in big matches (≤ 8)", "Frágeis em jogos importantes (≤ 8)"),
            ("Jeunes peu professionnels (≤ 21 ans, pro ≤ 9)", "Unprofessional youngsters (≤ 21, prof. ≤ 9)", "Jovens pouco profissionais (≤ 21 anos, prof. ≤ 9)"),
            ("Marge de progression importante (PA - CA ≥ 25)", "Lots of room to develop (PA - CA ≥ 25)", "Grande margem de progressão (PA - CA ≥ 25)"),

            // comparateur
            ("{0} résultat(s) dans toute la base", "{0} result(s) across the whole database", "{0} resultado(s) em toda a base de dados"),
            ("Tape un nom (choisis ton club dans « Mon club » pour voir ton effectif ici)", "Type a name (choose your club in “My club” to see your squad here)", "Escreve um nome (escolhe o teu clube em “O meu clube” para veres aqui o teu plantel)"),
            ("Ton effectif ({0}) — tape un nom pour chercher partout", "Your squad ({0}) — type a name to search everywhere", "O teu plantel ({0}) — escreve um nome para pesquisar em todo o lado"),
            ("Choisis un joueur à gauche et un joueur à droite.", "Choose one player on the left and one on the right.", "Escolhe um jogador à esquerda e outro à direita."),
            ("Profil", "Profile", "Perfil"),
            ("Notes par rôle", "Role ratings", "Notas por função"),
        };
    }
}
