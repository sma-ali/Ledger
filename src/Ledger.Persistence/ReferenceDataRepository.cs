using Dapper;
using Ledger.Domain;

namespace Ledger.Persistence;

/// <summary>
/// Lecture des données de référence : les sources et la correspondance
/// des desks. Elles changent rarement et sont chargées une fois par run.
/// </summary>
public sealed class ReferenceDataRepository
{
    private readonly LedgerDatabase _database;

    public ReferenceDataRepository(LedgerDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    /// <summary>
    /// Associe chaque code métier de source à son identifiant technique.
    /// Le domaine ne connaît que les codes ; la base ne stocke que les
    /// identifiants. La traduction se fait ici, et nulle part ailleurs.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, int>> GetSourceIdsAsync(
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT Code, SourceId
            FROM   dbo.Source;
            """;

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);

        var rows = await connection.QueryAsync<(string Code, int SourceId)>(
            new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.ToDictionary(
            row => row.Code,
            row => row.SourceId,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Correspondance des desks pour une source donnée.</summary>
    public async Task<DeskMap> GetDeskMapAsync(string sourceCode, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT   m.ExternalDeskCode, m.CanonicalDesk
            FROM     dbo.DeskMapping AS m
            JOIN     dbo.Source      AS s ON s.SourceId = m.SourceId
            WHERE    s.Code = @sourceCode
            ORDER BY m.ExternalDeskCode;
            """;

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);

        var rows = await connection.QueryAsync<(string ExternalDeskCode, string CanonicalDesk)>(
            new CommandDefinition(sql, new { sourceCode }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return new DeskMap(rows.ToDictionary(
            row => row.ExternalDeskCode,
            row => row.CanonicalDesk,
            StringComparer.OrdinalIgnoreCase));
    }
}
