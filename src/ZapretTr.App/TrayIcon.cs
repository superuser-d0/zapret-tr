using System.Drawing;
using System.Windows;
using System.Windows.Forms;

namespace ZapretTr.App;

/// <summary>
/// Saat yanindaki bildirim alani simgesi.
/// </summary>
/// <remarks>
/// Pencereyi X ile kapatmak uygulamayi SONLANDIRMAMALI: koruma arka planda
/// calismaya devam etsin isteniyor. Ama sonlandirmiyorsa kullanicinin
/// uygulamaya geri donebilmesi de gerekiyor -- yoksa gorunmez bir surec
/// birakmis oluruz ve kullanicinin onu durdurmasinin tek yolu Gorev Yoneticisi
/// olur. Simge, gizlenen pencerenin geri getirilebilecegi tek yer.
///
/// Simge WinForms'tan: WPF'in kendi bildirim alani API'si yok ve tek bir simge
/// icin ucuncu taraf bir paket almak, guvendigimiz kod miktarini bosuna
/// buyutur.
/// </remarks>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Window _window;
    private bool _disposed;

    /// <summary>Kullanici simgeden "Çıkış" dedi.</summary>
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
            // Uygulamanin kendi simgesi: ayri bir .ico dosyasi tasimak yerine
            // calisan exe'den okunuyor, boylece ikisi ayrisamiyor.
            Icon = SimgeyiAl(),
            Text = "ZapretTR",
            Visible = false,
            ContextMenuStrip = menu,
        };

        _icon.DoubleClick += (_, _) => Goster();
    }

    /// <summary>Pencereyi gizler ve simgeyi gosterir.</summary>
    public void Gizle()
    {
        _window.Hide();
        _icon.Visible = true;

        // Ilk gizlemede bir kez bilgilendir: kullanici uygulamanin kapandigini
        // saniyorsa simgeyi aramaz ve "kapatamiyorum" diye geri gelir.
        _icon.BalloonTipTitle = "ZapretTR arka planda";
        _icon.BalloonTipText = "Koruma çalışmaya devam ediyor. " +
                               "Pencereyi geri getirmek için simgeye çift tıklayın.";
        _icon.ShowBalloonTip(3000);
    }

    /// <summary>Pencereyi geri getirir.</summary>
    public void Goster()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
        _icon.Visible = false;
    }

    private static Icon SimgeyiAl()
    {
        try
        {
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
            // Simge okunamadi. Bu, uygulamanin calismasini engelleyecek bir sey
            // degil -- varsayilan simgeyle devam.
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

        // Simge acikca gizlenmeli: yalnizca Dispose etmek, Windows'un bildirim
        // alaninda "hayalet" bir simge birakabiliyor (fare uzerine gelene kadar
        // silinmiyor).
        _icon.Visible = false;
        _icon.Dispose();
    }
}
