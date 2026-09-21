using System.Reflection;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Ledger.Persistence;

/// <summary>
/// Point d'entrée vers la base : ouvre les connexions et sait créer le
/// schéma. Tout le reste du projet passe par ici plutôt que de fabriquer
/// ses propres connexions.
/// </summary>
public sealed class LedgerDatabase
{
    private readonly string _connectionString;

    public LedgerDatabase(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <summary>
    /// Ouvre une connexion. L'appelant la libère avec « using », ce qui la
    /// rend au pool de connexions plutôt que de la fermer réellement.
    /// </summary>
    public async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    /// <summary>
    /// Crée la base si elle n'existe pas, applique le schéma puis les
    /// données de référence. Les deux scripts sont idempotents, donc cette
    /// méthode peut tourner à chaque démarrage sans rien casser.
    /// </summary>
    public async Task EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder(_connectionString);
        var databaseName = builder.InitialCatalog;
        builder.InitialCatalog = "master";

        await using (var master = new SqlConnection(builder.ConnectionString))
        {
            await master.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Le nom de base ne peut pas être un paramètre dans un CREATE
            // DATABASE. QUOTENAME l'échappe côté serveur, ce qui empêche
            // l'injection tout en gardant un nom configurable.
            await master.ExecuteAsync(new CommandDefinition(
                """
                IF DB_ID(@databaseName) IS NULL
                BEGIN
                    DECLARE @sql NVARCHAR(300) = N'CREATE DATABASE ' + QUOTENAME(@databaseName);
                    EXEC sp_executesql @sql;
                END
                """,
                new { databaseName },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            ReadScript("Schema.sql"),
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            ReadScript("Seed.sql"),
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>Lit un script SQL embarqué dans la DLL.</summary>
    private static string ReadScript(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"Ledger.Persistence.Scripts.{fileName}";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Script embarqué introuvable : {resourceName}.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
