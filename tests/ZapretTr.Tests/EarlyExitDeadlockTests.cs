using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using ZapretTr.Core.Engine;
using IoPath = System.IO.Path;

namespace ZapretTr.Tests;

/// <summary>
/// Hemen olen bir surecin ciktisini beklemek arayuzu kilitlememeli.
/// </summary>
/// <remarks>
/// 2026-09-14, gercek kullanici: ZapretTR kapatilip yeniden acildiginda dondu ve
/// Windows iki kez "yanit vermiyor" deyip kapatti. Sebep: "Baslat" arayuz is
/// parcaciginda WinwsRunner.Start'i cagiriyor; winws 250 ms icinde olunce Start
/// ciktinin bitmesini SINIRSIZ bekliyordu, ciktiyi okuyan is parcacigi ise satiri
/// arayuze Dispatcher.Invoke ile yazmak icin arayuzu bekliyordu.
///
/// WinwsRunner.Start yonetici yetkisi istedigi icin burada dogrudan kosamiyor. Test
/// ayni kosulu kuruyor: arayuz is parcacigi (Dispatcher), hemen olen ve stderr'e
/// yazan bir surec (where.exe), satiri Dispatcher.Invoke ile gonderen okuyucu -- ve
/// bekleme olarak Start'in kullandigi WinwsRunner.WaitForEarlyExit.
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

            // Kilitlenmenin diger yarisi, bilerek: satiri arayuze SENKRON gonder.
            // try: makine cok yukluyse satir, test arayuzu kapattiktan SONRA gelebilir;
            // kapali Dispatcher'a Invoke istisna firlatir ve arka plan is parcacigindaki
            // yakalanmamis istisna test surecini dusurur.
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

            // Sureler BOL tutuluyor ve bu, mutlu yolda hicbir sey yavaslatmiyor:
            // WaitForEarlyExit surec olur olmaz donuyor (icerideki WaitForExit bir
            // ust sinir, bekleme suresi degil). Yani buyuk deger yalnizca surec
            // GERCEKTEN olmediginde bekleniyor -- ki zaten gormek istedigimiz hata o.
            //
            // KIRILGANLIK, olculdu: 2026-09-15'te bu test 2000 ms ile bir YAYINI
            // dusurdu (yayin is akisi, kosum 35020852773). Ayni commit dakikalar once
            // "derle ve test"te gecmisti; yuklu bir runner'da where.exe 2 saniyede
            // baslayip bitemedi. Dusen sey urun degil, testin zamanlama varsayimiydi:
            // surec hala yasiyorsa WaitForEarlyExit'in false donmesi DOGRU davranis.
            olduMu = WinwsRunner.WaitForEarlyExit(surec, 15000, ciktiBitti.WaitHandle, hataBitti.WaitHandle);
            bitti.Set();
        });

        // Eski kodla (parametresiz WaitForExit) bu bekleme HIC bitmiyordu; kilitlenme
        // sonsuz surdugu icin ust sinirin buyuk olmasi testin gucunu azaltmiyor.
        // Ust sinir, yukaridaki bekleme penceresinden buyuk olmak ZORUNDA: yavas ama
        // kilitlenmemis bir makinede once bu iddia dusup yanlis teshis verirdi.
        Assert.True(bitti.Wait(TimeSpan.FromSeconds(60)), "Arayuz is parcacigi 60 saniyede donmedi: kilitlenme.");
        Assert.True(olduMu, "where.exe 15 saniyede olmeliydi; bu sure asilmissa makine asiri yuklu ya da surec gercekten asili kalmis.");

        arayuz.InvokeShutdown();
    }

    [Fact]
    public void Kaynakta_sinirsiz_WaitForExit_yok()
    {
        // Parametresiz WaitForExit yonlendirilmis akislarin sonunu sinirsiz bekliyor.
        // Arayuzden ulasilabilen bir yolda kullanilirsa kilitlenme geri gelir.
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
