using System.Threading;
using ZapretTr.App.ViewModels;

namespace ZapretTr.Tests;

/// <summary>
/// "Raporu Kaydet" düğmesinin ürettiği metin.
/// </summary>
/// <remarks>
/// Bu düğme bir kolaylık değil, eksik bir kanaldı. Türksat Kablonet kullanıcısı
/// 0.1.6'nın çalıştığını bildirdi ama profil hâlâ doğrulanmamış durumda: hangi
/// adayın kazandığını bilmiyoruz, çünkü günlüğü bize ulaştırmanın tek yolu
/// pencereden metni elle seçip kopyalamaktı. Bildirim geldi, veri gelmedi.
///
/// Rapor bu yüzden günlüğün yanında ORTAM ÖZETİNİ de taşımak zorunda: "şu aday
/// çalıştı" satırını okuyup hangi profil ve hangi sürümle olduğunu bilmeden
/// profile işleyemiyoruz. Test tam da o alanların kaybolmadığını bekliyor.
/// </remarks>
public sealed class ReportTests
{
    [Fact]
    public void Rapor_ortam_ozetini_ve_gunlugu_tasiyor()
    {
        string? rapor = null;
        Exception? hata = null;

        // MainViewModel WPF bağlamına bağlı (ObservableCollection, Dispatcher),
        // bu yüzden STA iş parçacığında kuruluyor.
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

        // Paylaşım uyarısı: kullanıcı dosyayı foruma koymadan önce içinde ne
        // olduğunu bilmeli. Kaybolursa rapor sessizce bir gizlilik sorunu olur.
        Assert.Contains("hicbir yere gonderilmedi", rapor, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Paylasmadan once", rapor, StringComparison.OrdinalIgnoreCase);

        // Teşhis için gereken alanlar. Biri eksikse gelen rapor yine
        // "çalıştı/çalışmadı"dan ibaret kalır ve profile işlenemez.
        foreach (var alan in new[]
                 {
                     "Tarih", "ZapretTR", "Motor", "Windows",
                     "Servis saglayici", "Strateji", "Parametre",
                     "Sifreli DNS", "Otomatik baslatma", "Durum", "Gunluk",
                 })
        {
            Assert.Contains(alan, rapor, StringComparison.Ordinal);
        }

        // MAKİNENİN ÖLÇÜLEN DURUMU.
        //
        // Yukarıdaki alanların hepsi görünüm modelinin kendi BİLDİĞİ şeyler ve
        // "olmadı" bildirimlerinin çoğunda hiçbiri yanlış değil. Yanlış olan şey
        // görünüm modelinin bakmadığı yerde duruyor: yönetici yetkisi yok, dosya
        // eksik, servis kurulu ama durmuş, sistem DNS'i bizde asılı kalmış.
        //
        // Özellikle önemli olan durum: kullanıcı bilgisayarı yeniden başlatıp
        // uygulamayı YENİ açtıysa günlük neredeyse boş oluyor ve raporun geri
        // kalanı "çalışmadı" cümlesine hiçbir şey eklemiyordu.
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
