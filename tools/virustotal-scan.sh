#!/usr/bin/env bash
#
# Yayinlanan ikilileri VirusTotal'a sokar ve sonucu markdown tablo olarak yazar.
#
# NEDEN VAR
# ---------
# ZapretTR imzasiz dagitiliyor ve cekirdek modunda calisan bir paket yakalama
# surucusu tasiyor. Bu ikisi bir arada neredeyse her zaman false-positive
# uretir. Kullaniciya "antivirusunuz yalan soyluyor" demenin tek durust yolu,
# hangi motorun ne dedigini ONCEDEN olcup yazmak. Tahmin degil, olcum.
#
# Bu betik yerelde de kosar ama asil yeri CI: GitHub kosucusunun VirusTotal'a
# erisimi var, gelistirme ortamlarinin cogunun yok.
#
# KULLANIM
#   VT_API_KEY=... tools/virustotal-scan.sh dosya1 [dosya2 ...]
#
# CIKIS KODU
#   0  tarama tamamlandi (tespit CIKSA BILE -- asagiya bak)
#   1  yapilandirma/API hatasi (anahtar yok, kota bitti, dosya yok)
#
# TESPIT NEDEN BASARISIZLIK SAYILMIYOR: bu paket icin bir miktar tespit
# BEKLENEN durum, cunku WinDivert'i butun antivirusler "hacking tool" ailesine
# koyuyor. Isi akisini kirmizi yapmak, gercek bir regresyonu gurultunun icinde
# kaybederdi. Sayilar ozete yaziliyor, karari insan veriyor.

set -euo pipefail

# Uc nokta disaridan verilebilir. Tek sebebi test: betigin akisi (ozetle sorma,
# 404'te yukleme, buyuk dosyada upload_url, analiz bekleme) gercek VirusTotal'a
# sokulmadan sahte bir sunucuyla kosturulabilsin.
VT_API="${VT_API_URL:-https://www.virustotal.com/api/v3}"

# Ucretsiz VirusTotal anahtari dakikada 4 istege izin veriyor. 16 saniye ->
# dakikada 3.75 istek, kenarda pay birakiyor. Kotayi asmak 429 dondurur ve
# tarama yarim kalir; beklemek bedava, yeniden kosmak degil.
# (Ucretli anahtarda ve testte disaridan kisaltilabilir.)
ISTEK_ARASI="${VT_ISTEK_ARASI:-16}"

# Son istegin zamani DOSYADA tutuluyor, degiskende degil.
#
# Sebep kabuga ozgu ve sessiz: vt_istek cogu yerde $(...) icinde cagriliyor,
# yani ALT KABUKTA kosuyor. Alt kabugun degisken atamalari ust kabuga donmez.
# Zaman damgasi degiskende tutulsaydi ust kabukta hep baslangic degerinde
# kalir, "son istekten beri cok zaman gecti" hesabi hep dogru cikar ve betik
# HIC BEKLEMEZDI -- kota sinirlayici sessizce hicbir sey yapmaz, hata da
# vermez. Ayni tuzak HTTP kodu icin de gecerli, orada cozum ayri: kod
# govdenin ILK SATIRI olarak donuyor (asagiya bak).
SON_ISTEK_DOSYASI=$(mktemp)
echo 0 >"$SON_ISTEK_DOSYASI"

# 32 MiB VirusTotal'in dogrudan yukleme siniri. Ustunu ayri bir uc nokta
# istiyor (once upload_url alinir). Kurulum paketi 55 MB, yani bu yol
# opsiyonel degil -- en cok merak edilen dosya tam da o.
DOGRUDAN_YUKLEME_SINIRI=$((32 * 1024 * 1024))

temizle() { rm -f "$SON_ISTEK_DOSYASI"; }
trap temizle EXIT

log() { printf '%s\n' "$*" >&2; }

# Istekler arasinda kota penceresini koru.
bekle() {
  local simdi son gecen
  simdi=$(date +%s)
  son=$(cat "$SON_ISTEK_DOSYASI")
  gecen=$((simdi - son))
  if [ "$gecen" -lt "$ISTEK_ARASI" ]; then
    sleep $((ISTEK_ARASI - gecen))
  fi
  date +%s >"$SON_ISTEK_DOSYASI"
}

# curl sarmalayici. ILK SATIR HTTP kodu, kalani govde -- global degiskene
# yazmak alt kabukta kaybolacagi icin (yukariya bak) kod cikti akisindan
# geciyor. 429 (kota) gelirse bir kez uzun bekleyip tekrar dener.
vt_istek() {
  local yanit kod govde deneme=0
  while :; do
    bekle
    yanit=$(curl -sS -w $'\n%{http_code}' -H "x-apikey: $VT_API_KEY" "$@" || true)
    kod=$(printf '%s' "$yanit" | tail -n1)
    govde=$(printf '%s' "$yanit" | sed '$d')
    if [ "$kod" = "429" ] && [ "$deneme" -lt 2 ]; then
      deneme=$((deneme + 1))
      log "  kota siniri (429), 60 sn bekleniyor (deneme $deneme/2)"
      sleep 60
      continue
    fi
    break
  done
  printf '%s\n%s' "$kod" "$govde"
}

kod_of()   { printf '%s' "$1" | head -n1; }
govde_of() { printf '%s' "$1" | tail -n +2; }

# Dosyayi yukler ve analiz kimligini doner. Boyuta gore uc nokta secer.
yukle() {
  local dosya="$1" boyut hedef yanit kod govde
  boyut=$(stat -c%s "$dosya")

  if [ "$boyut" -gt "$DOGRUDAN_YUKLEME_SINIRI" ]; then
    log "  buyuk dosya ($((boyut / 1024 / 1024)) MB), upload_url aliniyor"
    yanit=$(vt_istek "$VT_API/files/upload_url")
    kod=$(kod_of "$yanit"); govde=$(govde_of "$yanit")
    if [ "$kod" != "200" ]; then
      log "  upload_url alinamadi (HTTP $kod): $govde"
      return 1
    fi
    hedef=$(printf '%s' "$govde" | jq -r '.data')
  else
    hedef="$VT_API/files"
  fi

  yanit=$(vt_istek -X POST "$hedef" -F "file=@$dosya")
  kod=$(kod_of "$yanit"); govde=$(govde_of "$yanit")
  if [ "$kod" != "200" ]; then
    log "  yukleme basarisiz (HTTP $kod): $govde"
    return 1
  fi
  printf '%s' "$govde" | jq -r '.data.id'
}

# Analiz bitene kadar bekler. VirusTotal buyuk dosyalari kuyruga aliyor;
# 40 x 16 sn ~ 10 dakikalik tavan, kosucuyu sonsuza kadar tutmuyor.
analizi_bekle() {
  local id="$1" i yanit kod durum
  for i in $(seq 1 40); do
    yanit=$(vt_istek "$VT_API/analyses/$id")
    kod=$(kod_of "$yanit")
    if [ "$kod" != "200" ]; then
      log "  analiz sorgulanamadi (HTTP $kod)"
      return 1
    fi
    durum=$(govde_of "$yanit" | jq -r '.data.attributes.status')
    if [ "$durum" = "completed" ]; then
      return 0
    fi
    log "  analiz durumu: $durum ($i/40)"
  done
  log "  analiz zaman asimina ugradi"
  return 1
}

# --- giris kontrolu ----------------------------------------------------------

if [ "${VT_API_KEY:-}" = "" ]; then
  log "HATA: VT_API_KEY tanimli degil."
  log "Ucretsiz anahtar: https://www.virustotal.com/gui/my-apikey"
  log "CI icin depo ayarlarinda Secrets -> Actions -> VT_API_KEY."
  exit 1
fi

if [ "$#" -eq 0 ]; then
  log "Kullanim: VT_API_KEY=... $0 dosya1 [dosya2 ...]"
  exit 1
fi

command -v jq >/dev/null || { log "HATA: jq gerekli."; exit 1; }

# --- tarama ------------------------------------------------------------------

OZET=$(mktemp)
{
  echo "| Dosya | Boyut | Tespit | Isaretleyen motorlar | Rapor |"
  echo "|---|---|---|---|---|"
} >"$OZET"

HATA=0

for dosya in "$@"; do
  if [ ! -f "$dosya" ]; then
    log "HATA: dosya yok: $dosya"
    HATA=1
    continue
  fi

  ad=$(basename "$dosya")
  ozet=$(sha256sum "$dosya" | cut -d' ' -f1)
  boyut_mb=$(awk "BEGIN{printf \"%.1f\", $(stat -c%s "$dosya")/1048576}")
  log "== $ad ($boyut_mb MB)"
  log "   sha256: $ozet"

  # ONCE OZETLE SOR. Dosya VirusTotal'da zaten varsa yuklemeye gerek yok:
  # kota harcamiyor, dakikalar suren kuyrugu atliyor ve -- daha onemlisi --
  # baskasinin yukledigi bir kopyanin sonucunu da goruyoruz.
  yanit=$(vt_istek "$VT_API/files/$ozet")
  kod=$(kod_of "$yanit")

  if [ "$kod" = "404" ]; then
    log "  VirusTotal'da kayit yok, yukleniyor"
    if ! analiz_id=$(yukle "$dosya"); then
      HATA=1
      continue
    fi
    if ! analizi_bekle "$analiz_id"; then
      HATA=1
      continue
    fi
    yanit=$(vt_istek "$VT_API/files/$ozet")
    kod=$(kod_of "$yanit")
  fi

  if [ "$kod" != "200" ]; then
    log "  rapor alinamadi (HTTP $kod)"
    HATA=1
    continue
  fi

  rapor=$(govde_of "$yanit")

  kotu=$(printf '%s' "$rapor" | jq -r '.data.attributes.last_analysis_stats.malicious // 0')
  supheli=$(printf '%s' "$rapor" | jq -r '.data.attributes.last_analysis_stats.suspicious // 0')
  temiz=$(printf '%s' "$rapor" | jq -r '.data.attributes.last_analysis_stats.undetected // 0')
  toplam=$((kotu + supheli + temiz))

  motorlar=$(printf '%s' "$rapor" | jq -r '
    .data.attributes.last_analysis_results
    | to_entries
    | map(select(.value.category == "malicious" or .value.category == "suspicious"))
    | map("\(.key): `\(.value.result // "?")`")
    | join("<br>")')
  [ -n "$motorlar" ] || motorlar="_yok_"

  log "  sonuc: $((kotu + supheli)) / $toplam"

  printf '| `%s` | %s MB | **%s / %s** | %s | [rapor](https://www.virustotal.com/gui/file/%s) |\n' \
    "$ad" "$boyut_mb" "$((kotu + supheli))" "$toplam" "$motorlar" "$ozet" >>"$OZET"
done

# --- cikti -------------------------------------------------------------------

echo
cat "$OZET"
echo

if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  {
    echo "## VirusTotal taramasi"
    echo
    cat "$OZET"
    echo
    echo "Bir miktar tespit BEKLENEN durumdur: WinDivert cekirdek surucusu"
    echo "butun motorlarda \"hacking tool\" ailesinde. Ayrinti ve kullaniciya"
    echo "verilecek cevap: [docs/GUVENLIK.md](../blob/main/docs/GUVENLIK.md)."
  } >>"$GITHUB_STEP_SUMMARY"
fi

rm -f "$OZET"
exit "$HATA"
