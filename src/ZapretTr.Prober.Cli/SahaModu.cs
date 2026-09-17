using System.Diagnostics;
using System.IO;

/// <summary>
/// zapret-tr-test.exe argümansız (çift tıklanarak) çalıştırıldığında saha testini
/// TESTI-BASLAT.bat ile AYNI ayarlarla koşturur ve pencereyi açık tutar.
/// </summary>
/// <remarks>
/// ÖLÇÜLDÜ (2026-09-16, gerçek makine, Türk Telekom): kullanıcı paketi açıp
/// doğrudan zapret-tr-test.exe'ye çift tıkladı. Argüman yoktu, dolayısıyla:
/// hat tespiti yapılmadı ("Diger TR profilleri 1/18"), şifreli DNS kullanılmadı
/// ("DNS: sistem"), rapor yazılmadı (--out yok) ve test bitince konsol penceresi
/// kapandı. Kullanıcının elinde ne bir dosya ne de sonucu okuyabileceği bir ekran
/// kaldı. Çift tıklama en doğal yol; o yolun en yararsız sonucu vermesi hataydı.
///
/// Test ayrı bir süreçte koşuyor, bu süreç yalnızca bekleyip sonucu açıklıyor.
/// Böylece test kodundaki onlarca "return" yolunun hepsi pencereyi açık tutuyor
/// ve sonuç mesajı çıkış koduna bakıyor; TESTI-BASLAT.bat'taki kuralın aynısı.
/// Alt süreç yükseltilmiş belirteci devralıyor; ikinci bir UAC sorusu çıkmıyor.
/// </remarks>
internal static class SahaModu
{
    public const string RaporAdi = "zapret-tr-rapor.json";

    /// <summary>TESTI-BASLAT.bat'taki satırın birebir aynısı.</summary>
    public static readonly string[] Argumanlar =
        ["--isp", "auto", "--doh", "--max-candidates", "25", "--out", RaporAdi];

    public static int CiftTiklamaIleCalistir()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var exe = Environment.ProcessPath;
        if (exe is null)
        {
            Console.WriteLine("Test aracının yolu bulunamadı. TESTI-BASLAT.bat dosyasını çalıştırın.");
            Bekle();
            return 3;
        }

        var rapor = Path.Combine(AppContext.BaseDirectory, RaporAdi);
        var baslangic = DateTime.UtcNow;

        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        foreach (var arguman in Argumanlar)
        {
            psi.ArgumentList.Add(arguman);
        }

        // Ctrl+C alt sürece de gidiyor ve o kendi iptal yolunu (130) işletiyor. Bu
        // süreç ölürse pencere sonucu göstermeden kapanır; yakalayıp bekliyoruz.
        Console.CancelKeyPress += (_, e) => e.Cancel = true;

        int kod;
        try
        {
            // Üst sınır var (EarlyExitDeadlockTests kuralı), ama akışlar yönlendirilmediği
            // için kilitlenme riski yok; sınır yalnızca asılı kalan bir test için.
            // Normal test 2-5 dakika.
            using var test = Process.Start(psi)!;
            if (test.WaitForExit(TimeSpan.FromMinutes(60)))
            {
                kod = test.ExitCode;
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("Test 60 dakikada bitmedi; pencereyi kapatıp TEMIZLIK.bat çalıştırın.");
                kod = 5;
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Console.WriteLine("Test başlatılamadı: " + ex.Message);
            kod = 2;
        }

        // Klasörde önceki bir testten kalan dosya "rapor var" sayılmamalı.
        var raporVar = File.Exists(rapor);
        var raporYeni = raporVar && File.GetLastWriteTimeUtc(rapor) >= baslangic.AddSeconds(-2);

        Console.WriteLine();
        Console.WriteLine(new string('=', 60));
        foreach (var satir in SonucMesaji(kod, raporYeni, eskiRaporVar: raporVar && !raporYeni))
        {
            Console.WriteLine(" " + satir);
        }

        Console.WriteLine();
        Console.WriteLine(" Sürücüyü kaldırmak için TEMIZLIK.bat dosyasını çalıştırın.");
        Console.WriteLine(new string('=', 60));
        Bekle();
        return kod;
    }

    /// <summary>
    /// Çıkış koduna ve raporun bu testte yazılıp yazılmadığına göre kullanıcıya
    /// ne olacağını söyler. "Gönderin" yalnızca gerçekten yeni bir rapor varsa.
    /// </summary>
    public static IReadOnlyList<string> SonucMesaji(int kod, bool raporYeni, bool eskiRaporVar)
    {
        if (raporYeni && kod is 0 or 1)
        {
            return
            [
                "Test bitti. Sonuç dosyası: " + RaporAdi,
                "Bu klasörde oluşan bu dosyayı geri gönderin.",
            ];
        }

        if (raporYeni && kod == 5)
        {
            return
            [
                "Test bir hatayla yarıda kesildi, ama hata raporu yazıldı: " + RaporAdi,
                "Bu dosyayı geri gönderin; neyin bozulduğunu gösteriyor.",
            ];
        }

        var satirlar = new List<string> { $"Test TAMAMLANMADI ve rapor OLUŞMADI (kod {kod})." };
        switch (kod)
        {
            case 130:
                satirlar.Add("Test iptal edildi. Tekrar çalıştırıp soruya E yazın ve Enter'a basın.");
                break;
            case 2:
                satirlar.Add("Yönetici yetkisi alınamadı.");
                break;
            case 3:
                satirlar.Add("Paket eksik. Klasörü zip dosyasından yeniden çıkarın.");
                break;
        }

        if (eskiRaporVar)
        {
            satirlar.Add("DİKKAT: klasördeki " + RaporAdi + " ÖNCEKİ bir teste ait.");
        }

        return satirlar;
    }

    private static void Bekle()
    {
        Console.WriteLine();
        Console.Write("Pencereyi kapatmak için Enter'a basın.");
        Console.ReadLine();
    }
}
