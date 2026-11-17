#!/bin/sh
# Baut das Plugin, packt es nach plugin/dist und trägt es in den Staging-Katalog manifest-staging.json ein.
set -e
cd "$(dirname "$0")/.."
sh plugin/build.sh
python3 -I - <<'PY'
import hashlib, json, re, zipfile, datetime, os
ver = re.search(r'<AssemblyVersion>([^<]+)<', open('plugin/src/Jellyfin.Plugin.CinemaGlass.csproj').read()).group(1)
os.makedirs('plugin/dist', exist_ok=True)
zp = 'plugin/dist/cinema-glass_%s.zip' % ver
with zipfile.ZipFile(zp, 'w', zipfile.ZIP_DEFLATED) as z:
    info = zipfile.ZipInfo('Jellyfin.Plugin.CinemaGlass.dll', (2026, 1, 1, 0, 0, 0)); info.compress_type = zipfile.ZIP_DEFLATED; info.external_attr = 0o644 << 16
    z.writestr(info, open('plugin/out/Jellyfin.Plugin.CinemaGlass.dll', 'rb').read())
    wn = zipfile.ZipInfo('whatsnew.json', (2026, 1, 1, 0, 0, 0)); wn.compress_type = zipfile.ZIP_DEFLATED; wn.external_attr = 0o644 << 16
    z.writestr(wn, open('whatsnew.json', 'rb').read())
md5 = hashlib.md5(open(zp, 'rb').read()).hexdigest()
# Neue Versionen gehen erst in den Staging-Katalog (manifest-staging.json); freigegeben wird mit promote.py
m = json.load(open('manifest-staging.json')) if os.path.exists('manifest-staging.json') else [dict(json.load(open('manifest.json'))[0], versions=[])]
# Release Notes: Einträge dieser Version aus whatsnew.json (Deutsch), sonst Verweis aufs Repository
norm = lambda v: tuple((list(map(int, v.split('.'))) + [0, 0, 0, 0])[:4])
notes = [i['de'][0] + ': ' + i['de'][1] for e in json.load(open('whatsnew.json')) if norm(e['version']) == norm(ver) for i in e.get('items', [])]
entry = {"version": ver, "changelog": '\n'.join('- ' + n for n in notes) or "Siehe Repository.", "targetAbi": "12.4.0.0",
         "sourceUrl": "https://raw.githubusercontent.com/derpfiff/cinema-glass/main/" + zp,
         "checksum": md5, "timestamp": datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')}
m[0]["versions"] = [entry] + [v for v in m[0]["versions"] if v["version"] != ver]
json.dump(m, open('manifest-staging.json', 'w'), indent=2, ensure_ascii=False); open('manifest-staging.json', 'a').write('\n')
print(zp, md5)
PY
