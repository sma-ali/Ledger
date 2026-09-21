using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Ledger.Domain;

namespace Ledger.Ingestion;

/// <summary>
/// Lit un fichier CSV décrit par un <see cref="CsvSourceFormat"/> et le
/// convertit vers le modèle commun.
///
/// Une seule classe pour toutes les sources CSV : ce qui change d'un
/// fichier à l'autre est décrit par des données, pas par du code.
///
/// Aucune ligne ne fait échouer la lecture. Une ligne invalide devient un
/// rejet tracé, et le fichier continue d'être ingéré : un batch de nuit
/// qui s'arrête à la première anomalie ne produit rien du tout.
/// </summary>
public sealed class CsvPnlReader
{
    private readonly CsvSourceFormat _format;

    public CsvPnlReader(CsvSourceFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        _format = format;
    }

    /// <param name="textReader">Le contenu du fichier.</param>
    /// <param name="deskMap">Correspondance des desks pour cette source.</param>
    /// <param name="expectedBusinessDate">
    /// Date métier du run. Une ligne portant une autre date est rejetée
    /// plutôt qu'ingérée : c'est le symptôme d'un fichier livré en double
    /// ou décalé, et il vaut mieux le voir que le consolider en silence.
    /// </param>
    public IngestionResult Read(
        TextReader textReader,
        DeskMap deskMap,
        DateOnly expectedBusinessDate)
    {
        ArgumentNullException.ThrowIfNull(textReader);
        ArgumentNullException.ThrowIfNull(deskMap);

        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = _format.Delimiter.ToString(CultureInfo.InvariantCulture),
            HasHeaderRecord = true,
            TrimOptions = TrimOptions.Trim,

            // Par défaut CsvHelper lève une exception sur une colonne
            // absente ou une donnée malformée. On neutralise, pour traiter
            // ces cas nous-mêmes en rejets plutôt qu'en plantage.
            MissingFieldFound = null,
            BadDataFound = null,
        };

        var entries = new List<PnlEntry>();
        var rejects = new List<IngestionReject>();

        using var csv = new CsvReader(textReader, configuration);

        if (!csv.Read() || !csv.ReadHeader())
        {
            return new IngestionResult { Entries = entries, Rejects = rejects };
        }

        while (csv.Read())
        {
            var lineNumber = csv.Parser.Row;
            var rawLine = csv.Parser.RawRecord.Trim('\r', '\n');

            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            if (TryReadLine(csv, deskMap, expectedBusinessDate, lineNumber, out var entry, out var reason))
            {
                entries.Add(entry);
            }
            else
            {
                rejects.Add(new IngestionReject
                {
                    SourceCode = _format.SourceCode,
                    SourceLineNumber = lineNumber,
                    RawLine = rawLine,
                    Reason = reason,
                });
            }
        }

        return new IngestionResult { Entries = entries, Rejects = rejects };
    }

    /// <summary>
    /// Tente de convertir la ligne courante. Le motif de rejet est renvoyé
    /// plutôt que levé : un rejet est un résultat attendu, pas un incident.
    /// </summary>
    private bool TryReadLine(
        CsvReader csv,
        DeskMap deskMap,
        DateOnly expectedBusinessDate,
        int lineNumber,
        out PnlEntry entry,
        out string reason)
    {
        entry = null!;

        var rawDate = csv.GetField(_format.DateColumn);
        var rawDesk = csv.GetField(_format.DeskColumn);
        var rawInstrument = csv.GetField(_format.InstrumentColumn);
        var rawCurrency = csv.GetField(_format.CurrencyColumn);
        var rawAmount = csv.GetField(_format.AmountColumn);

        if (string.IsNullOrWhiteSpace(rawDate)
            || string.IsNullOrWhiteSpace(rawDesk)
            || string.IsNullOrWhiteSpace(rawInstrument)
            || string.IsNullOrWhiteSpace(rawCurrency)
            || string.IsNullOrWhiteSpace(rawAmount))
        {
            reason = "Champ obligatoire absent ou vide.";
            return false;
        }

        if (!DateOnly.TryParseExact(
                rawDate,
                _format.DateFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var businessDate))
        {
            reason = $"Date « {rawDate} » illisible, format attendu {_format.DateFormat}.";
            return false;
        }

        if (businessDate != expectedBusinessDate)
        {
            reason = $"Date {businessDate:yyyy-MM-dd} hors du run {expectedBusinessDate:yyyy-MM-dd}.";
            return false;
        }

        if (!deskMap.TryResolve(rawDesk, out var canonicalDesk))
        {
            reason = $"Desk « {rawDesk} » absent du référentiel de correspondance.";
            return false;
        }

        if (!TryParseAmount(rawAmount, _format.AmountStyle, out var amount))
        {
            reason = $"Montant « {rawAmount} » illisible.";
            return false;
        }

        var currency = rawCurrency.Trim().ToUpperInvariant();

        if (currency.Length != 3)
        {
            reason = $"Devise « {rawCurrency} » invalide, trois lettres attendues.";
            return false;
        }

        entry = new PnlEntry
        {
            SourceCode = _format.SourceCode,
            Key = new BusinessKey(
                businessDate,
                canonicalDesk,
                rawInstrument.Trim().ToUpperInvariant(),
                currency),
            Amount = amount,
            SourceLineNumber = lineNumber,
        };

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Convertit un montant selon la convention de la source.
    /// La cible est decimal : un double fabriquerait exactement les écarts
    /// que l'application est censée détecter.
    /// </summary>
    public static bool TryParseAmount(string raw, AmountStyle style, out decimal amount)
    {
        var normalized = style switch
        {
            // 125 340,55 -> 125340.55. On retire tous les caractères
            // d'espacement, y compris l'espace insécable et l'espace fine
            // insécable qu'Excel insère sans prévenir.
            AmountStyle.French => raw
                .Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace(',', '.'),

            _ => raw.Trim(),
        };

        // AllowDecimalPoint et AllowLeadingSign, et surtout PAS
        // AllowThousands : la virgule est le séparateur de milliers de la
        // culture invariante, donc « 12,34 » serait lu 1234 sans broncher.
        // À ce stade les séparateurs de milliers ont déjà été retirés, donc
        // il n'y a rien à autoriser - et tout à refuser.
        return decimal.TryParse(
            normalized,
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out amount);
    }
}
