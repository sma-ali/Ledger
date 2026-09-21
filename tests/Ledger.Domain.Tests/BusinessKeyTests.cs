namespace Ledger.Domain.Tests;

/// <summary>
/// Le moteur de réconciliation regroupera les lignes par clé. Toute cette
/// mécanique repose sur l'égalité par valeur de BusinessKey : ces tests
/// vérifient l'hypothèse avant qu'on construise dessus.
/// </summary>
public class BusinessKeyTests
{
    private static BusinessKey Cle(
        string desk = "PARIS-FX",
        string instrument = "FR0000120271",
        string devise = "EUR")
        => new(new DateOnly(2026, 9, 18), desk, instrument, devise);

    [Fact]
    public void Deux_cles_de_memes_valeurs_sont_egales()
    {
        Assert.Equal(Cle(), Cle());
    }

    [Fact]
    public void Deux_cles_de_memes_valeurs_ont_le_meme_hash()
    {
        // Sans cette propriété, deux clés égales tomberaient dans deux
        // compartiments différents d'un dictionnaire et le regroupement
        // du moteur produirait des écarts fantômes.
        Assert.Equal(Cle().GetHashCode(), Cle().GetHashCode());
    }

    [Fact]
    public void Une_cle_sert_de_cle_de_dictionnaire()
    {
        var totaux = new Dictionary<BusinessKey, decimal>
        {
            [Cle()] = 100m,
        };

        totaux[Cle()] += 25m;

        Assert.Single(totaux);
        Assert.Equal(125m, totaux[Cle()]);
    }

    [Theory]
    [InlineData("LONDON-FX", "FR0000120271", "EUR")]
    [InlineData("PARIS-FX", "FR0000121014", "EUR")]
    [InlineData("PARIS-FX", "FR0000120271", "USD")]
    public void Une_difference_sur_un_seul_champ_rend_les_cles_distinctes(
        string desk, string instrument, string devise)
    {
        Assert.NotEqual(Cle(), Cle(desk, instrument, devise));
    }

    [Fact]
    public void La_devise_fait_partie_de_la_cle()
    {
        // 100 EUR et 100 USD ne se rapprochent pas. La devise est dans la
        // clé, pas à côté : on ne peut pas l'oublier par accident.
        var euro = Cle(devise: "EUR");
        var dollar = Cle(devise: "USD");

        Assert.NotEqual(euro, dollar);
    }
}
