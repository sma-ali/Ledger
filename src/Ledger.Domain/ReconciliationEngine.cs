using System.Diagnostics.CodeAnalysis;

namespace Ledger.Domain;

/// <summary>
/// Rapproche les montants annoncés par plusieurs sources et qualifie les
/// écarts. Tout se passe en mémoire : aucune base, aucun fichier, aucune
/// horloge. C'est ce qui rend ce moteur testable en quelques millisecondes.
/// </summary>
public sealed class ReconciliationEngine
{
    /// <summary>
    /// Produit un résultat par clé métier rencontrée.
    /// </summary>
    /// <param name="entries">
    /// Toutes les lignes normalisées du run, toutes sources confondues.
    /// </param>
    /// <param name="expectedSources">
    /// Les codes des sources qui devraient avoir alimenté ce run (« FO », « BO »).
    /// Sert à détecter qu'une clé manque d'un côté.
    /// </param>
    /// <param name="tolerances">Tolérances admises sur l'écart, par devise.</param>
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Le moteur sera enregistré comme service dans le conteneur "
                      + "d'injection de dépendances et recevra un logger : le garder "
                      + "en méthode d'instance évite d'avoir à tout reprendre.")]
    public IReadOnlyList<ReconciliationResult> Reconcile(
        IReadOnlyCollection<PnlEntry> entries,
        IReadOnlyCollection<string> expectedSources,
        ToleranceRuleSet tolerances)
    {
        // Si l'appelant nous passe null, on s'arrête ici avec un message
        // clair, plutôt que de planter vingt lignes plus bas sans raison
        // compréhensible.
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(expectedSources);
        ArgumentNullException.ThrowIfNull(tolerances);

        // La liste qu'on remplit au fur et à mesure et qu'on renverra.
        var resultats = new List<ReconciliationResult>();

        // GroupBy range les lignes en paquets partageant la même clé métier.
        // Un tour de boucle = une clé = un résultat à produire.
        foreach (var groupeDeLaCle in entries.GroupBy(ligne => ligne.Key))
        {
            var cle = groupeDeLaCle.Key;

            // Dans ce paquet, on regroupe encore, cette fois par source,
            // pour savoir ce que chacune annonce.
            var legs = new List<ReconciliationLeg>();

            foreach (var groupeDeLaSource in groupeDeLaCle.GroupBy(ligne => ligne.SourceCode))
            {
                legs.Add(new ReconciliationLeg
                {
                    SourceCode = groupeDeLaSource.Key,
                    Amount = groupeDeLaSource.Sum(ligne => ligne.Amount),
                    EntryCount = groupeDeLaSource.Count(),
                });
            }

            // --- Étape 1 : toutes les sources attendues sont-elles là ? ---
            //
            // On range les codes des sources présentes dans un HashSet, une
            // collection sans doublon qui répond très vite à « est-ce que
            // tu contiens cette valeur ? ». OrdinalIgnoreCase pour que
            // « fo » et « FO » désignent la même source.
            var sourcesPresentes = new HashSet<string>(
                legs.Select(leg => leg.SourceCode),
                StringComparer.OrdinalIgnoreCase);

            var toutesLesSourcesSontLa =
                expectedSources.All(source => sourcesPresentes.Contains(source));

            // Déclarés sans valeur : chaque branche du if ci-dessous leur en
            // donne une, et le compilateur vérifie qu'aucun chemin ne les
            // laisse non assignés.
            ReconciliationStatus statut;
            decimal ecart;

            if (!toutesLesSourcesSontLa)
            {
                // Comparer un montant à rien ne produit pas d'écart
                // chiffrable : c'est le statut qui porte l'information.
                statut = ReconciliationStatus.Missing;
                ecart = 0m;
            }
            else
            {
                // --- Étape 2 : de combien les sources divergent-elles ? ---
                //
                // Le plus grand montant moins le plus petit. Toujours positif,
                // quel que soit le sens de l'écart, et la formule reste juste
                // si une troisième source arrive un jour.
                ecart = legs.Max(leg => leg.Amount) - legs.Min(leg => leg.Amount);

                // --- Étape 3 : cet écart est-il acceptable ? ---
                if (ecart == 0m)
                {
                    statut = ReconciliationStatus.Matched;
                }
                else if (ecart <= tolerances.For(cle.Currency))
                {
                    // Borne incluse : un écart pile à la tolérance passe.
                    statut = ReconciliationStatus.WithinTolerance;
                }
                else
                {
                    statut = ReconciliationStatus.Break;
                }
            }

            resultats.Add(new ReconciliationResult
            {
                Key = cle,
                Status = statut,
                Difference = ecart,
                Legs = legs,
            });
        }

        return resultats;
    }
}
