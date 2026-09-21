namespace Ledger.Ingestion;

/// <summary>Convention d'écriture des montants dans un fichier source.</summary>
public enum AmountStyle
{
    /// <summary>Point décimal, pas de séparateur de milliers : 125340.55</summary>
    Invariant = 1,

    /// <summary>Virgule décimale, espaces de milliers : 125 340,55</summary>
    French = 2,
}
