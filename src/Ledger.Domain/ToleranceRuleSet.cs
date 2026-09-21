namespace Ledger.Domain;

/// <summary>
/// Tolérances admises sur un écart, par devise. En deçà, l'écart est accepté
/// et la clé est qualifiée WithinTolerance ; au-delà, c'est un Break.
///
/// Le moteur reçoit cet objet en paramètre et ignore totalement d'où il
/// vient : aujourd'hui appsettings.json, demain une table SQL, sans avoir à
/// modifier une ligne de la logique de réconciliation.
/// </summary>
public sealed class ToleranceRuleSet
{
    private readonly Dictionary<string, decimal> _byCurrency;

    /// <param name="defaultTolerance">Tolérance appliquée aux devises non listées.</param>
    /// <param name="byCurrency">Tolérances spécifiques, par code devise.</param>
    public ToleranceRuleSet(
        decimal defaultTolerance,
        IReadOnlyDictionary<string, decimal>? byCurrency = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(defaultTolerance);

        DefaultTolerance = defaultTolerance;

        // StringComparer.OrdinalIgnoreCase : « eur » et « EUR » désignent la
        // même devise. Ordinal et non Culture, car comparer des codes
        // techniques selon la culture du serveur est une source de bugs
        // connus (le fameux problème du « i » turc).
        _byCurrency = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        if (byCurrency is null)
        {
            return;
        }

        foreach (var (currency, tolerance) in byCurrency)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(tolerance, nameof(byCurrency));
            _byCurrency[currency] = tolerance;
        }
    }

    /// <summary>Tolérance appliquée aux devises sans règle spécifique.</summary>
    public decimal DefaultTolerance { get; }

    /// <summary>Jeu de règles n'admettant aucun écart : tout doit tomber juste.</summary>
    public static ToleranceRuleSet Exact { get; } = new(0m);

    /// <summary>Tolérance applicable à une devise donnée.</summary>
    public decimal For(string currency)
    {
        ArgumentNullException.ThrowIfNull(currency);

        return _byCurrency.TryGetValue(currency, out var tolerance)
            ? tolerance
            : DefaultTolerance;
    }
}
