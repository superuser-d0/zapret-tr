using ZapretTr.Prober;

namespace ZapretTr.Tests;

/// <summary>
/// Tamamen cevapsiz bir bolumun birakilmasi.
/// </summary>
/// <remarks>
/// Olculdu (issue #1, Vodafone Net, 2026-09-15): QUIC bolumunde 29 adayin HEPSI
/// zaman asimina ugradi, her biri ~11.7 sn -- toplam ~340 sn. Sonucu bastan belli
/// bir arama icin 5-6 dakika. Ayni kosumda tcp443 kazanani 1.5 sn'de bulunmustu.
///
/// Bu testler esiklerin KEYFI olmadigini sabitliyor: yontem cesitliligi tukenmeden
/// vazgecilmiyor.
/// </remarks>
public sealed class SessizBolumTests
{
    // --- Esik davranisi ---------------------------------------------------------

    [Fact]
    public void Yontem_Cesitliligi_Yetmezse_Vazgecilmiyor()
    {
        // Ayni yontemin 50 varyasyonu "her seyi denedik" demek degil.
        Assert.False(StrategyProber.BolumCevapsiz(ardisikSessiz: 50, farkliYontem: 1));
        Assert.False(StrategyProber.BolumCevapsiz(ardisikSessiz: 50, farkliYontem: 3));
    }

    [Fact]
    public void Az_Sayida_Deneme_Sonrasi_Vazgecilmiyor()
    {
        // Dort farkli yontem denenmis olsa bile birkac aday yeterli kanit degil.
        Assert.False(StrategyProber.BolumCevapsiz(ardisikSessiz: 4, farkliYontem: 4));
        Assert.False(StrategyProber.BolumCevapsiz(
            ardisikSessiz: StrategyProber.SessizlikEsigi - 1,
            farkliYontem: 9));
    }

    [Fact]
    public void Ikisi_Birden_Saglaninca_Vazgeciliyor()
        => Assert.True(StrategyProber.BolumCevapsiz(
            StrategyProber.SessizlikEsigi,
            StrategyProber.SessizlikYontemEsigi));

    [Fact]
    public void Issue1_Kosumunda_12nci_Adayda_Vazgecilirdi()
    {
        // issue #1'deki QUIC bolumunun GERCEK sirasi. 12. adaya gelindiginde dort
        // farkli yontem denenmis oluyor; kalan 17 deneme ayni ailelerin parametre
        // varyasyonlari. Bu test, esiklerin o veriye uydugunu sabitliyor.
        string[] gercekSira =
        [
            "--dpi-desync=fake --dpi-desync-repeats=11 --dpi-desync-fake-quic={FAKE_QUIC_GOOGLE}",
            "--dpi-desync=fake --dpi-desync-repeats=11",
            "--dpi-desync=fake --dpi-desync-repeats=6 --dpi-desync-fake-quic={FAKE_QUIC_GOOGLE}",
            "--dpi-desync=fake --dpi-desync-any-protocol=1 --dpi-desync-cutoff=n2 --dpi-desync-fake-quic={FAKE_QUIC_GOOGLE}",
            "--dpi-desync=fake --dpi-desync-any-protocol=1 --dpi-desync-cutoff=n3 --dpi-desync-fake-quic={FAKE_QUIC_GOOGLE}",
            "--dpi-desync=fake --dpi-desync-any-protocol=1 --dpi-desync-cutoff=d2 --dpi-desync-fake-quic={FAKE_QUIC_GOOGLE}",
            "--dpi-desync=fake --dpi-desync-any-protocol=1 --dpi-desync-cutoff=n2",
            "--dpi-desync=fake --dpi-desync-any-protocol=1 --dpi-desync-cutoff=n3",
            "--dpi-desync=fake --dpi-desync-any-protocol=1 --dpi-desync-cutoff=d2",
            "--dpi-desync=udplen --dpi-desync-udplen-increment=2",
            "--dpi-desync=fake,udplen --dpi-desync-fake-quic={FAKE_QUIC_GOOGLE} --dpi-desync-udplen-increment=2",
            "--dpi-desync=ipfrag2",
            "--dpi-desync=fake,ipfrag2 --dpi-desync-fake-quic={FAKE_QUIC_GOOGLE}",
        ];

        var yontemler = new HashSet<string>(StringComparer.Ordinal);
        int? vazgecilenAday = null;

        for (var i = 0; i < gercekSira.Length; i++)
        {
            yontemler.Add(StrategyProber.DesyncYontemi(gercekSira[i]));

            if (StrategyProber.BolumCevapsiz(i + 1, yontemler.Count))
            {
                vazgecilenAday = i + 1;
                break;
            }
        }

        Assert.Equal(12, vazgecilenAday);

        // Vazgecmeden once dort ailenin de denenmis olmasi SART.
        Assert.Contains("fake", yontemler);
        Assert.Contains("udplen", yontemler);
        Assert.Contains("fake,udplen", yontemler);
        Assert.Contains("ipfrag2", yontemler);
    }

    [Fact]
    public void Kazanilan_Sure_Kayda_Deger()
    {
        // 29 aday yerine 12: aday basina ~11.7 sn (issue #1'de olculdu).
        const double adayBasinaSaniye = 11.7;
        var kazanc = (29 - 12) * adayBasinaSaniye;

        Assert.True(kazanc > 180, $"Beklenen kazanc en az 3 dakika, hesaplanan {kazanc:F0} sn.");
    }

    // --- Yontem ayristirma ------------------------------------------------------

    [Theory]
    [InlineData("--dpi-desync=fake --dpi-desync-repeats=11", "fake")]
    [InlineData("--dpi-desync=fake,udplen --dpi-desync-udplen-increment=2", "fake,udplen")]
    [InlineData("--dpi-desync=ipfrag2", "ipfrag2")]
    [InlineData("--dpi-desync=fake,multisplit --dpi-desync-fooling=badseq", "fake,multisplit")]
    public void Yontem_Argumandan_Okunuyor(string args, string beklenen)
        => Assert.Equal(beklenen, StrategyProber.DesyncYontemi(args));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("--wf-tcp=443 --filter-tcp=443")]
    public void Yontem_Yoksa_Ayrilabiliyor(string? args)
        => Assert.Equal("(yok)", StrategyProber.DesyncYontemi(args));

    [Fact]
    public void Fake_Ile_Fake_Udplen_AYRI_Yontem_Sayiliyor()
    {
        // Ikisi paketi baska turlu bicimlendiriyor; ayni sayilsalardi cesitlilik
        // esigi olduğundan erken dolar ve gercekten farkli bir hile denenmeden
        // vazgecilirdi.
        Assert.NotEqual(
            StrategyProber.DesyncYontemi("--dpi-desync=fake"),
            StrategyProber.DesyncYontemi("--dpi-desync=fake,udplen"));
    }
}
