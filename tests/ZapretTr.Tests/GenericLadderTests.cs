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
        // CountFor ilerleme çubuğu için kullanılacak; Expand ile ayrışırsa
        // kullanıcı yüzde yüze varmadan biten ya da yüzde yüzü geçen bir çubuk görür.
        foreach (var section in Enum.GetValues<StrategySection>())
        {
            Assert.Equal(Ladder.CountFor(section), Ladder.Expand(section).Count());
        }
    }

    [Fact]
    public void AdaylarBenzersiz()
    {
        // Aynı argümanın iki kez üretilmesi test süresini boşa uzatır.
        // Eksenlerde boş dizgi kullanıldığı için bu kolayca olabilir.
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
        // Aile içinde ilk eksenin değerleri gruplanmış gelmeli. Kartezyen çarpım
        // ters yönde üretilirse arama sırası tasarlandığı gibi olmaz.
        var family = Ladder.Sections["tcp443"].First(f => f.Family == "fake-fooling");
        var firstAxisValues = family.Axes[0].Values;
        var secondAxisCount = family.Axes[1].Values.Count;

        var expanded = Ladder.Expand(StrategySection.Tcp443)
            .Where(c => c.Family == "fake-fooling")
            .Select(c => c.Args)
            .ToList();

        // İlk eksenin ilk değeri, ilk N adayın hepsinde bulunmalı (N = ikinci eksenin boyu).
        var firstValue = firstAxisValues[0];
        Assert.All(expanded.Take(secondAxisCount), a => Assert.Contains(firstValue, a, StringComparison.Ordinal));

        // Ve (N+1). adayda artık bulunmamalı.
        Assert.DoesNotContain(firstValue, expanded[secondAxisCount], StringComparison.Ordinal);
    }

    [Fact]
    public void BosEksenDegeri_FazladanBosluk_Uretmez()
    {
        // Eksenlerde "" değeri "bu ekseni kullanma" demek. Naif birleştirme
        // "--dpi-desync=fake  --x" gibi çift boşluklu argümanlar üretirdi.
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
        // Tasarım gerekçesi: DPI sahte paketleri eliyorsa "fake" tabanlı ailelerin
        // HEPSİ birden çöker. O senaryoda çalışacak tek şey saf bölme.
        var pureSplit = Ladder.Expand(StrategySection.Tcp443)
            .Where(c => !c.Args.Contains("dpi-desync=fake", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(pureSplit);
    }

    [Fact]
    public void AramaUzayi_MakulBuyuklukte()
    {
        // Tier 3 son çare; yine de bitmesi gereken bir iş. Sıralı testte ~2 sn/aday
        // varsayımıyla 300 aday ~10 dakika demek; üstüne çıkarsa merdiven
        // budanmalı, kullanıcıyı belirsiz süre bekletmek çözüm değil.
        var total = Enum.GetValues<StrategySection>().Sum(Ladder.CountFor);
        Assert.InRange(total, 50, 300);
    }

    [Fact]
    public void GenelArama_Aileleri_Sirayla_Dolasiyor()
    {
        // Bütçe sınırlı olduğu için SIRA sonucu belirliyor. Gerçek bir kullanıcıda
        // ölçüldü: TTNET hattında 176 aday denendi, 1105 sn sürdü, hiçbiri tutmadı.
        // Sebep, genel aramanın aileleri peş peşe tüketmesiydi: tcp443'te ilk aile
        // (fake-fooling) 45 varyant ve genel aramaya kalan ~33'lük bütçenin tamamını
        // yiyordu; multisplit, multidisorder, fakedsplit, tls-mod ve syndata
        // aileleri HİÇ denenmiyordu.
        //
        // Bu test o davranışın geri gelmesini engelliyor: ilk turda her aileden
        // BİRER aday gelmeli.
        var expanded = Ladder.Expand(StrategySection.Tcp443).ToList();
        var familyCount = expanded.Select(c => c.Family).Distinct(StringComparer.Ordinal).Count();
        Assert.True(familyCount > 1, "Test anlamli olmasi icin birden fazla aile gerekiyor.");

        var ordered = StrategyProber.InterleaveFamilies(expanded);

        // İlk N aday, N ailenin her birinden tam olarak birer tane olmalı.
        var firstRound = ordered
            .Take(familyCount)
            .Select(c => c.Id.Split('#')[0])
            .ToList();

        Assert.Equal(familyCount, firstRound.Distinct(StringComparer.Ordinal).Count());

        // Hiçbir aday kaybolmamalı: sıralama değişti, içerik değişmedi.
        Assert.Equal(
            expanded.Select(c => c.Args).Distinct(StringComparer.Ordinal).Count(),
            ordered.Select(c => c.Args).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void GenelArama_Aile_Icinde_Sirayi_Koruyor()
    {
        // Aileler arasında dolaşıyoruz ama aile İÇİNDEKİ sıra profil yazarının
        // kararı ve değişmemeli.
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
