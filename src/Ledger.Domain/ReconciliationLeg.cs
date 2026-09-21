namespace Ledger.Domain;

/// <summary>
/// Contribution d'une source à une clé métier donnée : le montant qu'elle
/// annonce, et le nombre de lignes agrégées pour y arriver.
/// </summary>
public sealed record ReconciliationLeg
{
    public required string SourceCode { get; init; }

    /// <summary>Somme des montants de cette source pour la clé.</summary>
    public required decimal Amount { get; init; }

    /// <summary>
    /// Nombre de lignes agrégées. Une source qui annonce le bon total avec
    /// dix lignes là où l'autre en a une seule reste une anomalie à voir.
    /// </summary>
    public required int EntryCount { get; init; }
}
