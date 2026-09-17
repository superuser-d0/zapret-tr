using System.Drawing;
using System.Windows;
using System.Windows.Forms;

namespace ZapretTr.App;

/// <summary>
/// Saat yanındaki bildirim alanı simgesi.
/// </summary>
/// <remarks>
/// Pencereyi X ile kapatmak uygulamayı SONLANDIRMAMALI: koruma arka planda
/// çalışmaya devam etsin isteniyor. Ama sonlandırmıyorsa kullanıcının
/// uygulamaya geri dönebilmesi de gerekiyor; yoksa görünmez bir süreç
/// bırakmış oluruz ve kullanıcının onu durdurmasının tek yolu Görev Yöneticisi
/// olur. Simge, gizlenen pencerenin geri getirilebileceği tek yer.
///
/// Simge WinForms'tan: WPF'in kendi bildirim alanı API'si yok ve tek bir simge
/// için üçüncü taraf bir paket almak, güvendiğimiz kod miktarını boşuna
/// büyütür.
/// </remarks>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Window _window;
    private bool _disposed;

    /// <summary>Kullanıcı simgeden "Çıkış" dedi.</summary>
    public event EventHandler? ExitRequested;

    public TrayIcon(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Göster", null, (_, _) => Goster());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Çıkış", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _icon = new NotifyIcon
        {
            // Uygulamanın kendi simgesi: ayrı bir .ico dosyası taşımak yerine
            // çalışan exe'den okunuyor, böylece ikisi ayrışamıyor.
            Icon = SimgeyiAl(),
            Text = "ZapretTR",
            Visible = false,
            ContextMenuStrip = menu,
        };

        _icon.DoubleClick += (_, _) => Goster();
    }

    /// <summary>Pencereyi gizler ve simgeyi gösterir.</summary>
    public void Gizle()
    {
        _window.Hide();
        _icon.Visible = true;

        // İlk gizlemede bir kez bilgilendir: kullanıcı uygulamanın kapandığını
        // sanıyorsa simgeyi aramaz ve "kapatamıyorum" diye geri gelir.
        _icon.BalloonTipTitle = "ZapretTR arka planda";
        _icon.BalloonTipText = "Koruma çalışmaya devam ediyor. " +
                               "Pencereyi geri getirmek için simgeye çift tıklayın.";
        _icon.ShowBalloonTip(3000);
    }

    /// <summary>Pencereyi geri getirir.</summary>
    public void Goster()
    {
        _window.Show();

        // Yalnızca simge durumundaysa: tam ekrana alınmış bir pencereyi gizleyip
        // geri getirmek onu küçültüyordu.
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
        _icon.Visible = false;
    }

    private static Icon SimgeyiAl()
    {
        try
        {
            // Önce gömülü .ico: bildirim alanının boyutuna (ölçeğe göre 16, 20, 24...)
            // uyan görüntü küçültme yapılmadan seçiliyor.
            using var akis = typeof(TrayIcon).Assembly.GetManifestResourceStream("ZapretTR.ico");
            if (akis is not null)
            {
                return new Icon(akis, SystemInformation.SmallIconSize);
            }

            var yol = Environment.ProcessPath;
            if (yol is not null)
            {
                var cikarilan = Icon.ExtractAssociatedIcon(yol);
                if (cikarilan is not null)
                {
                    return cikarilan;
                }
            }
        }
        catch (Exception)
        {
            // Simge okunamadı. Bu, uygulamanın çalışmasını engelleyecek bir şey
            // değil; varsayılan simgeyle devam.
        }

        return SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Simge açıkça gizlenmeli: yalnızca Dispose etmek, Windows'un bildirim
        // alanında "hayalet" bir simge bırakabiliyor (fare üzerine gelene kadar
        // silinmiyor).
        _icon.Visible = false;
        _icon.Dispose();
    }
}
