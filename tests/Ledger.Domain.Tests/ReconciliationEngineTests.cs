namespace Ledger.Domain.Tests;

/// <summary>
/// Spécification exécutable du moteur de réconciliation. Chaque test décrit
/// une situation métier réelle ; le nom du test est la règle.
/// </summary>
public class ReconciliationEngineTests
{
    private static readonly DateOnly Jour = new(2026, 9, 18);
    private static readonly string[] DeuxSources = ["FO", "BO"];

    private readonly ReconciliationEngine _moteur = new();

    /// <summary>Fabrique une ligne normalisée, avec des valeurs par défaut raisonnables.</summary>
    private static PnlEntry Ligne(
        string source,
        decimal montant,
        string desk = "PARIS-FX",
        string instrument = "FR0000120271",
        string devise = "EUR",
        int numeroLigne = 1)
        => new()
        {
            SourceCode = source,
            Key = new BusinessKey(Jour, desk, instrument, devise),
            Amount = montant,
            SourceLineNumber = numeroLigne,
        };

    [Fact]
    public void Deux_sources_d_accord_donnent_un_rapprochement()
    {
        var lignes = new[] { Ligne("FO", 125_340.55m), Ligne("BO", 125_340.55m) };

        var resultats = _moteur.Reconcile(lignes, DeuxSources, ToleranceRuleSet.Exact);

        var resultat = Assert.Single(resultats);
        Assert.Equal(ReconciliationStatus.Matched, resultat.Status);
        Assert.Equal(0m, resultat.Difference);
    }

    [Fact]
    public void Un_ecart_sous_la_tolerance_est_accepte()
    {
        var lignes = new[] { Ligne("FO", 1_000.00m), Ligne("BO", 1_000.40m) };
        var tolerances = new ToleranceRuleSet(defaultTolerance: 0.50m);

        var resultats = _moteur.Reconcile(lignes, DeuxSources, tolerances);

        var resultat = Assert.Single(resultats);
        Assert.Equal(ReconciliationStatus.WithinTolerance, resultat.Status);
        Assert.Equal(0.40m, resultat.Difference);
    }

    [Fact]
    public void Un_ecart_egal_a_la_tolerance_est_accepte()
    {
        // Choix assumé : la borne est incluse. Un écart pile à la tolérance
        // passe. L'inverse se défendrait aussi, mais il faut trancher une
        // fois et le tester, pas le découvrir en production.
        var lignes = new[] { Ligne("FO", 1_000.00m), Ligne("BO", 1_000.50m) };
        var tolerances = new ToleranceRuleSet(defaultTolerance: 0.50m);

        var resultats = _moteur.Reconcile(lignes, DeuxSources, tolerances);

        Assert.Equal(ReconciliationStatus.WithinTolerance, Assert.Single(resultats).Status);
    }

    [Fact]
    public void Un_ecart_au_dessus_de_la_tolerance_est_un_break()
    {
        var lignes = new[] { Ligne("FO", 1_000.00m), Ligne("BO", 1_002.00m) };
        var tolerances = new ToleranceRuleSet(defaultTolerance: 0.50m);

        var resultats = _moteur.Reconcile(lignes, DeuxSources, tolerances);

        var resultat = Assert.Single(resultats);
        Assert.Equal(ReconciliationStatus.Break, resultat.Status);
        Assert.Equal(2.00m, resultat.Difference);
    }

    [Fact]
    public void L_ecart_est_toujours_positif_quel_que_soit_le_sens()
    {
        // La source qui annonce le plus n'est pas toujours la même.
        var lignes = new[] { Ligne("FO", 1_002.00m), Ligne("BO", 1_000.00m) };

        var resultats = _moteur.Reconcile(lignes, DeuxSources, ToleranceRuleSet.Exact);

        Assert.Equal(2.00m, Assert.Single(resultats).Difference);
    }

    [Fact]
    public void Une_cle_absente_d_une_source_est_signalee_manquante()
    {
        var lignes = new[] { Ligne("FO", 1_000.00m) };

        var resultats = _moteur.Reconcile(lignes, DeuxSources, ToleranceRuleSet.Exact);

        var resultat = Assert.Single(resultats);
        Assert.Equal(ReconciliationStatus.Missing, resultat.Status);
        Assert.Equal(0m, resultat.Difference);
        Assert.Equal("FO", Assert.Single(resultat.Legs).SourceCode);
    }

    [Fact]
    public void Les_lignes_multiples_d_une_meme_source_sont_agregees()
    {
        // Le front peut éclater un montant en plusieurs lignes là où la
        // compta n'en produit qu'une. Ce n'est pas un écart.
        var lignes = new[]
        {
            Ligne("FO", 600.00m, numeroLigne: 1),
            Ligne("FO", 400.00m, numeroLigne: 2),
            Ligne("BO", 1_000.00m, numeroLigne: 1),
        };

        var resultats = _moteur.Reconcile(lignes, DeuxSources, ToleranceRuleSet.Exact);

        var resultat = Assert.Single(resultats);
        Assert.Equal(ReconciliationStatus.Matched, resultat.Status);

        var legFront = Assert.Single(resultat.Legs, l => l.SourceCode == "FO");
        Assert.Equal(1_000.00m, legFront.Amount);
        Assert.Equal(2, legFront.EntryCount);

        var legCompta = Assert.Single(resultat.Legs, l => l.SourceCode == "BO");
        Assert.Equal(1_000.00m, legCompta.Amount);
        Assert.Equal(1, legCompta.EntryCount);
    }

    [Fact]
    public void La_tolerance_depend_de_la_devise()
    {
        var tolerances = new ToleranceRuleSet(
            defaultTolerance: 0.01m,
            byCurrency: new Dictionary<string, decimal> { ["JPY"] = 5m });

        var lignes = new[]
        {
            Ligne("FO", 1_000m, devise: "JPY"),
            Ligne("BO", 1_003m, devise: "JPY"),
            Ligne("FO", 1_000m, instrument: "FR0000121014"),
            Ligne("BO", 1_003m, instrument: "FR0000121014"),
        };

        var resultats = _moteur.Reconcile(lignes, DeuxSources, tolerances);

        var yen = Assert.Single(resultats, r => r.Key.Currency == "JPY");
        Assert.Equal(ReconciliationStatus.WithinTolerance, yen.Status);

        var euro = Assert.Single(resultats, r => r.Key.Currency == "EUR");
        Assert.Equal(ReconciliationStatus.Break, euro.Status);
    }

    [Fact]
    public void Des_cles_differentes_donnent_des_resultats_distincts()
    {
        var lignes = new[]
        {
            Ligne("FO", 100m, desk: "PARIS-FX"),
            Ligne("BO", 100m, desk: "PARIS-FX"),
            Ligne("FO", 200m, desk: "LONDON-FX"),
            Ligne("BO", 250m, desk: "LONDON-FX"),
        };

        var resultats = _moteur.Reconcile(lignes, DeuxSources, ToleranceRuleSet.Exact);

        Assert.Equal(2, resultats.Count);
        Assert.Equal(
            ReconciliationStatus.Matched,
            Assert.Single(resultats, r => r.Key.Desk == "PARIS-FX").Status);
        Assert.Equal(
            ReconciliationStatus.Break,
            Assert.Single(resultats, r => r.Key.Desk == "LONDON-FX").Status);
    }

    [Fact]
    public void Aucune_ligne_ne_produit_aucun_resultat()
    {
        var resultats = _moteur.Reconcile([], DeuxSources, ToleranceRuleSet.Exact);

        Assert.Empty(resultats);
    }

    [Fact]
    public void Chaque_source_presente_a_son_leg()
    {
        var lignes = new[] { Ligne("FO", 100m), Ligne("BO", 100m) };

        var resultats = _moteur.Reconcile(lignes, DeuxSources, ToleranceRuleSet.Exact);

        var legs = Assert.Single(resultats).Legs;
        Assert.Equal(2, legs.Count);
        Assert.Contains(legs, l => l.SourceCode == "FO");
        Assert.Contains(legs, l => l.SourceCode == "BO");
    }
}
