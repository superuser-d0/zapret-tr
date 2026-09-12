using System.Threading;
using ZapretTr.App.ViewModels;

namespace ZapretTr.Tests;

/// <summary>
/// "Raporu Kaydet" dugmesinin urettigi metin.
/// </summary>
/// <remarks>
/// Bu dugme bir kolaylik degil, eksik bir kanaldi. Turksat Kablonet kullanicisi
/// 0.1.6'nin calistigini bildirdi ama profil hala dogrulanmamis durumda: hangi
/// adayin kazandigini bilmiyoruz, cunku gunlugu bize ulastirmanin tek yolu
/// pencereden metni elle secip kopyalamakti. Bildirim geldi, veri gelmedi.
///
/// Rapor bu yuzden gunlugun yaninda ORTAM OZETINI de tasimak zorunda: "su aday
/// calisti" satirini okuyup hangi profil ve hangi surumle oldugunu bilmeden
/// profile isleyemiyoruz. Test tam da o alanlarin kaybolmadigini bekliyor.
/// </remarks>
public sealed class ReportTests
{
    [Fact]
    public void Rapor_ortam_ozetini_ve_gunlugu_tasiyor()
    {
        string? rapor = null;
        Exception? hata = null;

        // MainViewModel WPF baglamina bagli (ObservableCollection, Dispatcher),
        // bu yuzden STA is parcaciginda kuruluyor.
        var thread = new Thread(() =>
        {
            try
            {
                var vm = new MainViewModel();
                rapor = vm.BuildReport();
            }
            catch (Exception ex)
            {
                hata = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(hata);
        Assert.NotNull(rapor);

        // Paylasim uyarisi: kullanici dosyayi foruma koymadan once icinde ne
        // oldugunu bilmeli. Kaybolursa rapor sessizce bir gizlilik sorunu olur.
        Assert.Contains("hicbir yere gonderilmedi", rapor, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Paylasmadan once", rapor, StringComparison.OrdinalIgnoreCase);

        // Teshis icin gereken alanlar. Biri eksikse gelen rapor yine
        // "calisti/calismadi"dan ibaret kalir ve profile islenemez.
        foreach (var alan in new[]
                 {
                     "Tarih", "ZapretTR", "Motor", "Windows",
                     "Servis saglayici", "Strateji", "Parametre",
                     "Sifreli DNS", "Otomatik baslatma", "Durum", "Gunluk",
                 })
        {
            Assert.Contains(alan, rapor, StringComparison.Ordinal);
        }

        // MAKINENIN OLCULEN DURUMU.
        //
        // Yukaridaki alanlarin hepsi gorunum modelinin kendi BILDIGI seyler ve
        // "olmadi" bildirimlerinin cogunda hicbiri yanlis degil. Yanlis olan sey
        // gorunum modelinin bakmadigi yerde duruyor: yonetici yetkisi yok, dosya
        // eksik, servis kurulu ama durmus, sistem DNS'i bizde asili kalmis.
        //
        // Ozellikle onemli olan durum: kullanici bilgisayari yeniden baslatip
        // uygulamayi YENI actiysa gunluk neredeyse bos oluyor ve raporun geri
        // kalani "calismadi" cumlesine hicbir sey eklemiyordu.
        foreach (var bolum in new[]
                 {
                     "Makine durumu", "Yonetici yetkisi", "Kurulum dosyalari",
                     "Calisan surecler", "Servisler", "Sistem DNS'i",
                     "Cakisan araclar", "Kullanici verisi",
                 })
        {
            Assert.Contains(bolum, rapor, StringComparison.Ordinal);
        }
    }
}
