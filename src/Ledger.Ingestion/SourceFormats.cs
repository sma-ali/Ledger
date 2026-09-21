namespace Ledger.Ingestion;

/// <summary>
/// Les formats des deux sources du MVP. Les voir côte à côte montre
/// précisément le problème que l'application résout : mêmes données,
/// cinq conventions différentes.
/// </summary>
public static class SourceFormats
{
    /// <summary>
    /// Export du front-office, format anglo-saxon.
    /// TradeDate,DeskCode,Instrument,Ccy,PnL
    /// 2026-09-18,FXD-PARIS,FR0000120271,EUR,125340.55
    /// </summary>
    public static CsvSourceFormat FrontOffice { get; } = new()
    {
        SourceCode = "FO",
        Delimiter = ',',
        DateColumn = "TradeDate",
        DeskColumn = "DeskCode",
        InstrumentColumn = "Instrument",
        CurrencyColumn = "Ccy",
        AmountColumn = "PnL",
        DateFormat = "yyyy-MM-dd",
        AmountStyle = AmountStyle.Invariant,
    };

    /// <summary>
    /// Export de la comptabilité, format français.
    /// DATE_COMPTA;PORTEFEUILLE;CODE_ISIN;DEVISE;MONTANT
    /// 18/09/2026;PARIS_FX_DESK;FR0000120271;EUR;125 340,55
    /// </summary>
    public static CsvSourceFormat BackOffice { get; } = new()
    {
        SourceCode = "BO",
        Delimiter = ';',
        DateColumn = "DATE_COMPTA",
        DeskColumn = "PORTEFEUILLE",
        InstrumentColumn = "CODE_ISIN",
        CurrencyColumn = "DEVISE",
        AmountColumn = "MONTANT",
        DateFormat = "dd/MM/yyyy",
        AmountStyle = AmountStyle.French,
    };
}
