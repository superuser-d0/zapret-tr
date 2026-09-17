using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace ZapretTr.App;

/// <summary>
/// Liste kutusunu her yeni satırda alta kaydırır.
/// </summary>
/// <remarks>
/// Canlı günlükte son satır görünmüyorsa günlüğün bir faydası kalmıyor; kullanıcı
/// test sürerken her satırda elle aşağı kaydırmak zorunda kalırdı.
/// </remarks>
public static class AutoScrollBehavior
{
    public static readonly DependencyProperty AutoScrollProperty =
        DependencyProperty.RegisterAttached(
            "AutoScroll",
            typeof(bool),
            typeof(AutoScrollBehavior),
            new PropertyMetadata(false, OnAutoScrollChanged));

    public static void SetAutoScroll(DependencyObject element, bool value)
        => element.SetValue(AutoScrollProperty, value);

    public static bool GetAutoScroll(DependencyObject element)
        => (bool)element.GetValue(AutoScrollProperty);

    private static void OnAutoScrollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox listBox)
        {
            return;
        }

        if (e.NewValue is true)
        {
            listBox.Loaded += Attach;
            listBox.Unloaded += Detach;

            if (listBox.IsLoaded)
            {
                Attach(listBox, new RoutedEventArgs());
            }
        }
        else
        {
            Detach(listBox, new RoutedEventArgs());
        }
    }

    private static void Attach(object sender, RoutedEventArgs e)
    {
        if (sender is ListBox { ItemsSource: INotifyCollectionChanged collection } listBox)
        {
            // Önce çıkar, sonra ekle: Loaded birden fazla kez tetiklenebilir ve
            // aynı işleyicinin iki kez bağlı olması her satırda iki kaydırma demek.
            collection.CollectionChanged -= listBox.ScrollToLast;
            collection.CollectionChanged += listBox.ScrollToLast;
        }
    }

    private static void Detach(object sender, RoutedEventArgs e)
    {
        if (sender is ListBox { ItemsSource: INotifyCollectionChanged collection } listBox)
        {
            collection.CollectionChanged -= listBox.ScrollToLast;
        }
    }

    private static void ScrollToLast(this ListBox listBox, object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || listBox.Items.Count == 0)
        {
            return;
        }

        listBox.ScrollIntoView(listBox.Items[^1]);
    }
}
