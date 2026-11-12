/* ============================================================================
   atv-ratings.js – Bewertungs-Badges (IMDb, Rotten Tomatoes Kritiker/Publikum,
   Metacritic, TMDb, Letterboxd) in der Faktenzeile der Detailseite (.atv-detail__facts).
   Teil der „Extras“ zum Apple-TV-Klon. Icons sind eigene Inline-SVGs (keine Fremdbilder).

   Datenweg (kein Schlüssel im Browser): <server>/CinemaGlass/mdb/tmdb/{movie|show}/{tmdbId}
   Das Plugin lässt nur angemeldete Jellyfin-Nutzer durch und fragt MDBList mit dem
   serverseitig hinterlegten Schlüssel ab. Ohne Plugin bleibt dieses Modul still.
   Antworten werden 7 Tage im Browser zwischengespeichert (localStorage), damit das
   Tageskontingent von MDBList (1000 Abfragen) nicht angetastet wird.
   ========================================================================== */
(function () {
    'use strict';
    if (window.__atvRatings) return;
    window.__atvRatings = true;

    var CFG = {
        cacheDays: 7,
        order: ['imdb', 'tomatoes', 'popcorn', 'metacritic', 'tmdb', 'letterboxd'],
        hideThemeStars: true   // Theme-eigene ★/-Badges ausblenden, sobald eigene Badges stehen (keine Dubletten)
    };

    var CSS = [
        '.atv-badge--rating{gap:.35em;border-color:rgba(255,255,255,.4)!important;padding-right:max(5px,calc(7 * var(--atv-u,.052vw)))!important}',
        '.atv-badge--rating svg{width:1.1em;height:1.1em;margin-right:0!important;fill:currentColor;flex:0 0 auto}',
        '.atv-badge--rating b{font-weight:700;letter-spacing:.2px}',
        '.atv-badge--rating small{font-weight:600;opacity:.6;font-size:.78em;letter-spacing:.4px}',
        '.atv-badge--rating[data-src=tomatoes] svg{color:#ff453a}.atv-badge--rating[data-src=popcorn] svg{color:#ffd60a}.atv-badge--rating[data-src=imdb] small{color:#f5c518;opacity:1}',
        '.atv-badge--rating[data-src=letterboxd] svg{color:#00e054}.atv-badge--rating[data-src=metacritic] svg{color:#ffcc00}.atv-badge--rating[data-src=tmdb] svg{color:#01b4e4}',
        '.atv-badge--rating[data-src=tomatoes][data-rotten] svg{color:#30d158}',
        /* Rückfall, falls das Theme-CSS fehlt */
        '.atv-hero__meta .atv-badge--rating{margin-left:calc(6 * var(--atv-u,.052vw))}',
        '.atv-badge--rating.atv-badge--solo{display:inline-flex;align-items:center;height:22px;padding:0 7px;border:1px solid rgba(255,255,255,.5);border-radius:4px;font:600 13px -apple-system,system-ui,sans-serif;color:rgba(255,255,255,.9);margin-left:6px}'
    ].join('\n');

    // Eigene, schlichte Symbole (24er-Raster)
    var ICON = {
        imdb: null, // Wortmarke als Text
        tomatoes: 'M12 5.2c-.9 0-1.4.6-2.1.6-.7 0-1.3-.6-2.3-.2.7.4 1.1 1 1.2 1.6C5.4 7.6 3 10 3 13.2 3 17 7 20 12 20s9-3 9-6.8c0-3.2-2.4-5.6-5.8-6 .1-.6.5-1.2 1.2-1.6-1-.4-1.6.2-2.3.2-.7 0-1.2-.6-2.1-.6z',
        popcorn: 'M7 9h10l-1.2 11H8.2L7 9zm-.5-2.5c0-1.1.9-2 2-2 .3-1.2 1.3-2 2.5-2s2.2.8 2.5 2c1.1 0 2 .9 2 2v.5h-9V6.5zM9.3 11l.6 7h1.2l-.5-7H9.3zm4.1 0-.5 7h1.2l.6-7h-1.3z',
        metacritic: 'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zm0 2.2a6.8 6.8 0 1 1 0 13.6 6.8 6.8 0 0 1 0-13.6zM8.2 15.5V9l3.8 3.2L15.8 9v6.5h-1.7v-3l-2.1 1.8-2.1-1.8v3H8.2z',
        tmdb: 'M3 8h18v2.2H3V8zm0 3.4h12.5v2.2H3v-2.2zm0 3.4h16v2.2H3v-2.2z',
        letterboxd: 'M7 8.5a3.5 3.5 0 1 0 0 7 3.5 3.5 0 0 0 0-7zm5 0a3.5 3.5 0 1 0 0 7 3.5 3.5 0 0 0 0-7zm5 0a3.5 3.5 0 1 0 0 7 3.5 3.5 0 0 0 0-7z'
    };
    var LABEL = { imdb: 'IMDb', tomatoes: 'Rotten Tomatoes', popcorn: 'Rotten Tomatoes (Publikum)', metacritic: 'Metacritic', tmdb: 'TMDb', letterboxd: 'Letterboxd' };

    function el(tag, cls, text) { var e = document.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; }
    function svg(path) { var ns = 'http://www.w3.org/2000/svg', s = document.createElementNS(ns, 'svg'); s.setAttribute('viewBox', '0 0 24 24'); s.setAttribute('aria-hidden', 'true'); var p = document.createElementNS(ns, 'path'); p.setAttribute('d', path); s.appendChild(p); return s; }
    function api() { return window.ApiClient; }
    function apiReady() { var a = api(); return !!(a && typeof a.accessToken === 'function' && a.accessToken() && a.getCurrentUserId && a.getCurrentUserId()); }
    function injectCss() { if (document.getElementById('atv-ratings-css')) return; var s = el('style'); s.id = 'atv-ratings-css'; s.textContent = CSS; document.head.appendChild(s); }
    function hashParam(name) { var m = location.hash.match(new RegExp('[?&]' + name + '=([^&]+)')); return m ? decodeURIComponent(m[1]) : null; }

    /* ------------------------------------------------------------ Daten */
    var disabled = false;
    function cacheGet(key) {
        try { var raw = localStorage.getItem(key); if (!raw) return null; var o = JSON.parse(raw); if (Date.now() - o.t > CFG.cacheDays * 864e5) { localStorage.removeItem(key); return null; } return o.v; } catch (e) { return null; }
    }
    function cacheSet(key, v) { try { localStorage.setItem(key, JSON.stringify({ t: Date.now(), v: v })); } catch (e) { /* voll/privat */ } }
    function fetchRatings(kind, tmdbId) {
        var key = 'atvx-mdb:' + kind + ':' + tmdbId, hit = cacheGet(key);
        if (hit) return Promise.resolve(hit);
        if (disabled) return Promise.resolve(null);
        var g = window.__cinemaGlass; if (!g || !g.base) return Promise.resolve(null);
        var url = g.base + '/mdb/tmdb/' + kind + '/' + tmdbId;
        var hdr = { 'Authorization': 'MediaBrowser Token="' + api().accessToken() + '"', 'Accept': 'application/json' };
        function go(n) {
            return fetch(url, { headers: hdr, credentials: 'omit' }).then(function (r) {
                if ((r.status === 502 || r.status === 503 || r.status === 504 || r.status === 401) && n > 0) return new Promise(function (ok) { setTimeout(ok, 1500); }).then(function () { return go(n - 1); });
                return r;
            }, function (e) { if (n > 0) return new Promise(function (ok) { setTimeout(ok, 1500); }).then(function () { return go(n - 1); }); throw e; });
        }
        return go(2).then(function (r) {
            if (r.status === 401 || r.status === 404 || r.status === 502 || r.status === 503) { disabled = (r.status === 404); return null; }
            if (!r.ok) return null;
            return r.json();
        }).then(function (j) {
            if (!j || !Array.isArray(j.ratings)) return null;
            var slim = {};
            j.ratings.forEach(function (x) { if (x && x.source && x.value != null) slim[x.source] = { value: x.value, score: x.score, votes: x.votes }; });
            cacheSet(key, slim);
            return slim;
        }).catch(function () { return null; });
    }

    /* ------------------------------------------------------------ Badges */
    function fmt(src, r) {
        var v = r.value;
        if (src === 'imdb') return (Math.round(v * 10) / 10).toFixed(1);
        if (src === 'letterboxd') return (Math.round(v * 10) / 10).toFixed(1);
        if (src === 'metacritic') return String(Math.round(v));
        return Math.round(v) + ' %';
    }
    function badge(src, r) {
        var b = el('span', 'atv-badge atv-badge--rating'); b.setAttribute('data-src', src); b.title = LABEL[src];
        b.setAttribute('aria-label', LABEL[src] + ' ' + fmt(src, r));
        if (ICON[src]) b.appendChild(svg(ICON[src])); else b.appendChild(el('small', null, LABEL[src]));
        if (src === 'tomatoes' && r.value < 60) b.setAttribute('data-rotten', '');
        b.appendChild(el('b', null, fmt(src, r)));
        return b;
    }
    function render(facts, ratings, itemKey) {
        facts.querySelectorAll('.atv-badge--rating').forEach(function (x) { x.remove(); });
        facts.querySelectorAll('[data-atv-ratings-hidden]').forEach(function (x) { x.hidden = false; x.style.display = ''; x.removeAttribute('data-atv-ratings-hidden'); });
        var n = 0;
        CFG.order.forEach(function (src) { var r = ratings[src]; if (r && r.value != null && !(r.votes === 0)) { facts.appendChild(badge(src, r)); n++; } });
        facts.setAttribute('data-atv-ratings', itemKey);
        if (n && CFG.hideThemeStars) {
            Array.prototype.forEach.call(facts.querySelectorAll('.atv-badge:not(.atv-badge--rating)'), function (x) {
                var tx = (x.textContent || '').trim();
                if (x.classList.contains('atv-badge--rt') || x.classList.contains('atv-badge--star') || /^★/.test(tx)) { x.hidden = true; x.style.display = 'none'; x.setAttribute('data-atv-ratings-hidden', ''); }
            });
        }
    }

    /* ------------------------------------------------------------ Seite */
    var pending = null;
    function resolveItem(id) {
        return api().getItem(api().getCurrentUserId(), id).then(function (it) {
            if (!it) return null;
            if (it.Type === 'Episode' || it.Type === 'Season') { return it.SeriesId ? api().getItem(api().getCurrentUserId(), it.SeriesId) : null; }
            return it;
        });
    }
    function tick() {
        var page = document.querySelector('.itemDetailPage:not(.hide)');
        if (!page || !apiReady()) return;
        var id = hashParam('id'); if (!id) return;
        var facts = page.querySelector('.atv-detail__facts');
        if (!facts) return;                                  // Theme-Faktenzeile noch nicht gebaut → nächster Tick
        if (facts.getAttribute('data-atv-ratings') === id) return;
        if (pending === id + ':' + facts.childElementCount) return;
        pending = id + ':' + facts.childElementCount;
        injectCss();
        resolveItem(id).then(function (it) {
            var tm = it && it.ProviderIds && (it.ProviderIds.Tmdb || it.ProviderIds.tmdb);
            if (!tm) { facts.setAttribute('data-atv-ratings', id); return; }
            var kind = it.Type === 'Series' ? 'show' : 'movie';
            return fetchRatings(kind, tm).then(function (ratings) {
                var f2 = page.querySelector('.atv-detail__facts'); if (!f2 || hashParam('id') !== id) return;
                if (ratings) render(f2, ratings, id); else f2.setAttribute('data-atv-ratings', id);
            });
        }).catch(function (e) { console.warn('[atv-ratings]', e); }).then(function () { pending = null; });
    }
    /* ------------------------------------------------------------ Hero (nur IMDb + ) */
    var HERO_ORDER = ['imdb', 'tomatoes'];
    function heroItemId(slide) {
        var art = slide.querySelector('[data-land], [data-port]');
        var u = (art && (art.getAttribute('data-land') || art.getAttribute('data-port'))) || '';
        var m = u.match(/\/Items\/([0-9a-fA-F]{32})\//); return m ? m[1] : null;
    }
    function heroTick() {
        if (!apiReady()) return;
        var slides = document.querySelectorAll('.atv-hero__slide');
        Array.prototype.forEach.call(slides, function (slide) {
            if (slide.__atvRatings) return;
            var meta = slide.querySelector('.atv-hero__meta'); var id = heroItemId(slide);
            if (!meta || !id) return;
            slide.__atvRatings = true;
            injectCss();
            resolveItem(id).then(function (it) {
                var tm = it && it.ProviderIds && (it.ProviderIds.Tmdb || it.ProviderIds.tmdb); if (!tm) return;
                return fetchRatings(it.Type === 'Series' ? 'show' : 'movie', tm).then(function (ratings) {
                    if (!ratings || !slide.isConnected) return;
                    HERO_ORDER.forEach(function (src) { var r = ratings[src]; if (r && r.value != null) meta.appendChild(badge(src, r)); });
                });
            }).catch(function () {});
        });
    }
    function schedule() { clearTimeout(schedule.t); schedule.t = setTimeout(function () { tick(); heroTick(); }, 250); }
    document.addEventListener('viewshow', schedule, true);
    window.addEventListener('hashchange', schedule);
    var mo = new MutationObserver(function (m) {
        for (var i = 0; i < m.length; i++) {
            var t = m[i].target;
            if (t.classList && (t.classList.contains('atv-hero') || t.classList.contains('atv-hero__slide') || t.classList.contains('atv-detail__lockup') || t.classList.contains('atv-detail') || t.classList.contains('itemDetailPage') || t.classList.contains('mainAnimatedPages') || t.classList.contains('skinBody') || t.classList.contains('atv-detail__facts'))) { schedule(); return; }
            for (var k = 0; k < m[i].addedNodes.length; k++) { var n = m[i].addedNodes[k]; if (n.nodeType === 1 && (n.classList.contains('atv-hero') || n.classList.contains('atv-detail__facts') || n.querySelector && n.querySelector('.atv-detail__facts'))) { schedule(); return; } }
        }
    });
    function start() { if (!document.body) { setTimeout(start, 50); return; } mo.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['class'] }); schedule(); }
    start();
    var wait = setInterval(function () { if (apiReady()) { clearInterval(wait); schedule(); } }, 400);
    setTimeout(function () { clearInterval(wait); }, 60000);
})();
