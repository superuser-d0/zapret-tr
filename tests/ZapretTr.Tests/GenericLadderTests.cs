using ZapretTr.Core.Profiles;
using ZapretTr.Prober;

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

    [Fact]
    public void GenelArama_Aileleri_Sirayla_Dolasiyor()
    {
        // Butce sinirli oldugu icin SIRA sonucu belirliyor. Gercek bir kullanicida
        // olculdu: TTNET hattinda 176 aday denendi, 1105 sn surdu, hicbiri tutmadi.
        // Sebep, genel aramanin aileleri pes pese tuketmesiydi -- tcp443'te ilk aile
        // (fake-fooling) 45 varyant ve genel aramaya kalan ~33 butcenin tamamini
        // yiyordu; multisplit, multidisorder, fakedsplit, tls-mod ve syndata
        // aileleri HIC denenmiyordu.
        //
        // Bu test o davranisin geri gelmesini engelliyor: ilk turda her aileden
        // BIRER aday gelmeli.
        var expanded = Ladder.Expand(StrategySection.Tcp443).ToList();
        var familyCount = expanded.Select(c => c.Family).Distinct(StringComparer.Ordinal).Count();
        Assert.True(familyCount > 1, "Test anlamli olmasi icin birden fazla aile gerekiyor.");

        var ordered = StrategyProber.InterleaveFamilies(expanded);

        // Ilk N aday, N ailenin her birinden tam olarak birer tane olmali.
        var firstRound = ordered
            .Take(familyCount)
            .Select(c => c.Id.Split('#')[0])
            .ToList();

        Assert.Equal(familyCount, firstRound.Distinct(StringComparer.Ordinal).Count());

        // Hicbir aday kaybolmamali: siralama degisti, icerik degismedi.
        Assert.Equal(
            expanded.Select(c => c.Args).Distinct(StringComparer.Ordinal).Count(),
            ordered.Select(c => c.Args).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void GenelArama_Aile_Icinde_Sirayi_Koruyor()
    {
        // Aileler arasinda dolasiyoruz ama aile ICINDEKI sira profil yazarinin
        // karari ve degismemeli.
        var expanded = Ladder.Expand(StrategySection.Quic).ToList();
        var ordered = StrategyProber.InterleaveFamilies(expanded);

        foreach (var family in expanded.Select(c => c.Family).Distinct(StringComparer.Ordinal))
        {
            var beklenen = expanded.Where(c => c.Family == family).Select(c => c.Args).ToList();
            var gelen = ordered
                .Where(c => c.Id.StartsWith($"ladder/{family}#", StringComparison.Ordinal))
                .Select(c => c.Args)
                .ToList();

            Assert.Equal(beklenen, gelen);
        }
    }
}
