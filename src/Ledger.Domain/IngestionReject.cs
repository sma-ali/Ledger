namespace Ledger.Domain;

/// <summary>
/// Une ligne source refusée, conservée telle quelle avec son motif. C'est la
/// traçabilité qu'on réclame en support : quand un montant manque, il faut
/// pouvoir répondre « ta ligne 412 a été rejetée, voici pourquoi ».
/// </summary>
public sealed record IngestionReject
{
    public required string SourceCode { get; init; }

    public required int SourceLineNumber { get; init; }

    /// <summary>La ligne brute, non interprétée.</summary>
    public required string RawLine { get; init; }

    /// <summary>Motif lisible par un humain, pas une trace d'exception.</summary>
    public required string Reason { get; init; }
}
