using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using ZapretTr.Core.Engine;
using IoPath = System.IO.Path;

namespace ZapretTr.Tests;

/// <summary>
/// Hemen ölen bir sürecin çıktısını beklemek arayüzü kilitlememeli.
/// </summary>
/// <remarks>
/// 2026-09-14, gerçek kullanıcı: ZapretTR kapatılıp yeniden açıldığında dondu ve
/// Windows iki kez "yanıt vermiyor" deyip kapattı. Sebep: "Başlat" arayüz iş
/// parçacığında WinwsRunner.Start'ı çağırıyor; winws 250 ms içinde ölünce Start
/// çıktının bitmesini SINIRSIZ bekliyordu, çıktıyı okuyan iş parçacığı ise satırı
/// arayüze Dispatcher.Invoke ile yazmak için arayüzü bekliyordu.
///
/// WinwsRunner.Start yönetici yetkisi istediği için burada doğrudan koşamıyor. Test
/// aynı koşulu kuruyor: arayüz iş parçacığı (Dispatcher), hemen ölen ve stderr'e
/// yazan bir süreç (where.exe), satırı Dispatcher.Invoke ile gönderen okuyucu ve
/// bekleme olarak Start'ın kullandığı WinwsRunner.WaitForEarlyExit.
/// </remarks>
public sealed class EarlyExitDeadlockTests
{
    [Fact]
    public void Erken_olen_surec_arayuz_is_parcacigini_kilitlemiyor()
    {
        Dispatcher? arayuz = null;
        var hazir = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            arayuz = Dispatcher.CurrentDispatcher;
            hazir.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        hazir.Wait();

        var bitti = new ManualResetEventSlim();
        var olduMu = false;

        arayuz!.BeginInvoke(() =>
        {
            var psi = new ProcessStartInfo(
                IoPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "where.exe"),
                "zapret-tr-olmayan-dosya-" + Guid.NewGuid().ToString("N"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var surec = new Process { StartInfo = psi };
            using var ciktiBitti = new ManualResetEventSlim();
            using var hataBitti = new ManualResetEventSlim();

            // Kilitlenmenin diğer yarısı, bilerek: satırı arayüze SENKRON gönder.
            // try: makine çok yüklüyse satır, test arayüzü kapattıktan SONRA gelebilir;
            // kapalı Dispatcher'a Invoke istisna fırlatır ve arka plan iş parçacığındaki
            // yakalanmamış istisna test sürecini düşürür.
            void Gonder()
            {
                try { arayuz.Invoke(() => { }); }
                catch (Exception) { }
            }

            surec.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null) { ciktiBitti.Set(); return; }
                Gonder();
            };
            surec.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) { hataBitti.Set(); return; }
                Gonder();
            };

            surec.Start();
            surec.BeginOutputReadLine();
            surec.BeginErrorReadLine();

            // Süreler BOL tutuluyor ve bu, mutlu yolda hiçbir şeyi yavaşlatmıyor:
            // WaitForEarlyExit süreç ölür ölmez dönüyor (içerideki WaitForExit bir
            // üst sınır, bekleme süresi değil). Yani büyük değer yalnızca süreç
            // GERÇEKTEN ölmediğinde bekleniyor; zaten görmek istediğimiz hata o.
            //
            // KIRILGANLIK, ölçüldü: 2026-09-15'te bu test 2000 ms ile bir YAYINI
            // düşürdü (yayın iş akışı, koşum 35020852773). Aynı commit dakikalar önce
            // "derle ve test"te geçmişti; yüklü bir runner'da where.exe 2 saniyede
            // başlayıp bitemedi. Düşen şey ürün değil, testin zamanlama varsayımıydı:
            // süreç hâlâ yaşıyorsa WaitForEarlyExit'in false dönmesi DOĞRU davranış.
            olduMu = WinwsRunner.WaitForEarlyExit(surec, 15000, ciktiBitti.WaitHandle, hataBitti.WaitHandle);
            bitti.Set();
        });

        // Eski kodla (parametresiz WaitForExit) bu bekleme HİÇ bitmiyordu; kilitlenme
        // sonsuz sürdüğü için üst sınırın büyük olması testin gücünü azaltmıyor.
        // Üst sınır, yukarıdaki bekleme penceresinden büyük olmak ZORUNDA: yavaş ama
        // kilitlenmemiş bir makinede önce bu iddia düşüp yanlış teşhis verirdi.
        Assert.True(bitti.Wait(TimeSpan.FromSeconds(60)), "Arayuz is parcacigi 60 saniyede donmedi: kilitlenme.");
        Assert.True(olduMu, "where.exe 15 saniyede olmeliydi; bu sure asilmissa makine asiri yuklu ya da surec gercekten asili kalmis.");

        arayuz.InvokeShutdown();
    }

    [Fact]
    public void Kaynakta_sinirsiz_WaitForExit_yok()
    {
        // Parametresiz WaitForExit yönlendirilmiş akışların sonunu sınırsız bekliyor.
        // Arayüzden ulaşılabilen bir yolda kullanılırsa kilitlenme geri gelir.
        var kok = IoPath.Combine(XmlCommentTests.RepoRoot, "src");
        var bulunan = Directory.EnumerateFiles(kok, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{IoPath.DirectorySeparatorChar}obj{IoPath.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => File.ReadLines(f).Select((satir, i) => (f, i, satir)))
            .Where(x => !x.satir.TrimStart().StartsWith("//", StringComparison.Ordinal)
                        && Regex.IsMatch(x.satir, @"\.WaitForExit\(\s*\)"))
            .Select(x => $"{IoPath.GetFileName(x.f)}:{x.i + 1}")
            .ToList();

        Assert.Empty(bulunan);
    }
}
