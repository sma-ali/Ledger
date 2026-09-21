namespace Ledger.Domain;

/// <summary>
/// Une ligne de résultat normalisée : le modèle commun vers lequel converge
/// chaque source, quel que soit son format d'origine.
/// </summary>
public sealed record PnlEntry
{
    /// <summary>
    /// Code métier de la source (« FO », « BO »). Volontairement pas un
    /// identifiant de base de données : le domaine ignore que SQL existe.
    /// </summary>
    public required string SourceCode { get; init; }

    /// <summary>Clé de rapprochement portée par cette ligne.</summary>
    public required BusinessKey Key { get; init; }

    /// <summary>
    /// Montant. Le type est decimal et non double : sur un montant,
    /// l'arrondi binaire d'un flottant est une faute professionnelle.
    /// </summary>
    public required decimal Amount { get; init; }

    /// <summary>
    /// Numéro de ligne dans le fichier d'origine, pour pouvoir remonter
    /// à la donnée brute quand un écart est contesté.
    /// </summary>
    public required int SourceLineNumber { get; init; }
}
