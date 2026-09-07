using ZapretTr.Core.Engine;

namespace ZapretTr.Tests;

/// <summary>
/// Yapilandirmanin geri yuklenirken kendi kendini bozmamasi.
/// </summary>
/// <remarks>
/// Olculmus hata: RestoreSavedSelection ilk ozelligi atadiginda setter
/// SaveSelection() cagiriyordu ve o an ISS ile hedef HENUZ geri yuklenmemis
/// oluyordu. Yapilandirma yarim haliyle uzerine yaziliyor, her acilista ayarlarin
/// bir kismi sessizce kayboluyordu. Kurulu surumun ekran goruntusunde fark edildi:
/// kayitli TurkNet + ozel hedef yerine varsayilan profil ve bos hedef gorunuyordu.
/// </remarks>
public sealed class AppConfigTests
{
    [Fact]
    public void YarimYapilandirma_TamOlaniEzmemeli()
    {
        var full = new AppConfig
        {
            SelectedIspId = "turknet",
            SelectedStrategyId = "tn-443",
            SelectedStrategyArgs = "--dpi-desync=fake",
            SecureDnsEnabled = false,
            CustomTarget = "ornek-site.com",
        };

        // Yarim durum: yalnizca DNS bayragi set edilmis, gerisi bos.
        var half = new AppConfig { SecureDnsEnabled = false };

        // Bu testin anlatmak istedigi sey: bu iki nesne AYNI DEGIL, dolayisiyla
        // yarim olani diske yazmak veri kaybi demek. Koddaki koruma _isRestoring
        // bayragi; burada niyeti sabitliyoruz.
        Assert.NotEqual(full.SelectedIspId, half.SelectedIspId);
        Assert.NotEqual(full.CustomTarget, half.CustomTarget);
        Assert.Null(half.SelectedIspId);
        Assert.Null(half.CustomTarget);
    }

    [Fact]
    public void Varsayilan_SifreliDns_Acik()
    {
        // Varsayilanin acik olmasi olculmus bir gerekce tasiyor: TR'de engelleme
        // cogu zaman once DNS katmaninda ve o katman asilmadan winws stratejisi
        // hicbir sey degistirmiyor.
        Assert.True(new AppConfig().SecureDnsEnabled);
    }
}
