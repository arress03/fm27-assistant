// Traductions de : Features.cs, MainWindow.Features.cs

namespace FM27Scout
{
    internal static partial class L
    {
        static readonly (string fr, string en, string pt)[] FeatureStrings =
        {
            // Features.cs : changements de la shortlist
            ("club : {0} → {1}", "club: {0} → {1}", "clube: {0} → {1}"),
            ("libre", "free agent", "livre"),
            ("valeur {0:F0} %", "value {0:F0}%", "valor {0:F0}%"),
            ("valeur +{0:F0} %", "value +{0:F0}%", "valor +{0:F0}%"),
            ("contrat fini dans {0} mois", "contract ends in {0} month(s)", "contrato acaba dentro de {0} mês(es)"),
            ("contrat ≤ 12 mois", "contract ≤ 12 months", "contrato ≤ 12 meses"),
            // Features.cs : bonnes affaires
            ("Joueurs libres", "Free agents", "Jogadores livres"),
            ("Fin de contrat ≤ 6 mois", "Contract ending ≤ 6 months", "Fim de contrato ≤ 6 meses"),
            ("Fin de contrat ≤ 12 mois", "Contract ending ≤ 12 months", "Fim de contrato ≤ 12 meses"),
            ("Meilleur rapport qualité-prix", "Best value for money", "Melhor relação qualidade-preço"),
            // Features.cs : vestiaire
            ("Fragiles", "Injury-prone", "Propensos a lesões"),
            ("Irréguliers", "Inconsistent", "Inconsistentes"),
            ("Faibles en grands matchs", "Weak in big matches", "Fracos nos jogos grandes"),
            ("Peu professionnels", "Unprofessional", "Pouco profissionais"),
            ("Tempérament à risque", "Volatile temperament", "Temperamento de risco"),
            ("Craquent sous la pression", "Crack under pressure", "Cedem à pressão"),
            ("Risque de départ (ambitieux, peu loyaux)", "Flight risk (ambitious, not loyal)", "Risco de saída (ambiciosos, pouco leais)"),
            ("Fiables pour une finale", "Trusted for a final", "De confiança para uma final"),
            ("Modèles pour les jeunes", "Role models for youngsters", "Exemplos para os jovens"),
            ("Polyvalents", "Versatile", "Polivalentes"),
            ("fragilité {0}", "injury proneness {0}", "propensão a lesões {0}"),
            ("régularité {0}", "consistency {0}", "consistência {0}"),
            ("grands matchs {0}", "important matches {0}", "jogos importantes {0}"),
            ("professionnalisme {0}", "professionalism {0}", "profissionalismo {0}"),
            ("tempérament {0}", "temperament {0}", "temperamento {0}"),
            ("vice {0}", "dirtiness {0}", "jogo sujo {0}"),
            ("controverse {0}", "controversy {0}", "controvérsia {0}"),
            ("pression {0}", "pressure {0}", "pressão {0}"),
            ("ambition {0}, loyauté {1}", "ambition {0}, loyalty {1}", "ambição {0}, lealdade {1}"),
            ("grands matchs {0}, régularité {1}", "important matches {0}, consistency {1}", "jogos importantes {0}, consistência {1}"),
            ("pro {0}, détermination {1}", "pro {0}, determination {1}", "pro {0}, determinação {1}"),
            ("polyvalence {0}", "versatility {0}", "polivalência {0}"),
            // Features.cs : axes du radar (Physique/Technique/Mental : table Core)
            ("Vitesse", "Pace", "Velocidade"),
            ("Création", "Creativity", "Criação"),
            ("Finition", "Finishing", "Finalização"),
            ("Défense", "Defending", "Defesa"),
            ("Jeu aérien", "Aerial", "Jogo aéreo"),
            ("Fiabilité", "Reliability", "Fiabilidade"),
            // MainWindow.Features.cs : shortlist
            ("Changements depuis l'ajout", "Changes since added", "Alterações desde a adição"),
            ("Prog. CA", "CA prog.", "Prog. CA"),
            ("Vide : ajoute des joueurs depuis leur fiche.", "Empty: add players from their profile.", "Vazia: adiciona jogadores a partir da ficha deles."),
            ("{0} joueur(s)", "{0} player(s)", "{0} jogador(es)"),
            ("{0} à surveiller (club, valeur en baisse, fin de contrat)", "{0} to watch (club, falling value, contract expiry)", "{0} a vigiar (clube, valor em queda, fim de contrato)"),
            ("Shortlist · {0} à voir", "Shortlist · {0} to check", "Lista de alvos · {0} a ver"),
            ("Dans la shortlist", "On the shortlist", "Na lista de alvos"),
            ("Ajouter à la shortlist", "Add to shortlist", "Adicionar à lista de alvos"),
            // MainWindow.Features.cs : bonnes affaires
            ("Base pas encore lue", "Database not read yet", "Base de dados ainda não lida"),
            ("Qualité/prix", "Value/price", "Qualidade/preço"),
            ("Salaire/sem.", "Wage/wk", "Salário/sem."),
            ("date de jeu inconnue : saisis-la en haut", "game date unknown: enter it at the top", "data do jogo desconhecida: introduz-a em cima"),
            ("qualité/prix = CA² par million de £ (plus c'est haut, mieux c'est)", "value/price = CA² per £1 million (the higher, the better)", "qualidade/preço = CA² por milhão de £ (quanto mais alto, melhor)"),
            // MainWindow.Features.cs : vestiaire
            ("Choisis ton club dans « Mon club ».", "Choose your club in “My club”.", "Escolhe o teu clube em “O meu clube”."),
            ("Tout le vestiaire", "Whole dressing room", "Todo o balneário"),
            ("Pourquoi", "Why", "Porquê"),
            ("{0} joueurs · points forts en vert, risques en orange (stats cachées)", "{0} players · strengths in green, risks in orange (hidden attributes)", "{0} jogadores · pontos fortes a verde, riscos a laranja (atributos ocultos)"),
            // MainWindow.Features.cs : fiche joueur (progression, profils similaires)
            ("Progression", "Progress", "Progressão"),
            ("Un relevé est enregistré à chaque date de jeu : la courbe apparaîtra quand tu auras avancé dans ta partie.", "A snapshot is saved for each game date: the graph will appear once you've moved further into your save.", "É guardado um registo em cada data do jogo: o gráfico aparecerá quando tiveres avançado no teu jogo."),
            ("CA {0} → {1} ({2}) depuis le {3:dd/MM/yyyy}  ·  PA {4}", "CA {0} → {1} ({2}) since {3:dd/MM/yyyy}  ·  PA {4}", "CA {0} → {1} ({2}) desde {3:dd/MM/yyyy}  ·  PA {4}"),
            ("{0:dd/MM/yyyy} : CA {1}", "{0:dd/MM/yyyy}: CA {1}", "{0:dd/MM/yyyy}: CA {1}"),
            ("Plus jeunes", "Younger", "Mais novos"),
            ("Moins chers", "Cheaper", "Mais baratos"),
            ("Profils similaires (remplaçants possibles)", "Similar profiles (possible replacements)", "Perfis semelhantes (possíveis substitutos)"),
            ("Attributs les plus proches, sur au moins un de ses postes. Clic = fiche.", "Closest attributes, in at least one of their positions. Click = profile.", "Atributos mais próximos, em pelo menos uma das suas posições. Clique = ficha."),
            ("Aucun joueur trouvé avec ces filtres.", "No players found with these filters.", "Nenhum jogador encontrado com estes filtros."),
            ("Joueur", "Player", "Jogador"),
            ("Club", "Club", "Clube"),
            ("Âge", "Age", "Idade"),
            ("Postes", "Positions", "Posições"),
            ("Valeur", "Value", "Valor"),
            ("ressemblance {0} %", "{0}% match", "semelhança {0}%"),
            ("Libre", "Free agent", "Livre"),
            // MainWindow.Features.cs : comparateur (radar)
            ("Moyenne des attributs de chaque famille (sur 20). « Fiabilité » = régularité, grands matchs, professionnalisme, pression.", "Average of each group's attributes (out of 20). “Reliability” = consistency, important matches, professionalism, pressure.", "Média dos atributos de cada grupo (em 20). “Fiabilidade” = consistência, jogos importantes, profissionalismo, pressão."),
        };
    }
}
