#!/usr/bin/env python3
"""Lädt Avatare aus einer öffentlichen Google-Tabelle in den Avatar-Ordner von Cinema Glass.

Die Tabelle braucht die Spalten ID und URL (wie die Sammlung der Netflix-Profilbilder, siehe docs/PLUGIN.md).
In ID steht „AVATAR|…|<Netflix-Titelnummer>“; je Titel entsteht ein Unterordner, benannt nach dem Titel
(der Name kommt von der öffentlichen Titelseite bei Netflix). Die Titelnummer der Klassiker wird zu „00 Klassiker“,
damit die Reihe oben steht. Schon vorhandene Dateien werden übersprungen.

Aufruf:  python3 avatars-aus-tabelle.py <Tabellen-ID> <Zielordner>
Beispiel: python3 avatars-aus-tabelle.py 1T5q4qNTFOiU-45iTDUDDkxsj_Hb9S9mZpg4GyLNYarw \\
          "<Jellyfin-Konfiguration>/plugins/configurations/CinemaGlass-avatars"
"""
import concurrent.futures as cf
import csv
import html
import io
import os
import re
import sys
import urllib.request

UA = {'User-Agent': 'Mozilla/5.0', 'Accept-Language': 'en'}
CLASSICS = '80148069'


def fetch(url, timeout=30):
    return urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=timeout).read()


def clean(name):
    return re.sub(r'[\\/:*?"<>|]+', '', name).strip()


def show_name(tid, cache={}):
    if tid == CLASSICS:
        return '00 Klassiker'
    if tid not in cache:
        try:
            page = fetch('https://www.netflix.com/title/' + tid, 25).decode('utf-8', 'ignore')
            m = re.search(r'<title[^>]*>(.*?)</title>', page, re.S)
            name = re.sub(r'^Watch\s+', '', re.sub(r'\s*\|\s*Netflix.*$', '', html.unescape(m.group(1)).strip())) if m else ''
        except Exception:
            name = ''
        cache[tid] = clean(name) if name and name.lower() != 'watch' else 'Weitere'
    return cache[tid]


def main():
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    sheet, target = sys.argv[1], sys.argv[2]
    rows = csv.DictReader(io.StringIO(fetch('https://docs.google.com/spreadsheets/d/%s/export?format=csv' % sheet).decode('utf-8')))
    jobs, used = [], set()
    for r in rows:
        url, ident = (r.get('URL') or '').strip(), (r.get('ID') or '').strip()
        if not url.startswith('http') or not ident.startswith('AVATAR'):
            continue
        tid = re.sub(r'\D', '', ident.split('|')[-1])
        if len(tid) < 8:
            tid = CLASSICS if not tid else tid
        show = show_name(tid) if len(tid) >= 8 else 'Weitere'
        title = clean(r.get('TITLE') or '') or 'Avatar'
        base, n = title, 2
        while (show, title) in used:
            title, n = '%s %d' % (base, n), n + 1
        used.add((show, title))
        jobs.append((url, os.path.join(target, show, title + ('.png' if '.png' in url.lower() else '.jpg'))))

    def get(job):
        url, path = job
        if os.path.exists(path):
            return True
        os.makedirs(os.path.dirname(path), exist_ok=True)
        try:
            data = fetch(url)
        except Exception:
            return False
        with open(path, 'wb') as f:
            f.write(data)
        return True

    with cf.ThreadPoolExecutor(4) as ex:
        ok = sum(ex.map(get, jobs))
    print('%d von %d Avataren in %s' % (ok, len(jobs), target))


if __name__ == '__main__':
    main()
