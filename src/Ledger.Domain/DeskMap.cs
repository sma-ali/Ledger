namespace Ledger.Domain;

/// <summary>
/// Table de correspondance entre le libellé de desk d'une source et le
/// libellé canonique. C'est le coeur de l'homogénéisation : le front dit
/// « FXD-PARIS », la compta dit « PARIS_FX_DESK », et les deux désignent
/// le même desk. Sans cette table, aucun rapprochement n'est possible.
/// </summary>
public sealed class DeskMap
{
    private readonly Dictionary<string, string> _canonicalByExternal;

    public DeskMap(IReadOnlyDictionary<string, string> canonicalByExternal)
    {
        ArgumentNullException.ThrowIfNull(canonicalByExternal);

        _canonicalByExternal = new Dictionary<string, string>(
            canonicalByExternal,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Correspondance vide : tout desk sera inconnu.</summary>
    public static DeskMap Empty { get; } = new(new Dictionary<string, string>());

    /// <summary>
    /// Traduit un libellé de source en libellé canonique. Renvoie false si
    /// le desk n'est pas référencé : la ligne sera rejetée plutôt que
    /// rapprochée sous un mauvais desk.
    /// </summary>
    public bool TryResolve(string externalDeskCode, out string canonicalDesk)
    {
        ArgumentNullException.ThrowIfNull(externalDeskCode);

        return _canonicalByExternal.TryGetValue(externalDeskCode.Trim(), out canonicalDesk!);
    }
}
