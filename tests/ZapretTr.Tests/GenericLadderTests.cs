using ZapretTr.Core.Profiles;

namespace ZapretTr.Tests;

public sealed class GenericLadderTests
{
    private static readonly GenericLadder Ladder = ProfileStore.Load().Ladder;

    [Fact]
    public void Tcp443_Merdiveni_Bos_Degil()
    {
        var candidates = Ladder.Expand(StrategySection.Tcp443).ToList();
        Assert.NotEmpty(candidates);
        Assert.All(candidates, c => Assert.StartsWith("--dpi-desync=", c.Args, StringComparison.Ordinal));
    }

    [Fact]
    public void SayimVeAcilim_Ayni_Sonucu_Verir()
    {
        // CountFor ilerleme cubugu icin kullanilacak; Expand ile ayrisirsa
        // kullanici yuzde yuze varmadan biten ya da yuzde yuzu gecen bir cubuk gorur.
        foreach (var section in Enum.GetValues<StrategySection>())
        {
            Assert.Equal(Ladder.CountFor(section), Ladder.Expand(section).Count());
        }
    }

    [Fact]
    public void AdaylarBenzersiz()
    {
        // Ayni argumanin iki kez uretilmesi test suresini bosa uzatir.
        // Eksenlerde bos dizgi kullanildigi icin bu kolayca olabilir.
        foreach (var section in Enum.GetValues<StrategySection>())
        {
            var args = Ladder.Expand(section).Select(c => c.Args).ToList();
            var duplicates = args.GroupBy(a => a, StringComparer.Ordinal)
                                 .Where(g => g.Count() > 1)
                                 .Select(g => g.Key)
                                 .ToList();

            Assert.True(duplicates.Count == 0,
                $"{section.ToJsonName()}: tekrar eden aday(lar): {string.Join(" | ", duplicates)}");
        }
    }

    [Fact]
    public void IlkEksen_EnYavas_Degisir()
    {
        // Aile icinde ilk eksenin degerleri gruplanmis gelmeli. Kartezyen carpim
        // ters yonde uretilirse arama sirasi tasarlandigi gibi olmaz.
        var family = Ladder.Sections["tcp443"].First(f => f.Family == "fake-fooling");
        var firstAxisValues = family.Axes[0].Values;
        var secondAxisCount = family.Axes[1].Values.Count;

        var expanded = Ladder.Expand(StrategySection.Tcp443)
            .Where(c => c.Family == "fake-fooling")
            .Select(c => c.Args)
            .ToList();

        // Ilk eksenin ilk degeri, ilk N adayin hepsinde bulunmali (N = ikinci eksenin boyu).
        var firstValue = firstAxisValues[0];
        Assert.All(expanded.Take(secondAxisCount), a => Assert.Contains(firstValue, a, StringComparison.Ordinal));

        // Ve (N+1). adayda artik bulunmamali.
        Assert.DoesNotContain(firstValue, expanded[secondAxisCount], StringComparison.Ordinal);
    }

    [Fact]
    public void BosEksenDegeri_FazladanBosluk_Uretmez()
    {
        // Eksenlerde "" degeri "bu ekseni kullanma" demek. Naif birlestirme
        // "--dpi-desync=fake  --x" gibi cift bosluklu argumanlar uretirdi.
        foreach (var section in Enum.GetValues<StrategySection>())
        {
            Assert.All(Ladder.Expand(section), c =>
            {
                Assert.DoesNotContain("  ", c.Args, StringComparison.Ordinal);
                Assert.Equal(c.Args.Trim(), c.Args);
            });
        }
    }

    [Fact]
    public void Tcp443_SafBolme_Ailesi_Merdivende_Var()
    {
        // Tasarim gerekcesi: DPI sahte paketleri eliyorsa "fake" tabanli ailelerin
        // HEPSI birden coker. O senaryoda calisacak tek sey saf bolme.
        var pureSplit = Ladder.Expand(StrategySection.Tcp443)
            .Where(c => !c.Args.Contains("dpi-desync=fake", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(pureSplit);
    }

    [Fact]
    public void AramaUzayi_MakulBuyuklukte()
    {
        // Tier 3 son care; yine de bitmesi gereken bir is. Sirali testte ~2 sn/aday
        // varsayimiyla 300 aday ~10 dakika demek -- ustune cikarsa merdiven
        // budanmali, kullaniciyi belirsiz sure bekletmek cozum degil.
        var total = Enum.GetValues<StrategySection>().Sum(Ladder.CountFor);
        Assert.InRange(total, 50, 300);
    }
}
