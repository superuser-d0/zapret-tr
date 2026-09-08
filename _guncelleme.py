import io

p = 'src/ZapretTr.App/ViewModels/MainViewModel.cs'
s = io.open(p, encoding='utf-8-sig', newline='').read()
NL = '\r\n'

# --- 1. Ozellik: guncelleme mesaji ------------------------------------------
anchor = '    /// <summary>Kullanici uygulamadan GERCEKTEN cikmak istedi.</summary>'
assert anchor in s, 'ExitRequested yorumu bulunamadi'

ozellik = NL.join([
    '    private string? _updateMessage;',
    '',
    '    /// <summary>Yeni surum varsa gosterilecek satir; yoksa <c>null</c>.</summary>',
    '    public string? UpdateMessage',
    '    {',
    '        get => _updateMessage;',
    '        private set',
    '        {',
    '            if (Set(ref _updateMessage, value))',
    '            {',
    '                OnPropertyChanged(nameof(HasUpdate));',
    '            }',
    '        }',
    '    }',
    '',
    '    /// <summary>Guncelleme satiri gosterilsin mi.</summary>',
    '    public bool HasUpdate => !string.IsNullOrEmpty(UpdateMessage);',
    '',
    anchor,
])
s = s.replace(anchor, ozellik, 1)

# --- 2. Acilista kontrol -----------------------------------------------------
eski = '            Append("Profiller yüklendi: " + _profiles.Profiles.Count + " servis sağlayıcısı.");'
assert eski in s, 'acilis satiri bulunamadi'

yeni = NL.join([
    eski,
    '',
    '            // Beklemiyoruz: ag yavassa uygulamanin acilisini geciktirmesin.',
    '            _ = CheckForUpdateAsync();',
])
s = s.replace(eski, yeni, 1)

# --- 3. Kontrol metodu -------------------------------------------------------
anchor2 = '    /// <summary>Gunlugu ve ortam ozetini kullanicinin sectigi bir dosyaya yazar.</summary>'
assert anchor2 in s, 'SaveReport yorumu bulunamadi'

metot = NL.join([
    '    /// <summary>Yayinlanmis daha yeni bir surum var mi diye bakar.</summary>',
    '    /// <remarks>',
    '    /// Gunde birkac surum cikabiliyor ve her seferinde kullanicilara tek tek',
    '    /// "sunu kur" demek gerekiyordu; kullaniciya ulasmayan bir duzeltme ise',
    '    /// yaramiyor. Bu, uygulamanin disari istek yapan TEK yeri: GitHub\'a',
    '    /// yalnizca "en son surum ne" sorusu gidiyor, baska hicbir sey degil.',
    '    /// Ayardan kapatilabilir.',
    '    /// </remarks>',
    '    private async Task CheckForUpdateAsync()',
    '    {',
    '        try',
    '        {',
    '            if (!ConfigStore.Load().UpdateCheckEnabled)',
    '            {',
    '                return;',
    '            }',
    '',
    '            var latest = await UpdateChecker',
    '                .GetLatestVersionAsync(TimeSpan.FromSeconds(8))',
    '                .ConfigureAwait(true);',
    '',
    '            if (!UpdateChecker.IsNewer(latest, SurumMetni()))',
    '            {',
    '                return;',
    '            }',
    '',
    '            UpdateMessage = $"Yeni sürüm var: {latest} — indirmek için: {UpdateChecker.ReleasesPage}";',
    '            Append($"Yeni sürüm yayınlandı: {latest} (kurulu: {SurumMetni().Split(\'+\')[0]})");',
    '            Append("İndirme: " + UpdateChecker.ReleasesPage);',
    '        }',
    '        catch (Exception)',
    '        {',
    '            // Guncelleme kontrolu bir kolaylik, korumanin parcasi degil:',
    '            // basarisiz olmasi kullaniciya hata olarak gosterilmemeli.',
    '        }',
    '    }',
    '',
    anchor2,
])
s = s.replace(anchor2, metot, 1)

io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('guncelleme kontrolu eklendi')
