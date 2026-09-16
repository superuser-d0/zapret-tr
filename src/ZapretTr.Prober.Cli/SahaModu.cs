using System.Diagnostics;
using System.IO;

/// <summary>
/// zapret-tr-test.exe argumansiz (cift tiklanarak) calistirildiginda saha testini
/// TESTI-BASLAT.bat ile AYNI ayarlarla kosturur ve pencereyi acik tutar.
/// </summary>
/// <remarks>
/// OLCULDU (2026-09-16, gercek makine, Turk Telekom): kullanici paketi acip
/// dogrudan zapret-tr-test.exe'ye cift tikladi. Arguman yoktu, dolayisiyla:
/// hat tespiti yapilmadi ("Diger TR profilleri 1/18"), sifreli DNS kullanilmadi
/// ("DNS: sistem"), rapor yazilmadi (--out yok) ve test bitince konsol penceresi
/// kapandi. Kullanici elinde ne bir dosya ne de sonucu okuyabilecegi bir ekran
/// kaldi. Cift tiklama en dogal yol; o yolun en yararsiz sonucu vermesi hataydi.
///
/// Test ayri bir surecte kosuyor, bu surec yalnizca bekleyip sonucu acikliyor.
/// Boylece test kodundaki onlarca "return" yolunun hepsi pencereyi acik tutuyor
/// ve sonuc mesaji cikis koduna bakiyor -- TESTI-BASLAT.bat'taki kuralin aynisi.
/// Alt surec yukseltilmis belirteci devraliyor; ikinci bir UAC sorusu cikmiyor.
/// </remarks>
internal static class SahaModu
{
    public const string RaporAdi = "zapret-tr-rapor.json";

    /// <summary>TESTI-BASLAT.bat'taki satirin birebir aynisi.</summary>
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

        // Ctrl+C alt surece de gidiyor ve o kendi iptal yolunu (130) isletiyor. Bu
        // surec olurse pencere sonucu gostermeden kapanir; yakalayip bekliyoruz.
        Console.CancelKeyPress += (_, e) => e.Cancel = true;

        int kod;
        try
        {
            // Ust sinir var (EarlyExitDeadlockTests kurali), ama akislar yonlendirilmedigi
            // icin kilitlenme riski yok; sinir yalnizca asili kalan bir test icin.
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

        // Klasorde onceki bir testten kalan dosya "rapor var" sayilmamali.
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
    /// Cikis koduna ve raporun bu testte yazilip yazilmadigina gore kullaniciya
    /// ne olacagini soyler. "Gonderin" yalnizca gercekten yeni bir rapor varsa.
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
