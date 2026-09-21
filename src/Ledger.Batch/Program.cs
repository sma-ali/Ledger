using System.Globalization;
using Ledger.Batch;
using Ledger.Domain;
using Ledger.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

// Hôte générique .NET : il assemble la configuration (appsettings.json puis
// variables d'environnement), la journalisation et le conteneur d'injection
// de dépendances. C'est le socle standard d'une application .NET moderne.
var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<LedgerOptions>(
    builder.Configuration.GetSection(LedgerOptions.SectionName));

var connectionString = builder.Configuration.GetConnectionString("Ledger")
    ?? throw new InvalidOperationException("Chaîne de connexion « Ledger » absente de la configuration.");

// AddSingleton : une seule instance pour toute la durée du processus. Un
// batch fait une passe et s'arrête, il n'y a rien à cloisonner.
builder.Services.AddSingleton(new LedgerDatabase(connectionString));
builder.Services.AddSingleton<ReferenceDataRepository>();
builder.Services.AddSingleton<BatchRepository>();
builder.Services.AddSingleton<ReportingRepository>();
builder.Services.AddSingleton<ReconciliationEngine>();
builder.Services.AddSingleton<BatchRunner>();

using var host = builder.Build();

var options = host.Services.GetRequiredService<IOptions<LedgerOptions>>().Value;
var businessDate = ResolveBusinessDate(args, options);

var runner = host.Services.GetRequiredService<BatchRunner>();

// Le code de sortie du processus est ce que lit l'ordonnanceur : zéro s'il
// faut passer à la tâche suivante, non nul s'il faut alerter.
return await runner.RunAsync(businessDate, CancellationToken.None).ConfigureAwait(false);

// Fonction locale : --date de la ligne de commande, sinon la configuration,
// sinon la veille.
static DateOnly ResolveBusinessDate(string[] args, LedgerOptions options)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i] is "--date" or "-d")
        {
            return ParseOrThrow(args[i + 1]);
        }
    }

    return string.IsNullOrWhiteSpace(options.BusinessDate)
        ? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1))
        : ParseOrThrow(options.BusinessDate);

    static DateOnly ParseOrThrow(string value)
        => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new ArgumentException($"Date « {value} » invalide, format attendu aaaa-MM-jj.");
}
