namespace Ledger.Domain;

/// <summary>
/// Clé métier sur laquelle les montants sont rapprochés entre les sources.
/// Deux lignes venant de deux outils différents décrivent le même fait
/// si et seulement si elles portent la même clé.
/// </summary>
public readonly record struct BusinessKey(
    DateOnly BusinessDate,
    string Desk,
    string InstrumentId,
    string Currency);
