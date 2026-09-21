using Dapper;
using Ledger.Domain;

namespace Ledger.Persistence;

/// <summary>
/// Écritures d'un run : ouverture et clôture, purge de rejeu, insertion
/// des lignes normalisées, des rejets et des résultats.
/// </summary>
public sealed class BatchRepository
{
    private readonly LedgerDatabase _database;

    public BatchRepository(LedgerDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    /// <summary>
    /// Efface tout ce qu'un run précédent a produit pour cette date métier.
    /// C'est ce qui rend le batch rejouable sans effet de bord : relancer
    /// la même date deux fois donne exactement le même contenu en base, et
    /// non des doublons. C'est le comportement qu'on attend d'un traitement
    /// relancé après incident par un ordonnanceur.
    ///
    /// Les suppressions descendent des tables filles vers les tables mères,
    /// sinon les clés étrangères s'y opposent.
    /// </summary>
    public async Task PurgeBusinessDateAsync(DateOnly businessDate, CancellationToken cancellationToken)
    {
        const string sql = """
            DELETE l
            FROM   dbo.ReconciliationLeg    AS l
            JOIN   dbo.ReconciliationResult AS r ON r.ReconciliationResultId = l.ReconciliationResultId
            JOIN   dbo.BatchRun             AS b ON b.BatchRunId = r.BatchRunId
            WHERE  b.BusinessDate = @businessDate;

            DELETE r
            FROM   dbo.ReconciliationResult AS r
            JOIN   dbo.BatchRun             AS b ON b.BatchRunId = r.BatchRunId
            WHERE  b.BusinessDate = @businessDate;

            DELETE e
            FROM   dbo.PnlEntry AS e
            JOIN   dbo.BatchRun AS b ON b.BatchRunId = e.BatchRunId
            WHERE  b.BusinessDate = @businessDate;

            DELETE j
            FROM   dbo.IngestionReject AS j
            JOIN   dbo.BatchRun        AS b ON b.BatchRunId = j.BatchRunId
            WHERE  b.BusinessDate = @businessDate;

            DELETE FROM dbo.BatchRun
            WHERE  BusinessDate = @businessDate;
            """;

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { businessDate = ToDateTime(businessDate) },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>Ouvre un run et renvoie son identifiant.</summary>
    public async Task<int> StartRunAsync(DateOnly businessDate, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT dbo.BatchRun (BusinessDate, Status)
            OUTPUT INSERTED.BatchRunId
            VALUES (@businessDate, 'Running');
            """;

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);

        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            sql,
            new { businessDate = ToDateTime(businessDate) },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>Clôt un run avec son statut final.</summary>
    public async Task CompleteRunAsync(int batchRunId, string status, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE dbo.BatchRun
            SET    Status = @status,
                   FinishedAt = SYSUTCDATETIME()
            WHERE  BatchRunId = @batchRunId;
            """;

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { batchRunId, status },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// Insère les lignes normalisées. Dapper exécute l'ordre une fois par
    /// élément de la liste ; suffisant aux volumes du projet. Au-delà de
    /// quelques dizaines de milliers de lignes, SqlBulkCopy serait le bon
    /// outil, et c'est le premier endroit à changer.
    /// </summary>
    public async Task InsertEntriesAsync(
        int batchRunId,
        IReadOnlyDictionary<string, int> sourceIds,
        IReadOnlyCollection<PnlEntry> entries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceIds);
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0)
        {
            return;
        }

        const string sql = """
            INSERT dbo.PnlEntry
                (BatchRunId, SourceId, BusinessDate, CanonicalDesk,
                 InstrumentId, Currency, Amount, SourceLineNumber)
            VALUES
                (@BatchRunId, @SourceId, @BusinessDate, @CanonicalDesk,
                 @InstrumentId, @Currency, @Amount, @SourceLineNumber);
            """;

        var rows = entries.Select(entry => new
        {
            BatchRunId = batchRunId,
            SourceId = sourceIds[entry.SourceCode],
            BusinessDate = ToDateTime(entry.Key.BusinessDate),
            CanonicalDesk = entry.Key.Desk,
            InstrumentId = entry.Key.InstrumentId,
            Currency = entry.Key.Currency,
            entry.Amount,
            entry.SourceLineNumber,
        }).ToList();

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            sql, rows, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>Insère les lignes refusées à l'ingestion.</summary>
    public async Task InsertRejectsAsync(
        int batchRunId,
        IReadOnlyDictionary<string, int> sourceIds,
        IReadOnlyCollection<IngestionReject> rejects,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceIds);
        ArgumentNullException.ThrowIfNull(rejects);

        if (rejects.Count == 0)
        {
            return;
        }

        const string sql = """
            INSERT dbo.IngestionReject
                (BatchRunId, SourceId, SourceLineNumber, RawLine, Reason)
            VALUES
                (@BatchRunId, @SourceId, @SourceLineNumber, @RawLine, @Reason);
            """;

        var rows = rejects.Select(reject => new
        {
            BatchRunId = batchRunId,
            SourceId = sourceIds[reject.SourceCode],
            reject.SourceLineNumber,
            reject.RawLine,
            reject.Reason,
        }).ToList();

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            sql, rows, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// Insère les résultats et leurs legs. Chaque résultat renvoie son
    /// identifiant généré via OUTPUT, qui sert ensuite de clé étrangère aux
    /// legs. Le tout dans une transaction : un run interrompu ne laisse pas
    /// des résultats sans leur détail.
    /// </summary>
    public async Task InsertResultsAsync(
        int batchRunId,
        IReadOnlyDictionary<string, int> sourceIds,
        IReadOnlyCollection<ReconciliationResult> results,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceIds);
        ArgumentNullException.ThrowIfNull(results);

        if (results.Count == 0)
        {
            return;
        }

        const string insertResult = """
            INSERT dbo.ReconciliationResult
                (BatchRunId, BusinessDate, CanonicalDesk, InstrumentId, Currency, Status, Difference)
            OUTPUT INSERTED.ReconciliationResultId
            VALUES
                (@batchRunId, @businessDate, @canonicalDesk, @instrumentId, @currency, @status, @difference);
            """;

        const string insertLeg = """
            INSERT dbo.ReconciliationLeg
                (ReconciliationResultId, SourceId, Amount, EntryCount)
            VALUES
                (@ReconciliationResultId, @SourceId, @Amount, @EntryCount);
            """;

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var result in results)
        {
            var resultId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                insertResult,
                new
                {
                    batchRunId,
                    businessDate = ToDateTime(result.Key.BusinessDate),
                    canonicalDesk = result.Key.Desk,
                    instrumentId = result.Key.InstrumentId,
                    currency = result.Key.Currency,
                    status = (byte)result.Status,
                    difference = result.Difference,
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            var legs = result.Legs.Select(leg => new
            {
                ReconciliationResultId = resultId,
                SourceId = sourceIds[leg.SourceCode],
                leg.Amount,
                leg.EntryCount,
            }).ToList();

            await connection.ExecuteAsync(new CommandDefinition(
                insertLeg, legs, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Le pilote SQL Server ne prend pas DateOnly en paramètre : on
    /// convertit à la frontière, et le domaine garde le bon type.
    /// </summary>
    private static DateTime ToDateTime(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);
}
