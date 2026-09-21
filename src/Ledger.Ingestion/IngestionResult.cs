using Ledger.Domain;

namespace Ledger.Ingestion;

/// <summary>
/// Ce que produit la lecture d'un fichier : les lignes acceptées et les
/// lignes refusées. Un fichier partiellement valide n'est pas une erreur,
/// c'est le quotidien - on ingère ce qui est bon et on trace le reste.
/// </summary>
public sealed record IngestionResult
{
    public required IReadOnlyList<PnlEntry> Entries { get; init; }

    public required IReadOnlyList<IngestionReject> Rejects { get; init; }
}
