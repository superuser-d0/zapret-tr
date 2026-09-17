using ZapretTr.Prober;

namespace ZapretTr.Tests;

/// <summary>
/// Tamamen cevapsız bir bölümün bırakılması.
/// </summary>
/// <remarks>
/// Ölçüldü (issue #1, Vodafone Net, 2026-09-15): QUIC bölümünde 25 adayın HEPSİ
/// zaman aşımına uğradı, her biri ~11.7 sn; toplam 293 sn. Sonucu baştan belli
/// bir arama için ~5 dakika. (Önce "29 aday" yazılmıştı: 29, o koşumdaki TOPLAM
/// deneme sayısıydı.) Aynı koşumda tcp443 kazananı 1.5 sn'de bulunmuştu.
///
/// Bu testler eşiklerin KEYFÎ olmadığını sabitliyor: yöntem çeşitliliği tükenmeden
/// vazgeçilmiyor.
/// </remarks>
public sealed class SessizBolumTests
{
    // --- Eşik davranışı ---------------------------------------------------------

    [Fact]
    public void Yontem_Cesitliligi_Yetmezse_Vazgecilmiyor()
    {
        // Aynı yöntemin 50 varyasyonu "her şeyi denedik" demek değil.
        Assert.False(StrategyProber.BolumCevapsiz(ardisikSessiz: 50, farkliYontem: 1));
        Assert.False(StrategyProber.BolumCevapsiz(ardisikSessiz: 50, farkliYontem: 3));
    }

    [Fact]
    public void Az_Sayida_Deneme_Sonrasi_Vazgecilmiyor()
    {
        // Dört farklı yöntem denenmiş olsa bile birkaç aday yeterli kanıt değil.
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
        // issue #1'deki QUIC bölümünün GERÇEK sırası. 12. adaya gelindiğinde dört
        // farklı yöntem denenmiş oluyor; kalan 17 deneme aynı ailelerin parametre
        // varyasyonları. Bu test, eşiklerin o veriye uyduğunu sabitliyor.
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

        // Vazgeçmeden önce dört ailenin de denenmiş olması ŞART.
        Assert.Contains("fake", yontemler);
        Assert.Contains("udplen", yontemler);
        Assert.Contains("fake,udplen", yontemler);
        Assert.Contains("ipfrag2", yontemler);
    }

    [Fact]
    public void Kazanilan_Sure_Kayda_Deger()
    {
        // Aynı hatta iki GERÇEK koşumun rapor dosyalarından (issue #1): QUIC bölümü
        // 0.2.2'de 25 aday / 293.1 sn, 0.2.3'te 12 aday / 141.0 sn.
        //
        // Bu test önce hesapla yazılıydı: "(29 - 12) * 11.7 > 180". 29 yanlıştı; o
        // koşumdaki TOPLAM deneme sayısıydı, QUIC 25'ti. Test yanlış veriyle
        // geçiyordu. Ölçülen değerlerle kazanç 3 dakika değil, ~2.5 dakika.
        const double onceki = 293.1;
        const double sonraki = 141.0;
        var kazanc = onceki - sonraki;

        Assert.True(kazanc > 120, $"Beklenen kazanc en az 2 dakika, olculen {kazanc:F0} sn.");
    }

    // --- Yöntem ayrıştırma ------------------------------------------------------

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
        // İkisi paketi başka türlü biçimlendiriyor; aynı sayılsalardı çeşitlilik
        // eşiği olduğundan erken dolar ve gerçekten farklı bir hile denenmeden
        // vazgeçilirdi.
        Assert.NotEqual(
            StrategyProber.DesyncYontemi("--dpi-desync=fake"),
            StrategyProber.DesyncYontemi("--dpi-desync=fake,udplen"));
    }
}
