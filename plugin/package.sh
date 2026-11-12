#!/bin/sh
# Baut das Plugin, packt es nach plugin/dist und trägt Prüfsumme und Zeit in manifest.json ein.
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
md5 = hashlib.md5(open(zp, 'rb').read()).hexdigest()
m = json.load(open('manifest.json'))
entry = {"version": ver, "changelog": "Siehe Repository.", "targetAbi": "12.4.0.0",
         "sourceUrl": "https://raw.githubusercontent.com/derpfiff/cinema-glass/main/" + zp,
         "checksum": md5, "timestamp": datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')}
m[0]["versions"] = [entry] + [v for v in m[0]["versions"] if v["version"] != ver]
json.dump(m, open('manifest.json', 'w'), indent=2, ensure_ascii=False); open('manifest.json', 'a').write('\n')
print(zp, md5)
PY
