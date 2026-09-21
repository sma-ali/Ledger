using Ledger.Domain;

namespace Ledger.Ingestion.Tests;

/// <summary>
/// L'ingestion est l'endroit où les formats hétérogènes deviennent un
/// modèle unique. Ces tests vérifient qu'un même fait, écrit selon deux
/// conventions différentes, produit bien la même donnée.
/// </summary>
public class CsvPnlReaderTests
{
    private static readonly DateOnly Jour = new(2026, 9, 18);

    private static DeskMap DesksFrontOffice => new(new Dictionary<string, string>
    {
        ["FXD-PARIS"] = "PARIS-FX",
        ["RATES-PARIS"] = "PARIS-RATES",
    });

    private static DeskMap DesksBackOffice => new(new Dictionary<string, string>
    {
        ["PARIS_FX_DESK"] = "PARIS-FX",
        ["PARIS_RATES_DESK"] = "PARIS-RATES",
    });

    private static IngestionResult LireFrontOffice(string contenu)
        => new CsvPnlReader(SourceFormats.FrontOffice)
            .Read(new StringReader(contenu), DesksFrontOffice, Jour);

    private static IngestionResult LireBackOffice(string contenu)
        => new CsvPnlReader(SourceFormats.BackOffice)
            .Read(new StringReader(contenu), DesksBackOffice, Jour);

    [Fact]
    public void Le_format_front_office_est_lu()
    {
        var resultat = LireFrontOffice(
            """
            TradeDate,DeskCode,Instrument,Ccy,PnL
            2026-09-18,FXD-PARIS,FR0000120271,EUR,125340.55
            """);

        Assert.Empty(resultat.Rejects);
        var ligne = Assert.Single(resultat.Entries);
        Assert.Equal("FO", ligne.SourceCode);
        Assert.Equal(125_340.55m, ligne.Amount);
        Assert.Equal(new BusinessKey(Jour, "PARIS-FX", "FR0000120271", "EUR"), ligne.Key);
    }

    [Fact]
    public void Le_format_comptable_francais_est_lu()
    {
        // Date jj/mm/aaaa, séparateur point-virgule, espace de milliers,
        // virgule décimale : quatre conventions différentes du front.
        var resultat = LireBackOffice(
            """
            DATE_COMPTA;PORTEFEUILLE;CODE_ISIN;DEVISE;MONTANT
            18/09/2026;PARIS_FX_DESK;FR0000120271;EUR;125 340,55
            """);

        Assert.Empty(resultat.Rejects);
        var ligne = Assert.Single(resultat.Entries);
        Assert.Equal("BO", ligne.SourceCode);
        Assert.Equal(125_340.55m, ligne.Amount);
    }

    [Fact]
    public void Les_deux_formats_produisent_la_meme_cle_metier()
    {
        // Le test qui justifie tout le projet : deux outils, deux
        // conventions, un seul fait. Si les clés divergent, rien ne se
        // rapproche jamais.
        var front = LireFrontOffice(
            """
            TradeDate,DeskCode,Instrument,Ccy,PnL
            2026-09-18,FXD-PARIS,FR0000120271,EUR,125340.55
            """);

        var compta = LireBackOffice(
            """
            DATE_COMPTA;PORTEFEUILLE;CODE_ISIN;DEVISE;MONTANT
            18/09/2026;PARIS_FX_DESK;FR0000120271;EUR;125 340,55
            """);

        Assert.Equal(
            Assert.Single(front.Entries).Key,
            Assert.Single(compta.Entries).Key);
    }

    [Fact]
    public void Un_desk_absent_du_referentiel_est_rejete()
    {
        var resultat = LireFrontOffice(
            """
            TradeDate,DeskCode,Instrument,Ccy,PnL
            2026-09-18,DESK-INCONNU,FR0000120271,EUR,100.00
            """);

        Assert.Empty(resultat.Entries);
        var rejet = Assert.Single(resultat.Rejects);
        Assert.Equal(2, rejet.SourceLineNumber);
        Assert.Contains("DESK-INCONNU", rejet.Reason, StringComparison.Ordinal);
        Assert.Contains("DESK-INCONNU", rejet.RawLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_montant_illisible_est_rejete()
    {
        var resultat = LireFrontOffice(
            """
            TradeDate,DeskCode,Instrument,Ccy,PnL
            2026-09-18,FXD-PARIS,FR0000120271,EUR,n/a
            """);

        Assert.Empty(resultat.Entries);
        Assert.Contains("Montant", Assert.Single(resultat.Rejects).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Une_ligne_hors_de_la_date_du_run_est_rejetee()
    {
        // Symptôme classique d'un fichier livré en double ou décalé d'un
        // jour. Mieux vaut le voir que le consolider en silence.
        var resultat = LireFrontOffice(
            """
            TradeDate,DeskCode,Instrument,Ccy,PnL
            2026-09-17,FXD-PARIS,FR0000120271,EUR,100.00
            """);

        Assert.Empty(resultat.Entries);
        Assert.Contains("hors du run", Assert.Single(resultat.Rejects).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Une_ligne_valide_survit_a_une_ligne_rejetee()
    {
        // Un batch de nuit qui s'arrête à la première anomalie ne produit
        // rien du tout. On ingère ce qui est bon et on trace le reste.
        var resultat = LireFrontOffice(
            """
            TradeDate,DeskCode,Instrument,Ccy,PnL
            2026-09-18,DESK-INCONNU,FR0000120271,EUR,100.00
            2026-09-18,FXD-PARIS,FR0000120271,EUR,200.00
            """);

        Assert.Single(resultat.Rejects);
        Assert.Equal(200.00m, Assert.Single(resultat.Entries).Amount);
    }

    [Fact]
    public void Un_fichier_sans_ligne_de_donnees_ne_produit_rien()
    {
        var resultat = LireFrontOffice("TradeDate,DeskCode,Instrument,Ccy,PnL");

        Assert.Empty(resultat.Entries);
        Assert.Empty(resultat.Rejects);
    }

    [Theory]
    [InlineData("125340.55", AmountStyle.Invariant, "125340.55")]
    [InlineData("-48210.10", AmountStyle.Invariant, "-48210.10")]
    [InlineData("125 340,55", AmountStyle.French, "125340.55")]
    [InlineData("-48 210,40", AmountStyle.French, "-48210.40")]
    [InlineData("250,00", AmountStyle.French, "250.00")]
    public void Les_montants_sont_convertis_selon_la_convention_de_la_source(
        string brut, AmountStyle style, string attendu)
    {
        Assert.True(CsvPnlReader.TryParseAmount(brut, style, out var montant));
        Assert.Equal(decimal.Parse(attendu, System.Globalization.CultureInfo.InvariantCulture), montant);
    }

    [Theory]
    [InlineData("", AmountStyle.Invariant)]
    [InlineData("n/a", AmountStyle.Invariant)]
    [InlineData("12,34", AmountStyle.Invariant)]
    public void Un_montant_hors_convention_est_refuse(string brut, AmountStyle style)
    {
        Assert.False(CsvPnlReader.TryParseAmount(brut, style, out _));
    }
}
