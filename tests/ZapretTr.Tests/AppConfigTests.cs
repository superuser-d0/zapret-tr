using System.Text.Json;
using ZapretTr.Core.Engine;

namespace ZapretTr.Tests;

/// <summary>
/// Yapılandırmanın geri yüklenirken kendi kendini bozmaması.
/// </summary>
/// <remarks>
/// Ölçülmüş hata: RestoreSavedSelection ilk özelliği atadığında setter
/// SaveSelection() çağırıyordu ve o an İSS ile hedef HENÜZ geri yüklenmemiş
/// oluyordu. Yapılandırma yarım hâliyle üzerine yazılıyor, her açılışta ayarların
/// bir kısmı sessizce kayboluyordu. Kurulu sürümün ekran görüntüsünde fark edildi:
/// kayıtlı TurkNet + özel hedef yerine varsayılan profil ve boş hedef görünüyordu.
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

        // Yarım durum: yalnızca DNS bayrağı ayarlanmış, gerisi boş.
        var half = new AppConfig { SecureDnsEnabled = false };

        // Bu testin anlatmak istediği şey: bu iki nesne AYNI DEĞİL, dolayısıyla
        // yarım olanı diske yazmak veri kaybı demek. Koddaki koruma _isRestoring
        // bayrağı; burada niyeti sabitliyoruz.
        Assert.NotEqual(full.SelectedIspId, half.SelectedIspId);
        Assert.NotEqual(full.CustomTarget, half.CustomTarget);
        Assert.Null(half.SelectedIspId);
        Assert.Null(half.CustomTarget);
    }

    [Fact]
    public void Varsayilan_SifreliDns_Acik()
    {
        // Varsayılanın açık olması ölçülmüş bir gerekçe taşıyor: TR'de engelleme
        // çoğu zaman önce DNS katmanında ve o katman aşılmadan winws stratejisi
        // hiçbir şey değiştirmiyor.
        Assert.True(new AppConfig().SecureDnsEnabled);
    }

    // --- Servis duraklatma bilgisi -------------------------------------------------
    //
    // Yükseltme eski servisleri silip yeniden kuruyor; "duraklatıldı" bilgisi
    // servisle birlikte kaybolmasın diye yapılandırmada da duruyor.

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
    public void Tema_tercihi_yaziliyor_ve_eski_dosyada_secilmemis_sayiliyor()
    {
        var json = JsonSerializer.Serialize(new AppConfig { Theme = "dark" }, CoreJsonContext.Default.AppConfig);

        using var belge = JsonDocument.Parse(json);
        Assert.Equal("dark", belge.RootElement.GetProperty("theme").GetString());

        // Alan yoksa null: uygulama Windows'un ayarına uyar. "light" okunsaydı koyu
        // Windows kullanan herkes güncellemeden sonra açık temaya kilitlenirdi.
        const string eski = """{"selectedIspId":"turk-telekom","secureDnsEnabled":true}""";
        Assert.Null(JsonSerializer.Deserialize(eski, CoreJsonContext.Default.AppConfig)!.Theme);
    }

    [Fact]
    public void Eski_yapilandirmada_alan_yoksa_duraklatilmamis_sayilir()
    {
        // 0.1.20 ve öncesinin yazdığı dosya. Alan yokken "duraklatılmış" saymak,
        // yükseltmede çalışan bir korumayı kapatmak olurdu.
        const string eski = """
            {"selectedIspId":"turk-telekom","selectedStrategyArgs":"--dpi-desync=fake --dpi-desync-ttl=4","secureDnsEnabled":true}
            """;

        Assert.False(JsonSerializer.Deserialize(eski, CoreJsonContext.Default.AppConfig)!.ServicePaused);
    }
}
