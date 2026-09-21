namespace Ledger.Ingestion;

/// <summary>
/// Description d'un format de fichier CSV : son séparateur, le nom de
/// chacune de ses colonnes, et les conventions de date et de montant
/// qu'il emploie.
///
/// C'est la pièce qui rend l'ingestion générique. Une nouvelle source CSV
/// n'est pas une nouvelle classe à écrire, c'est une instance de plus.
/// </summary>
public sealed record CsvSourceFormat
{
    /// <summary>Code métier de la source alimentée par ce format.</summary>
    public required string SourceCode { get; init; }

    public required char Delimiter { get; init; }

    public required string DateColumn { get; init; }
    public required string DeskColumn { get; init; }
    public required string InstrumentColumn { get; init; }
    public required string CurrencyColumn { get; init; }
    public required string AmountColumn { get; init; }

    /// <summary>Format de date attendu, au sens de DateOnly.TryParseExact.</summary>
    public required string DateFormat { get; init; }

    public required AmountStyle AmountStyle { get; init; }
}
