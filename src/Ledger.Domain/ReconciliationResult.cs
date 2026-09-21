namespace Ledger.Domain;

/// <summary>
/// Résultat du rapprochement pour une clé métier : son statut, l'écart
/// constaté, et le détail de ce qu'annonce chaque source.
/// </summary>
public sealed record ReconciliationResult
{
    public required BusinessKey Key { get; init; }

    public required ReconciliationStatus Status { get; init; }

    /// <summary>
    /// Écart constaté entre les sources. Vaut zéro quand une seule source
    /// porte la clé : dans ce cas c'est le statut Missing qui informe.
    /// </summary>
    public required decimal Difference { get; init; }

    /// <summary>
    /// Détail par source. IReadOnlyList et non List : l'appelant peut lire
    /// la liste, pas la modifier après coup.
    /// </summary>
    public required IReadOnlyList<ReconciliationLeg> Legs { get; init; }
}
