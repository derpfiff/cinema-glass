/* ============================================================================
   atv-seerr.js – Seerr-Treffer in der Jellyfin-Suche (Apple-TV-Stil)
   Teil der „Extras“ zum Apple-TV-Klon. Eigenständig, nutzt nur window.ApiClient
   und die CSS-Variablen/Klassen des Themes (fällt ohne Theme auf eigene Werte zurück).

   Datenweg (kein Schlüssel im Browser): das Skript ruft NUR das Plugin auf
   (<server>/CinemaGlass/seerr/api/v1/…). Das Plugin lässt nur angemeldete
   Jellyfin-Nutzer durch und hängt den Seerr-Schlüssel serverseitig an.
   Ohne Plugin bleibt dieses Modul still.
   ========================================================================== */
(function () {
    'use strict';
    if (window.__atvSeerr) return;
    window.__atvSeerr = true;
    var PLUG = window.__cinemaGlass;
    if (!PLUG || !PLUG.base) return;                                         // läuft nur mit dem Plugin (dessen Weiterleitung)

    var CFG = {
        minChars: 2,
        debounceMs: 450,
        maxCards: 18,
        posterSize: 'w300_and_h450_face',
        language: (document.documentElement.lang || navigator.language || 'de').slice(0, 2)
    };
    var DE = (CFG.language !== 'en');
    var T = DE ? {
        title: 'Nicht in deiner Mediathek', request: 'Anfragen', requested: 'Angefragt', processing: 'In Arbeit',
        partial: 'Teilweise', available: 'Verfügbar', movie: 'Film', series: 'Serie', seasons: 'Staffeln anfragen',
        all: 'Alle Staffeln', cancel: 'Abbrechen', send: 'Anfragen', ok: 'Anfrage gesendet', fail: 'Anfrage fehlgeschlagen',
        offline: 'Seerr nicht erreichbar', noconn: 'Keine Verbindung zu Seerr', retry: 'Erneut versuchen', season: 'Staffel', episodes: 'Folgen', more: 'Mehr', close: 'Schließen', quality: 'Qualität', standard: 'Standard',
        open: 'Öffnen', moreSeasons: 'Weitere Staffeln anfragen', reqAll: 'Alle Staffeln anfragen', reqOne: '1 Staffel anfragen', reqN: '%1 Staffeln anfragen', noSeasons: 'Zu dieser Serie sind noch keine Staffeln bekannt.'
    } : {
        title: 'Not in your library', request: 'Request', requested: 'Requested', processing: 'Processing',
        partial: 'Partial', available: 'Available', movie: 'Movie', series: 'Series', seasons: 'Request seasons',
        all: 'All seasons', cancel: 'Cancel', send: 'Request', ok: 'Request sent', fail: 'Request failed',
        offline: 'Seerr unreachable', noconn: 'No connection to Seerr', retry: 'Retry', season: 'Season', episodes: 'episodes', more: 'More', close: 'Close', quality: 'Quality', standard: 'Default',
        open: 'Open', moreSeasons: 'Request more seasons', reqAll: 'Request all seasons', reqOne: 'Request 1 season', reqN: 'Request %1 seasons', noSeasons: 'No seasons are known for this show yet.'
    };

    // Optik: dieselben Bausteine wie das Theme (Regal, Poster, Detailseite .atv-detail, Pille, Glas-Kreis).
    // Die Variablen (--tv-*, --atv-*) kommen aus dem Theme; ohne Theme greifen die Rückfallwerte.
    var CSS = [
        '.atv-seerr{color:#fff;margin:calc(var(--tv-shelf-gap,48px) - var(--tv-focus-pad,10px)) 0 40px!important;font-family:var(--tv-font,-apple-system,BlinkMacSystemFont,"SF Pro Display","Inter",system-ui,sans-serif)}',
        '.atv-seerr[hidden]{display:none}',
        '.atv-seerr__retry{margin-left:10px;padding:6px 14px;border:0;border-radius:999px;color:#fff;background:rgba(120,120,128,.4);font:inherit;font-weight:600;cursor:pointer}',
        '.atv-seerr__msg{margin:0;color:rgba(255,255,255,.55);font-size:15px}',
        '.atv-seerr__title{margin:0;padding:0 var(--tv-gutter,16px);font-size:var(--tv-shelf-title,20px);line-height:1.2;font-weight:700;letter-spacing:.012em;color:#fff}',
        '.atv-seerr__count{margin-left:.45em;font-weight:400;color:rgba(255,255,255,.5);font-size:.72em;letter-spacing:0}',
        '.atv-seerr__wrap{position:relative}',
        '.atv-seerr__grid{display:flex;gap:var(--tv-gap,16px);overflow-x:auto;overflow-y:hidden;scrollbar-width:none;-webkit-overflow-scrolling:touch;padding:var(--tv-title-to-cards,14px) var(--tv-gutter,16px) 14px;scroll-padding-left:var(--tv-gutter,16px)}',
        '.atv-seerr__grid::-webkit-scrollbar{display:none}',
        '.atv-seerr__card{flex:0 0 auto;width:var(--tv-poster-w,144px);cursor:pointer;outline:0;-webkit-tap-highlight-color:transparent}',
        '.atv-seerr__poster{position:relative;aspect-ratio:2/3;border-radius:calc(var(--tv-poster-w,144px) * .077);overflow:hidden;background:#1c1c1e;transition:transform .35s cubic-bezier(.2,.8,.2,1),box-shadow .35s cubic-bezier(.2,.8,.2,1)}',
        '.atv-seerr__poster img{width:100%;height:100%;object-fit:cover;display:block}',
        '@media (hover:hover){.atv-seerr__card:hover .atv-seerr__poster,.atv-seerr__card:focus-visible .atv-seerr__poster{transform:scale(1.05);box-shadow:0 18px 36px rgba(0,0,0,.5)}}',
        '.atv-seerr__card:focus-visible .atv-seerr__poster{outline:3px solid #fff;outline-offset:3px}',
        '.atv-seerr__name{margin:10px 2px 0;font-size:clamp(12px,.9vw,15px);font-weight:600;line-height:1.25;color:rgba(255,255,255,.92);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}',
        '.atv-seerr__sub{margin:1px 2px 0;font-size:clamp(11px,.78vw,13px);line-height:1.3;color:rgba(255,255,255,.55);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}',
        '.atv-seerr__card--noimg .atv-seerr__poster{display:flex;align-items:center;justify-content:center;padding:10px;text-align:center;font-size:13px;font-weight:600;color:rgba(255,255,255,.7)}',
        /* Detailansicht (wie die Detailseite eines Titels aus der Mediathek) */
        '.atv-xd{position:fixed;inset:0;z-index:1200;overflow-x:hidden;overflow-y:auto;-webkit-overflow-scrolling:touch;overscroll-behavior:contain;background:var(--atv-bg,#000000);color:#fff;animation:atv-xd-in .28s ease-out;font-family:var(--tv-font,-apple-system,BlinkMacSystemFont,"SF Pro Display","Inter",system-ui,sans-serif)}',
        '@keyframes atv-xd-in{from{opacity:0;transform:translateY(12px)}to{opacity:1;transform:none}}',
        '.atv-xd .atv-detail{top:0;margin:0}',
        '.atv-xd .atv-detail__art{opacity:0;transition:opacity .4s ease}.atv-xd .atv-detail__art.is-in{opacity:1}',
        '.atv-xd__desc{display:-webkit-box;-webkit-box-orient:vertical;-webkit-line-clamp:3;overflow:hidden;cursor:pointer;padding-right:0!important}',
        '.atv-xd__desc.is-open{-webkit-line-clamp:unset;display:block}',
        '.atv-xd__back{position:fixed;z-index:2;top:max(18px,env(safe-area-inset-top,0px));left:max(16px,calc(var(--tv-gutter,60px) - 8px));width:44px;height:44px;padding:0;border:0;border-radius:50%;display:flex;align-items:center;justify-content:center;color:#fff;cursor:pointer;background:rgba(40,40,42,.55);-webkit-backdrop-filter:blur(24px) saturate(1.8);backdrop-filter:blur(24px) saturate(1.8);box-shadow:inset 0 0 0 1px rgba(255,255,255,.16),0 8px 24px rgba(0,0,0,.3)}',
        '.atv-xd__back svg{width:24px;height:24px;fill:currentColor}',
        'html.atv-mbar-on .atv-xd__back{display:none}',   /* Handy: der Zurück-Knopf des Themes liegt darüber und schließt die Ansicht */
        '.atv-xd .atv-pill:disabled{opacity:.55;cursor:default;transform:none!important}',
        '.atv-xd .atv-pill.is-state{background:rgba(120,120,128,.36);color:#fff;-webkit-backdrop-filter:blur(24px) saturate(1.6);backdrop-filter:blur(24px) saturate(1.6);box-shadow:inset 0 0 0 1px rgba(255,255,255,.14);opacity:1}',
        '.atv-xd .atv-pill.is-state i{width:9px;height:9px;border-radius:50%;background:currentColor;display:inline-block}',
        '.atv-xd .atv-pill.is-state span{color:#fff}',
        '.atv-xd__note{margin:0;font-size:max(11px,calc(16 * var(--atv-u,.052vw)));color:rgba(255,255,255,.64)}',
        '.atv-xd__sec{padding:0 var(--tv-gutter,16px);margin:calc(var(--tv-shelf-gap,48px) - 20px) 0 0}',
        '.atv-xd__sec:last-child{padding-bottom:calc(64px + env(safe-area-inset-bottom,0px))}',
        '.atv-xd__h{margin:0 0 var(--tv-title-to-cards,14px);font-size:var(--tv-shelf-title,20px);font-weight:700;letter-spacing:.012em;line-height:1.2}',
        '.atv-xd__seasons{display:flex;flex-wrap:wrap;gap:10px}',
        '.atv-xd__season{display:inline-flex;align-items:center;gap:8px;height:40px;padding:0 18px;border-radius:999px;background:rgba(120,120,128,.24);box-shadow:inset 0 0 0 1px rgba(255,255,255,.12);font-size:15px;font-weight:600;cursor:pointer;user-select:none;transition:background .2s,color .2s}',
        '.atv-xd__season input{position:absolute;opacity:0;pointer-events:none}',
        '.atv-xd__season small{font-weight:400;font-size:13px;opacity:.6}',
        '.atv-xd__season.is-on{background:#fff;color:#000}',
        '.atv-xd__season.is-done{opacity:.45;cursor:default}',
        '.atv-xd__season em{font-style:normal;font-weight:500;font-size:11px;letter-spacing:.3px;text-transform:uppercase;opacity:.6}',
        '.atv-xd__season.is-on em{opacity:.55}',
        '.atv-xd__cast{display:flex;gap:var(--tv-gap,16px);overflow-x:auto;scrollbar-width:none;margin:0 calc(-1 * var(--tv-gutter,16px));padding:0 var(--tv-gutter,16px) 8px}',
        '.atv-xd__cast::-webkit-scrollbar{display:none}',
        '.atv-xd__person{flex:0 0 auto;width:clamp(94px,7.4vw,142px);text-align:center}',
        '.atv-xd__person span{display:block;aspect-ratio:1;border-radius:50%;overflow:hidden;background:#1c1c1e}',
        '.atv-xd__person img{width:100%;height:100%;object-fit:cover;display:block}',
        '.atv-xd__person b{display:block;margin-top:10px;font-size:clamp(12px,.83vw,16px);font-weight:500;color:rgba(255,255,255,.92);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}',
        '.atv-xd__person small{display:block;font-size:clamp(11px,.73vw,14px);color:rgba(255,255,255,.6);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}',
        '.atv-toast{position:fixed;left:50%;bottom:calc(28px + env(safe-area-inset-bottom,0px));z-index:10001;transform:translateX(-50%) translateY(10px);opacity:0;padding:10px 18px;border-radius:999px;background:rgba(44,44,48,.8);color:#fff;font:600 14px -apple-system,BlinkMacSystemFont,system-ui,sans-serif;-webkit-backdrop-filter:blur(20px);backdrop-filter:blur(20px);box-shadow:0 10px 30px rgba(0,0,0,.4),inset 0 0 0 1px rgba(255,255,255,.14);transition:opacity .25s,transform .25s;pointer-events:none}',
        '.atv-toast.is-on{opacity:1;transform:translateX(-50%)}',
        '@media (max-width:800px){.atv-xd__seasons{gap:8px}.atv-xd__season{height:36px;padding:0 14px;font-size:14px}.atv-xd .atv-detail__actions>.atv-pill:first-child{flex:1 1 100%}.atv-xd__note{order:4;text-align:center;align-self:center}}'
    ].join('\n');

    /* ------------------------------------------------------------ Hilfen */
    function el(tag, cls, text) { var e = document.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; }
    function api() { return window.ApiClient; }
    function apiReady() { var a = api(); return !!(a && typeof a.accessToken === 'function' && a.accessToken() && a.getCurrentUserId && a.getCurrentUserId()); }
    function injectCss() { if (document.getElementById('atv-seerr-css')) return; var s = el('style'); s.id = 'atv-seerr-css'; s.textContent = CSS; document.head.appendChild(s); }
    function toast(msg) {
        var t = document.querySelector('.atv-toast') || document.body.appendChild(el('div', 'atv-toast'));
        t.textContent = msg; t.classList.add('is-on'); clearTimeout(toast.t); toast.t = setTimeout(function () { t.classList.remove('is-on'); }, 2600);
    }
    function proxyUrl(path) { return PLUG.base + '/seerr' + path; }          // Weiterleitung des Plugins
    function call(path, opts) {
        opts = opts || {};
        var h = { 'Authorization': 'MediaBrowser Token="' + api().accessToken() + '"', 'Accept': 'application/json' };
        if (opts.body) h['Content-Type'] = 'application/json';
        return fetch(proxyUrl(path), { method: opts.method || 'GET', headers: h, body: opts.body ? JSON.stringify(opts.body) : undefined, credentials: 'omit', signal: opts.signal })
            .then(function (r) {
                if (r.status === 401 || r.status === 404 || r.status === 502 || r.status === 503) { var e = new Error('seerr ' + r.status); e.status = r.status; throw e; }
                return r.json().then(function (j) { if (!r.ok) { var e2 = new Error((j && j.message) || ('HTTP ' + r.status)); e2.status = r.status; e2.body = j; throw e2; } return j; });
            });
    }
    function sleep(ms) { return new Promise(function (r) { setTimeout(r, ms); }); }   // nicht 'wait': so heißt weiter unten der Timer im Router
    function callRetry(path, opts, n) {
        return call(path, opts).catch(function (e) {
            if (e && e.name === 'AbortError') throw e;
            var transient = !e.status || e.status === 401 || e.status === 502 || e.status === 503 || e.status === 504;
            if (transient && n > 0) return sleep(1500 * (5 - n)).then(function () { return callRetry(path, opts, n - 1); });
            throw e;
        });
    }
    // Einmalige Erreichbarkeitsprüfung je Seitenaufruf (404/401 = im Plugin nicht eingerichtet → Modul bleibt still)
    var avail = null;
    function available() {
        if (avail) return avail;
        avail = callRetry('/api/v1/status', {}, 4).then(function () { return true; }).catch(function (e) { avail = null; if (e.status === 404) { avail = Promise.resolve(false); return false; } available.err = e.status || 'Netz'; return false; });
        return avail;
    }
    function posterUrl(p) { return p ? proxyUrl('/imageproxy/tmdb/t/p/' + CFG.posterSize + p) : null; }
    // Bilder laufen ebenfalls durch das Plugin (Token-Prüfung): <img src> kann keinen Header senden,
    // daher per fetch + Blob-URL. Kein Token in URLs, nichts im Browser-Verlauf.
    var blobCache = {};
    function loadImg(im, url) {
        if (!url) return;
        if (blobCache[url]) { im.src = blobCache[url]; return; }
        fetch(url, { headers: { 'Authorization': 'MediaBrowser Token="' + api().accessToken() + '"' }, credentials: 'omit' })
            .then(function (r) { if (!r.ok) throw new Error('img ' + r.status); return r.blob(); })
            .then(function (b) { blobCache[url] = URL.createObjectURL(b); im.src = blobCache[url]; })
            .catch(function () { im.dispatchEvent(new Event('error')); });
    }
    // Plakate erst holen, wenn die Karte in Sichtweite kommt: lange Regale und Reihen weit unten auf der Seite lösten
    // sonst Dutzende Bildabrufe aus, die niemand sieht.
    var lazyIo = null;
    function lazyImg(c, im, url) {
        if (!url || blobCache[url] || !window.IntersectionObserver) { loadImg(im, url); return; }
        if (!lazyIo) lazyIo = new IntersectionObserver(function (es) {
            es.forEach(function (e) {
                if (!e.isIntersecting) return;
                lazyIo.unobserve(e.target);
                var job = e.target.__atvImg; e.target.__atvImg = null;
                if (job) loadImg(job[0], job[1]);
            });
        }, { rootMargin: '400px 600px' });
        c.__atvImg = [im, url]; lazyIo.observe(c);
    }
    // Titel: TMDB liefert für manche (unveröffentlichte) Titel eine fremdsprachige „Übersetzung“ in anderer Schrift –
    // dann den Originaltitel zeigen, sofern der lateinisch geschrieben ist.
    function title(r) {
        var t = r.title || r.name || '', o = r.originalTitle || r.originalName || '';
        if (o && t && !/[A-Za-z\u00c0-\u024f]/.test(t) && /[A-Za-z]/.test(o)) return o;
        return t || o;
    }
    function year(r) { var d = r.releaseDate || r.firstAirDate || ''; return d ? d.slice(0, 4) : ''; }
    function statusOf(r) { var mi = r.mediaInfo; return mi && mi.status ? mi.status : 1; }   // 1 unbekannt 2 angefragt 3 in Arbeit 4 teilweise 5 verfügbar
    function inLibrary(r) { var mi = r.mediaInfo; return !!(mi && (mi.jellyfinMediaId || mi.jellyfinMediaId4k)); }
    var STATUS = { 2: ['requested', T.requested], 3: ['processing', T.processing], 4: ['partial', T.partial], 5: ['available', T.available] };
    var COLOR = { requested: '#ffd60a', processing: '#64d2ff', partial: '#ff9f0a', available: '#30d158' };
    var ICON = {
        back: 'M15.41 7.41 14 6l-6 6 6 6 1.41-1.41L10.83 12z',
        get: 'M11 4h2v8.17l2.59-2.58L17 11l-5 5-5-5 1.41-1.41L11 12.17zM5 18h14v2H5z',
        film: 'M4 4h16a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2zm0 2v2h2V6zm0 4v2h2v-2zm0 4v2h2v-2zm16-8h-2v2h2zm0 4h-2v2h2zm0 4h-2v2h2zM8 6v12h8V6z',
        tv: 'M21 3H3a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h5v2h8v-2h5a2 2 0 0 0 2-2V5a2 2 0 0 0-2-2zm0 14H3V5h18z',
        star: 'M12 17.27 18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21z',
        play: 'M8 5v14l11-7z',
        plus: 'M11 5h2v6h6v2h-6v6h-2v-6H5v-2h6z',
        check: 'M9.5 16.2 5.3 12l-1.4 1.4 5.6 5.6 12-12-1.4-1.4z',
        close: 'M18.3 5.71 12 12l6.3 6.29-1.41 1.42L10.59 13.4 4.3 19.71 2.88 18.3 9.17 12 2.88 5.71 4.3 4.29l6.29 6.3 6.3-6.3z'
    };
    function svg(path) {
        var ns = 'http://www.w3.org/2000/svg', s = document.createElementNS(ns, 'svg'); s.setAttribute('viewBox', '0 0 24 24'); s.setAttribute('aria-hidden', 'true');
        var p = document.createElementNS(ns, 'path'); p.setAttribute('d', path); s.appendChild(p); return s;
    }
    function tmdbImg(size, path) { return path ? proxyUrl('/imageproxy/tmdb/t/p/' + size + path) : null; }
    function runtimeText(min) {
        if (!min) return ''; var h = Math.floor(min / 60), m = min % 60;
        return DE ? (h ? h + ' Std.' + (m ? ' ' + m + ' Min.' : '') : m + ' Min.') : (h ? h + 'h' + (m ? ' ' + m + 'm' : '') : m + 'm');
    }

    /* ------------------------------------------------------------ Karten */
    // Karte = reines Poster wie in der Mediathek, Titel/Jahr darunter. KEIN Anfragen-Knopf auf der Karte
    // (versehentliche Anfragen): angefragt wird nur in der Detailansicht.
    function card(r) {
        var c = el('div', 'atv-seerr__card'); c.tabIndex = 0; c.setAttribute('role', 'button'); c.__atvR = r;
        var box = el('div', 'atv-seerr__poster'); c.appendChild(box);
        var im = el('img'); im.alt = ''; im.loading = 'lazy';
        im.addEventListener('error', function () { im.remove(); c.classList.add('atv-seerr__card--noimg'); box.insertBefore(document.createTextNode(title(r)), box.firstChild); });
        box.appendChild(im); lazyImg(c, im, posterUrl(r.posterPath));
        c.appendChild(el('div', 'atv-seerr__name', title(r)));
        c.appendChild(el('div', 'atv-seerr__sub', [r.mediaType === 'tv' ? T.series : T.movie, year(r)].filter(Boolean).join(' · ')));
        setStatus(c, r);
        c.addEventListener('click', function () { openDetail(r, c); });
        c.addEventListener('keydown', function (ev) { if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); openDetail(r, c); } });
        return c;
    }
    // Stand in der Ecke oben links, überall im Theme derselbe: „+“ auf Glas = fehlt (das Plakat ist dazu eine Spur
    // gedämpft), Zeichen mit Kurzmarke = angefragt, weißer Punkt mit ▶ = vorhanden. Aussehen: Stylesheet des Themes.
    function setStatus(c, r) {
        if (!c || !c.classList || !c.classList.contains('atv-seerr__card')) return;   // andere Karten führt das Theme selbst nach
        var box = c.querySelector('.atv-seerr__poster') || c, s = statusOf(r), D = window.__atvDiscover, flag;
        var rq = D && D.requestOf ? D.requestOf(r.mediaType + ':' + r.id) : null, there = s >= 4, open = s === 2 || s === 3;
        var old = box.querySelector('.atv-dc__flag'); if (old) old.remove();
        c.classList.toggle('is-there', there); c.classList.toggle('is-req', open); c.classList.toggle('is-missing', !there && !open);
        if (open) {
            flag = el('span', 'atv-dc__flag atv-dc__flag--req');
            flag.appendChild(D && D.mark ? D.mark(rq) : el('i'));
            flag.appendChild(el('b', null, (rq && rq.short) || STATUS[s][1]));
        } else if (there) { flag = el('span', 'atv-dc__flag atv-dc__flag--play'); flag.appendChild(svg(ICON.play)); }
        else { flag = el('span', 'atv-dc__flag atv-dc__flag--add'); flag.appendChild(svg(ICON.plus)); }
        box.appendChild(flag);
        c.setAttribute('aria-label', title(r) + (year(r) ? ' (' + year(r) + ')' : '') + ', ' + (there ? T.available : open ? ((rq && rq.text) || STATUS[s][1]) : T.title));
    }
    // Neuer Stand der Anfragen vom Server (Theme, „Entdecken“): Marken der sichtbaren Karten nachführen.
    document.addEventListener('atv-dsc-requests', function () {
        [].forEach.call(document.querySelectorAll('.atv-seerr__card'), function (c) { if (c.__atvR) setStatus(c, c.__atvR); });
    });

    /* ------------------------------------------------------------ Anfrage */
    // Neuer Stand eines Titels (angefragt, inzwischen vorhanden): Regale, die Ergebnisse zwischenspeichern, hören mit.
    function announce(r) {
        try { document.dispatchEvent(new CustomEvent('atv-seerr-status', { detail: { mediaType: r.mediaType, id: r.id, status: statusOf(r), inLibrary: inLibrary(r) } })); } catch (e) { /* ohne CustomEvent: Stand erst beim nächsten Abruf */ }
    }
    // seasons: Liste von Staffelnummern oder „all“ (nur Serien).
    function sendRequest(r, seasons, c, quality) {
        var body = { mediaType: r.mediaType, mediaId: r.id };
        // Qualitätsprofil nur mitschicken, wenn bewusst ein anderes als der Standard gewählt wurde
        if (quality && quality.profileId != null) { body.serverId = quality.serverId; body.profileId = quality.profileId; if (quality.rootFolder) body.rootFolder = quality.rootFolder; }
        if (r.mediaType === 'tv') body.seasons = seasons && seasons !== 'all' && seasons.length ? seasons : 'all';
        return call('/api/v1/request', { method: 'POST', body: body }).then(function (res) {
            r.mediaInfo = r.mediaInfo || {}; r.mediaInfo.status = (res && res.media && res.media.status) || 2;
            setStatus(c, r);
            announce(r);
            toast(T.ok);
            return res || {};
        }).catch(function (e) {
            console.warn('[atv-seerr] request failed', e);
            toast(T.fail + (e.body && e.body.message ? ': ' + e.body.message : ''));
            throw e;
        });
    }
    // Qualitätsprofile aus Radarr/Sonarr (über Seerr), je Dienst einmal geholt. null: keine Wahl (ein Profil, Dienst schläft).
    var services = {};
    function service(kind) {
        if (services[kind]) return services[kind];
        services[kind] = call('/api/v1/service/' + kind).then(function (list) {
            var sv = (list || []).filter(function (x) { return x.isDefault && !x.is4k; })[0] || (list || []).filter(function (x) { return !x.is4k; })[0];
            return sv ? call('/api/v1/service/' + kind + '/' + sv.id) : null;
        }).then(function (d) {
            return d && d.profiles && d.profiles.length > 1 ? { server: d.server, profiles: d.profiles } : null;
        }).catch(function () { services[kind] = null; return null; });
        return services[kind];
    }
    // Auswahl des Qualitätsprofils als Pillen; Standard = das in Seerr hinterlegte Profil. set(null) = Standard.
    function qualityChips(box, svc, current, set, name) {
        var def = svc.server.activeProfileId, cur = current && current.profileId != null ? current.profileId : def, chips = [];
        box.textContent = '';
        svc.profiles.forEach(function (pf) {
            var lab = el('label', 'atv-xd__season' + (pf.id === cur ? ' is-on' : '')); var cb = el('input'); cb.type = 'radio'; cb.name = name; cb.checked = pf.id === cur;
            lab.appendChild(cb); lab.appendChild(el('span', null, pf.name)); if (pf.id === def) lab.appendChild(el('em', null, T.standard));
            cb.addEventListener('change', function () {
                set(pf.id === def ? null : { serverId: svc.server.id, profileId: pf.id, rootFolder: svc.server.activeDirectory });
                chips.forEach(function (c2) { c2.lab.classList.toggle('is-on', c2.cb.checked); });
            });
            chips.push({ lab: lab, cb: cb }); box.appendChild(lab);
        });
    }

    /* ------------------------------------------------------------ Staffeln wählen */
    // Fenster wie in Seerr: „Alle Staffeln“ ist vorgewählt, einzelne lassen sich ab- und anwählen; was schon da oder
    // angefragt ist, steht gekennzeichnet und gesperrt in der Liste. Dazu, wenn Seerr mehrere kennt, das Qualitätsprofil;
    // unten wird bestätigt. tvP: Seerrs Auskunft zur Serie, svcP: Profile (beides Promises); send(seasons, quality)
    // stellt die Anfrage. Ergebnis: Antwort der Anfrage, null bei „Abbrechen“.
    function seasonSheet(r, tvP, svcP, send) {
        return new Promise(function (resolve) {
            var old = document.querySelector('.atv-sheet'); if (old) old.remove();
            var wrap = el('div', 'atv-sheet atv-sheet--seasons'); wrap.setAttribute('role', 'dialog'); wrap.setAttribute('aria-modal', 'true'); wrap.setAttribute('aria-label', T.seasons + ': ' + title(r));
            var box = el('div', 'atv-sheet__box'); wrap.appendChild(box);
            var x = el('button', 'atv-sheet__close'); x.type = 'button'; x.setAttribute('aria-label', T.close); x.appendChild(svg(ICON.close)); box.appendChild(x);
            var body = el('div', 'atv-sheet__body'); box.appendChild(body);
            body.appendChild(el('div', 'atv-sheet__tag', T.seasons.toUpperCase()));
            body.appendChild(el('h2', 'atv-sheet__title', title(r)));
            var list = el('div', 'atv-ss'); body.appendChild(list);
            var qsec = el('div', 'atv-ss__quality'); qsec.hidden = true; body.appendChild(qsec);
            var foot = el('div', 'atv-ss__foot'), cancel = el('button', 'atv-pill atv-pill--glass'), go = el('button', 'atv-pill'), goText = el('span', null, T.request);
            cancel.type = go.type = 'button'; cancel.appendChild(el('span', null, T.cancel)); go.appendChild(svg(ICON.plus)); go.appendChild(goText); go.disabled = true;
            foot.appendChild(cancel); foot.appendChild(go); box.appendChild(foot);
            for (var i = 0; i < 4; i++) list.appendChild(el('div', 'atv-ss__row atv-ss__row--ph'));
            var prevOv = document.documentElement.style.overflow, shut = false, rows = [], all = null, quality = null, openedAt = Date.now();
            function onKey(e) { if (e.key === 'Escape') { e.stopPropagation(); close(null); } }
            function close(result) {
                if (shut) return; shut = true;
                document.removeEventListener('keydown', onKey, true); document.documentElement.style.overflow = prevOv;
                wrap.classList.add('is-out'); setTimeout(function () { wrap.remove(); }, 180);
                resolve(result);
            }
            function chosen() { return rows.filter(function (x2) { return x2.cb.checked && !x2.cb.disabled; }).map(function (x2) { return x2.n; }); }
            function update() {
                var free = rows.filter(function (x2) { return !x2.cb.disabled; }), sel = chosen();
                rows.forEach(function (x2) { x2.row.classList.toggle('is-on', x2.cb.checked && !x2.cb.disabled); });
                if (all) { all.cb.checked = free.length > 0 && sel.length === free.length; all.row.classList.toggle('is-on', all.cb.checked); }
                go.disabled = !sel.length;
                goText.textContent = !sel.length ? T.request : free.length > 1 && sel.length === free.length ? T.reqAll : sel.length === 1 ? T.reqOne : T.reqN.replace('%1', sel.length);
            }
            function row(text, sub, cls) {
                var lab = el('label', 'atv-ss__row' + (cls ? ' ' + cls : '')), cb = el('input'); cb.type = 'checkbox';
                var textBox = el('span', 'atv-ss__text'); textBox.appendChild(el('b', null, text)); if (sub) textBox.appendChild(el('small', null, sub));
                lab.appendChild(cb); lab.appendChild(el('span', 'atv-ss__box')); lab.appendChild(textBox);
                return { row: lab, cb: cb };
            }
            x.addEventListener('click', function () { close(null); });
            cancel.addEventListener('click', function () { close(null); });
            // erst einen Augenblick nach dem Öffnen: Der Klick, der das Fenster geöffnet hat, soll es nicht gleich wieder schließen
            wrap.addEventListener('click', function (e) { if (e.target === wrap && Date.now() - openedAt > 350) close(null); });
            document.addEventListener('keydown', onKey, true);
            document.documentElement.style.overflow = 'hidden';
            document.body.appendChild(wrap);
            try { x.focus({ preventScroll: true }); } catch (e0) { /* Fokus optional */ }
            go.addEventListener('click', function () {
                var free = rows.filter(function (x2) { return !x2.cb.disabled; }), sel = chosen(), locked = rows.length - free.length;
                go.disabled = true;
                // „all“ nur, wenn wirklich alles gemeint ist: so nimmt Seerr auch Staffeln mit, die erst noch kommen
                send(sel.length === free.length && !locked ? 'all' : sel, quality).then(function (res) { close(res || {}); }).catch(function () { go.disabled = false; });
            });
            Promise.resolve(tvP).then(function (tv) {
                if (shut) return;
                var seasons = ((tv && tv.seasons) || []).filter(function (s) { return s.seasonNumber > 0; }), state = {}, mi = (tv && tv.mediaInfo) || {};
                (mi.seasons || []).forEach(function (ms) { if (ms.status >= 2) state[ms.seasonNumber] = ms.status; });
                (mi.requests || []).forEach(function (rq) { if (rq.status !== 3) (rq.seasons || []).forEach(function (sr) { if (!state[sr.seasonNumber]) state[sr.seasonNumber] = 2; }); });
                list.textContent = '';
                if (!seasons.length) { list.appendChild(el('p', 'atv-ss__msg', T.noSeasons)); return; }
                var eps = seasons.reduce(function (a, s) { return a + (s.episodeCount || 0); }, 0);
                all = row(T.all, seasonCount(seasons.length) + (eps ? ' · ' + eps + ' ' + T.episodes : ''), 'atv-ss__row--all');
                all.cb.addEventListener('change', function () { rows.forEach(function (x2) { if (!x2.cb.disabled) x2.cb.checked = all.cb.checked; }); update(); });
                list.appendChild(all.row);
                seasons.forEach(function (s) {
                    var it = row(T.season + ' ' + s.seasonNumber, s.episodeCount ? s.episodeCount + ' ' + T.episodes : ''), st = state[s.seasonNumber];
                    it.n = s.seasonNumber; it.cb.value = String(s.seasonNumber);
                    if (st) { it.cb.disabled = true; it.row.classList.add('is-done'); it.row.appendChild(el('em', null, st === 5 ? T.available : st === 4 ? T.partial : T.requested)); }
                    else it.cb.checked = true;                                // „Alle Staffeln“ ist vorgewählt
                    it.cb.addEventListener('change', update);
                    rows.push(it); list.appendChild(it.row);
                });
                if (!rows.some(function (x2) { return !x2.cb.disabled; })) { all.cb.disabled = true; all.row.classList.add('is-done'); }
                update();
            }).catch(function (e) { console.warn('[atv-seerr] seasons', e); if (!shut) { list.textContent = ''; list.appendChild(el('p', 'atv-ss__msg', T.offline)); } });
            Promise.resolve(svcP).then(function (svc) {
                if (shut || !svc) return;
                var chips = el('div', 'atv-xd__seasons');
                qsec.appendChild(el('h3', 'atv-ss__h', T.quality)); qsec.appendChild(chips); qsec.hidden = false;
                qualityChips(chips, svc, quality, function (q) { quality = q; }, 'atv-ss-q');
            });
        });
    }
    function seasonCount(n) { return n + ' ' + (DE ? (n === 1 ? 'Staffel' : 'Staffeln') : (n === 1 ? 'season' : 'seasons')); }
    // Anfragen ohne die Detailansicht (großes Bild von „Entdecken“): Film mit einem Tipp, Serie über das Staffel-Fenster.
    function request(r) {
        if (!apiReady()) return Promise.reject(new Error('api'));
        injectCss();
        if (r.mediaType !== 'tv') return sendRequest(r, null, null, null);
        return seasonSheet(r, call('/api/v1/tv/' + r.id + '?language=' + CFG.language), service('sonarr'), function (seasons, quality) { return sendRequest(r, seasons, null, quality); });
    }

    /* ------------------------------------------------------------ Detailansicht */
    // Vollbild im Stil der Detailseite: Hintergrundbild, Titel, Zeile „Film · Genre“, Beschreibung, Fakten,
    // statt „Abspielen“ die Pille „Anfragen“. Schließen: Zurück-Knopf, Esc, Browser-/Wisch-Zurück.
    // extra.item: Titel in der Form von „Entdecken“ (Übereinstimmung, Begründung), wenn die Ansicht von dort kommt.
    var openXd = null;
    function openDetail(r, c, extra) {
        if (openXd) openXd(true);
        extra = extra || {};
        var isTv = r.mediaType === 'tv', key = r.mediaType + ':' + r.id;
        var D = window.__atvDiscover && window.__atvDiscover.on() ? window.__atvDiscover : null;   // Bausteine von „Entdecken“, falls eingeschaltet
        var item = extra.item || { key: key, mediaType: r.mediaType, tmdbId: r.id, title: title(r), year: year(r), poster: r.posterPath || null, state: 'missing' };
        var wrap = el('div', 'atv-xd'); wrap.setAttribute('role', 'dialog'); wrap.setAttribute('aria-modal', 'true'); wrap.setAttribute('aria-label', title(r));
        var back = el('button', 'atv-xd__back'); back.type = 'button'; back.setAttribute('aria-label', T.close); back.appendChild(svg(ICON.back)); wrap.appendChild(back);
        var hero = el('section', 'atv-detail'); wrap.appendChild(hero);
        var art = el('img', 'atv-detail__art'); art.alt = ''; art.addEventListener('load', function () { art.classList.add('is-in'); }); hero.appendChild(art);
        hero.appendChild(el('div', 'atv-detail__shade'));
        var lock = el('div', 'atv-detail__lockup'); hero.appendChild(lock);
        lock.appendChild(el('h1', 'atv-detail__title', title(r)));
        var meta = el('div', 'atv-detail__meta'); var coin = el('span', 'atv-coin'); coin.appendChild(svg(isTv ? ICON.tv : ICON.film)); meta.appendChild(coin);
        var metaText = el('span', null, isTv ? T.series : T.movie); meta.appendChild(metaText); lock.appendChild(meta);
        // von „Entdecken“: wie gut der Titel passt und warum er vorgeschlagen wird
        if (D && extra.item) {
            var why = el('p', 'atv-xd__why'), sc = D.score(extra.item, true);
            if (sc) why.appendChild(sc);
            if (extra.item.reason && extra.item.reasonKind !== 'request') why.appendChild(el('span', null, extra.item.reason));
            if (why.childNodes.length) lock.appendChild(why);
        }
        var desc = el('p', 'atv-detail__desc atv-xd__desc', r.overview || ''); desc.addEventListener('click', function () { desc.classList.toggle('is-open'); });
        if (r.overview) lock.appendChild(desc);
        var facts = el('div', 'atv-detail__facts'); lock.appendChild(facts);
        var act = el('div', 'atv-detail__actions'); lock.appendChild(act);
        var note = el('p', 'atv-xd__note', T.title); lock.appendChild(note);
        var credits = el('div', 'atv-detail__credits'); hero.appendChild(credits);
        var below = el('div'); wrap.appendChild(below);
        if (r.backdropPath) loadImg(art, tmdbImg('w1280', r.backdropPath)); else if (r.posterPath) loadImg(art, tmdbImg('w780', r.posterPath)); else hero.classList.add('atv-detail--noart');

        var quality = null, svc = null, detailsP = null, line = null;
        function fillFacts(d) {
            facts.textContent = '';
            var parts = [year(d || r)];
            if (d && d.runtime) parts.push(runtimeText(d.runtime));
            if (d && isTv && d.numberOfSeasons) parts.push(seasonCount(d.numberOfSeasons));
            parts = parts.filter(Boolean);
            if (parts.length) facts.appendChild(el('span', null, parts.join(' · ')));
            var va = (d && d.voteAverage) || r.voteAverage;
            if (va) { var st = el('span', 'atv-badge atv-badge--star'); st.appendChild(svg(ICON.star)); st.appendChild(document.createTextNode(va.toFixed(1).replace('.', DE ? ',' : '.'))); facts.appendChild(st); }
        }
        function pill(icon, label, glass) {
            var b = el('button', 'atv-pill' + (glass ? ' atv-pill--glass' : '')); b.type = 'button';
            b.appendChild(svg(icon)); b.appendChild(el('span', null, label));
            return b;
        }
        function requested() { fillActions(); fillQuality(); loadDetails(); }
        // Serie: erst das Fenster mit den Staffeln, dort wird bestätigt. Film: ein Tipp.
        function ask(b) {
            if (isTv) {
                seasonSheet(r, detailsP || loadDetails(), service('sonarr'), function (seasons, q) { return sendRequest(r, seasons, c, q); })
                    .then(function (res) { if (res && wrap.isConnected) requested(); });
                return;
            }
            b.disabled = true;
            sendRequest(r, null, c, quality).then(requested).catch(function () { b.disabled = false; });
        }
        // Stand der Anfrage: mit „Entdecken“ die Zeile mit Zeichen, Prozent und Zeitangabe, die sich selbst erneuert; sonst
        // (und für „Teilweise“/„Verfügbar“ ohne Titel in der Mediathek) die schlichte Marke.
        function statusEl(def, live) {
            if (D && live) { line = D.statusLine({ key: key, request: D.requestOf(key) }); D.watch(wrap); return line; }
            var stp = el('button', 'atv-pill is-state'); stp.type = 'button'; stp.disabled = true; stp.style.color = COLOR[def[0]];
            stp.appendChild(el('i')); stp.appendChild(el('span', null, def[1]));
            return stp;
        }
        function fillActions() {
            act.textContent = ''; line = null;
            // Laut „Entdecken“ schon angekommen: Seerr gleicht seinen eigenen Stand erst in Abständen ab
            var here = D && D.itemIdOf ? D.itemIdOf(key) : null;
            if (here) { r.mediaInfo = r.mediaInfo || {}; r.mediaInfo.status = 5; r.mediaInfo.jellyfinMediaId = here; }
            var s = statusOf(r), def = STATUS[s], mi = r.mediaInfo || {}, there = (s === 5 || s === 4) && !!mi.jellyfinMediaId, pending = s === 2 || s === 3, b, seen, hide;
            if (there) {
                b = pill(ICON.play, T.open, false);
                b.addEventListener('click', function () { var id = mi.jellyfinMediaId; close(true); location.hash = '#/details?id=' + id; });
                act.appendChild(b);
            }
            if (!pending && s !== 5) {
                b = pill(ICON.plus, s === 4 ? T.moreSeasons : T.request, there);
                (function (btn) { btn.addEventListener('click', function () { ask(btn); }); })(b);
                act.appendChild(b);
            }
            if (pending) act.appendChild(statusEl(def, true));
            else if (def && !there) act.appendChild(statusEl(def, false));
            // Rückmelden (nur solange der Titel fehlt): runde Knöpfe mit Beschriftung beim Darüberfahren, kein Kreuz
            if (D && !def) {
                seen = D.tip(el('button', 'atv-circle'), D.text('seen')); seen.type = 'button'; seen.setAttribute('aria-haspopup', 'menu'); seen.appendChild(svg(ICON.check));
                seen.addEventListener('pointerdown', function () { seen.__had = !!document.querySelector('.atv-menu--dsc'); }, true);
                seen.addEventListener('click', function (e) { e.stopPropagation(); if (seen.__had) { seen.__had = false; return; } D.seenMenu(seen, item, 'detail', e.detail === 0, function () { close(); }); });
                act.appendChild(seen);
                hide = D.tip(el('button', 'atv-circle'), D.text('hide')); hide.type = 'button'; hide.appendChild(svg(D.icons.eyeOff));
                hide.addEventListener('click', function () { close(); D.feedback(item, 'hide', 'detail'); });
                act.appendChild(hide);
            }
            note.textContent = def ? '' : T.title; note.hidden = !!def;
        }
        // Qualitätsprofil wählen (nur Filme; bei Serien steht die Wahl im Staffel-Fenster). Erscheint nur, wenn Seerr
        // mehrere Profile liefert und noch angefragt werden kann. Standard = das in Seerr hinterlegte Profil.
        function fillQuality() {
            var old = below.querySelector('.atv-xd__sec--quality'); if (old) old.remove();
            if (isTv || !svc || STATUS[statusOf(r)]) return;
            var sec = el('section', 'atv-xd__sec atv-xd__sec--quality'); sec.appendChild(el('h2', 'atv-xd__h', T.quality));
            var box = el('div', 'atv-xd__seasons'); sec.appendChild(box);
            qualityChips(box, svc, quality, function (q) { quality = q; }, 'atv-xd-q');
            below.insertBefore(sec, below.firstChild);
        }
        function fillCast(d) {
            var old = below.querySelector('.atv-xd__sec--cast'); if (old) old.remove();
            var cast = ((d.credits && d.credits.cast) || []).slice(0, 14); if (!cast.length) return;
            var sec = el('section', 'atv-xd__sec atv-xd__sec--cast'); sec.appendChild(el('h2', 'atv-xd__h', DE ? 'Besetzung' : 'Cast'));
            var row = el('div', 'atv-xd__cast'); sec.appendChild(row);
            cast.forEach(function (p) {
                var it = el('div', 'atv-xd__person'), ph = el('span'); it.appendChild(ph);
                if (p.profilePath) { var im = el('img'); im.alt = ''; im.loading = 'lazy'; im.addEventListener('error', function () { im.remove(); }); ph.appendChild(im); loadImg(im, tmdbImg('w185', p.profilePath)); }
                it.appendChild(el('b', null, p.name || '')); if (p.character) it.appendChild(el('small', null, p.character));
                row.appendChild(it);
            });
            below.appendChild(sec);
        }
        function loadDetails() {
            detailsP = call('/api/v1/' + (isTv ? 'tv' : 'movie') + '/' + r.id + '?language=' + CFG.language);
            detailsP.then(function (d) {
                if (!wrap.isConnected) return;
                if (d.mediaInfo) { var was = statusOf(r); r.mediaInfo = d.mediaInfo; if (statusOf(r) !== was) announce(r); }
                var g = (d.genres || []).slice(0, 2).map(function (x) { return x.name; });
                metaText.textContent = [isTv ? T.series : T.movie].concat(g).join(' · ');
                if (d.overview && !r.overview) { desc.textContent = d.overview; if (!desc.isConnected) lock.insertBefore(desc, facts); }
                if (!r.backdropPath && d.backdropPath) loadImg(art, tmdbImg('w1280', d.backdropPath));
                fillFacts(d);
                credits.textContent = '';
                var cast = ((d.credits && d.credits.cast) || []).slice(0, 3).map(function (p) { return p.name; });
                var crew = (d.credits && d.credits.crew) || [];
                var dirs = isTv ? (d.createdBy || []).map(function (p) { return p.name; }) : crew.filter(function (p) { return p.job === 'Director'; }).map(function (p) { return p.name; });
                function ln(label, names) { if (!names.length) return; var p = el('p'); p.appendChild(el('span', 'atv-label', label)); p.appendChild(document.createTextNode(names.join(', '))); credits.appendChild(p); }
                ln(DE ? 'Mit' : 'Starring', cast); ln(isTv ? (DE ? 'Erfinder' : 'Creator') : (DE ? 'Regie' : 'Director'), dirs.slice(0, 2));
                fillQuality(); fillCast(d);
                fillActions(); setStatus(c, r);
            }).catch(function (e) { console.warn('[atv-seerr] details', e); detailsP = null; if (wrap.isConnected) fillActions(); });
            return detailsP;
        }
        fillFacts(null); fillActions();

        // Schließen über den Verlauf: Zurück-Knopf (auch der des Themes), Wischgeste und Browser-Zurück schließen die Ansicht
        var pushed = false, closed = false;
        function onKey(ev) {
            if (ev.key !== 'Escape' || document.querySelector('.atv-sheet')) return;
            ev.stopPropagation();
            var menu = document.querySelector('.atv-menu--float'); if (menu) { menu.remove(); return; }   // erst das offene Menü
            close();
        }
        function onPop() { close(true); }
        // Neuer Stand vom Server: Zeile nachführen; ist der Titel angekommen, Seerr noch einmal fragen (dann steht „Öffnen“ da)
        function onRq() {
            var rq = D ? D.requestOf(key) : null;
            if (rq && rq.phase === 'available' && statusOf(r) !== 5) { fillActions(); setStatus(c, r); if (statusOf(r) !== 5) loadDetails(); return; }
            if (line && line.isConnected) D.statusFill(line, rq);
        }
        function close(fromHistory) {
            if (closed) return; closed = true; openXd = null;
            document.removeEventListener('keydown', onKey, true); window.removeEventListener('popstate', onPop);
            document.removeEventListener('atv-dsc-requests', onRq);
            document.documentElement.style.overflow = prevOverflow;
            wrap.remove();
            if (pushed && fromHistory !== true) { try { history.back(); } catch (e) { /* Verlauf nicht verfügbar */ } }
            if (c && c.isConnected) { try { c.focus({ preventScroll: true }); } catch (e2) { /* Fokus optional */ } }
        }
        openXd = close;
        back.addEventListener('click', function () { close(); });
        document.addEventListener('keydown', onKey, true);
        var prevOverflow = document.documentElement.style.overflow; document.documentElement.style.overflow = 'hidden';
        document.body.appendChild(wrap);
        try { history.pushState({ atvXd: 1 }, ''); pushed = true; window.addEventListener('popstate', onPop); } catch (e) { /* ohne Verlauf: nur Knopf/Esc */ }
        loadDetails();
        if (!isTv) service('radarr').then(function (s) { if (!s || !wrap.isConnected) return; svc = s; fillQuality(); });
        if (D) { document.addEventListener('atv-dsc-requests', onRq); if (statusOf(r) === 2 || statusOf(r) === 3) D.reload(); }
        try { back.focus({ preventScroll: true }); } catch (e3) { /* Fokus optional */ }
    }

    /* ------------------------------------------------------------ Suche */
    function mount(page) {
        var input = page.querySelector('.searchfields-txtSearch'), results = page.querySelector('.searchResults');
        if (!input || !apiReady()) return;                              // Trefferbereich fehlt, wenn die Mediathek nichts findet – gerade dann wird das Regal gebraucht
        if (page.__atvSeerr) return;
        page.__atvSeerr = true;
        injectCss();
        var sec = el('section', 'atv-seerr atv-shelf'); sec.hidden = true;
        var h = el('h2', 'atv-seerr__title', T.title); var cnt = el('span', 'atv-seerr__count'); h.appendChild(cnt); sec.appendChild(h);
        var gw = el('div', 'atv-seerr__wrap atv-shelf__wrap'); sec.appendChild(gw);
        var grid = el('div', 'atv-seerr__grid'); gw.appendChild(grid);
        // Blätterpfeile wie in den übrigen Regalen (nur mit Maus sichtbar; Stil kommt aus dem Theme)
        var arrows = el('div', 'atv-shelf__arrows');
        var bPrev = el('button', 'atv-shelf__arrow'); bPrev.type = 'button'; bPrev.appendChild(svg(ICON.back));
        var bNext = el('button', 'atv-shelf__arrow'); bNext.type = 'button'; var nx = svg(ICON.back); nx.style.transform = 'scaleX(-1)'; bNext.appendChild(nx);
        arrows.appendChild(bPrev); arrows.appendChild(bNext); gw.appendChild(arrows);
        function syncArrows() { var max = grid.scrollWidth - grid.clientWidth; bPrev.disabled = grid.scrollLeft <= 2; bNext.disabled = grid.scrollLeft >= max - 2; arrows.style.display = max <= 4 ? 'none' : ''; }
        bPrev.addEventListener('click', function () { grid.scrollBy({ left: -grid.clientWidth * .8, behavior: 'smooth' }); });
        bNext.addEventListener('click', function () { grid.scrollBy({ left: grid.clientWidth * .8, behavior: 'smooth' }); });
        grid.addEventListener('scroll', function () { clearTimeout(syncArrows.t); syncArrows.t = setTimeout(syncArrows, 80); }, { passive: true });
        // Immer unter den Treffern der eigenen Bibliothek. Jellyfin legt seinen Trefferbereich manchmal erst nach diesem
        // Regal an; dann stünde „Nicht in deiner Mediathek“ darüber. Der Platz wird deshalb nachgehalten.
        function place() {
            var res = page.querySelector('.searchResults');
            if (res && res.parentNode) { if (res.nextElementSibling !== sec) res.parentNode.insertBefore(sec, res.nextSibling); }
            else if (!sec.parentNode) page.appendChild(sec);
        }
        place();
        if (window.MutationObserver) new MutationObserver(place).observe(page, { childList: true });
        var last = '', ctrl = null, timer = null;
        // Fehler sichtbar, mit "Erneut versuchen"; läuft automatisch neu, sobald das Netz zurück ist oder die App wieder vorne ist
        function showErr(code) {
            grid.textContent = ''; cnt.textContent = ''; arrows.style.display = 'none';
            var p = el('p', 'atv-seerr__msg', (code === 'Netz' ? T.noconn : T.offline + ' (' + code + ')') + ' ');
            var b = el('button', 'atv-seerr__retry', T.retry); b.type = 'button';
            b.addEventListener('click', function () { retry(); });
            p.appendChild(b); grid.appendChild(p); sec.hidden = false; sec.__err = true; last = '';
        }
        function retry() { sec.__err = false; last = ''; avail = null; available.err = null; run(); }
        function autoRetry() { if (sec.__err && !sec.hidden) retry(); }
        window.addEventListener('online', autoRetry);
        document.addEventListener('visibilitychange', function () { if (!document.hidden) autoRetry(); });
        function run() {
            var q = (input.value || '').trim();
            if (q === last) return; last = q;
            if (ctrl) { ctrl.abort(); ctrl = null; }
            if (q.length < CFG.minChars) { sec.hidden = true; grid.textContent = ''; return; }
            available().then(function (ok) {
                if ((input.value || '').trim() !== q) return;
                if (!ok) { if (available.err) { showErr(available.err); avail = null; } return; }
                ctrl = new AbortController();
                callRetry('/api/v1/search?query=' + encodeURIComponent(q) + '&page=1&language=' + CFG.language, { signal: ctrl.signal }, 4).then(function (res) {
                    if ((input.value || '').trim() !== q) return;
                    var items = (res.results || []).filter(function (r) { return (r.mediaType === 'movie' || r.mediaType === 'tv') && !inLibrary(r) && r.posterPath; }).slice(0, CFG.maxCards);
                    grid.textContent = '';
                    items.forEach(function (r) { grid.appendChild(card(r)); });
                    cnt.textContent = items.length ? String(items.length) : '';
                    grid.scrollLeft = 0; requestAnimationFrame(syncArrows);
                    sec.hidden = !items.length; sec.__err = false;
                })            .catch(function (e) { if (e.name !== 'AbortError') { console.warn('[atv-seerr]', e); showErr(e.status || 'Netz'); } });
            });
        }
        input.addEventListener('input', function () { clearTimeout(timer); timer = setTimeout(run, CFG.debounceMs); });
        run();
    }

    /* ------------------------------------------------------------ Filmreihe */
    // Für das Regal „Filmreihe“ auf der Filmseite des Themes: die Teile einer Reihe, wie Seerr sie von TMDb kennt, samt Stand
    // der Anfrage. Kennt der Film die Nummer seiner Reihe nicht, verrät sie Seerr über den Film. Ohne Reihe oder ohne
    // eingerichtetes Seerr: null. Die Karte ist dieselbe wie in der Suche und öffnet dieselbe Ansicht mit „Anfragen“.
    function collection(tmdbId, collectionId) {
        var num = /^\d+$/;                                                   // die Nummern landen im Pfad
        if (!apiReady() || !(num.test(collectionId) || num.test(tmdbId))) return Promise.resolve(null);
        return available().then(function (ok) {
            if (!ok) return null;
            var id = num.test(collectionId) ? Promise.resolve(collectionId)
                : callRetry('/api/v1/movie/' + tmdbId + '?language=' + CFG.language, {}, 1).then(function (d) { return d && d.collection && d.collection.id; });
            return id.then(function (cid) { return cid ? callRetry('/api/v1/collection/' + cid + '?language=' + CFG.language, {}, 1) : null; });
        }).then(function (col) {
            if (!col || !col.parts) return null;
            var parts = col.parts.filter(function (r) { return r && r.id; });
            parts.forEach(function (r) { r.mediaType = 'movie'; });
            return { id: col.id, name: col.name || '', parts: parts };
        }).catch(function (e) { console.warn('[atv-seerr] collection', e); return null; });
    }
    // Für das Theme (Filmreihe, „Entdecken“): Karte, Anfrage-Ansicht und Anfrage ohne Ansicht.
    window.__atvSeerrApi = {
        collection: collection,
        card: function (r) { injectCss(); return card(r); },
        open: function (r, c, extra) { injectCss(); openDetail(r, c || null, extra); },
        request: request,
        title: title, year: year, status: statusOf, inLibrary: inLibrary
    };

    /* ------------------------------------------------------------ Router */
    // Personenseite (Schauspiel/Regie): Regal mit Titeln dieser Person, die NICHT in der Mediathek sind – zum Anfragen.
    function mountPerson(page) {
        var m = /[?&]id=([0-9a-f]+)/i.exec(location.hash), id = m && m[1];
        if (!id || !apiReady() || page.__atvSeerrPerson === id) return;
        page.__atvSeerrPerson = id;
        page.querySelectorAll('.atv-seerr--person').forEach(function (n) { n.remove(); });
        injectCss();
        api().getItem(api().getCurrentUserId(), id).then(function (person) {
            var tm = person && person.Type === 'Person' && person.ProviderIds && (person.ProviderIds.Tmdb || person.ProviderIds.tmdb);
            if (!tm) return;
            return available().then(function (ok) {
                if (!ok) return;
                return callRetry('/api/v1/person/' + tm + '/combined_credits?language=' + CFG.language, {}, 2).then(function (res) {
                    if (page.__atvSeerrPerson !== id || !page.isConnected) return;
                    var seen = {}, list = [];
                    // Regie/Erfinder zuerst, danach Auftritte als Darsteller (ohne Talkshow-/Gastauftritte „Self“)
                    var crew = ((res && res.crew) || []).filter(function (c) { return c.job === 'Director' || c.job === 'Creator'; }); crew.forEach(function (c) { c.__lead = 1; });
                    var cast = ((res && res.cast) || []).filter(function (c) { return !/\b(self|himself|herself)\b/i.test(c.character || '') && !/\(voice\)|uncredited/i.test(c.character || ''); });
                    crew.concat(cast).forEach(function (r) {
                        var k = r.mediaType + r.id;
                        if (seen[k] || (r.mediaType !== 'movie' && r.mediaType !== 'tv') || !r.posterPath || inLibrary(r)) return;
                        if (statusOf(r) === 5) return;
                        seen[k] = 1; list.push(r);
                    });
                    list.sort(function (a, b) { return ((b.__lead || 0) - (a.__lead || 0)) || ((b.voteCount || b.popularity || 0) - (a.voteCount || a.popularity || 0)); });
                    list = list.slice(0, CFG.maxCards);
                    if (!list.length) return;
                    var sec = el('section', 'atv-seerr atv-shelf atv-seerr--person');
                    var h = el('h2', 'atv-seerr__title', T.title); h.appendChild(el('span', 'atv-seerr__count', String(list.length))); sec.appendChild(h);
                    var gw = el('div', 'atv-seerr__wrap atv-shelf__wrap'), grid = el('div', 'atv-seerr__grid'); gw.appendChild(grid); sec.appendChild(gw);
                    var arrows = el('div', 'atv-shelf__arrows'), bPrev = el('button', 'atv-shelf__arrow'), bNext = el('button', 'atv-shelf__arrow');
                    bPrev.type = bNext.type = 'button'; bPrev.appendChild(svg(ICON.back)); var nx = svg(ICON.back); nx.style.transform = 'scaleX(-1)'; bNext.appendChild(nx);
                    arrows.appendChild(bPrev); arrows.appendChild(bNext); gw.appendChild(arrows);
                    var sync = function () { var max = grid.scrollWidth - grid.clientWidth; bPrev.disabled = grid.scrollLeft <= 2; bNext.disabled = grid.scrollLeft >= max - 2; arrows.style.display = max <= 4 ? 'none' : ''; };
                    bPrev.addEventListener('click', function () { grid.scrollBy({ left: -grid.clientWidth * .8, behavior: 'smooth' }); });
                    bNext.addEventListener('click', function () { grid.scrollBy({ left: grid.clientWidth * .8, behavior: 'smooth' }); });
                    grid.addEventListener('scroll', function () { clearTimeout(sync.t); sync.t = setTimeout(sync, 80); }, { passive: true });
                    list.forEach(function (r) { grid.appendChild(card(r)); });
                    var host = page.querySelector('.detailPageSecondaryContainer') || page.querySelector('.detailPageWrapperContainer') || page;
                    host.appendChild(sec); requestAnimationFrame(sync);
                });
            });
        }).catch(function (e) { console.warn('[atv-seerr] person', e); });
    }
    function tick() {
        var page = document.querySelector('#searchPage:not(.hide), .searchPage:not(.hide)');
        if (page) mount(page);
        var pp = document.querySelector('.itemDetailPage:not(.hide)');
        if (pp && pp.querySelector('.atv-person')) mountPerson(pp);
    }
    function schedule() { clearTimeout(schedule.t); schedule.t = setTimeout(tick, 200); }
    document.addEventListener('viewshow', schedule, true);
    window.addEventListener('hashchange', schedule);
    var mo = new MutationObserver(function (m) { for (var i = 0; i < m.length; i++) { var t = m[i].target; if (t.id === 'searchPage' || (t.classList && (t.classList.contains('mainAnimatedPages') || t.classList.contains('skinBody') || t.classList.contains('itemDetailPage') || t.classList.contains('atv-person')))) { schedule(); return; } } });
    function start() { if (!document.body) { setTimeout(start, 50); return; } mo.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['class'] }); schedule(); }
    start();
    var wait = setInterval(function () { if (apiReady()) { clearInterval(wait); schedule(); } }, 400);
    setTimeout(function () { clearInterval(wait); }, 60000);
})();
