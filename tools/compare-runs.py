"""Birden fazla --exhaustive raporunu karsilastirir.

Kesisim ARGUMANLAR uzerinden alinir, aday kimligi uzerinden degil: merdiven
adaylarinin kimlikleri surumler arasinda degisebiliyor, argumanlar ise adayin
gercek kimligi (ConfigStore.AddLearned de bu anahtari kullaniyor).

Amac tek bir kosumun gurultusunu ayiklamak: bir aday ancak butun kosumlarda
calistiysa "tekrarlanabilir" sayilir.
"""
import json
import sys
from collections import OrderedDict

runs = []
for path in sys.argv[1:]:
    with open(path, encoding='utf-8-sig') as handle:
        doc = json.load(handle)

    ok = {}
    seen = OrderedDict()
    for attempt in doc['attempts']:
        key = (attempt['section'], attempt['args'])
        seen.setdefault(key, attempt['candidateId'])
        if attempt['succeeded']:
            ok.setdefault(key, set()).add(attempt['targetCategory'])

    runs.append({'path': path, 'ok': ok, 'seen': seen,
                 'attempts': len(doc['attempts']),
                 'duration': doc['durationSeconds']})

for run in runs:
    print(f"{run['path'].split(chr(92))[-1]:<20} {run['attempts']:>3} deneme "
          f"{run['duration']:>6.1f} sn  {len(run['ok']):>2} calisan aday")

if len(runs) < 2:
    sys.exit(0)

# Yalnizca HER kosumda denenmis adaylar karsilastirilabilir. Butce siniri
# yuzunden bir kosumda hic denenmemis bir aday "basarisiz" sayilmamali.
common = set(runs[0]['seen'])
for run in runs[1:]:
    common &= set(run['seen'])

print(f"\nHer kosumda denenen aday sayisi: {len(common)}")

rows = []
for key in common:
    hits = sum(1 for run in runs if key in run['ok'])
    if hits:
        rows.append((key, hits))

rows.sort(key=lambda r: (r[0][0], -r[1], runs[0]['seen'][r[0]]))

print(f"\n{'bolum':<8} {len(runs)}'te  aday")
print('-' * 78)
last = None
for (section, args), hits in rows:
    if section != last:
        print()
        last = section
    name = runs[0]['seen'][(section, args)]
    mark = 'TAM ' if hits == len(runs) else '    '
    print(f"{section:<8} {mark}{hits}/{len(runs)}  {name}")
    print(f"{'':<17}{args}")
