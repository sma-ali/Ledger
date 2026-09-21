namespace Ledger.Domain;

/// <summary>
/// Qualification d'une clé métier après rapprochement.
/// Les valeurs sont explicites car elles sont stockées en base : elles ne
/// doivent pas changer si quelqu'un réordonne les membres de l'énumération.
/// </summary>
public enum ReconciliationStatus
{
    /// <summary>Les montants des sources sont strictement égaux.</summary>
    Matched = 1,

    /// <summary>Les montants diffèrent, mais l'écart reste dans la tolérance admise.</summary>
    WithinTolerance = 2,

    /// <summary>L'écart dépasse la tolérance : à investiguer.</summary>
    Break = 3,

    /// <summary>La clé n'est présente que dans une partie des sources attendues.</summary>
    Missing = 4,
}
