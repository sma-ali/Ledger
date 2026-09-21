using Dapper;

namespace Ledger.Persistence;

/// <summary>Une ligne du récapitulatif par statut.</summary>
public sealed record StatusSummaryRow(byte Status, int Count, decimal TotalDifference);

/// <summary>Un écart à investiguer, avec le détail de ce qu'annonce chaque source.</summary>
public sealed record BreakRow(
    string CanonicalDesk,
    string InstrumentId,
    string Currency,
    byte Status,
    decimal Difference,
    string Detail);

/// <summary>Un total consolidé par desk, devise et source.</summary>
public sealed record DeskTotalRow(
    string CanonicalDesk,
    string Currency,
    string SourceCode,
    decimal Total,
    int EntryCount);

/// <summary>
/// Requêtes de consultation. Elles sont écrites à la main, lisibles telles
/// quelles, et c'est exactement ce qu'on attend d'un accès en Dapper.
/// </summary>
public sealed class ReportingRepository
{
    private readonly LedgerDatabase _database;

    public ReportingRepository(LedgerDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    /// <summary>Combien de clés dans chaque statut, et l'écart cumulé.</summary>
    public async Task<IReadOnlyList<StatusSummaryRow>> GetStatusSummaryAsync(
        int batchRunId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT   Status,
                     COUNT(*)        AS Count,
                     SUM(Difference) AS TotalDifference
            FROM     dbo.ReconciliationResult
            WHERE    BatchRunId = @batchRunId
            GROUP BY Status
            ORDER BY Status;
            """;

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);

        var rows = await connection.QueryAsync<StatusSummaryRow>(new CommandDefinition(
            sql, new { batchRunId }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.ToList();
    }

    /// <summary>
    /// Les clés à investiguer : écarts au-delà de la tolérance et clés
    /// manquantes. STRING_AGG recompose en une colonne ce que chaque source
    /// annonce, ce qui évite de renvoyer une ligne par leg.
    /// </summary>
    public async Task<IReadOnlyList<BreakRow>> GetBreaksAsync(
        int batchRunId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT   r.CanonicalDesk,
                     r.InstrumentId,
                     r.Currency,
                     r.Status,
                     r.Difference,
                     STRING_AGG(CONCAT(s.Code, ' ', CAST(l.Amount AS VARCHAR(30))), '  |  ')
                         WITHIN GROUP (ORDER BY s.Code) AS Detail
            FROM     dbo.ReconciliationResult AS r
            JOIN     dbo.ReconciliationLeg    AS l ON l.ReconciliationResultId = r.ReconciliationResultId
            JOIN     dbo.Source               AS s ON s.SourceId = l.SourceId
            WHERE    r.BatchRunId = @batchRunId
              AND    r.Status IN (3, 4)
            GROUP BY r.CanonicalDesk, r.InstrumentId, r.Currency, r.Status, r.Difference
            ORDER BY r.Status, r.Difference DESC;
            """;

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);

        var rows = await connection.QueryAsync<BreakRow>(new CommandDefinition(
            sql, new { batchRunId }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.ToList();
    }

    /// <summary>
    /// La consolidation : montants agrégés par desk, devise et source.
    /// C'est la requête que consulte un responsable de desk.
    /// </summary>
    public async Task<IReadOnlyList<DeskTotalRow>> GetDeskTotalsAsync(
        int batchRunId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT   e.CanonicalDesk,
                     e.Currency,
                     s.Code        AS SourceCode,
                     SUM(e.Amount) AS Total,
                     COUNT(*)      AS EntryCount
            FROM     dbo.PnlEntry AS e
            JOIN     dbo.Source   AS s ON s.SourceId = e.SourceId
            WHERE    e.BatchRunId = @batchRunId
            GROUP BY e.CanonicalDesk, e.Currency, s.Code
            ORDER BY e.CanonicalDesk, e.Currency, s.Code;
            """;

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);

        var rows = await connection.QueryAsync<DeskTotalRow>(new CommandDefinition(
            sql, new { batchRunId }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.ToList();
    }

    /// <summary>Les lignes refusées à l'ingestion, pour le support.</summary>
    public async Task<IReadOnlyList<(string SourceCode, int LineNumber, string Reason)>> GetRejectsAsync(
        int batchRunId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT   s.Code AS SourceCode,
                     j.SourceLineNumber AS LineNumber,
                     j.Reason
            FROM     dbo.IngestionReject AS j
            JOIN     dbo.Source          AS s ON s.SourceId = j.SourceId
            WHERE    j.BatchRunId = @batchRunId
            ORDER BY s.Code, j.SourceLineNumber;
            """;

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);

        var rows = await connection.QueryAsync<(string SourceCode, int LineNumber, string Reason)>(
            new CommandDefinition(sql, new { batchRunId }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return rows.ToList();
    }
}
