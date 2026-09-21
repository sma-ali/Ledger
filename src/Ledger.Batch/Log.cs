using Microsoft.Extensions.Logging;

namespace Ledger.Batch;

/// <summary>
/// Messages de journalisation générés à la compilation.
///
/// L'attribut LoggerMessage fait écrire au générateur de source une
/// implémentation qui évite le boxing des arguments et ne construit le
/// message que si le niveau est effectivement actif. Les appels directs à
/// LogInformation paient ce coût à chaque ligne, même quand la trace est
/// désactivée - ce qui compte sur un batch qui en écrit des milliers.
///
/// Effet de bord appréciable : tous les messages du batch sont réunis ici
/// plutôt qu'éparpillés dans le code.
/// </summary>
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Run de la date métier {businessDate}")]
    public static partial void RunStarting(ILogger logger, string businessDate);

    [LoggerMessage(Level = LogLevel.Information, Message = "Run {batchRunId} ouvert")]
    public static partial void RunOpened(ILogger logger, int batchRunId);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Source {sourceCode} : {accepted} ligne(s) acceptée(s), {rejected} rejetée(s)")]
    public static partial void SourceIngested(ILogger logger, string sourceCode, int accepted, int rejected);

    [LoggerMessage(Level = LogLevel.Error, Message = "Fichier introuvable pour la source {sourceCode} : {path}")]
    public static partial void SourceFileMissing(ILogger logger, string sourceCode, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "{count} clé(s) métier rapprochée(s)")]
    public static partial void KeysReconciled(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Le run {batchRunId} a échoué")]
    public static partial void RunFailed(ILogger logger, int batchRunId, Exception exception);
}
