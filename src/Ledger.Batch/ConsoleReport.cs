using System.Globalization;
using Ledger.Domain;
using Ledger.Persistence;

namespace Ledger.Batch;

/// <summary>
/// Restitution du run sur la sortie standard. Les chiffres sont relus
/// depuis la base et non gardés en mémoire : ce qui s'affiche est bien ce
/// qui a été écrit.
/// </summary>
public static class ConsoleReport
{
    public static async Task WriteAsync(
        ReportingRepository reporting,
        int batchRunId,
        DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reporting);

        var culture = CultureInfo.InvariantCulture;

        Console.WriteLine();
        Console.WriteLine($"  LEDGER — run {batchRunId} — date métier {businessDate:yyyy-MM-dd}");
        Console.WriteLine(new string('=', 78));

        Console.WriteLine();
        Console.WriteLine("  CONSOLIDATION PAR DESK");
        Console.WriteLine($"  {"Desk",-14} {"Dev",-4} {"Source",-7} {"Montant",18} {"Lignes",8}");
        Console.WriteLine($"  {new string('-', 53)}");

        foreach (var total in await reporting.GetDeskTotalsAsync(batchRunId, cancellationToken).ConfigureAwait(false))
        {
            Console.WriteLine(
                $"  {total.CanonicalDesk,-14} {total.Currency,-4} {total.SourceCode,-7} " +
                $"{total.Total.ToString("N2", culture),18} {total.EntryCount,8}");
        }

        Console.WriteLine();
        Console.WriteLine("  RÉCONCILIATION");
        Console.WriteLine($"  {"Statut",-18} {"Clés",6} {"Écart cumulé",16}");
        Console.WriteLine($"  {new string('-', 42)}");

        foreach (var row in await reporting.GetStatusSummaryAsync(batchRunId, cancellationToken).ConfigureAwait(false))
        {
            var statut = (ReconciliationStatus)row.Status;
            Console.WriteLine(
                $"  {statut,-18} {row.Count,6} {row.TotalDifference.ToString("N2", culture),16}");
        }

        var breaks = await reporting.GetBreaksAsync(batchRunId, cancellationToken).ConfigureAwait(false);

        if (breaks.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  À INVESTIGUER");
            Console.WriteLine($"  {"Desk",-14} {"Instrument",-14} {"Dev",-4} {"Statut",-10} {"Écart",12}   Détail");
            Console.WriteLine($"  {new string('-', 74)}");

            foreach (var row in breaks)
            {
                var statut = (ReconciliationStatus)row.Status;
                Console.WriteLine(
                    $"  {row.CanonicalDesk,-14} {row.InstrumentId,-14} {row.Currency,-4} {statut,-10} " +
                    $"{row.Difference.ToString("N2", culture),12}   {row.Detail}");
            }
        }

        var rejects = await reporting.GetRejectsAsync(batchRunId, cancellationToken).ConfigureAwait(false);

        if (rejects.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  LIGNES REJETÉES À L'INGESTION");
            Console.WriteLine($"  {new string('-', 74)}");

            foreach (var (sourceCode, lineNumber, reason) in rejects)
            {
                Console.WriteLine($"  {sourceCode,-4} ligne {lineNumber,-5} {reason}");
            }
        }

        Console.WriteLine();
    }
}
