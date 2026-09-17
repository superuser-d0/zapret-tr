using ZapretTr.Core.Engine;

namespace ZapretTr.Tests;

/// <summary>
/// "Kurulu" ile "çalışıyor" ayrımı.
/// </summary>
/// <remarks>
/// İkisini aynı şey saymak kullanıcıyı KİLİTLİYORDU: servis kurulu ama durmuşsa
/// arayüz "SERVİS MODU AKTİF" deyip Başlat düğmesini kapatıyor, koruma olmuyor
/// ve kullanıcının yapabileceği bir şey de kalmıyordu. Gerçek bir kullanıcıda
/// 0.1.9'dan 0.1.15'e yükseltmeden sonra yaşandı: yükseltme servisi geri
/// kuruyor, "sc start" bir sebeple başarısız oluyor, servis VAR ama DURMUŞ
/// kalıyor.
///
/// Bu yüzden üçüncü bir durum var: kurulu ama durmuş. Uygulama o durumda
/// Başlat'ı AÇIK bırakıyor ki kullanıcı korumasını elle başlatabilsin.
/// </remarks>
public sealed class ServiceStatusTests
{
    [Fact]
    public void Kurulu_ve_calisiyor()
    {
        var durum = new ServiceStatus(WinwsInstalled: true, DnsInstalled: true, WinwsRunning: true);

        Assert.True(durum.AnyInstalled);
        Assert.False(durum.InstalledButStopped);
    }

    [Fact]
    public void Kurulu_ama_durmus_ayirt_ediliyor()
    {
        // TEHLİKELİ DURUM. Eskiden bu "kurulu" sayılıp Başlat kapatılıyordu.
        var durum = new ServiceStatus(WinwsInstalled: true, DnsInstalled: true, WinwsRunning: false);

        Assert.True(durum.AnyInstalled);
        Assert.True(durum.InstalledButStopped);
    }

    [Fact]
    public void Hic_kurulu_degilse_durmus_sayilmaz()
    {
        // Servis yoksa "durmuş" demek yanlış olur: kullanıcı otomatik başlatmayı
        // hiç kurmamış olabilir ve ona bir sorun varmış gibi göstermemeliyiz.
        var durum = new ServiceStatus(WinwsInstalled: false, DnsInstalled: false, WinwsRunning: false);

        Assert.False(durum.AnyInstalled);
        Assert.False(durum.InstalledButStopped);
    }

    [Fact]
    public void Yalnizca_DNS_servisi_kuruluysa_winws_durmus_sayilmaz()
    {
        // Şifreli DNS kapalıyken yalnızca winws servisi kuruluyor; tersi de
        // mümkün. "winws durmuş" uyarısı yalnızca winws servisi VARSA anlamlı.
        var durum = new ServiceStatus(WinwsInstalled: false, DnsInstalled: true, WinwsRunning: false);

        Assert.True(durum.AnyInstalled);
        Assert.False(durum.InstalledButStopped);
    }

    [Fact]
    public void Kullanicinin_duraklattigi_servis_durmus_uyarisi_vermez()
    {
        // VPN için bilerek kapatılan koruma "SERVİS DURMUŞ" diye alarm vermemeli.
        var durum = new ServiceStatus(WinwsInstalled: true, DnsInstalled: true, WinwsRunning: false, WinwsPaused: true);

        Assert.True(durum.AnyInstalled);
        Assert.False(durum.InstalledButStopped);
    }

    // --- Duraklatmanın izi: başlangıç turu ----------------------------------------

    [Fact]
    public void Elle_baslatilan_servis_duraklatilmis_sayilir()
    {
        const string cikti = """
            [SC] QueryServiceConfig SUCCESS

            SERVICE_NAME: ZapretTR
                    TYPE               : 10  WIN32_OWN_PROCESS
                    START_TYPE         : 3   DEMAND_START
                    ERROR_CONTROL      : 1   NORMAL
            """;

        Assert.True(ServiceManager.IsDemandStartQcOutput(cikti));
    }

    [Fact]
    public void Acilista_baslayan_servis_duraklatilmis_sayilmaz()
    {
        const string cikti = """
            [SC] QueryServiceConfig SUCCESS

            SERVICE_NAME: ZapretTR
                    TYPE               : 10  WIN32_OWN_PROCESS
                    START_TYPE         : 2   AUTO_START
                    ERROR_CONTROL      : 1   NORMAL
            """;

        Assert.False(ServiceManager.IsDemandStartQcOutput(cikti));
    }

    [Fact]
    public void Olmayan_servis_duraklatilmis_sayilmaz()
    {
        Assert.False(ServiceManager.IsDemandStartQcOutput(
            "[SC] OpenService FAILED 1060: The specified service does not exist as an installed service."));
    }

    // --- Parametre testi için servisi durdurma -----------------------------------
    //
    // Test, servisin winws'i durana kadar bekliyor; bekleme "sc query" çıktısından
    // okunuyor. STOP_PENDING'i durmuş saymak, süreç sürücüyü hâlâ tutarken testi
    // başlatmak demek; düzeltilen hatanın ta kendisi.

    [Fact]
    public void Durmus_servis_durmus_sayilir()
    {
        const string cikti = """
            SERVICE_NAME: ZapretTR
                    TYPE               : 10  WIN32_OWN_PROCESS
                    STATE              : 1  STOPPED
                    WIN32_EXIT_CODE    : 0  (0x0)
            """;

        Assert.True(ServiceManager.IsStoppedQueryOutput(cikti));
    }

    [Fact]
    public void Durmakta_olan_servis_durmus_sayilmaz()
    {
        const string cikti = """
            SERVICE_NAME: ZapretTR
                    TYPE               : 10  WIN32_OWN_PROCESS
                    STATE              : 3  STOP_PENDING
            """;

        Assert.False(ServiceManager.IsStoppedQueryOutput(cikti));
    }

    [Fact]
    public void Calisan_servis_durmus_sayilmaz()
    {
        const string cikti = """
            SERVICE_NAME: ZapretTR
                    TYPE               : 10  WIN32_OWN_PROCESS
                    STATE              : 4  RUNNING
            """;

        Assert.False(ServiceManager.IsStoppedQueryOutput(cikti));
    }

    [Fact]
    public void Servis_hic_yoksa_beklenecek_bir_sey_yok()
    {
        const string cikti = "[SC] EnumQueryServicesStatus:OpenService FAILED 1060:";

        Assert.True(ServiceManager.IsStoppedQueryOutput(cikti));
    }
}
