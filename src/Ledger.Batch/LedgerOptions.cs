namespace Ledger.Batch;

/// <summary>
/// Configuration du batch, liée depuis appsettings.json et surchargeable
/// par variables d'environnement.
///
/// Les propriétés ont un accesseur set, contrairement au reste du projet
/// qui utilise init : le lieur de configuration de .NET construit l'objet
/// vide puis remplit les propriétés une à une.
/// </summary>
public sealed class LedgerOptions
{
    public const string SectionName = "Ledger";

    /// <summary>Répertoire contenant les fichiers sources.</summary>
    public string DataDirectory { get; set; } = "data";

    /// <summary>
    /// Date métier traitée par défaut, au format aaaa-MM-jj. L'argument
    /// --date de la ligne de commande la remplace.
    /// </summary>
    public string? BusinessDate { get; set; }

    public IList<SourceFileOptions> Sources { get; set; } = [];

    public ToleranceOptions Tolerance { get; set; } = new();
}

/// <summary>Où trouver le fichier d'une source donnée.</summary>
public sealed class SourceFileOptions
{
    /// <summary>Code métier de la source : FO, BO.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Motif de nom de fichier, où {0} est la date métier.
    /// Exemple : fo_{0:yyyyMMdd}.csv
    /// </summary>
    public string FilePattern { get; set; } = string.Empty;
}

/// <summary>
/// Seuils d'écart acceptés. Ils vivent dans la configuration et non dans
/// le code : les changer ne demande pas de recompiler.
/// </summary>
public sealed class ToleranceOptions
{
    public decimal Default { get; set; }

    public Dictionary<string, decimal> ByCurrency { get; set; } = [];
}
