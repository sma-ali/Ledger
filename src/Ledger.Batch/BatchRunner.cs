using System.Globalization;
using Ledger.Domain;
using Ledger.Ingestion;
using Ledger.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ledger.Batch;

/// <summary>
/// Orchestration d'un run complet : préparation, ingestion, réconciliation,
/// restitution. Chaque étape est journalisée, parce qu'un batch qui tourne
/// la nuit ne se débogue qu'avec ses traces.
/// </summary>
public sealed class BatchRunner
{
    private readonly LedgerDatabase _database;
    private readonly ReferenceDataRepository _reference;
    private readonly BatchRepository _batch;
    private readonly ReportingRepository _reporting;
    private readonly ReconciliationEngine _engine;
    private readonly LedgerOptions _options;
    private readonly ILogger<BatchRunner> _logger;

    /// <summary>
    /// Toutes les dépendances arrivent par le constructeur : c'est
    /// l'injection de dépendances. La classe déclare ce dont elle a besoin,
    /// le conteneur le fournit, et un test peut substituer n'importe laquelle.
    /// </summary>
    public BatchRunner(
        LedgerDatabase database,
        ReferenceDataRepository reference,
        BatchRepository batch,
        ReportingRepository reporting,
        ReconciliationEngine engine,
        IOptions<LedgerOptions> options,
        ILogger<BatchRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _database = database;
        _reference = reference;
        _batch = batch;
        _reporting = reporting;
        _engine = engine;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Exécute le run. Renvoie le code de sortie du processus : zéro si le
    /// traitement s'est déroulé correctement, un s'il a échoué.
    ///
    /// Des écarts détectés ne sont pas un échec : c'est le résultat attendu
    /// du traitement. Un ordonnanceur ne doit alerter l'exploitation que si
    /// le batch n'a pas pu faire son travail.
    /// </summary>
    public async Task<int> RunAsync(DateOnly businessDate, CancellationToken cancellationToken)
    {
        // La date est formatée dans une variable locale : passer
        // directement l'appel à ToString en argument ferait payer le
        // formatage même quand la trace est désactivée.
        var dateIso = businessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Log.RunStarting(_logger, dateIso);

        await _database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        // Rejouabilité : on efface ce qu'un run précédent a produit pour
        // cette date avant de recommencer. Relancer donne le même contenu,
        // pas des doublons.
        await _batch.PurgeBusinessDateAsync(businessDate, cancellationToken).ConfigureAwait(false);

        var batchRunId = await _batch.StartRunAsync(businessDate, cancellationToken).ConfigureAwait(false);
        Log.RunOpened(_logger, batchRunId);

        try
        {
            var sourceIds = await _reference.GetSourceIdsAsync(cancellationToken).ConfigureAwait(false);

            var entries = new List<PnlEntry>();
            var rejects = new List<IngestionReject>();

            foreach (var source in _options.Sources)
            {
                var format = ResolveFormat(source.Code);
                var path = ResolvePath(source, businessDate);

                if (!File.Exists(path))
                {
                    Log.SourceFileMissing(_logger, source.Code, path);
                    await _batch.CompleteRunAsync(batchRunId, "Failed", cancellationToken).ConfigureAwait(false);
                    return 1;
                }

                var deskMap = await _reference.GetDeskMapAsync(source.Code, cancellationToken).ConfigureAwait(false);

                using var reader = new StreamReader(path);
                var result = new CsvPnlReader(format).Read(reader, deskMap, businessDate);

                entries.AddRange(result.Entries);
                rejects.AddRange(result.Rejects);

                Log.SourceIngested(_logger, source.Code, result.Entries.Count, result.Rejects.Count);
            }

            await _batch.InsertEntriesAsync(batchRunId, sourceIds, entries, cancellationToken).ConfigureAwait(false);
            await _batch.InsertRejectsAsync(batchRunId, sourceIds, rejects, cancellationToken).ConfigureAwait(false);

            var expectedSources = _options.Sources.Select(source => source.Code).ToList();
            var tolerances = new ToleranceRuleSet(_options.Tolerance.Default, _options.Tolerance.ByCurrency);

            var results = _engine.Reconcile(entries, expectedSources, tolerances);
            Log.KeysReconciled(_logger, results.Count);

            await _batch.InsertResultsAsync(batchRunId, sourceIds, results, cancellationToken).ConfigureAwait(false);
            await _batch.CompleteRunAsync(batchRunId, "Completed", cancellationToken).ConfigureAwait(false);

            await ConsoleReport.WriteAsync(_reporting, batchRunId, businessDate, cancellationToken)
                .ConfigureAwait(false);

            return 0;
        }
        catch (Exception exception)
        {
            // On marque le run en échec avant de laisser remonter : sans
            // ça, la base garderait un run « Running » éternel, et personne
            // ne saurait dire si le traitement a eu lieu.
            Log.RunFailed(_logger, batchRunId, exception);
            await _batch.CompleteRunAsync(batchRunId, "Failed", CancellationToken.None).ConfigureAwait(false);
            return 1;
        }
    }

    private static CsvSourceFormat ResolveFormat(string sourceCode) => sourceCode switch
    {
        "FO" => SourceFormats.FrontOffice,
        "BO" => SourceFormats.BackOffice,
        _ => throw new InvalidOperationException($"Aucun format connu pour la source « {sourceCode} »."),
    };

    private string ResolvePath(SourceFileOptions source, DateOnly businessDate)
    {
        var fileName = string.Format(
            CultureInfo.InvariantCulture,
            source.FilePattern,
            businessDate.ToDateTime(TimeOnly.MinValue));

        return Path.Combine(_options.DataDirectory, fileName);
    }
}
