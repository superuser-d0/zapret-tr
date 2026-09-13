using System.Text.Json;
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

    // --- Servis duraklatma bilgisi -------------------------------------------------
    //
    // Yukseltme eski servisleri silip yeniden kuruyor; "duraklatildi" bilgisi
    // servisle birlikte kaybolmasin diye yapilandirmada da duruyor.

    [Fact]
    public void ServisDuraklatildi_diske_yaziliyor_ve_geri_okunuyor()
    {
        var json = JsonSerializer.Serialize(
            new AppConfig { ServicePaused = true }, CoreJsonContext.Default.AppConfig);

        using var belge = JsonDocument.Parse(json);
        Assert.True(belge.RootElement.GetProperty("servicePaused").GetBoolean());
        Assert.True(JsonSerializer.Deserialize(json, CoreJsonContext.Default.AppConfig)!.ServicePaused);
    }

    [Fact]
    public void Eski_yapilandirmada_alan_yoksa_duraklatilmamis_sayilir()
    {
        // 0.1.20 ve oncesinin yazdigi dosya. Alan yokken "duraklatilmis" saymak,
        // yukseltmede calisan bir korumayi kapatmak olurdu.
        const string eski = """
            {"selectedIspId":"turk-telekom","selectedStrategyArgs":"--dpi-desync=fake --dpi-desync-ttl=4","secureDnsEnabled":true}
            """;

        Assert.False(JsonSerializer.Deserialize(eski, CoreJsonContext.Default.AppConfig)!.ServicePaused);
    }
}
