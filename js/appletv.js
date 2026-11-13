/* ============================================================================
   appletv.js – Apple-TV-App-Klon (tvOS 26 / Liquid Glass) für Jellyfin Web 12.x
   Teil „JavaScript“: alles, was Branding-CSS allein nicht kann.
   Erzeugt aus src/{core,hero,detail,episodes,profiles,tabbar,router}.js (build.py).
   Module:  1 Hero-Karussell (Startseite)        2 Detailseite (Film/Serie/Folge)
            3 Episodenregal mit Staffel-Menü     4 Profilauswahl „Wer schaut?“
            5 Glas-Tableiste (Mobil < 800 px)    6 Router (SPA-Navigation)
   Laden:   über das Plugin „Cinema Glass“ (empfohlen) oder von Hand über das Plugin
            „JavaScript Injector“ („Requires authentication“ AUS, damit die Profilauswahl läuft).
   Regeln:  nur window.ApiClient / öffentliche Jellyfin-API, keine Fremdhosts,
            kein eval, Texte per textContent, native Elemente nur verbergen (.atv-hide).
   CSS:     appletv-js.css; build.py bettet es an der Markierung ATV_CSS ins Bundle ein.
   ========================================================================== */
(function () {
    'use strict';
    if (window.__atvJs) return;
    window.__atvJs = true;
    document.documentElement.classList.add('atv-js');

    var CSS_INLINE = '/* ==========================================================================\n   appletv-js.css – Stile für die per JavaScript erzeugten Elemente des\n   Apple-TV-App-Klons (tvOS 26) in Jellyfin Web 12.4. Gilt nur, wenn appletv.js\n   läuft (html.atv-js). Alle Maße sind aus den Spezifikationen (spec/home,\n   spec/detail) bei 1920×1080 gemessen; --atv-u = 1 px @1920 (skaliert mit vw).\n   Klassenpräfix: atv-   |   Verstecken nativer Elemente: .atv-hide\n   ========================================================================== */\nhtml.atv-js {\n    --atv-u: calc(100vw / 1920);\n    --atv-bg: #000000;\n    --atv-font: -apple-system, BlinkMacSystemFont, "SF Pro Display", "SF Pro Text", "Inter", "Helvetica Neue", system-ui, sans-serif;\n    --atv-glass: rgba(20, 20, 22, .42);\n    --atv-glass-blur: blur(24px) saturate(160%);\n    --atv-glass-line: inset 0 0 0 1px rgba(255, 255, 255, .12), inset 0 1px 0 rgba(255, 255, 255, .20);\n    --atv-ease: cubic-bezier(.2, .8, .2, 1);\n}\nhtml.atv-js .atv-hide { display: none !important; }\nhtml.atv-js .atv-hero, html.atv-js .atv-detail, html.atv-js .atv-shelf,\nhtml.atv-js .atv-who, html.atv-js .atv-tabbar, html.atv-js .atv-menu, html.atv-js .atv-about {\n    font-family: var(--atv-font);\n    color: #fff;\n    -webkit-font-smoothing: antialiased;\n}\nhtml.atv-js .atv-hero *, html.atv-js .atv-detail *, html.atv-js .atv-shelf *,\nhtml.atv-js .atv-who *, html.atv-js .atv-tabbar *, html.atv-js .atv-menu *, html.atv-js .atv-about *, html.atv-js .atv-person, html.atv-js .atv-person *, html.atv-js .atv-search, html.atv-js .atv-search * { box-sizing: border-box; }\nhtml.atv-js .atv-hero button, html.atv-js .atv-detail button, html.atv-js .atv-shelf button,\nhtml.atv-js .atv-who button, html.atv-js .atv-tabbar a, html.atv-js .atv-menu button {\n    font: inherit; border: 0; padding: 0; margin: 0; cursor: pointer;\n    -webkit-tap-highlight-color: transparent; text-decoration: none;\n}\nhtml.atv-js button.atv-hero__nav, html.atv-js button.atv-season, html.atv-js button.atv-who__user,\nhtml.atv-js button.atv-who__close, html.atv-js button.atv-menu__item, html.atv-js button.atv-ep__more { background: transparent; color: #fff; }\nhtml.atv-js .atv-hero svg, html.atv-js .atv-detail svg, html.atv-js .atv-shelf svg,\nhtml.atv-js .atv-who svg, html.atv-js .atv-tabbar svg, html.atv-js .atv-menu svg { fill: currentColor; display: block; }\n\n/* ---------- gemeinsame Bausteine: Pille, Kreis, Glas ---------- */\nhtml.atv-js .atv-pill {\n    position: relative; overflow: hidden;\n    display: inline-flex; align-items: center; justify-content: center; gap: calc(12 * var(--atv-u));\n    height: max(44px, calc(66 * var(--atv-u))); min-width: max(140px, calc(218 * var(--atv-u)));\n    padding: 0 max(22px, calc(40 * var(--atv-u)));\n    border-radius: 999px; background: #fff; color: #000;\n    font-size: max(16px, calc(29 * var(--atv-u))); font-weight: 600; letter-spacing: .4px; white-space: nowrap;\n    transition: transform .25s var(--atv-ease), background .25s, color .25s;\n}\nhtml.atv-js .atv-pill svg { width: max(16px, calc(26 * var(--atv-u))); height: max(16px, calc(26 * var(--atv-u))); }\nhtml.atv-js .atv-pill:hover, html.atv-js .atv-pill:focus-visible { transform: scale(1.04); }\nhtml.atv-js .atv-pill:active { transform: scale(.98); }\nhtml.atv-js .atv-pill--glass { background: var(--atv-glass); color: #fff; -webkit-backdrop-filter: var(--atv-glass-blur); backdrop-filter: var(--atv-glass-blur); box-shadow: var(--atv-glass-line); }\nhtml.atv-js .atv-pill--glass:hover, html.atv-js .atv-pill--glass:focus-visible { background: #fff; color: #000; }\nhtml.atv-js .atv-pill__bar { position: absolute; left: 0; right: 0; bottom: 0; height: max(3px, calc(4 * var(--atv-u))); background: rgba(0, 0, 0, .15); }\nhtml.atv-js .atv-pill__bar i { display: block; height: 100%; background: rgba(0, 0, 0, .6); }\nhtml.atv-js .atv-circle {\n    display: inline-flex; align-items: center; justify-content: center; flex: 0 0 auto;\n    width: max(44px, calc(66 * var(--atv-u))); height: max(44px, calc(66 * var(--atv-u))); border-radius: 50%;\n    background: var(--atv-glass); color: #fff;\n    -webkit-backdrop-filter: var(--atv-glass-blur); backdrop-filter: var(--atv-glass-blur); box-shadow: var(--atv-glass-line);\n    transition: transform .25s var(--atv-ease), background .25s, color .25s;\n}\nhtml.atv-js .atv-circle svg { width: 46%; height: 46%; }\nhtml.atv-js .atv-circle:hover, html.atv-js .atv-circle:focus-visible { background: #fff; color: #000; transform: scale(1.13); }\nhtml.atv-js .atv-circle.is-on { background: rgba(255, 255, 255, .92); color: #000; }\nhtml.atv-js .atv-circle:focus-visible, html.atv-js .atv-pill:focus-visible { outline: 3px solid rgba(255, 255, 255, .9); outline-offset: 3px; }\nhtml.atv-js .atv-coin {\n    display: inline-flex; align-items: center; justify-content: center; flex: 0 0 auto;\n    width: max(22px, calc(36 * var(--atv-u))); height: max(22px, calc(36 * var(--atv-u))); border-radius: 50%;\n    background: rgba(0, 0, 0, .55); color: #fff; box-shadow: inset 0 0 0 1px rgba(255, 255, 255, .25);\n}\nhtml.atv-js .atv-coin svg { width: 55%; height: 55%; }\nhtml.atv-js .atv-badge {\n    display: inline-flex; align-items: center; height: max(16px, calc(22 * var(--atv-u))); padding: 0 max(4px, calc(6 * var(--atv-u)));\n    border: 1px solid rgba(255, 255, 255, .64); border-radius: max(3px, calc(4 * var(--atv-u)));\n    font-size: max(10px, calc(15 * var(--atv-u))); font-weight: 600; line-height: 1; letter-spacing: .3px; color: rgba(255, 255, 255, .9); white-space: nowrap;\n}\nhtml.atv-js .atv-badge--rt { border-color: transparent; padding-left: 0; }\nhtml.atv-js .atv-badge svg { width: 1em; height: 1em; margin-right: .3em; }\nhtml.atv-js .atv-more {\n    position: absolute !important; right: 0; bottom: .08em;\n    display: inline-flex !important; align-items: center; justify-content: center;\n    height: auto !important; padding: .28em .9em !important; border: 0; border-radius: 999px !important;\n    background: rgba(120, 120, 128, .42) !important; color: #fff !important;\n    -webkit-backdrop-filter: blur(20px) saturate(1.6); backdrop-filter: blur(20px) saturate(1.6);\n    box-shadow: inset 0 0 0 1px rgba(255, 255, 255, .14) !important;\n    font-size: clamp(11px, .68vw, 14px) !important; font-weight: 600 !important; letter-spacing: .6px; line-height: 1.25 !important;\n    text-transform: uppercase; cursor: pointer; text-shadow: none; transition: background .2s;\n}\nhtml.atv-js .atv-more:hover, html.atv-js .atv-more:focus-visible { background: rgba(120, 120, 128, .62) !important; outline: 0; }\nhtml.atv-js .atv-clamp { position: relative; width: fit-content; display: -webkit-box; -webkit-box-orient: vertical; overflow: hidden; transition: max-height .3s var(--atv-ease); }\n/* aufgeklappt: kein Clamp, Höhe wird per max-height animiert (JS), bei sehr langem Text eigener Scrollbereich */\nhtml.atv-js .atv-clamp.is-open, html.atv-js .atv-clamp.is-closing { display: block !important; -webkit-line-clamp: unset !important; overflow: hidden; }\nhtml.atv-js .atv-clamp.is-open { overflow-y: auto; scrollbar-width: thin; padding-right: 0; text-shadow: 0 1px 3px rgba(0, 0, 0, .55); }\nhtml.atv-js .atv-clamp.is-open .atv-more, html.atv-js .atv-clamp.is-closing .atv-more { position: static; display: inline-flex; vertical-align: middle; margin-left: 10px; text-shadow: none; }\nhtml.atv-js .atv-clamp--2 { -webkit-line-clamp: 2; }\nhtml.atv-js .atv-clamp--3 { -webkit-line-clamp: 3; }\nhtml.atv-js .atv-sep { opacity: .7; margin: 0 .35em; }\n\n/* ==========================================================================\n   1. Hero-Karussell (Startseite)\n   ========================================================================== */\nhtml.atv-js .homePage .homeSectionsContainer { position: relative; z-index: 1; }\nhtml.atv-js .atv-hero {\n    position: relative; z-index: 0; isolation: isolate; overflow: hidden;\n    width: 100%; height: 100vh; min-height: 560px;\n    /* relative top statt negativem margin-top: Jellyfins Seiten-Margin würde sonst kollabieren;\n       Breite 100 % (nicht 100vw: mit Bildlaufleiste gäbe das horizontales Scrollen) */\n    top: calc(-1 * var(--atv-pad, 0px));\n    margin: 0 0 calc(var(--atv-hero-overlap, -19vh) - var(--atv-pad, 0px));\n    background: var(--atv-bg); color: #fff;\n}\nhtml.atv-js .atv-hero__slide { position: absolute; inset: 0; opacity: 0; pointer-events: none; transition: opacity .5s var(--atv-ease); }\nhtml.atv-js .atv-hero__slide.is-active { opacity: 1; pointer-events: auto; }\nhtml.atv-js .atv-hero__art { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: cover; object-position: 50% 20%; background: #000; cursor: pointer; }\nhtml.atv-js .atv-hero__shade {\n    position: absolute; inset: 0; pointer-events: none;\n    background: linear-gradient(to top, #000000 0%, #000000 14%, rgba(0, 0, 0, .88) 24%, rgba(0, 0, 0, .45) 38%, rgba(0, 0, 0, 0) 58%),\n                linear-gradient(to right, rgba(0, 0, 0, .5) 0%, rgba(0, 0, 0, .15) 30%, rgba(0, 0, 0, 0) 45%);\n}\nhtml.atv-js .atv-hero__lockup {\n    position: absolute; left: calc(80 * var(--atv-u)); bottom: calc(318 * var(--atv-u)); width: calc(620 * var(--atv-u)); max-width: 78vw;\n    display: flex; flex-direction: column; align-items: flex-start; gap: calc(17 * var(--atv-u));\n    opacity: 0; transform: translateY(6px); transition: opacity .2s ease-in, transform .2s ease-in;\n    text-shadow: 0 1px 2px rgba(0, 0, 0, .35);\n}\nhtml.atv-js .atv-hero__slide.is-active .atv-hero__lockup { opacity: 1; transform: none; transition: opacity .3s var(--atv-ease) .1s, transform .3s var(--atv-ease) .1s; }\nhtml.atv-js .atv-hero__logo { display: block; max-width: calc(527 * var(--atv-u)); max-height: calc(139 * var(--atv-u)); width: auto; height: auto; object-fit: contain; object-position: left bottom; filter: drop-shadow(0 2px 10px rgba(0, 0, 0, .45)); }\nhtml.atv-js .atv-hero__title { margin: 0; font-size: max(28px, calc(57 * var(--atv-u))); font-weight: 700; letter-spacing: -.5px; line-height: 1.05; max-width: 100%; }\nhtml.atv-js .atv-hero__meta { display: flex; align-items: center; gap: calc(10 * var(--atv-u)); font-size: max(13px, calc(25 * var(--atv-u))); font-weight: 500; letter-spacing: .15px; color: #fff; white-space: nowrap; }\nhtml.atv-js .atv-hero__meta .atv-badge { margin-left: calc(4 * var(--atv-u)); }\nhtml.atv-js .atv-hero__desc { margin: 0; max-width: calc(545 * var(--atv-u)); padding-right: max(52px, calc(64 * var(--atv-u))); font-size: max(13px, calc(25 * var(--atv-u))); line-height: max(18px, calc(32 * var(--atv-u))); font-weight: 400; letter-spacing: .15px; color: rgba(255, 255, 255, .85); }\nhtml.atv-js .atv-hero__actions { display: flex; align-items: center; gap: calc(24 * var(--atv-u)); margin-top: calc(57 * var(--atv-u)); }\nhtml.atv-js .atv-hero__dots {\n    position: absolute; z-index: 3; left: 50%; bottom: calc(201 * var(--atv-u)); transform: translateX(-50%);\n    display: flex; align-items: center; gap: calc(11 * var(--atv-u)); height: max(24px, calc(42 * var(--atv-u))); padding: 0 calc(47 * var(--atv-u));\n    border-radius: 999px; background: rgba(0, 0, 0, .30); -webkit-backdrop-filter: saturate(1.8) blur(10px); backdrop-filter: saturate(1.8) blur(10px);\n}\nhtml.atv-js button.atv-hero__dot { width: max(6px, calc(10 * var(--atv-u))); height: max(6px, calc(10 * var(--atv-u))); border-radius: 999px; background: rgba(255, 255, 255, .45); transition: width .3s ease-out, background .3s ease-out; }\nhtml.atv-js button.atv-hero__dot.is-active { width: max(18px, calc(30 * var(--atv-u))); background: #fff; }\nhtml.atv-js .atv-hero__dot:focus-visible { outline: 2px solid #fff; outline-offset: 2px; }\nhtml.atv-js .atv-hero__nav { position: absolute; z-index: 3; top: 30%; height: 40%; width: 7%; opacity: 0; color: #fff; transition: opacity .25s linear; display: flex; align-items: center; justify-content: center; }\nhtml.atv-js .atv-hero__nav--prev { left: 0; } html.atv-js .atv-hero__nav--next { right: 0; }\nhtml.atv-js .atv-hero:hover .atv-hero__nav, html.atv-js .atv-hero__nav:focus-visible { opacity: .85; }\nhtml.atv-js .atv-hero__nav svg { width: calc(44 * var(--atv-u)); height: calc(44 * var(--atv-u)); filter: drop-shadow(0 1px 4px rgba(0, 0, 0, .6)); }\nhtml.atv-js .atv-hero__hint { display: none; }\n\n/* ==========================================================================\n   2. Detailseite\n   ========================================================================== */\nhtml.atv-js .atv-detail {\n    position: relative; isolation: isolate; overflow: hidden;\n    width: 100%; top: calc(-1 * var(--atv-pad, 0px)); margin: 0 0 calc(-1 * var(--atv-pad, 0px)); height: min(56.25vw, 100vh); min-height: 560px;\n    background: var(--atv-bg); color: #fff;\n}\nhtml.atv-js .atv-detail--noart { height: min(40vw, 62vh); min-height: 420px; background: radial-gradient(80% 70% at 30% 20%, #2c2c2e 0%, var(--atv-bg) 70%); }\nhtml.atv-js .atv-detail--noart .atv-detail__art { display: none; }\nhtml.atv-js .atv-detail__art { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: cover; object-position: 50% 20%; background: #000; }\nhtml.atv-js .atv-detail__shade {\n    position: absolute; inset: 0; pointer-events: none;\n    background: linear-gradient(to top, #000000 0%, rgba(0, 0, 0, .85) 12%, rgba(0, 0, 0, .45) 28%, rgba(0, 0, 0, 0) 46%),\n                linear-gradient(to right, rgba(0, 0, 0, .45) 0%, rgba(0, 0, 0, 0) 40%);\n}\nhtml.atv-js .atv-detail__lockup {\n    position: absolute; left: calc(80 * var(--atv-u)); bottom: calc(110 * var(--atv-u)); width: calc(640 * var(--atv-u)); max-width: 70vw;\n    display: flex; flex-direction: column; align-items: flex-start; gap: calc(14 * var(--atv-u));\n    text-shadow: 0 1px 2px rgba(0, 0, 0, .35);\n}\nhtml.atv-js .atv-detail__award { display: inline-flex; align-items: center; height: max(18px, calc(26 * var(--atv-u))); padding: 0 calc(10 * var(--atv-u)); border: 1px solid rgba(255, 255, 255, .64); border-radius: 999px; font-size: max(10px, calc(14 * var(--atv-u))); font-weight: 600; color: rgba(255, 255, 255, .9); }\nhtml.atv-js .atv-detail__logo { display: block; max-width: calc(420 * var(--atv-u)); max-height: calc(150 * var(--atv-u)); width: auto; height: auto; object-fit: contain; object-position: left bottom; filter: drop-shadow(0 2px 10px rgba(0, 0, 0, .45)); }\nhtml.atv-js .atv-detail__title { margin: 0; font-size: max(26px, calc(64 * var(--atv-u))); font-weight: 700; letter-spacing: -.5px; line-height: 1.05; }\nhtml.atv-js .atv-detail__sub { margin: calc(-6 * var(--atv-u)) 0 0; font-size: max(13px, calc(22 * var(--atv-u))); font-weight: 400; color: rgba(255, 255, 255, .85); }\nhtml.atv-js .atv-detail__meta { display: flex; align-items: center; flex-wrap: wrap; gap: calc(8 * var(--atv-u)); font-size: max(13px, calc(24 * var(--atv-u))); font-weight: 600; color: #fff; }\nhtml.atv-js .atv-detail__desc { margin: 0; max-width: calc(560 * var(--atv-u)); padding-right: max(52px, calc(64 * var(--atv-u))); font-size: max(13px, calc(22 * var(--atv-u))); line-height: max(18px, calc(28 * var(--atv-u))); font-weight: 400; color: rgba(255, 255, 255, .85); }\nhtml.atv-js .atv-detail__facts { display: flex; align-items: center; flex-wrap: wrap; gap: calc(8 * var(--atv-u)); font-size: max(12px, calc(20 * var(--atv-u))); color: rgba(255, 255, 255, .64); }\nhtml.atv-js .atv-detail__facts .atv-badge { opacity: .85; }\nhtml.atv-js .atv-detail__actions { display: flex; align-items: center; gap: calc(16 * var(--atv-u)); margin-top: calc(10 * var(--atv-u)); flex-wrap: nowrap; width: max-content; max-width: 90vw; }\nhtml.atv-js .atv-detail__note { margin: calc(-4 * var(--atv-u)) 0 0; font-size: max(11px, calc(16 * var(--atv-u))); color: rgba(255, 255, 255, .64); }\nhtml.atv-js .atv-detail__credits {\n    position: absolute; right: calc(80 * var(--atv-u)); bottom: calc(110 * var(--atv-u)); max-width: calc(420 * var(--atv-u)); text-align: right;\n    font-size: max(11px, calc(18 * var(--atv-u))); line-height: max(16px, calc(24 * var(--atv-u))); text-shadow: 0 1px 2px rgba(0, 0, 0, .35);\n}\nhtml.atv-js .atv-detail__credits p { margin: 0 0 calc(10 * var(--atv-u)); }\nhtml.atv-js .atv-detail__credits p:last-child { margin-bottom: 0; }\nhtml.atv-js .atv-detail__credits .atv-label { color: rgba(255, 255, 255, .64); margin-right: .35em; }\nhtml.atv-js .atv-detail__credits a { color: rgba(255, 255, 255, .55); text-decoration: none; transition: color .15s; }\nhtml.atv-js .atv-detail__credits a:hover { color: rgba(255, 255, 255, .92); }\n/* native Elemente, die der Umbau ersetzt: SOFORT verborgen, sobald html.atv-js steht (kein Flackern);\n   .atv-native (Fallback nach 2,5 s oder fremder Item-Typ) zeigt die native Seite wieder */\nhtml.atv-js .itemDetailPage:not(.atv-native) #itemBackdrop,\nhtml.atv-js .itemDetailPage:not(.atv-native) .detailLogo,\nhtml.atv-js .itemDetailPage:not(.atv-native) .detailRibbon,\nhtml.atv-js .itemDetailPage:not(.atv-native) .detailImageContainer,\nhtml.atv-js .itemDetailPage:not(.atv-native) .detailSectionContent,\nhtml.atv-js .itemDetailPage:not(.atv-native) .itemDetailsGroup,\nhtml.atv-js .itemDetailPage:not(.atv-native) .trackSelections { display: none !important; }\nhtml.atv-js .itemDetailPage:not(.atv-native):not(.atv-detail-on):not(.atv-person-on) .detailPageWrapperContainer { opacity: 0; }\nhtml.atv-js .itemDetailPage.atv-detail-on .detailPageWrapperContainer, html.atv-js .itemDetailPage.atv-person-on .detailPageWrapperContainer { opacity: 1; transition: opacity .25s ease-out; }\nhtml.atv-js .atv-detail, html.atv-js .atv-person { animation: atv-fade .3s ease-out; }\nhtml.atv-js .homePage:not(.atv-native):not(.atv-hero-on) .homeSectionsContainer { opacity: 0; }\nhtml.atv-js .homePage.atv-hero-on .homeSectionsContainer { opacity: 1; transition: opacity .3s ease-out; }\n/* Spezifität bewusst hoch: das Branding-CSS blendet .detailPagePrimaryContainer bei :has(.atv-person) aus,\n   darin liegen aber Jellyfins Regale „Filme/Serien“ der Personenseite */\nhtml.atv-js.atv-js .itemDetailPage.atv-detail-on.atv-detail-on .detailPagePrimaryContainer, html.atv-js.atv-js .itemDetailPage.atv-person-on.atv-person-on:has(.atv-person) .detailPagePrimaryContainer { display: block !important; opacity: 1 !important; min-height: 0 !important; height: auto !important; padding-top: 0 !important; padding-bottom: 0 !important; margin-top: 0 !important; animation: none !important; }\nhtml.atv-js.atv-js .itemDetailPage.atv-person-on .detailPagePrimaryContent { padding-top: 0 !important; }\nhtml.atv-js .itemDetailPage.atv-detail-on .detailPagePrimaryContent, html.atv-js .itemDetailPage.atv-person-on .detailPagePrimaryContent,\nhtml.atv-js .itemDetailPage.atv-detail-on .detailSection, html.atv-js .itemDetailPage.atv-person-on .detailSection { display: block !important; opacity: 1 !important; padding-left: 0 !important; margin-left: 0 !important; width: auto !important; }\nhtml.atv-js .itemDetailPage.atv-detail-on .detailPageWrapperContainer, html.atv-js .itemDetailPage.atv-person-on .detailPageWrapperContainer { min-height: 0 !important; padding-top: 0 !important; margin-top: 0 !important; }\n/* Personenkarten: nie ein Play-Overlay (Klick = nur Personenseite) */\nhtml.atv-js .personCard .cardOverlayFab-primary, html.atv-js .personCard [data-action="play"], html.atv-js .personCard [data-action="resume"] { display: none !important; }\n\n/* ---------- Personenseite ---------- */\nhtml.atv-js .atv-person { display: flex; align-items: flex-start; gap: calc(48 * var(--atv-u)); padding: calc(60 * var(--atv-u)) calc(80 * var(--atv-u)) calc(24 * var(--atv-u)); color: #fff; }\nhtml.atv-js .atv-person__photo { flex: 0 0 auto; width: max(140px, calc(260 * var(--atv-u))); height: max(140px, calc(260 * var(--atv-u))); border-radius: 50%; overflow: hidden; background: #2b2b2b; box-shadow: inset 0 0 0 1px rgba(255, 255, 255, .16), 0 20px 50px rgba(0, 0, 0, .45); display: flex; align-items: center; justify-content: center; font-size: max(48px, calc(96 * var(--atv-u))); font-weight: 600; color: rgba(255, 255, 255, .55); }\nhtml.atv-js .atv-person__photo img { width: 100%; height: 100%; object-fit: cover; object-position: 50% 15%; display: block; }\nhtml.atv-js .atv-person__body { flex: 1 1 auto; min-width: 0; padding-top: calc(16 * var(--atv-u)); }\nhtml.atv-js .atv-person__name { margin: 0 0 calc(10 * var(--atv-u)); font-size: max(28px, calc(56 * var(--atv-u))); font-weight: 700; letter-spacing: -.5px; line-height: 1.05; }\nhtml.atv-js .atv-person__facts { margin: 0 0 calc(18 * var(--atv-u)); font-size: max(13px, calc(20 * var(--atv-u))); color: rgba(255, 255, 255, .64); }\nhtml.atv-js .atv-person__bio { margin: 0; max-width: calc(900 * var(--atv-u)); padding-right: max(52px, calc(64 * var(--atv-u))); font-size: max(14px, calc(22 * var(--atv-u))); line-height: max(19px, calc(30 * var(--atv-u))); color: rgba(255, 255, 255, .85); -webkit-line-clamp: 4; }\nhtml.atv-js .itemDetailPage.atv-person-on #listChildrenCollapsible > .sectionTitle.hide { display: none; }\nhtml.atv-js .itemDetailPage.atv-person-on #listChildrenCollapsible { margin-top: 0 !important; }\nhtml.atv-js .itemDetailPage.atv-person-on #childrenContent .sectionTitle { padding-left: 0 !important; }\n@media (max-width: 799px) {\n    html.atv-js .atv-person { flex-direction: column; align-items: center; text-align: center; gap: 16px; padding: 72px 16px 8px; }\n    html.atv-js .atv-person__photo { width: 160px; height: 160px; }\n    html.atv-js .atv-person__body { padding-top: 0; }\n    html.atv-js .atv-person__name { font-size: 28px; }\n    html.atv-js .atv-person__bio { text-align: left; padding-right: 60px; max-width: none; width: 100%; }\n    html.atv-js .atv-person__body { width: 100%; }\n}\n\n/* ---------- Suche: Struktur (Optik gehört ins Branding-CSS) ---------- */\nhtml.atv-js #searchPage .searchSuggestions { display: none !important; }\nhtml.atv-js .atv-search { color: #fff; padding: 0 calc(80 * var(--atv-u)) calc(40 * var(--atv-u)); }\nhtml.atv-js .atv-search[hidden] { display: none; }\nhtml.atv-js .atv-search__title { margin: calc(24 * var(--atv-u)) 0 calc(16 * var(--atv-u)); font-size: max(17px, calc(31 * var(--atv-u))); font-weight: 700; letter-spacing: .4px; }\nhtml.atv-js .atv-search__pills { display: flex; flex-wrap: wrap; gap: calc(12 * var(--atv-u)); margin-bottom: calc(24 * var(--atv-u)); }\nhtml.atv-js .atv-search__pill { display: inline-flex; align-items: center; gap: 8px; height: max(36px, calc(44 * var(--atv-u))); padding: 0 max(14px, calc(20 * var(--atv-u))); border-radius: 999px; background: var(--atv-glass); color: #fff; font-size: max(14px, calc(18 * var(--atv-u))); font-weight: 500; -webkit-backdrop-filter: var(--atv-glass-blur); backdrop-filter: var(--atv-glass-blur); box-shadow: var(--atv-glass-line); white-space: nowrap; }\nhtml.atv-js .atv-search__pill svg { width: 1em; height: 1em; opacity: .7; }\nhtml.atv-js .atv-search__pill:hover, html.atv-js .atv-search__pill:focus-visible { background: #fff; color: #000; }\nhtml.atv-js .atv-search__grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(max(120px, calc(200 * var(--atv-u))), 1fr)); gap: calc(24 * var(--atv-u)) calc(20 * var(--atv-u)); }\nhtml.atv-js .atv-search__card { display: block; position: relative; aspect-ratio: 2 / 3; border-radius: max(10px, calc(14 * var(--atv-u))); overflow: hidden; background: #2b2b2b; box-shadow: inset 0 0 0 1px rgba(255, 255, 255, .16); transition: transform .25s var(--atv-ease), box-shadow .25s var(--atv-ease); color: #fff; text-decoration: none; }\nhtml.atv-js .atv-search__card img { width: 100%; height: 100%; object-fit: cover; display: block; }\nhtml.atv-js .atv-search__card:hover, html.atv-js .atv-search__card:focus-visible { transform: scale(1.06); box-shadow: 0 20px 40px rgba(0, 0, 0, .5), inset 0 1px 0 rgba(255, 255, 255, .25); z-index: 1; }\nhtml.atv-js .atv-search__card span { position: absolute; left: 0; right: 0; bottom: 0; padding: 40px 10px 10px; background: linear-gradient(to top, rgba(0, 0, 0, .75), rgba(0, 0, 0, 0)); font-size: 13px; font-weight: 600; text-align: center; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }\nhtml.atv-js .atv-search-count { margin-left: .5em; font-weight: 400; color: rgba(255, 255, 255, .5); font-size: .8em; }\nhtml.atv-js .atv-search__empty { padding: calc(48 * var(--atv-u)) 0; text-align: center; color: rgba(255, 255, 255, .64); font-size: max(15px, calc(22 * var(--atv-u))); }\nhtml.atv-js .atv-search__empty[hidden] { display: none; }\n@media (max-width: 799px) {\n    html.atv-js .atv-search { padding: 0 16px 24px; }\n    html.atv-js .atv-search__grid { grid-template-columns: repeat(auto-fill, minmax(104px, 1fr)); gap: 14px 10px; }\n}\nhtml.atv-js.atv-page-detail .backdropContainer, html.atv-js.atv-page-home .backdropContainer { display: none !important; }\nhtml.atv-js.atv-page-detail .backgroundContainer, html.atv-js.atv-page-home .backgroundContainer { background: var(--atv-bg) !important; }\nhtml.atv-js .itemDetailPage.atv-detail-on .detailPageWrapperContainer { background: transparent; }\nhtml.atv-js .itemDetailPage.atv-detail-on.atv-detail-episodes .nextUpSection,\nhtml.atv-js .itemDetailPage.atv-detail-on.atv-detail-episodes #listChildrenCollapsible,\nhtml.atv-js .itemDetailPage.atv-detail-on.atv-detail-episodes #childrenCollapsible { display: none !important; }\n\n@keyframes atv-fade { from { opacity: 0; } to { opacity: 1; } }\n@keyframes atv-pop { from { opacity: 0; transform: scale(.98); } to { opacity: 1; transform: none; } }\n\n/* ==========================================================================\n   3. Regal (Episoden) + Glas-Menü (Staffel)\n   ========================================================================== */\nhtml.atv-js .atv-shelf { position: relative; margin: calc(48 * var(--atv-u)) 0 calc(40 * var(--atv-u)); }\nhtml.atv-js .atv-shelf__head { position: relative; display: flex; align-items: center; gap: calc(12 * var(--atv-u)); padding: 0 var(--atv-gutter, calc(80 * var(--atv-u))); margin-bottom: calc(40 * var(--atv-u)); }\nhtml.atv-js .atv-shelf__title, html.atv-js .atv-season {\n    display: inline-flex; align-items: center; gap: calc(6 * var(--atv-u)); margin: 0;\n    font-size: max(17px, calc(31 * var(--atv-u))); font-weight: 700; letter-spacing: .4px; color: #fff; line-height: 1.2;\n}\nhtml.atv-js .atv-season svg { width: .8em; height: .8em; margin-top: .1em; color: #0a84ff; transition: transform .2s; }\nhtml.atv-js .atv-season[aria-expanded="true"] svg { transform: rotate(180deg); }\nhtml.atv-js .atv-shelf__count { font-size: max(12px, calc(20 * var(--atv-u))); color: rgba(255, 255, 255, .64); font-weight: 400; margin-left: calc(6 * var(--atv-u)); }\nhtml.atv-js .atv-shelf__scroller {\n    display: flex; gap: calc(20 * var(--atv-u)); overflow-x: auto; overflow-y: hidden; scroll-snap-type: x mandatory;\n    padding: 12px var(--atv-gutter, calc(80 * var(--atv-u))) 16px; scroll-padding-left: var(--atv-gutter, calc(80 * var(--atv-u))); scrollbar-width: none; -webkit-overflow-scrolling: touch;\n}\nhtml.atv-js .atv-shelf__scroller::-webkit-scrollbar { display: none; }\nhtml.atv-js .atv-ep {\n    position: relative; flex: 0 0 auto; scroll-snap-align: start; overflow: hidden; cursor: pointer;\n    width: max(230px, calc(304 * var(--atv-u))); aspect-ratio: 304 / 234; border-radius: max(12px, calc(14 * var(--atv-u)));\n    background: #2b2b2b; box-shadow: inset 0 0 0 1px rgba(255, 255, 255, .16);\n    transition: transform .25s var(--atv-ease), box-shadow .25s var(--atv-ease);\n}\nhtml.atv-js .atv-ep:focus-visible { outline: 3px solid #fff; outline-offset: 3px; }\nhtml.atv-js .atv-ep__img { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: cover; }\nhtml.atv-js .atv-ep__grad { position: absolute; left: 0; right: 0; bottom: 0; height: 68%; background: linear-gradient(to top, rgba(0, 0, 0, .78) 0%, rgba(0, 0, 0, .55) 35%, rgba(0, 0, 0, .25) 65%, rgba(0, 0, 0, 0) 100%); pointer-events: none; }\nhtml.atv-js .atv-ep__scrim { position: absolute; inset: 0; background: rgba(51, 51, 51, .3); opacity: 0; transition: opacity .15s linear; pointer-events: none; }\nhtml.atv-js .atv-ep:hover .atv-ep__scrim, html.atv-js .atv-ep:focus-within .atv-ep__scrim { opacity: 1; }\nhtml.atv-js .atv-ep__body { position: absolute; left: 0; right: 0; bottom: 0; padding: 0 max(12px, calc(14 * var(--atv-u))) max(12px, calc(14 * var(--atv-u))); display: flex; flex-direction: column; gap: max(2px, calc(3 * var(--atv-u))); }\nhtml.atv-js .atv-ep__tag { font-size: max(10px, calc(11 * var(--atv-u))); font-weight: 500; letter-spacing: .6px; text-transform: uppercase; color: rgba(255, 255, 255, .7); line-height: 1.3; }\nhtml.atv-js .atv-ep__title { font-size: max(14px, calc(16 * var(--atv-u))); font-weight: 600; color: #fff; line-height: 1.25; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; margin-bottom: max(2px, calc(2 * var(--atv-u))); }\nhtml.atv-js .atv-ep__desc { font-size: max(12px, calc(13.5 * var(--atv-u))); font-weight: 500; line-height: 1.25; color: rgba(255, 255, 255, .7); }\nhtml.atv-js .atv-ep__foot { display: flex; align-items: center; gap: 8px; margin-top: max(4px, calc(6 * var(--atv-u))); font-size: max(11px, calc(12.5 * var(--atv-u))); font-weight: 600; color: rgba(255, 255, 255, .7); }\nhtml.atv-js .atv-ep__foot svg { width: 1em; height: 1em; }\nhtml.atv-js .atv-ep__bar { width: max(70px, calc(100 * var(--atv-u))); height: 3px; border-radius: 2px; background: rgba(255, 255, 255, .3); overflow: hidden; }\nhtml.atv-js .atv-ep__bar i { display: block; height: 100%; background: rgba(255, 255, 255, .9); }\nhtml.atv-js button.atv-ep__more {\n    position: absolute; right: 10px; bottom: 10px; width: 28px; height: 28px; border-radius: 50%; display: flex; align-items: center; justify-content: center;\n    background: rgba(40, 40, 40, .5); color: #fff; -webkit-backdrop-filter: saturate(1.8) blur(20px); backdrop-filter: saturate(1.8) blur(20px);\n    opacity: 0; transition: opacity .15s linear, background .15s;\n}\nhtml.atv-js .atv-ep__more svg { width: 16px; height: 16px; }\nhtml.atv-js .atv-ep:hover .atv-ep__more, html.atv-js .atv-ep:focus-within .atv-ep__more, html.atv-js .atv-ep__more:focus-visible { opacity: 1; }\nhtml.atv-js .atv-ep__more:hover { background: rgba(255, 255, 255, .25); }\n/* Folgenkarten: sichtbares Play-Symbol (kein Hover nötig, auch auf dem Handy) */\nhtml.atv-js .atv-ep__play { position: absolute; left: 12px; top: 12px; z-index: 2; width: 38px; height: 38px; border-radius: 50%; display: flex; align-items: center; justify-content: center; color: #fff; pointer-events: none;\n    background: rgba(40, 40, 42, .55); -webkit-backdrop-filter: blur(20px) saturate(1.8); backdrop-filter: blur(20px) saturate(1.8);\n    box-shadow: inset 0 0 0 1px rgba(255, 255, 255, .18), 0 6px 18px rgba(0, 0, 0, .35); transition: transform .25s var(--atv-ease, ease), background .2s, color .2s; }\nhtml.atv-js .atv-ep__play svg { width: 20px; height: 20px; fill: currentColor; margin-left: 2px; }\nhtml.atv-js .atv-ep:hover .atv-ep__play, html.atv-js .atv-ep:focus-within .atv-ep__play { transform: scale(1.1); background: rgba(255, 255, 255, .92); color: #000; }\n\nhtml.atv-js .atv-shelf__empty { padding: 0 var(--atv-gutter, calc(80 * var(--atv-u))); color: rgba(255, 255, 255, .5); font-size: 15px; }\nhtml.atv-js .atv-menu {\n    position: absolute; z-index: 60; top: 100%; left: var(--atv-gutter, calc(80 * var(--atv-u))); margin-top: 8px; min-width: 200px; padding: 6px;\n    border-radius: 12px; background: rgba(40, 40, 40, .7); -webkit-backdrop-filter: blur(60px) saturate(2.2); backdrop-filter: blur(60px) saturate(2.2);\n    box-shadow: inset 0 0 0 1px rgba(255, 255, 255, .2), 0 8px 40px rgba(0, 0, 0, .55); animation: atv-pop .12s ease-out;\n}\nhtml.atv-js .atv-menu__item { display: flex; align-items: center; gap: 10px; width: 100%; padding: 8px 12px; border-radius: 8px; font-size: 15px; color: rgba(255, 255, 255, .92); text-align: left; white-space: nowrap; }\nhtml.atv-js .atv-menu__item:hover, html.atv-js .atv-menu__item:focus-visible { background: rgba(255, 255, 255, .1); }\nhtml.atv-js .atv-menu__item svg { width: 14px; height: 14px; visibility: hidden; }\nhtml.atv-js .atv-menu__item.is-selected svg { visibility: visible; }\n\n/* ---------- Infoblock (About) ---------- */\nhtml.atv-js .atv-about { margin: calc(24 * var(--atv-u)) var(--atv-gutter, calc(80 * var(--atv-u))) calc(48 * var(--atv-u)); display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 20px; max-width: calc(1600 * var(--atv-u)); }\nhtml.atv-js .atv-about__col h3 { margin: 0 0 10px; font-size: max(13px, calc(17 * var(--atv-u))); font-weight: 700; color: rgba(255, 255, 255, .92); }\nhtml.atv-js .atv-about__col dl { margin: 0; font-size: max(12px, calc(14 * var(--atv-u))); line-height: 1.4; }\nhtml.atv-js .atv-about__col dt { color: rgba(255, 255, 255, .92); margin-top: 10px; }\nhtml.atv-js .atv-about__col dt:first-child { margin-top: 0; }\nhtml.atv-js .atv-about__col dd { margin: 0; color: rgba(255, 255, 255, .64); }\n\n/* ==========================================================================\n   4. Profilauswahl („Wer schaut?“) auf der Anmeldeseite\n   ========================================================================== */\nhtml.atv-js #loginPage.atv-who-on { position: relative; min-height: 100vh; }\nhtml.atv-js .atv-who {\n    position: fixed; inset: 0; z-index: 1; overflow: hidden; color: #fff;\n    background: radial-gradient(55% 45% at 18% 12%, #f3c9b4 0%, rgba(243, 201, 180, 0) 65%),\n                radial-gradient(45% 40% at 82% 22%, #cfe5f4 0%, rgba(207, 229, 244, 0) 65%),\n                radial-gradient(60% 50% at 55% 55%, #8fb7d6 0%, rgba(143, 183, 214, 0) 70%),\n                radial-gradient(70% 55% at 45% 100%, #4f7fa6 0%, rgba(79, 127, 166, 0) 70%), #7aa6cc;\n}\nhtml.atv-js .atv-who::before { content: ""; position: absolute; inset: -10%; background: inherit; filter: blur(60px) brightness(1.08) saturate(1.15); }\nhtml.atv-js .atv-who__title { position: absolute; left: 0; right: 0; top: 27%; margin: 0; text-align: center; font-size: max(24px, calc(42 * var(--atv-u))); font-weight: 700; letter-spacing: -.3px; text-shadow: 0 2px 16px rgba(0, 0, 0, .25); }\nhtml.atv-js .atv-who__row { position: absolute; left: 0; right: 0; top: 48%; transform: translateY(-50%); display: flex; justify-content: center; align-items: flex-start; flex-wrap: wrap; gap: calc(25 * var(--atv-u)); padding: 0 24px; }\nhtml.atv-js .atv-who__user { display: flex; flex-direction: column; align-items: center; width: max(96px, calc(170 * var(--atv-u))); }\nhtml.atv-js .atv-who__avatar {\n    width: max(96px, calc(170 * var(--atv-u))); height: max(96px, calc(170 * var(--atv-u))); border-radius: 50%; overflow: hidden; position: relative;\n    display: flex; align-items: center; justify-content: center; font-size: max(40px, calc(72 * var(--atv-u))); font-weight: 600; color: rgba(28, 28, 30, .8);\n    transition: transform .25s var(--atv-ease), box-shadow .25s var(--atv-ease);\n}\nhtml.atv-js .atv-who__avatar img { width: 100%; height: 100%; object-fit: cover; display: block; }\nhtml.atv-js .atv-who__avatar--add { background: rgba(255, 255, 255, .25); color: #2b4a66; -webkit-backdrop-filter: blur(20px); backdrop-filter: blur(20px); box-shadow: inset 0 0 0 1px rgba(255, 255, 255, .2); }\nhtml.atv-js .atv-who__avatar--add svg { width: 32%; height: 32%; }\nhtml.atv-js .atv-who__name { margin-top: max(14px, calc(26 * var(--atv-u))); font-size: max(14px, calc(22 * var(--atv-u))); color: rgba(255, 255, 255, .7); text-shadow: 0 1px 8px rgba(0, 0, 0, .25); transition: color .2s; text-align: center; }\nhtml.atv-js .atv-who__user:hover .atv-who__avatar, html.atv-js .atv-who__user:focus-visible .atv-who__avatar { transform: scale(1.15); box-shadow: 0 24px 60px rgba(0, 0, 0, .35); }\nhtml.atv-js .atv-who__user:hover .atv-who__name, html.atv-js .atv-who__user:focus-visible .atv-who__name { color: rgba(255, 255, 255, .92); }\nhtml.atv-js .atv-who__user:focus-visible { outline: none; }\n/* natives Formular als Glas-Karte, nur wenn geöffnet */\nhtml.atv-js #loginPage.atv-who-on > .padded-left.padded-right.padded-bottom-page { display: none; }\nhtml.atv-js #loginPage.atv-who-on.atv-who--form > .padded-left.padded-right.padded-bottom-page {\n    display: block; position: fixed; z-index: 2; left: 50%; top: 50%; transform: translate(-50%, -50%);\n    width: min(440px, 92vw); max-height: 90vh; overflow: auto; margin: 0; padding: 28px 24px 20px !important;\n    border-radius: 36px; background: rgba(30, 30, 34, .55); color: #fff;\n    -webkit-backdrop-filter: blur(40px) saturate(1.8); backdrop-filter: blur(40px) saturate(1.8);\n    box-shadow: inset 0 0 0 1px rgba(255, 255, 255, .14), 0 24px 80px rgba(0, 0, 0, .45); animation: atv-pop .2s var(--atv-ease);\n}\nhtml.atv-js #loginPage.atv-who-on.atv-who--form .manualLoginForm { width: 100%; }\nhtml.atv-js #loginPage.atv-who-on.atv-who--form .padded-bottom-page { padding-bottom: 12px !important; }\nhtml.atv-js #loginPage.atv-who-on.atv-who--form .readOnlyContent { width: 100%; }\nhtml.atv-js #loginPage.atv-who-on .visualLoginForm, html.atv-js #loginPage.atv-who-on .btnManual { display: none !important; }\nhtml.atv-js #loginPage.atv-who-on .atv-who__scrim { display: none; }\nhtml.atv-js #loginPage.atv-who-on.atv-who--form .atv-who__scrim { display: block; position: fixed; inset: 0; z-index: 1; background: rgba(0, 0, 0, .35); }\nhtml.atv-js .atv-who__close { position: absolute; top: 16px; left: 16px; z-index: 3; width: 32px; height: 32px; border-radius: 50%; display: flex; align-items: center; justify-content: center; color: #fff; }\nhtml.atv-js .atv-who__close:hover { background: rgba(255, 255, 255, .12); }\nhtml.atv-js .atv-who__close svg { width: 22px; height: 22px; }\nhtml.atv-js #loginPage.atv-who-on .backdropImage { filter: blur(80px) brightness(1.2); }\n\n/* ==========================================================================\n   5. Glas-Tableiste (nur Mobil < 800 px)\n   ========================================================================== */\nhtml.atv-js .atv-tabbar { display: none; }\n@media (max-width: 799px) {\n    html.atv-js.atv-tabbar-on .atv-tabbar {\n        position: fixed; z-index: 1000; left: 50%; bottom: calc(8px + env(safe-area-inset-bottom, 0px)); transform: translateX(-50%);\n        display: flex; align-items: center; gap: 8px;\n    }\n    html.atv-js .atv-tabbar__caps, html.atv-js .atv-tabbar__search {\n        display: flex; align-items: center; height: 50px; border-radius: 999px;\n        background: rgba(30, 30, 34, .6); -webkit-backdrop-filter: blur(30px) saturate(1.8); backdrop-filter: blur(30px) saturate(1.8);\n        box-shadow: inset 0 0 0 1px rgba(255, 255, 255, .12), inset 0 1px 0 rgba(255, 255, 255, .18), 0 8px 30px rgba(0, 0, 0, .35);\n    }\n    html.atv-js .atv-tabbar__caps { padding: 0 6px; }\n    html.atv-js .atv-tabbar__search { width: 50px; justify-content: center; }\n    html.atv-js .atv-tabbar a { display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 2px; width: 68px; height: 50px; border-radius: 999px; color: rgba(255, 255, 255, .7); font-size: 10px; font-weight: 500; }\n    html.atv-js .atv-tabbar__search a { width: 50px; }\n    html.atv-js .atv-tabbar a svg { width: 24px; height: 24px; }\n    html.atv-js .atv-tabbar a.is-active { color: #0a84ff; }\n    html.atv-js.atv-tabbar-on .mainAnimatedPage { padding-bottom: 80px !important; }\n}\n\n/* ==========================================================================\n   6. Mobil (< 800 px): Hochkant-Hero, Detailseite zentriert, Regal 230 px\n   ========================================================================== */\n@media (max-width: 799px) {\n    html.atv-js .atv-hero { height: min(176vw, 88vh); min-height: 520px; --atv-hero-overlap: -10vh; }\n    html.atv-js .atv-hero__art { object-position: 50% 15%; }\n    html.atv-js .atv-hero__shade { background: linear-gradient(to top, #000000 0%, #000000 20%, rgba(0, 0, 0, .9) 34%, rgba(0, 0, 0, .55) 46%, rgba(0, 0, 0, 0) 64%); }\n    html.atv-js .atv-hero__lockup { left: 16px; right: 16px; width: auto; max-width: none; bottom: calc(10vh + 72px); align-items: center; text-align: center; gap: 10px; }\n    html.atv-js .atv-hero__logo { max-width: min(300px, 77vw); max-height: 90px; object-position: center bottom; }\n    html.atv-js .atv-hero__title { font-size: 30px; text-align: center; }\n    html.atv-js .atv-hero__meta { font-size: 17px; gap: 8px; }\n    html.atv-js .atv-hero__meta .atv-badge { font-size: 11px; height: 18px; }\n    html.atv-js .atv-hero__desc { font-size: 17px; line-height: 22px; max-width: 100%; padding: 0 36px; }\n    html.atv-js .atv-hero__desc.atv-clamp--2 { -webkit-line-clamp: 3; }\n    html.atv-js .atv-hero__actions { margin-top: 8px; gap: 12px; }\n    html.atv-js .atv-hero .atv-pill { height: 44px; min-width: min(264px, 72vw); font-size: 17px; padding: 0 20px; }\n    html.atv-js .atv-hero .atv-circle { width: 44px; height: 44px; }\n    html.atv-js .atv-hero .atv-circle--info, html.atv-js .atv-hero .atv-circle--next { display: none; }\n    html.atv-js .atv-hero__dots { bottom: calc(10vh + 32px); height: auto; padding: 0; gap: 12px; background: none; -webkit-backdrop-filter: none; backdrop-filter: none; }\n    html.atv-js button.atv-hero__dot { width: 8px; height: 8px; }\n    html.atv-js button.atv-hero__dot.is-active { width: 12px; height: 12px; }\n    html.atv-js .atv-hero__nav { display: none; }\n\n    html.atv-js .atv-detail { height: auto; min-height: 0; }\n    html.atv-js .atv-detail__art { position: relative; display: block; height: 70vh; min-height: 420px; object-position: 50% 30%; }\n    html.atv-js .atv-detail__shade { background: linear-gradient(to top, #000000 0%, rgba(0, 0, 0, 1) 28%, rgba(0, 0, 0, .6) 44%, rgba(0, 0, 0, 0) 60%); }\n    html.atv-js .atv-detail__lockup { position: relative; left: 0; bottom: 0; width: auto; max-width: none; margin: calc(-30vh) 16px 0; align-items: center; text-align: center; gap: 10px; }\n    html.atv-js .atv-detail__logo { max-width: min(300px, 77vw); max-height: 90px; object-position: center bottom; }\n    html.atv-js .atv-detail__title { font-size: 30px; text-align: center; }\n    html.atv-js .atv-detail__meta { font-size: 15px; justify-content: center; }\n    html.atv-js .atv-detail__desc { font-size: 15px; line-height: 20px; text-align: left; align-self: stretch; max-width: none; }\n    html.atv-js .atv-detail__facts { font-size: 13px; justify-content: center; }\n    html.atv-js .atv-detail__logo, html.atv-js .atv-detail__title { order: 0; }\n    html.atv-js .atv-detail__sub { order: 1; }\n    html.atv-js .atv-detail__meta { order: 2; }\n    html.atv-js .atv-detail__actions { order: 3; justify-content: center; flex-wrap: wrap; width: auto; max-width: 100%; gap: 12px; margin: 4px 0 2px; }\n    html.atv-js .atv-detail__note { order: 4; }\n    html.atv-js .atv-detail__desc { order: 5; }\n    html.atv-js .atv-detail__facts { order: 6; }\n    html.atv-js .atv-detail .atv-pill { height: 44px; min-width: 181px; font-size: 17px; padding: 0 22px; }\n    html.atv-js .atv-detail .atv-circle { width: 44px; height: 44px; }\n    html.atv-js .atv-detail__note { margin: -2px 0 2px; font-size: 13px; }\n    html.atv-js .atv-detail__credits { position: relative; right: auto; bottom: auto; margin: 14px 16px 0; max-width: none; text-align: left; font-size: 13px; line-height: 18px; }\n    html.atv-js .atv-detail__lockup + .atv-detail__credits { margin-bottom: 8px; }\n\n    html.atv-js .atv-shelf__head { padding: 0 25px; margin-bottom: 14px; }\n    html.atv-js .atv-shelf__scroller { gap: 10px; padding: 6px 25px 12px; scroll-padding-left: 25px; }\n    html.atv-js .atv-ep { width: 230px; }\n    html.atv-js .atv-menu { left: 25px; }\n    html.atv-js .atv-about { margin: 16px 25px 32px; }\n    html.atv-js .atv-who__title { top: 24%; }\n    html.atv-js .atv-who__row { gap: 18px; }\n}\n@media (prefers-reduced-motion: reduce) {\n    html.atv-js .atv-hero__slide, html.atv-js .atv-hero__lockup, html.atv-js .atv-pill, html.atv-js .atv-circle, html.atv-js .atv-ep,\n    html.atv-js .atv-who__avatar, html.atv-js .atv-clamp, html.atv-js .atv-menu { transition: none !important; animation: none !important; }\n}\n\n/* Sprach-Badges (Ton / Untertitel) neben den Qualitäts-Badges */\nhtml.atv-js .atv-badge--lang { white-space: nowrap; letter-spacing: .3px; cursor: default; }\nhtml.atv-js .atv-badge__pre { opacity: .6; margin-right: .45em; font-size: .85em; letter-spacing: .6px; }\n\n/* ===== build/css/dashboard.css (eingebettet) ===== */\n/* ==========================================================================\n   12 Dashboard/Admin (Jellyfin 12.x lädt das Branding-CSS NICHT auf\n   /dashboard-Seiten; diese Datei wird deshalb zusätzlich über appletv-js.css\n   ausgeliefert – siehe build/css/dashboard.css). Nur Farben/Radien, kein Layout.\n   ========================================================================== */\n.MuiDrawer-paper, .MuiDrawer-docked .MuiPaper-root { background: #1b1c22 !important; border-right: 1px solid rgba(255, 255, 255, .06) !important; color: #fff !important; }\n.MuiDrawer-paper .MuiListItemButton-root { border-radius: 10px; margin: 1px 10px; padding: 8px 14px; }\n.MuiDrawer-paper .MuiListItemButton-root:hover { background: rgba(255, 255, 255, .08) !important; }\n.MuiDrawer-paper .MuiListItemButton-root.Mui-selected, .MuiDrawer-paper .MuiListItemButton-root.active, .MuiDrawer-paper a.MuiListItemButton-root[aria-current="page"] { background: rgba(255, 255, 255, .14) !important; }\n.MuiDrawer-paper .MuiListItemIcon-root { color: rgba(255, 255, 255, .8) !important; }\n.MuiDrawer-paper .MuiListSubheader-root { background: transparent !important; color: rgba(255, 255, 255, .55) !important; }\n.MuiDrawer-paper .MuiListItem-root img, .MuiDrawer-paper .MuiAvatar-root img { border-radius: 8px; }\nhtml:has(.dashboard-appBar), html:has(.dashboard-appBar) body { background: #000000 !important; }\n.dashboard-appBar { background: transparent !important; }\n.dashboard-appBar .MuiIconButton-root { color: #fff; }\n#dashboardPage, .type-interior, .content-primary { background: transparent !important; color: #fff; }\n.MuiPaper-root:not(.MuiAppBar-root):not(.MuiDrawer-paper):not(.MuiMenu-paper):not(.MuiPopover-paper):not(.MuiDialog-paper) { background: #1b1c22 !important; background-image: none !important; color: #fff !important; border-radius: 18px !important; box-shadow: none !important; }\n.MuiDialog-paper { background: #1f2026 !important; border-radius: 24px !important; color: #fff !important; }\n.MuiCard-root, .paperList, .MuiDataGrid-root, .MuiTableContainer-root { background: #1b1c22 !important; border-radius: 18px !important; border: 1px solid rgba(255, 255, 255, .06) !important; overflow: hidden; color: #fff; }\n.MuiTableCell-root { border-color: rgba(255, 255, 255, .08) !important; color: #fff !important; }\n.MuiTableHead-root .MuiTableCell-root { color: rgba(255, 255, 255, .64) !important; }\n.MuiDataGrid-root { --DataGrid-rowBorderColor: rgba(255, 255, 255, .08); }\n.MuiDataGrid-columnHeaders, .MuiDataGrid-columnHeader, .MuiDataGrid-footerContainer { background: #1b1c22 !important; border-color: rgba(255, 255, 255, .08) !important; }\n.MuiTableRow-root:hover, .MuiDataGrid-row:hover { background: rgba(255, 255, 255, .05) !important; }\n/* Jellyfin-Cyan (#00a4dc) → Weiß/Grau; Rot bleibt */\n.MuiButton-contained, .MuiButton-containedPrimary, .MuiLoadingButton-root.MuiButton-contained { border-radius: 999px !important; background: rgba(255, 255, 255, .92) !important; color: #000 !important; box-shadow: none !important; text-transform: none !important; font-weight: 600; }\n.MuiButton-contained.MuiButton-containedError, .MuiButton-contained.MuiButton-colorError { background: #ff453a !important; color: #fff !important; }\n.MuiButton-outlined, .MuiButton-outlinedPrimary { border-radius: 999px !important; color: #fff !important; border-color: rgba(255, 255, 255, .3) !important; text-transform: none !important; }\n.MuiButton-text, .MuiButton-textPrimary { border-radius: 999px !important; color: #fff !important; text-transform: none !important; }\n.MuiFab-primary { background: rgba(255, 255, 255, .92) !important; color: #000 !important; }\n.MuiChip-filled, .MuiChip-filledPrimary, .MuiChip-colorPrimary { background: rgba(255, 255, 255, .14) !important; color: #fff !important; border-radius: 999px; }\n.MuiLink-root, a.MuiLink-root { color: #fff !important; text-decoration-color: rgba(255, 255, 255, .4); }\n.MuiCheckbox-colorPrimary, .MuiCheckbox-root svg, .MuiRadio-root svg { color: rgba(255, 255, 255, .75) !important; }\n.MuiCheckbox-root.Mui-checked svg, .MuiRadio-root.Mui-checked svg { color: #0a84ff !important; }\n.MuiSwitch-switchBase.Mui-checked + .MuiSwitch-track { background-color: #34c759 !important; opacity: 1 !important; }\n.MuiSwitch-track { background-color: rgba(255, 255, 255, .25) !important; }\n.MuiAvatar-root.MuiAvatar-colorDefault, .MuiListItemAvatar-root .MuiAvatar-root { background: rgba(255, 255, 255, .12) !important; color: #fff !important; }\n.MuiIconButton-colorPrimary, .MuiSvgIcon-colorPrimary, .MuiTypography-colorPrimary, .MuiTab-root.Mui-selected { color: #fff !important; }\n.MuiTabs-indicator { background-color: #fff !important; }\n.MuiLinearProgress-root { background-color: rgba(255, 255, 255, .15) !important; border-radius: 999px; }\n.MuiLinearProgress-bar { background-color: rgba(255, 255, 255, .75) !important; border-radius: 999px; }\n.MuiLinearProgress-colorSuccess .MuiLinearProgress-bar, .MuiLinearProgress-barColorSuccess { background-color: #34c759 !important; }\n.MuiLinearProgress-colorError .MuiLinearProgress-bar, .MuiLinearProgress-barColorError { background-color: #ff453a !important; }\n.MuiFormControl-root .MuiInputBase-root, .MuiOutlinedInput-root, .MuiFilledInput-root { background: rgba(255, 255, 255, .1) !important; border-radius: 12px !important; color: #fff !important; }\n.MuiOutlinedInput-notchedOutline { border-color: rgba(255, 255, 255, .14) !important; }\n.MuiOutlinedInput-root.Mui-focused .MuiOutlinedInput-notchedOutline { border-color: rgba(255, 255, 255, .6) !important; }\n.MuiInputLabel-root, .MuiFormLabel-root.Mui-focused { color: rgba(255, 255, 255, .64) !important; }\n.MuiAlert-standardInfo { background: rgba(10, 132, 255, .15) !important; color: #fff !important; border-radius: 14px; }\n.MuiPaginationItem-root.Mui-selected { background: #fff !important; color: #000 !important; }\n.MuiMenu-paper, .MuiPopover-paper { background: rgba(30, 30, 34, .95) !important; background-image: none !important; color: #fff !important; border-radius: 14px !important; }\n.MuiMenuItem-root:hover { background: rgba(255, 255, 255, .1) !important; }\n.MuiTooltip-tooltip { background: rgba(40, 40, 40, .95) !important; border-radius: 8px; }\n/* Gerätekarten / Aktivitätsicons mit Inline-Cyan */\n[style*="rgb(0, 164, 220)"], [style*="#00a4dc"], [style*="#00A4DC"] { background-color: #2c2d33 !important; }\n.activityItem .MuiAvatar-root, .MuiListItemAvatar-root .MuiAvatar-colorDefault { background: rgba(255, 255, 255, .12) !important; }\n/* Alte Jellyfin-Seiten im Dashboard (Benutzer, Bibliotheken, geplante Aufgaben, Plugins) */\n.dashboardDocument, .type-interior { background: transparent !important; }\n.card.squareCard.scalableCard .cardBox.visualCardBox, .visualCardBox { background: #1b1c22 !important; border-radius: 14px !important; box-shadow: none !important; overflow: hidden; }\n.visualCardBox .cardImage.defaultCardBackground, .defaultCardBackground { background: #30323a !important; }\n.visualCardBox .cardFooter { background: transparent !important; }\n.visualCardBox .cardText { color: #fff; }\n.visualCardBox .cardText-secondary { color: rgba(255, 255, 255, .64); }\n.fab.submit, .emby-button.fab { background: rgba(255, 255, 255, .92) !important; color: #000 !important; border-radius: 50% !important; }\n.raised.button-submit, .button-submit, .emby-button.raised.button-submit { background: #0a84ff !important; color: #fff !important; border-radius: 999px !important; }\n.raised, .emby-button.raised { border-radius: 999px !important; background: rgba(255, 255, 255, .14) !important; color: #fff !important; text-transform: none !important; }\n.raised.button-delete, .button-delete { background: rgba(255, 69, 58, .85) !important; color: #fff !important; }\n.emby-input, .emby-select, .emby-textarea { background: rgba(255, 255, 255, .1) !important; border-radius: 12px !important; border: 0 !important; color: #fff !important; }\n.emby-checkbox:checked + span + .checkboxOutline { background: #0a84ff !important; border-radius: 7px; }\n.checkboxOutline { background: rgba(255, 255, 255, .16) !important; border-radius: 7px !important; border: 0 !important; }\n.inputLabelFocused, .selectLabelFocused { color: #fff !important; }\n.listItem:hover { background: rgba(255, 255, 255, .06) !important; }\n.taskProgressOuter, .taskProgressInner { border-radius: 999px; }\n.taskProgressInner { background: rgba(255, 255, 255, .75) !important; }\n.paperList .listItem-border { border-color: rgba(255, 255, 255, .06) !important; }\n.sectionTitle a, .sectionTitleTextButton { color: #fff !important; }\n.MuiAppBar-root.MuiPaper-root { background: transparent !important; box-shadow: none !important; }\n';

    var VERSION = '1.4.0';
    var CFG = {
        updateCheck: true,     // Admins sehen einen Hinweis, wenn es im Repository eine neuere Version gibt (1 Abruf pro Tag bei GitHub)
        repo: 'derpfiff/cinema-glass',
        heroCount: 8,          // max. Folien (HIG: 3–8)
        heroPool: 30,          // aus den N neuesten Titeln
        heroIdleMs: 8000,      // Key-Art-Takt ohne Trailer (Spec §12: 8 s)
        heroUserPauseMs: 12000,// nach Nutzereingabe
        mobileMax: 800,
        cacheMs: 10 * 60 * 1000
    };
    // Schalter aus dem Plugin (Dashboard → Plugins → Cinema Glass). Ohne Plugin (Handinstallation) ist alles an.
    var PLUG = window.__cinemaGlass || null, FEAT = (PLUG && PLUG.features) || {};
    function on(k) { return FEAT[k] !== false; }
    if (PLUG) CFG.updateCheck = FEAT.updateCheck === true;                 // mit Plugin kommen Updates über den Plugin-Katalog

    /* ------------------------------------------------------------------ i18n */
    var STR = {
        de: { audioShort: 'TON', audioLong: 'Ton', subShort: 'UT', subLong: 'Untertitel', play: 'Abspielen', resume: 'Fortsetzen', more: 'MEHR', season: 'Staffel', episode: 'FOLGE', ep: 'F', s: 'S',
              with: 'Mit', director: 'Regie', creator: 'Erfinder', movie: 'Film', series: 'Serie', episodeType: 'Folge',
              left: 'noch %1', who: 'Wer schaut?', other: 'Anderer Benutzer', home: 'Start', movies: 'Filme', shows: 'Serien',
              search: 'Suche', h: 'Std.', m: 'Min.', watchlist: 'Merkliste', watched: 'Gesehen', info: 'Info', next: 'Weiter',
              prev: 'Zurück', close: 'Schließen', trailer: 'Trailer', slide: 'Titel %1 von %2', carousel: 'Karussell',
              nextUp: 'Als Nächstes', episodes: '%1 Folgen', information: 'Informationen', languages: 'Sprachen',
              accessibility: 'Barrierefreiheit', released: 'Erschienen', runtime: 'Laufzeit', genres: 'Genres', studio: 'Studio',
              audio: 'Audio', subtitles: 'Untertitel', rating: 'Freigabe', none: 'Keine', menu: 'Mehr',
              born: 'Geboren', died: 'Gestorben', suggestions: 'Vorschläge', recentlyAdded: 'Zuletzt hinzugefügt',
              noResults: 'Keine Ergebnisse für \u201e%1\u201c', collection: 'Sammlung', playlist: 'Wiedergabeliste', items: '%1 Titel', less: 'WENIGER' },
        en: { audioShort: 'AUDIO', audioLong: 'Audio', subShort: 'SUB', subLong: 'Subtitles', play: 'Play', resume: 'Resume', more: 'MORE', season: 'Season', episode: 'EPISODE', ep: 'E', s: 'S',
              with: 'Starring', director: 'Director', creator: 'Creator', movie: 'Movie', series: 'TV Show', episodeType: 'Episode',
              left: '%1 left', who: 'Who’s watching?', other: 'Other user', home: 'Home', movies: 'Movies', shows: 'TV Shows',
              search: 'Search', h: 'h', m: 'm', watchlist: 'Watchlist', watched: 'Watched', info: 'Info', next: 'Next',
              prev: 'Previous', close: 'Close', trailer: 'Trailer', slide: 'Item %1 of %2', carousel: 'Carousel',
              nextUp: 'Up next', episodes: '%1 episodes', information: 'Information', languages: 'Languages',
              accessibility: 'Accessibility', released: 'Released', runtime: 'Run time', genres: 'Genres', studio: 'Studio',
              audio: 'Audio', subtitles: 'Subtitles', rating: 'Rated', none: 'None', menu: 'More',
              born: 'Born', died: 'Died', suggestions: 'Suggestions', recentlyAdded: 'Recently added',
              noResults: 'No results for \u201c%1\u201d', collection: 'Collection', playlist: 'Playlist', items: '%1 items', less: 'LESS' }
    };
    function lang() {
        var l = (document.documentElement.lang || navigator.language || 'de').toLowerCase();
        return l.indexOf('en') === 0 ? 'en' : 'de';
    }
    function t(key) {
        var s = (STR[lang()] || STR.de)[key] || STR.de[key] || key;
        for (var i = 1; i < arguments.length; i++) s = s.replace('%' + i, arguments[i]);
        return s;
    }
    function fmtRuntime(ticks, style) {
        if (!ticks) return '';
        var m = Math.round(ticks / 600000000), h = Math.floor(m / 60), r = m % 60;
        if (lang() === 'en') return h ? h + 'h' + (r ? ' ' + r + 'm' : '') : m + 'm';
        if (style === 'short') return h ? h + ' Std.' + (r ? ' ' + r + ' Min.' : '') : m + ' Min.';
        return h ? h + ' ' + t('h') + (r ? ' ' + r + ' ' + t('m') : '') : m + ' ' + t('m');
    }
    function typeLabel(item) {
        if (item.Type === 'BoxSet') return t('collection');
        if (item.Type === 'Playlist') return t('playlist');
        if (item.Type === 'Series') return t('series');
        if (item.Type === 'Episode') return t('episodeType');
        if (item.Type === 'Season') return t('season');
        return t('movie');
    }

    /* --------------------------------------------------------------- Hilfen */
    function el(tag, cls, text) {
        var e = document.createElement(tag);
        if (cls) e.className = cls;
        if (text != null) e.textContent = text;
        return e;
    }
    function svg(path, box) {
        var ns = 'http://www.w3.org/2000/svg';
        var s = document.createElementNS(ns, 'svg');
        s.setAttribute('viewBox', box || '0 0 24 24');
        s.setAttribute('aria-hidden', 'true');
        var p = document.createElementNS(ns, 'path');
        p.setAttribute('d', path);
        s.appendChild(p);
        return s;
    }
    var ICON = {
        play: 'M8 5v14l11-7z',
        plus: 'M11 5h2v6h6v2h-6v6h-2v-6H5v-2h6z',
        check: 'M9.5 16.2 5.3 12l-1.4 1.4 5.6 5.6 12-12-1.4-1.4z',
        info: 'M11 17h2v-6h-2v6zm1-15C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8zM11 9h2V7h-2v2z',
        next: 'M10 6 8.59 7.41 13.17 12l-4.58 4.59L10 18l6-6z',
        prev: 'M15.41 7.41 14 6l-6 6 6 6 1.41-1.41L10.83 12z',
        more: 'M6 10a2 2 0 1 0 0 4 2 2 0 0 0 0-4zm6 0a2 2 0 1 0 0 4 2 2 0 0 0 0-4zm6 0a2 2 0 1 0 0 4 2 2 0 0 0 0-4z',
        close: 'M18.3 5.71 12 12l6.3 6.29-1.41 1.42L10.59 13.4 4.3 19.71 2.88 18.3 9.17 12 2.88 5.71 4.3 4.29l6.29 6.3 6.3-6.3z',
        chevron: 'M7.41 8.59 12 13.17l4.59-4.58L18 10l-6 6-6-6z',
        updown: 'M12 5.83 15.17 9l1.41-1.41L12 3 7.41 7.59 8.83 9zm0 12.34L8.83 15l-1.41 1.41L12 21l4.59-4.59L15.17 15z',
        film: 'M4 4h16a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2zm0 2v2h2V6zm0 4v2h2v-2zm0 4v2h2v-2zm16-8h-2v2h2zm0 4h-2v2h2zm0 4h-2v2h2zM8 6v12h8V6z',
        tv: 'M21 3H3a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h5v2h8v-2h5a2 2 0 0 0 2-2V5a2 2 0 0 0-2-2zm0 14H3V5h18z',
        home: 'M12 3 2 12h3v8h5v-6h4v6h5v-8h3z',
        search: 'M15.5 14h-.79l-.28-.27A6.47 6.47 0 0 0 16 9.5 6.5 6.5 0 1 0 9.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14z',
        star: 'M12 17.27 18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21z',
        tomato: 'M12 4c-1 0-1.5.6-2.2.6S8.6 4 7.6 4.5c.6.4 1 .9 1.1 1.5C5.6 6.5 3 9 3 12.5 3 16.6 7 20 12 20s9-3.4 9-7.5c0-3.5-2.6-6-5.7-6.5.1-.6.5-1.1 1.1-1.5-1-.5-1.5.1-2.2.1S13 4 12 4z'
    };
    function api() { return window.ApiClient; }
    function apiReady() {
        var a = api();
        return !!(a && typeof a.getCurrentUserId === 'function' && a.getCurrentUserId() && a.serverInfo && a.serverInfo());
    }
    function userId() { return api().getCurrentUserId(); }
    // Standardgrößen/-qualität, URL direkt gebaut: getScaledImageUrl würde maxWidth nochmal mit devicePixelRatio malnehmen
    // (1920 → 3840 auf Retina). Wenige, immer gleiche Varianten → der Jellyfin-Bildcache greift (2. Aufruf ~1 ms statt 250-450 ms).
    function imgUrl(id, opts) {
        opts = opts || {};
        var name = 'Items/' + id + '/Images/' + opts.type + (opts.index != null ? '/' + opts.index : '');
        var q = {};
        ['maxWidth', 'maxHeight', 'width', 'height', 'tag'].forEach(function (k) { if (opts[k] != null) q[k] = opts[k]; });
        q.quality = opts.quality || 80;
        return api().getUrl(name, q);
    }
    // Blur-up: erst ein winziges Bild (≈8 KB, ~50 ms), dann weich auf das scharfe wechseln
    function lowUrl(u) { return u ? u.replace(/maxWidth=\d+/, 'maxWidth=480').replace(/quality=\d+/, 'quality=45') : u; }
    // Kein Blur-up (verursachte Flackern: Bild, kurz weg, wieder da). Direkt das scharfe Bild; Vorladen übernimmt prefetchItem beim Hover.
    function progressiveSrc(img, url) {
        if (!img || !url || img.getAttribute('src') === url) return;
        img.decoding = 'sync'; img.src = url;
    }
    // Breite fürs Backdrop: auch bei hochkantem Zuschnitt (Mobil, Höhe 70 vh) scharf → mindestens Höhe × 16/9
    function screenW() { var w = Math.max(window.innerWidth, window.innerHeight * 16 / 9) * (window.devicePixelRatio || 1); return w <= 1300 ? 1280 : 1920; }
    function backdropUrl(item) {
        if (item.BackdropImageTags && item.BackdropImageTags.length) return imgUrl(item.Id, { type: 'Backdrop', index: 0, maxWidth: screenW(), tag: item.BackdropImageTags[0] });
        if (item.ParentBackdropItemId && item.ParentBackdropImageTags && item.ParentBackdropImageTags.length) return imgUrl(item.ParentBackdropItemId, { type: 'Backdrop', index: 0, maxWidth: screenW(), tag: item.ParentBackdropImageTags[0] });
        return null;
    }
    function primaryUrl(item, maxWidth) {
        var tag = item.ImageTags && item.ImageTags.Primary;
        if (tag) return imgUrl(item.Id, { type: 'Primary', maxWidth: maxWidth || 1200, tag: tag });
        if (item.SeriesPrimaryImageTag && item.SeriesId) return imgUrl(item.SeriesId, { type: 'Primary', maxWidth: maxWidth || 1200, tag: item.SeriesPrimaryImageTag });
        return null;
    }
    function logoUrl(item) {
        var tag = item.ImageTags && item.ImageTags.Logo;
        if (tag) return imgUrl(item.Id, { type: 'Logo', maxHeight: 300, tag: tag });
        if (item.ParentLogoItemId && item.ParentLogoImageTag) return imgUrl(item.ParentLogoItemId, { type: 'Logo', maxHeight: 300, tag: item.ParentLogoImageTag });
        return null;
    }
    function isMobile() { return window.innerWidth < CFG.mobileMax; }
    function navigate(url) {
        if (window.Emby && window.Emby.Page && typeof window.Emby.Page.show === 'function') window.Emby.Page.show(url);
        else { snapPage(); location.hash = '#' + url; }
    }
    function showDetails(id) { navigate('/details?id=' + id + '&serverId=' + api().serverId()); }
    function hashParam(name) {
        var m = location.hash.match(new RegExp('[?&]' + name + '=([^&]+)'));
        return m ? decodeURIComponent(m[1]) : null;
    }
    function ratingBadge(item) {
        if (!item.OfficialRating) return null;
        return el('span', 'atv-badge', String(item.OfficialRating).replace(/^FSK-?/i, 'FSK '));
    }
    // Wiedergabe: direkt über Jellyfins eigene Klick-Logik (itemShortcuts). Ein unsichtbarer Knopf mit
    // data-action="play" in einem nativen emby-itemscontainer löst playbackManager.play() aus – ohne Umweg
    // über die Detailseite der Folge (kein Seitenwechsel, „Zurück“ landet wieder dort, wo man war).
    // Fallback (kein nativer Container auf der Seite): Detailseite öffnen und dort den Play-Knopf auslösen.
    function playHost() {
        var p = document.querySelector('.mainAnimatedPage:not(.hide), .page:not(.hide)');
        return (p && p.querySelector('[is="emby-itemscontainer"]')) || document.querySelector('[is="emby-itemscontainer"]');
    }
    var playManager = null;   // Jellyfins Wiedergabe-Verwaltung, sobald das Wiedergabe-Plugin angelegt ist (siehe BridgePlugin)
    var lastPlay = null;   // zuletzt gestarteter Titel (für den Player-Titel während eines Vorspanns)
    function playDirect(item, ticks) {
        if (item && item.Id) lastPlay = { id: item.Id, at: Date.now() };
        var host = playHost(), sid = '';
        try { sid = api().serverId(); } catch (e) { return false; }
        if (!sid || !item || !item.Id) return false;
        if (playManager) {
            try { playManager.play({ ids: [item.Id], serverId: sid, startPositionTicks: ticks > 0 ? Math.floor(ticks) : 0 }); return true; } catch (e) { /* weiter unten */ }
        }
        if (!host) return false;
        var b = el('button', 'itemAction atv-playproxy'); b.type = 'button'; b.hidden = true; b.style.display = 'none';
        b.setAttribute('data-action', ticks > 0 ? 'resume' : 'play');
        b.setAttribute('data-id', item.Id); b.setAttribute('data-serverid', sid);
        b.setAttribute('data-type', item.Type || 'Video'); b.setAttribute('data-mediatype', item.MediaType || 'Video');
        b.setAttribute('data-isfolder', 'false'); b.setAttribute('data-positionticks', String(ticks > 0 ? Math.floor(ticks) : 0));
        try { host.appendChild(b); b.click(); } catch (e) { b.remove(); return false; }
        b.remove();
        return true;
    }
    function playViaDetails(item) {
        var sid = ''; try { sid = '&serverId=' + api().serverId(); } catch (e) { /* ohne serverId */ }
        location.hash = '#/details?id=' + item.Id + sid + '&atvPlay=1';
    }
    function playItem(item, ticks) {
        if (!item) return Promise.resolve();
        if (item.Type === 'Movie' || item.Type === 'Episode' || item.Type === 'Video' || item.Type === 'MusicVideo') {
            if (!playDirect(item, ticks || 0)) playViaDetails(item);
            return Promise.resolve();
        }
        if (item.Type === 'Series') {
            // Serie: nächste Folge („Als Nächstes“) direkt starten
            // „Als Nächstes“ kennt nur angefangene Serien. Für noch nie gestartete (oder komplett gesehene) Serien die Folgen
            // selbst durchsehen: angefangene Folge → erste ungesehene (ohne Specials) → erste Folge. Erst wenn auch das
            // nichts liefert, der Umweg über die Serienseite.
            return api().getNextUpEpisodes({ SeriesId: item.Id, UserId: userId(), Limit: 1, Fields: 'MediaSources' }).then(function (nu) {
                var e = nu && nu.Items && nu.Items[0];
                if (e) return e;
                return api().getEpisodes(item.Id, { userId: userId(), Fields: 'UserData' }).then(function (res) {
                    var eps = (res && res.Items) || [], pick = null;
                    eps.forEach(function (x) { if (!pick && x.UserData && x.UserData.PlaybackPositionTicks > 0) pick = x; });
                    if (!pick) eps.forEach(function (x) { if (!pick && x.ParentIndexNumber !== 0 && !(x.UserData && x.UserData.Played)) pick = x; });
                    return pick || eps.filter(function (x) { return x.ParentIndexNumber !== 0; })[0] || eps[0] || null;
                });
            }).then(function (e) {
                if (!e || !playDirect(e, (e.UserData && e.UserData.PlaybackPositionTicks) || 0)) playViaDetails(item);
            }).catch(function () { playViaDetails(item); });
        }
        playViaDetails(item);
        return Promise.resolve();
    }
    function toggleFavorite(item, btn) {
        var a = api(), fav = !(item.UserData && item.UserData.IsFavorite);
        var p = fav ? a.markFavorite(userId(), item.Id) : a.unmarkFavorite(userId(), item.Id);
        return p.then(function (ud) {
            item.UserData = ud || item.UserData || {};
            if (!ud) item.UserData.IsFavorite = fav;
            setFavButton(btn, item.UserData.IsFavorite);
        }).catch(function () {});
    }
    function setFavButton(btn, on) {
        btn.classList.toggle('is-on', !!on);
        btn.replaceChildren(svg(on ? ICON.check : ICON.plus));
        btn.setAttribute('aria-pressed', on ? 'true' : 'false');
    }
    function cssInject() {
        if (document.getElementById('atv-js-css')) return;
        if (!CSS_INLINE || CSS_INLINE.indexOf('__ATV_CSS__') >= 0) return;   // CSS kommt dann aus dem Branding
        var s = el('style'); s.id = 'atv-js-css'; s.textContent = CSS_INLINE; document.head.appendChild(s);
    }
    // MEHR-Pille nur zeigen, wenn der Text wirklich abgeschnitten ist
    function trimMore(root) {
        root.querySelectorAll('.atv-clamp').forEach(function (d) {
            var b = d.__atvMore || d.querySelector('.atv-more'); if (!b) return;
            // Mobil: Pille UNTER den Textblock (nie mitten im Text, kein angeschnittener Text im Innenabstand)
            if (isMobile() && b.parentNode === d && d.parentNode) { d.parentNode.insertBefore(b, d.nextSibling); b.classList.add('atv-more--below'); b.style.order = getComputedStyle(d).order; }
            var extra = d.scrollHeight - d.clientHeight;
            if (extra <= 2) { b.remove(); return; }
            var cs = getComputedStyle(d), lh = parseFloat(cs.lineHeight) || (parseFloat(cs.fontSize) * 1.35);
            if (extra <= lh * 1.3) {   // höchstens eine fehlende Zeile: komplett zeigen, keine MEHR-Pille für ein paar Wörter
                d.style.display = 'block'; d.style.webkitLineClamp = 'unset'; d.style.overflow = 'visible'; d.style.maxHeight = 'none';
                b.remove();
            }
        });
    }
    // Beim Darüberfahren/Antippen eines Titels Hintergrund + Logo schon laden (Klick kommt meist >300 ms später)
    var __pf = {}, __pfT = null;
    function prefetchItem(id) {
        if (!id || __pf[id] || !apiReady()) return; __pf[id] = 1;
        try {
            api().getItem(userId(), id).then(function (it) {
                if (!it || ['Movie', 'Series', 'Season', 'Episode', 'BoxSet'].indexOf(it.Type) < 0) return;
                var u = backdropUrl(it), l = logoUrl(it);
                if (u) { var a = new Image(); a.decoding = 'async'; a.src = u; }
                if (l) { var b = new Image(); b.src = l; }
            }).catch(function () { delete __pf[id]; });
        } catch (e) { delete __pf[id]; }
    }
    function pfTarget(e) { var c = e.target && e.target.closest ? e.target.closest('.card[data-id]') : null; return c ? c.getAttribute('data-id') : null; }
    if (!window.__atvPf) {
        window.__atvPf = true;
        document.addEventListener('mouseover', function (e) { var id = pfTarget(e); if (!id) return; clearTimeout(__pfT); __pfT = setTimeout(function () { prefetchItem(id); }, 90); }, true);
        document.addEventListener('touchstart', function (e) { var id = pfTarget(e); if (id) prefetchItem(id); }, { passive: true, capture: true });
        document.addEventListener('focusin', function (e) { var id = pfTarget(e); if (id) prefetchItem(id); }, true);
    }
    // Startseite: Klick auf das Bild einer 16:9-Karte („Weiterschauen“, „Als Nächstes“) startet sofort (wie „Als Nächstes“ bei Apple TV),
    // statt erst die Detailseite zu öffnen. Obere Titelzeile öffnet die Serie bzw. den Film, „···“ bleibt das Menü.
    if (!window.__atvCardPlay) {
        window.__atvCardPlay = true;
        document.addEventListener('click', function (e) {
            if (!on('homeCardPlay') || e.defaultPrevented || e.button || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
            var tg = e.target; if (!tg || !tg.closest) return;
            if (tg.closest('.atv-playproxy, .cardOverlayButton')) return;
            // Klare Trennung: obere Zeile (Serien-/Filmtitel mit „›“) = Seite öffnen; Bild und ▶-Zeile (Folge) = abspielen
            var epLine = tg.closest('.cardText-secondary');
            if (!epLine && tg.closest('button, .cardText, .textActionButton')) return;
            if (!epLine && !tg.closest('.cardImageContainer, .cardOverlayContainer')) return;
            var card = tg.closest('.card.overflowBackdropCard[data-id], .card.backdropCard[data-id]');
            if (!card || !card.closest('.homePage')) return;
            var type = card.getAttribute('data-type');
            if (type !== 'Episode' && type !== 'Movie' && type !== 'Video') return;
            if (!apiReady()) return;
            var ticks = parseInt(card.getAttribute('data-positionticks') || '0', 10) || 0;
            if (!playDirect({ Id: card.getAttribute('data-id'), Type: type, MediaType: card.getAttribute('data-mediatype') || 'Video' }, ticks)) return;
            e.preventDefault(); e.stopPropagation(); e.stopImmediatePropagation();
        }, true);
    }
    // Abstand eines Elements zur Seitenoberkante (auch negativ, wenn Jellyfin die Seite nach oben zieht)
    function offsetTopOf(node) {
        var r = node.getBoundingClientRect();
        return Math.round(r.top + window.scrollY);
    }

    /* ------------------------------------- MEHR: Beschreibung klappt an Ort und Stelle auf */
    // d = .atv-clamp-Element (Text), opts.onToggle(open) z. B. zum Pausieren des Karussells
    function addMore(d, opts) {
        opts = opts || {};
        var b = el('button', 'atv-more', t('more')); b.type = 'button'; b.setAttribute('aria-expanded', 'false');
        d.appendChild(b); d.__atvMore = b;
        function setLabel(open) { b.textContent = open ? t('less') : t('more'); b.setAttribute('aria-expanded', open ? 'true' : 'false'); }
        function open() {
            if (d.classList.contains('is-open')) return;
            var from = d.getBoundingClientRect().height;
            d.classList.add('is-open');                       // Clamp weg, volle Höhe messbar
            d.style.maxHeight = 'none';
            var to = isMobile() ? d.scrollHeight : Math.min(d.scrollHeight, Math.round(window.innerHeight * 0.6));   // mobil: kein innerer Scroll, die Seite scrollt
            d.style.maxHeight = from + 'px';
            d.getBoundingClientRect();                        // Reflow, dann animieren
            d.style.maxHeight = to + 'px';
            d.__atvOpenH = to;
            setLabel(true);
            if (opts.onToggle) opts.onToggle(true);
        }
        function close() {
            if (!d.classList.contains('is-open')) return;
            var from = d.getBoundingClientRect().height;
            d.style.maxHeight = from + 'px';
            d.classList.remove('is-open'); d.classList.add('is-closing');   // Clamp erst nach der Animation
            var to = d.__atvClosedH || from;
            d.getBoundingClientRect();
            d.style.maxHeight = to + 'px';
            setLabel(false);
            setTimeout(function () { d.classList.remove('is-closing'); d.style.maxHeight = ''; }, 320);
            if (opts.onToggle) opts.onToggle(false);
        }
        b.addEventListener('click', function (e) {
            e.stopPropagation(); e.preventDefault();
            if (isMobile() && opts.mobileNavigate) { opts.mobileNavigate(); return; }   // Hero mobil: kein Aufklappen im Bild (Text wuchs nach oben aus dem Bild), stattdessen Detailseite
            if (!d.__atvClosedH) d.__atvClosedH = d.getBoundingClientRect().height;
            if (d.classList.contains('is-open')) close(); else open();
        });
        d.__atvClose = close;
        return b;
    }
    function collapseAll() { document.querySelectorAll('.atv-clamp.is-open').forEach(function (d) { if (d.__atvClose) d.__atvClose(); }); }
    document.addEventListener('keydown', function (e) { if (e.key === 'Escape') { collapseAll(); closeMenus(); } });
    function closeMenus() { document.querySelectorAll('.atv-menu').forEach(function (m) { m.remove(); }); document.querySelectorAll('.atv-season[aria-expanded="true"]').forEach(function (b) { b.setAttribute('aria-expanded', 'false'); }); }
    document.addEventListener('click', function (e) { if (!e.target.closest('.atv-menu, .atv-season, .atv-circle--dl, .atv-ep__more')) closeMenus(); }, true);

    /* =====================================================================
       1. Hero-Karussell
       ===================================================================== */
    var hero = { root: null, page: null, items: [], index: 0, timer: null, io: null, cache: { user: null, time: 0, items: null } };

    function heroLoad() {
        var a = api(), uid = userId(), c = hero.cache;
        if (c.items && c.user === uid && Date.now() - c.time < CFG.cacheMs) return Promise.resolve(c.items);
        var fields = 'Overview,Genres,ProductionYear,OfficialRating,CommunityRating,RunTimeTicks,Taglines,ParentId,SeriesId';
        var resume = a.getItems(uid, { Limit: 6, Fields: fields, Recursive: true, IncludeItemTypes: 'Movie,Episode', Filters: 'IsResumable', SortBy: 'DatePlayed', SortOrder: 'Descending', EnableImageTypes: 'Backdrop,Logo,Primary', MediaTypes: 'Video' }).catch(function () { return { Items: [] }; });
        var latest = a.getItems(uid, { IncludeItemTypes: 'Movie,Series', Recursive: true, SortBy: 'DateCreated', SortOrder: 'Descending', Limit: CFG.heroPool, ImageTypes: 'Backdrop', EnableImageTypes: 'Backdrop,Logo,Primary', Fields: fields });
        return Promise.all([resume, latest]).then(function (res) {
            var out = [], seen = {};
            function add(item, resumeOf) {
                var key = item.SeriesId || item.Id;
                if (seen[key]) return;
                seen[key] = 1;
                out.push({ item: item, resume: resumeOf || null });
            }
            // Weiterschauen: Folge → Serie als Folie, Film → Film
            var resumeItems = (res[0] && res[0].Items) || [];
            var seriesFetch = resumeItems.filter(function (i) { return i.Type === 'Episode' && i.SeriesId; }).map(function (i) { return i.SeriesId; });
            var p = seriesFetch.length ? a.getItems(uid, { Ids: seriesFetch.join(','), Fields: fields, EnableImageTypes: 'Backdrop,Logo,Primary' }).catch(function () { return { Items: [] }; }) : Promise.resolve({ Items: [] });
            return p.then(function (sres) {
                var byId = {};
                ((sres && sres.Items) || []).forEach(function (s) { byId[s.Id] = s; });
                resumeItems.forEach(function (r) {
                    if (r.Type === 'Episode') { var s = byId[r.SeriesId]; if (s && backdropUrl(s)) add(s, r); }
                    else if (backdropUrl(r)) add(r, r);
                });
                ((res[1] && res[1].Items) || []).forEach(function (i) { if (out.length < CFG.heroCount && backdropUrl(i)) add(i); });
                out = out.slice(0, CFG.heroCount);
                hero.cache = { user: uid, time: Date.now(), items: out };
                return out;
            });
        });
    }

    function heroSlide(entry, i, n) {
        var item = entry.item, r = entry.resume;
        var s = el('div', 'atv-hero__slide' + (i === 0 ? ' is-active' : ''));
        s.setAttribute('role', 'group'); s.setAttribute('aria-roledescription', 'slide');
        s.setAttribute('aria-label', t('slide', i + 1, n) + ': ' + item.Name);
        var art = el('img', 'atv-hero__art'); art.alt = ''; art.decoding = 'async';
        art.setAttribute('data-land', backdropUrl(item) || '');
        art.setAttribute('data-port', backdropUrl(item) || primaryUrl(item, 1200) || '');   // auch am Handy das Hintergrundbild: auf Plakaten steht der Titel meist schon drauf und stünde mit dem Logo doppelt da
        art.addEventListener('click', function () { showDetails(item.Id); });
        s.appendChild(art);
        s.appendChild(el('div', 'atv-hero__shade'));
        var lock = el('div', 'atv-hero__lockup');
        var logo = logoUrl(item);
        if (logo) { var li = el('img', 'atv-hero__logo'); li.alt = item.Name; li.src = logo; lock.appendChild(li); }
        else lock.appendChild(el('h2', 'atv-hero__title', item.Name));
        var meta = el('div', 'atv-hero__meta');
        var coin = el('span', 'atv-coin'); coin.appendChild(svg(item.Type === 'Series' ? ICON.tv : ICON.film)); meta.appendChild(coin);
        var parts = [typeLabel(item)].concat((item.Genres || []).slice(0, 2));
        meta.appendChild(el('span', null, parts.join(' · ')));
        var rb = ratingBadge(item); if (rb) meta.appendChild(rb);
        lock.appendChild(meta);
        var ov = item.Overview || (item.Taglines && item.Taglines[0]) || '';
        if (ov) {
            var d = el('p', 'atv-hero__desc atv-clamp atv-clamp--2', ov);
            // MEHR klappt den Text an Ort und Stelle auf; solange offen, pausiert das Weiterschalten
            addMore(d, { mobileNavigate: function () { showDetails(item.Id); }, onToggle: function (open) { hero.paused = open; if (!open) heroArm(CFG.heroIdleMs); } });
            lock.appendChild(d);
        }
        var act = el('div', 'atv-hero__actions');
        var play = el('button', 'atv-pill'); play.type = 'button';
        play.appendChild(svg(ICON.play)); play.appendChild(el('span', null, r ? t('resume') : t('play')));
        if (r && r.UserData && r.UserData.PlayedPercentage) { var bar = el('span', 'atv-pill__bar'), fill = el('i'); fill.style.width = Math.min(100, r.UserData.PlayedPercentage) + '%'; bar.appendChild(fill); play.appendChild(bar); }
        play.addEventListener('click', function () { playItem(r || item, r && r.UserData ? r.UserData.PlaybackPositionTicks : 0); });
        act.appendChild(play);
        var fav = el('button', 'atv-circle atv-circle--fav'); fav.type = 'button'; fav.title = t('watchlist'); fav.setAttribute('aria-label', t('watchlist'));
        setFavButton(fav, item.UserData && item.UserData.IsFavorite);
        fav.addEventListener('click', function () { toggleFavorite(item, fav); heroUserPause(); });
        act.appendChild(fav);
        var info = el('button', 'atv-circle atv-circle--info'); info.type = 'button'; info.title = t('info'); info.setAttribute('aria-label', t('info'));
        info.appendChild(svg(ICON.info)); info.addEventListener('click', function () { showDetails(item.Id); });
        act.appendChild(info);
        if (n > 1) {
            var nx = el('button', 'atv-circle atv-circle--next'); nx.type = 'button'; nx.title = t('next'); nx.setAttribute('aria-label', t('next'));
            nx.appendChild(svg(ICON.next)); nx.addEventListener('click', function () { heroGo(hero.index + 1); heroUserPause(); });
            act.appendChild(nx);
        }
        lock.appendChild(act);
        s.appendChild(lock);
        return s;
    }

    function heroApplyArt(slide) {
        var img = slide.querySelector('.atv-hero__art');
        if (!img) return;
        var want = isMobile() ? img.getAttribute('data-port') : img.getAttribute('data-land');
        if (want && img.getAttribute('src') !== want) progressiveSrc(img, want);
    }

    function heroBuild(entries) {
        var root = el('section', 'atv-hero');
        root.setAttribute('aria-roledescription', t('carousel'));
        root.tabIndex = -1;
        var n = entries.length;
        entries.forEach(function (e, i) { root.appendChild(heroSlide(e, i, n)); });
        if (n > 1) {
            var dots = el('div', 'atv-hero__dots'); dots.setAttribute('role', 'tablist');
            entries.forEach(function (e, i) {
                var d = el('button', 'atv-hero__dot' + (i === 0 ? ' is-active' : '')); d.type = 'button'; d.setAttribute('role', 'tab');
                d.setAttribute('aria-label', t('slide', i + 1, n));
                d.addEventListener('click', function () { heroGo(i); heroUserPause(); });
                dots.appendChild(d);
            });
            root.appendChild(dots);
            var prev = el('button', 'atv-hero__nav atv-hero__nav--prev'), next = el('button', 'atv-hero__nav atv-hero__nav--next');
            prev.type = next.type = 'button'; prev.setAttribute('aria-label', t('prev')); next.setAttribute('aria-label', t('next'));
            prev.appendChild(svg(ICON.prev)); next.appendChild(svg(ICON.next));
            prev.addEventListener('click', function () { heroGo(hero.index - 1); heroUserPause(); });
            next.addEventListener('click', function () { heroGo(hero.index + 1); heroUserPause(); });
            root.appendChild(prev); root.appendChild(next);
        }
        var x0 = null, y0 = null;
        root.addEventListener('touchstart', function (e) { x0 = e.touches[0].clientX; y0 = e.touches[0].clientY; }, { passive: true });
        root.addEventListener('touchend', function (e) {
            if (x0 == null) return;
            var dx = e.changedTouches[0].clientX - x0, dy = e.changedTouches[0].clientY - y0; x0 = null;
            if (Math.abs(dx) > 40 && Math.abs(dx) > Math.abs(dy)) { heroGo(hero.index + (dx < 0 ? 1 : -1)); heroUserPause(); }
        }, { passive: true });
        root.addEventListener('keydown', function (e) {
            if (e.key === 'ArrowRight') { heroGo(hero.index + 1); heroUserPause(); e.preventDefault(); }
            else if (e.key === 'ArrowLeft') { heroGo(hero.index - 1); heroUserPause(); e.preventDefault(); }
        });
        return root;
    }

    function heroGo(i) {
        var n = hero.items.length; if (!n || !hero.root) return;
        hero.index = (i + n) % n;
        var slides = hero.root.querySelectorAll('.atv-hero__slide'), dots = hero.root.querySelectorAll('.atv-hero__dot');
        collapseAll(); hero.paused = false;
        for (var k = 0; k < n; k++) {
            var on = k === hero.index;
            slides[k].classList.toggle('is-active', on);
            slides[k].setAttribute('aria-hidden', on ? 'false' : 'true');
            if (dots[k]) { dots[k].classList.toggle('is-active', on); dots[k].setAttribute('aria-selected', on ? 'true' : 'false'); }
            if (on || k === (hero.index + 1) % n) heroApplyArt(slides[k]);   // aktuelle + nächste Folie laden
        }
    }
    function heroStop() { if (hero.timer) { clearTimeout(hero.timer); hero.timer = null; } }
    function heroVisible() {
        return !!(hero.root && hero.root.isConnected && !document.hidden && !hero.paused && hero.page && !hero.page.classList.contains('hide') && hero.inView !== false);
    }
    function heroArm(ms) {
        heroStop();
        hero.timer = setTimeout(function () {
            hero.timer = null;
            if (!hero.root || !hero.root.isConnected) return;
            if (heroVisible()) heroGo(hero.index + 1);
            heroArm(CFG.heroIdleMs);
        }, ms);
    }
    function heroUserPause() { heroArm(CFG.heroUserPauseMs); }
    document.addEventListener('visibilitychange', function () { if (hero.root && hero.root.isConnected) heroArm(document.hidden ? CFG.heroIdleMs : CFG.heroIdleMs); });

    window.__atvDebug = function () { return { paused: !!hero.paused, timer: !!hero.timer, inView: hero.inView, index: hero.index }; };
    var heroMounting = false;
    function mountHero(page) {
        var target = page.querySelector('#homeTab .homeSectionsContainer');
        if (!target || !apiReady()) return;
        if (!on('hero')) { page.classList.add('atv-native'); return; }
        if (!page.__atvFallbackT) page.__atvFallbackT = setTimeout(function () { if (!page.querySelector('.atv-hero')) page.classList.add('atv-native'); }, 2500);
        // schon montiert: Timer nur neu stellen, wenn keiner läuft (jeder Router-Lauf würde ihn sonst zurücksetzen)
        if (hero.root && hero.root.isConnected && hero.root.parentNode === target.parentNode) { if (!hero.timer) heroArm(CFG.heroIdleMs); return; }
        if (heroMounting) return;
        heroMounting = true;
        heroLoad().then(function (entries) {
            var target2 = page.querySelector('#homeTab .homeSectionsContainer');
            if (!target2 || !entries.length) return;
            if (hero.root && hero.root.parentNode) hero.root.remove();
            var keep = hero.items.length === entries.length && hero.items[hero.index] && entries[hero.index] && hero.items[hero.index].item.Id === entries[hero.index].item.Id ? hero.index : 0;
            hero.items = entries; hero.index = 0; hero.page = page;
            hero.root = heroBuild(entries);
            var tab = target2.parentNode;
            tab.insertBefore(hero.root, target2);
            page.classList.add('atv-hero-on'); page.classList.remove('atv-native');
            hero.root.style.setProperty('--atv-pad', offsetTopOf(hero.root) + 'px');   // bis unter die Kopfzeile ziehen
            heroGo(keep);
            trimMore(hero.root);
            if (isMobile()) mountHomeChips(page);                          // Kategorie-Chips sofort mit einsetzen (kamen sonst später und schoben die Regale nach unten)
            if (hero.io) hero.io.disconnect();
            hero.inView = true;
            if ('IntersectionObserver' in window) {
                hero.io = new IntersectionObserver(function (es) { hero.inView = es[0].isIntersecting; }, { threshold: 0.5 });
                hero.io.observe(hero.root);
            }
            heroArm(CFG.heroIdleMs);
        }).catch(function (e) { page.classList.add('atv-native'); console.warn('[atv] Hero konnte nicht geladen werden', e); })
          .then(function () { heroMounting = false; });
    }
    window.addEventListener('resize', function () {
        if (!hero.root || !hero.root.isConnected) return;
        var slides = hero.root.querySelectorAll('.atv-hero__slide');
        slides.forEach(function (s) { if (s.classList.contains('is-active')) heroApplyArt(s); });
    });

    /* =====================================================================
       2. Detailseite
       ===================================================================== */
    function mediaBadges(item) {
        var out = [], streams = [];
        (item.MediaSources || []).forEach(function (ms) { (ms.MediaStreams || []).forEach(function (s) { streams.push(s); }); });
        if (!streams.length && item.MediaStreams) streams = item.MediaStreams;
        var video = streams.filter(function (s) { return s.Type === 'Video'; })[0];
        var audio = streams.filter(function (s) { return s.Type === 'Audio'; });
        var subs = streams.filter(function (s) { return s.Type === 'Subtitle'; });
        if (video) {
            if ((video.Width || 0) >= 3800) out.push('4K'); else if ((video.Height || 0) >= 1080 || (video.Width || 0) >= 1900) out.push('HD');
            var vr = (video.VideoRangeType || video.VideoRange || '').toUpperCase();
            if (vr.indexOf('DOVI') >= 0) out.push('Dolby Vision'); else if (vr.indexOf('HDR') >= 0 || vr.indexOf('HLG') >= 0) out.push('HDR');
        }
        var atmos = audio.some(function (s) { return /atmos/i.test((s.Profile || '') + ' ' + (s.DisplayTitle || '') + ' ' + (s.Title || '')); });
        if (atmos) out.push('Dolby Atmos');
        else { var ch = Math.max.apply(null, audio.map(function (s) { return s.Channels || 0; }).concat([0])); if (ch >= 8) out.push('7.1'); else if (ch >= 6) out.push('5.1'); }
        if (subs.some(function (s) { return /sdh|hearing/i.test((s.DisplayTitle || '') + ' ' + (s.Title || '')); })) out.push('SDH');
        return out;
    }
    // Sprach-Badges: Tonspuren und Untertitel als Kürzel (DE EN JA …), ausgeschrieben im Tooltip
    var LANG2 = { deu: 'de', ger: 'de', eng: 'en', fra: 'fr', fre: 'fr', spa: 'es', ita: 'it', jpn: 'ja', kor: 'ko', zho: 'zh', chi: 'zh', rus: 'ru', por: 'pt',
        nld: 'nl', dut: 'nl', swe: 'sv', dan: 'da', nor: 'no', nob: 'no', fin: 'fi', pol: 'pl', tur: 'tr', ces: 'cs', cze: 'cs', hun: 'hu', ell: 'el', gre: 'el',
        heb: 'he', ara: 'ar', hin: 'hi', tha: 'th', ukr: 'uk', ron: 'ro', rum: 'ro', bul: 'bg', hrv: 'hr', srp: 'sr', slk: 'sk', slo: 'sk', lat: 'la' };
    function langInfo(code) {
        if (!code) return null; var c = String(code).toLowerCase(); if (c === 'und' || c === 'mis' || c === 'zxx') return null;
        var two = c.length === 2 ? c : (LANG2[c] || (c.length === 3 ? null : c.slice(0, 2))); if (!two) return { k: c.toUpperCase(), name: c.toUpperCase() };
        var name = two.toUpperCase();
        try { var dn = new Intl.DisplayNames([lang() === 'en' ? 'en' : 'de'], { type: 'language' }).of(two); if (dn) name = dn; } catch (e) { /* alter Browser */ }
        return { k: two.toUpperCase(), name: name };
    }
    function langBadges(item) {
        var streams = [];
        (item.MediaSources || []).slice(0, 1).forEach(function (ms) { (ms.MediaStreams || []).forEach(function (s) { streams.push(s); }); });
        if (!streams.length && item.MediaStreams) streams = item.MediaStreams;
        function uniq(list) {
            var seen = {}, out = [];
            list.forEach(function (s) { var l = langInfo(s.Language); if (l && !seen[l.k]) { seen[l.k] = 1; out.push(l); } });
            return out;
        }
        var res = [];
        var au = uniq(streams.filter(function (s) { return s.Type === 'Audio'; }));
        var su = uniq(streams.filter(function (s) { return s.Type === 'Subtitle' && !s.IsForced; }));
        function mk(prefix, list, title) {
            if (!list.length) return;
            var shown = list.slice(0, 4).map(function (l) { return l.k; }).join(' '), more = list.length > 4 ? ' +' + (list.length - 4) : '';
            res.push({ prefix: prefix, text: shown + more, title: title + ': ' + list.map(function (l) { return l.name; }).join(', ') });
        }
        mk(t('audioShort'), au, t('audioLong')); mk(t('subShort'), su, t('subLong'));
        return res;
    }
    function personLink(p) {
        var a = el('a', null, p.Name); a.href = '#/details?id=' + p.Id + '&serverId=' + api().serverId();
        return a;
    }
    function peopleLine(label, people, max) {
        if (!people.length) return null;
        var p = el('p'); p.appendChild(el('span', 'atv-label', label));
        people.slice(0, max).forEach(function (x, i) { if (i) p.appendChild(document.createTextNode(', ')); p.appendChild(personLink(x)); });
        return p;
    }
    function nativeBtn(page, cls) { var b = page.querySelector('.mainDetailButtons .' + cls); return b && !b.classList.contains('hide') ? b : null; }

    // Trailer-Knopf: der in Jellyfin hinterlegte YouTube-Trailer läuft in einem eigenen Vollbild-Fenster (eingebetteter
    // YouTube-Player). Jellyfins eigene Trailer-Wiedergabe endet in den Apps oft mit „Wiedergabefehler“; sie bleibt nur
    // Rückfall für Titel ohne YouTube-Adresse.
    function ytId(item) {
        var list = item.RemoteTrailers || [];
        for (var i = 0; i < list.length; i++) { var m = /(?:[?&]v=|youtu\.be\/|\/embed\/)([\w-]{11})/.exec(list[i].Url || ''); if (m) return m[1]; }
        return null;
    }
    function openTrailer(item, page) {
        var id = ytId(item);
        if (!id) { var nb0 = nativeBtn(page, 'btnPlayTrailer'); if (nb0) nb0.click(); return; }
        var old = document.querySelector('.atv-trailer'); if (old) old.remove();
        var wrap = el('div', 'atv-trailer'); wrap.setAttribute('role', 'dialog'); wrap.setAttribute('aria-modal', 'true'); wrap.setAttribute('aria-label', t('trailer'));
        var x = el('button', 'atv-trailer__close'); x.type = 'button'; x.setAttribute('aria-label', t('close')); x.appendChild(svg(ICON.close)); wrap.appendChild(x);
        var stage = el('div', 'atv-trailer__stage'); wrap.appendChild(stage);
        var f = document.createElement('iframe');
        f.setAttribute('allow', 'autoplay; encrypted-media; fullscreen; picture-in-picture'); f.setAttribute('allowfullscreen', ''); f.setAttribute('referrerpolicy', 'strict-origin-when-cross-origin');
        f.src = 'https://www.youtube-nocookie.com/embed/' + id + '?autoplay=1&playsinline=1&rel=0&modestbranding=1&origin=' + encodeURIComponent(location.origin);
        stage.appendChild(f);
        var prevOv = document.documentElement.style.overflow, closed = false;
        function onKey(e) { if (e.key === 'Escape') { e.stopPropagation(); close(); } }
        function close() { if (closed) return; closed = true; document.removeEventListener('keydown', onKey, true); document.documentElement.style.overflow = prevOv; wrap.remove(); }
        x.addEventListener('click', close);
        wrap.addEventListener('click', function (e) { if (e.target === wrap) close(); });
        document.addEventListener('keydown', onKey, true);
        document.documentElement.style.overflow = 'hidden';
        document.body.appendChild(wrap);
    }
    function buildDetail(page, item) {
        var root = el('section', 'atv-detail');
        var art = el('img', 'atv-detail__art'); art.alt = ''; art.decoding = 'async';
        var land = backdropUrl(item), port = primaryUrl(item, 1200);
        // Folge: Serien-Backdrop (scharf); das Standbild der Folge ist meist nur 400–800 px breit und wäre vollflächig unscharf
        if (item.Type === 'Episode' && !land && item.ImageTags && item.ImageTags.Primary) land = imgUrl(item.Id, { type: 'Primary', maxWidth: screenW(), tag: item.ImageTags.Primary });
        // Mobil: Backdrop hochkant beschnitten (Spec §9), nicht das Poster (Text im Bild)
        art.setAttribute('data-land', land || port || ''); art.setAttribute('data-port', land || port || '');
        if (land || port) {
            // weiches Einblenden, sobald das erste (kleine) Bild da ist; danach Blur-up auf die scharfe Fassung; Fallback nach 2,5 s
            art.style.opacity = '0'; art.style.transition = 'opacity .35s ease';
            var show = function () { art.style.opacity = ''; };
            art.addEventListener('load', show); art.addEventListener('error', show); setTimeout(show, 2500);
            progressiveSrc(art, land || port);
        } else root.classList.add('atv-detail--noart');
        root.appendChild(art);
        // Sammlung/Wiedergabeliste ohne eigenes Bild: Backdrop des ersten Titels
        if (!land && !port && (item.Type === 'BoxSet' || item.Type === 'Playlist')) {
            api().getItems(userId(), { ParentId: item.Id, Limit: 1, ImageTypes: 'Backdrop', EnableImageTypes: 'Backdrop', Fields: 'ParentId' }).then(function (res) {
                var c = res && res.Items && res.Items[0], u = c && backdropUrl(c);
                if (u && root.isConnected) { art.src = u; art.setAttribute('data-land', u); art.setAttribute('data-port', u); root.classList.remove('atv-detail--noart'); }
            }).catch(function () {});
        }
        root.appendChild(el('div', 'atv-detail__shade'));

        var lock = el('div', 'atv-detail__lockup');
        var logo = logoUrl(item);
        if (item.Type === 'Episode') {
            if (logo) { var li0 = el('img', 'atv-detail__logo'); li0.alt = item.SeriesName || ''; li0.src = logo; lock.appendChild(li0); }
            else lock.appendChild(el('h1', 'atv-detail__title', item.SeriesName || item.Name));
            lock.appendChild(el('p', 'atv-detail__sub', (item.ParentIndexNumber != null ? t('s') + item.ParentIndexNumber + ', ' : '') + (item.IndexNumber != null ? t('ep') + item.IndexNumber + ' · ' : '') + item.Name));
        } else if (item.Type === 'Season') {
            if (logo) { var li1 = el('img', 'atv-detail__logo'); li1.alt = item.SeriesName || ''; li1.src = logo; lock.appendChild(li1); }
            else lock.appendChild(el('h1', 'atv-detail__title', item.SeriesName || item.Name));
            lock.appendChild(el('p', 'atv-detail__sub', item.Name));
        } else {
            if (logo) { var li = el('img', 'atv-detail__logo'); li.alt = item.Name; li.src = logo; lock.appendChild(li); }
            else lock.appendChild(el('h1', 'atv-detail__title', item.Name));
        }
        var meta = el('div', 'atv-detail__meta');
        var coin = el('span', 'atv-coin'); coin.appendChild(svg(item.Type === 'Movie' ? ICON.film : ICON.tv)); meta.appendChild(coin);
        var parts = [typeLabel(item)].concat((item.Genres || []).slice(0, 2));
        meta.appendChild(el('span', null, parts.join(' · ')));
        var rb = ratingBadge(item); if (rb) meta.appendChild(rb);
        lock.appendChild(meta);
        var ov = item.Overview || (item.Taglines && item.Taglines[0]) || '';
        if (ov) {
            var d = el('p', 'atv-detail__desc atv-clamp atv-clamp--2', ov);
            addMore(d);
            lock.appendChild(d);
        }
        var facts = el('div', 'atv-detail__facts'), fparts = [];
        if (item.ProductionYear) fparts.push(String(item.ProductionYear));
        if (item.RunTimeTicks) fparts.push(fmtRuntime(item.RunTimeTicks, 'short'));
        if (item.Type === 'Series' && item.ChildCount) fparts.push(t('episodes', item.RecursiveItemCount || item.ChildCount));
        if ((item.Type === 'BoxSet' || item.Type === 'Playlist') && item.ChildCount) fparts.push(t('items', item.ChildCount));
        if (fparts.length) facts.appendChild(el('span', null, fparts.join(' · ')));
        if (item.CommunityRating) { var st = el('span', 'atv-badge atv-badge--star'); st.appendChild(svg(ICON.star)); st.appendChild(document.createTextNode(item.CommunityRating.toFixed(1).replace('.', lang() === 'en' ? '.' : ','))); facts.appendChild(st); }
        if (item.CriticRating) { var cr = el('span', 'atv-badge atv-badge--rt'); cr.appendChild(svg(ICON.tomato)); cr.appendChild(document.createTextNode(Math.round(item.CriticRating) + ' %')); facts.appendChild(cr); }
        mediaBadges(item).forEach(function (b) { facts.appendChild(el('span', 'atv-badge', b)); });
        langBadges(item).forEach(function (b) {
            var sp = el('span', 'atv-badge atv-badge--lang'); sp.title = b.title;
            sp.appendChild(el('span', 'atv-badge__pre', b.prefix)); sp.appendChild(document.createTextNode(b.text)); facts.appendChild(sp);
        });
        if (facts.childNodes.length) lock.appendChild(facts);

        var act = el('div', 'atv-detail__actions');
        var ud = item.UserData || {};
        var canPlay = !!nativeBtn(page, 'btnPlay') || item.Type === 'Movie' || item.Type === 'Episode' || item.Type === 'Series' || item.Type === 'Season';
        if ((item.Type === 'BoxSet' || item.Type === 'Playlist') && !nativeBtn(page, 'btnPlay')) canPlay = false;
        if (canPlay) {
            var play = el('button', 'atv-pill'); play.type = 'button';
            var resume = ud.PlaybackPositionTicks > 0;
            play.appendChild(svg(ICON.play)); play.appendChild(el('span', null, resume ? t('resume') : t('play')));
            if (resume && ud.PlayedPercentage) { var bar = el('span', 'atv-pill__bar'), fill = el('i'); fill.style.width = Math.min(100, ud.PlayedPercentage) + '%'; bar.appendChild(fill); play.appendChild(bar); }
            play.addEventListener('click', function () {
                // andere Fassung gewählt: diese Datei direkt starten (Jellyfins eigener Knopf verliert die Auswahl, sobald ein Vorspann davor läuft)
                if (page.__atvVersion && page.__atvVersion !== item.Id && playDirect({ Id: page.__atvVersion, Type: item.Type, MediaType: item.MediaType }, 0)) return;
                var nb = nativeBtn(page, 'btnPlay'); lastPlay = { id: item.Id, at: Date.now() };
                if (nb) nb.click(); else playItem(item, (item.UserData && item.UserData.PlaybackPositionTicks) || 0);
            });
            act.appendChild(play);
        }
        if (nativeBtn(page, 'btnPlayTrailer') || (item.RemoteTrailers && item.RemoteTrailers.length) || item.LocalTrailerCount) {
            var tr = el('button', 'atv-pill atv-pill--glass'); tr.type = 'button'; tr.appendChild(el('span', null, t('trailer')));
            tr.addEventListener('click', function () { var nb = nativeBtn(page, 'btnPlayTrailer'); if (!on('trailerModal') && nb) nb.click(); else openTrailer(item, page); });
            act.appendChild(tr);
        }
        var fav = el('button', 'atv-circle'); fav.type = 'button'; fav.title = t('watchlist'); fav.setAttribute('aria-label', t('watchlist'));
        setFavButton(fav, ud.IsFavorite);
        fav.addEventListener('click', function () { toggleFavorite(item, fav); });
        act.appendChild(fav);
        if (item.Type !== 'Series' || true) {
            var seen = el('button', 'atv-circle' + (ud.Played ? ' is-on' : '')); seen.type = 'button'; seen.title = t('watched'); seen.setAttribute('aria-label', t('watched'));
            seen.appendChild(svg(ICON.check)); seen.setAttribute('aria-pressed', ud.Played ? 'true' : 'false');
            seen.addEventListener('click', function () {
                var a = api(), p = ud.Played ? a.markUnplayed(userId(), item.Id) : a.markPlayed(userId(), item.Id);
                p.then(function (nud) { ud = item.UserData = nud || ud; if (!nud) ud.Played = !ud.Played; seen.classList.toggle('is-on', !!ud.Played); seen.setAttribute('aria-pressed', ud.Played ? 'true' : 'false'); }).catch(function () {});
            });
            act.appendChild(seen);
        }
        if (on('downloads') && ['Movie', 'Episode', 'Video', 'Series', 'Season'].indexOf(item.Type) >= 0) {
            var dl = el('button', 'atv-circle atv-circle--dl'); dl.type = 'button'; dl.hidden = true; dl.title = lang() === 'en' ? 'Download' : 'Herunterladen'; dl.setAttribute('aria-label', dl.title); dl.appendChild(svg(ICON_DL));
            canDownload().then(function (ok) { dl.hidden = !ok || !dlPlatform(); });
            dl.addEventListener('click', function (e) {
                e.stopPropagation();
                if (document.querySelector('.atv-menu--float')) { closeMenus(); return; }
                if (item.Type !== 'Series' && item.Type !== 'Season') { var inf0 = srcInfo(item); inf0.title = lang() === 'en' ? 'Download' : 'Herunterladen'; askQuality(dl, inf0, function (q) { downloadIds([page.__atvVersion || item.Id], q); }); return; }
                // Serie: aktuelle Staffel oder alles
                if (document.querySelector('.atv-menu--float')) { closeMenus(); return; }
                var seriesId = item.Type === 'Season' ? item.SeriesId : item.Id;
                var shelf = page.querySelector('.atv-episodes'), cur = shelf && shelf.__atvSeason;
                var menu = el('div', 'atv-menu atv-menu--float atv-menu--actions'); menu.setAttribute('role', 'menu');
                var add = function (label, query, confirmMany) {
                    var it = el('button', 'atv-menu__item'); it.type = 'button'; it.setAttribute('role', 'menuitem'); it.appendChild(svg(ICON_DL)); it.appendChild(el('span', null, label));
                    it.addEventListener('click', function (ev) {
                        ev.stopPropagation(); closeMenus();
                        api().getEpisodes(seriesId, query).then(function (res) {
                            var eps = ((res && res.Items) || []).filter(function (x) { return x.LocationType !== 'Virtual'; }), ids = eps.map(function (x) { return x.Id; });
                            var ticks = eps.reduce(function (a, x) { return a + (x.RunTimeTicks || 0); }, 0);
                            askQuality(dl, { ticks: ticks, title: ids.length + (lang() === 'en' ? ' episodes' : ' Folgen') }, function (q) {
                                if (confirmMany && ids.length > 30 && !window.confirm((lang() === 'en' ? 'Download ' + ids.length + ' episodes?' : ids.length + ' Folgen herunterladen?'))) return;
                                downloadIds(ids, q);
                            });
                        }).catch(function () {});
                    });
                    menu.appendChild(it);
                };
                if (cur) add((cur.Name || t('season')) + (lang() === 'en' ? ' (this season)' : ' laden'), { seasonId: cur.Id, userId: userId() }, false);
                add(lang() === 'en' ? 'Whole series' : 'Ganze Serie laden', { userId: userId() }, true);
                document.body.appendChild(menu);
                var r = dl.getBoundingClientRect(), mw = menu.offsetWidth, mh = menu.offsetHeight;
                var left = Math.min(Math.max(8, r.left), window.innerWidth - mw - 8), top = r.bottom + 6; if (top + mh > window.innerHeight - 8) top = Math.max(8, r.top - mh - 6);
                menu.style.left = Math.round(left) + 'px'; menu.style.top = Math.round(top) + 'px';
            });
            act.appendChild(dl);
        }
        if (nativeBtn(page, 'btnMoreCommands')) {
            var mo = el('button', 'atv-circle'); mo.type = 'button'; mo.title = t('menu'); mo.setAttribute('aria-label', t('menu'));
            mo.appendChild(svg(ICON.more)); mo.addEventListener('click', function () { var nb = nativeBtn(page, 'btnMoreCommands'); if (nb) nb.click(); });
            act.appendChild(mo);
        }
        lock.appendChild(act);
        // Mehrere Fassungen (z. B. Kinofassung / Extended): Auswahl als Pillen. Gesetzt wird Jellyfins eigene (verborgene)
        // Versions-Auswahl, der Abspielen-Knopf nimmt dann diese Fassung.
        var srcs = (item.MediaSources || []).filter(function (m) { return m && m.Id; });
        if (on('versionPicker') && srcs.length > 1 && (item.Type === 'Movie' || item.Type === 'Episode' || item.Type === 'Video')) {
            page.__atvVersion = null;
            var vers = el('div', 'atv-detail__versions');
            var nativeSel = function () { return page.querySelector('.selectSource'); };
            var vname = function (m, i) { return m.Name || ((lang() === 'en' ? 'Version ' : 'Fassung ') + (i + 1)); };
            var curId = srcs[0].Id;
            var vbtn = el('button', 'atv-season atv-verpick'); vbtn.type = 'button'; vbtn.setAttribute('aria-haspopup', 'menu'); vbtn.setAttribute('aria-expanded', 'false');
            var vlabel = el('span', null, vname(srcs[0], 0)), vsub = el('small', null, srcs[0].RunTimeTicks ? fmtRuntime(srcs[0].RunTimeTicks, 'short') : '');
            vbtn.appendChild(vlabel); vbtn.appendChild(svg(ICON.updown)); vbtn.appendChild(vsub);
            var setCur = function (id) {
                srcs.forEach(function (m, i) { if (m.Id === id) { curId = id; vlabel.textContent = vname(m, i); vsub.textContent = m.RunTimeTicks ? fmtRuntime(m.RunTimeTicks, 'short') : ''; } });
            };
            vbtn.addEventListener('pointerdown', function () { vbtn.__had = !!document.querySelector('.atv-menu--versions'); }, true);
            vbtn.addEventListener('click', function (e) {
                e.stopPropagation();
                if (vbtn.__had) { vbtn.__had = false; closeMenus(); return; }
                closeMenus();
                var menu = el('div', 'atv-menu atv-menu--float atv-menu--versions'); menu.setAttribute('role', 'menu');
                srcs.forEach(function (m, i) {
                    var it = el('button', 'atv-menu__item' + (m.Id === curId ? ' is-selected' : '')); it.type = 'button'; it.setAttribute('role', 'menuitem');
                    it.appendChild(svg(ICON.check)); it.appendChild(el('span', null, vname(m, i)));
                    if (m.RunTimeTicks) it.appendChild(el('small', null, fmtRuntime(m.RunTimeTicks, 'short')));
                    it.addEventListener('click', function (ev) {
                        ev.stopPropagation(); closeMenus();
                        var sel = nativeSel(); if (sel) { sel.value = m.Id; sel.dispatchEvent(new Event('change', { bubbles: true })); }
                        setCur(m.Id); page.__atvVersion = m.Id;
                    });
                    menu.appendChild(it);
                });
                document.body.appendChild(menu);
                var r = vbtn.getBoundingClientRect(), mw = menu.offsetWidth, mh = menu.offsetHeight;
                var left = Math.min(Math.max(8, r.left), window.innerWidth - mw - 8), top = r.bottom + 6; if (top + mh > window.innerHeight - 8) top = Math.max(8, r.top - mh - 6);
                menu.style.left = Math.round(left) + 'px'; menu.style.top = Math.round(top) + 'px';
            });
            vers.appendChild(vbtn);
            // Jellyfin füllt seine Auswahl etwas später: Stand übernehmen, sobald sie da ist
            var tries = 0, ivv = setInterval(function () { var sel = nativeSel(); tries++; if (sel && sel.value) { setCur(sel.value); clearInterval(ivv); } else if (tries > 40 || !vers.isConnected && tries > 5) clearInterval(ivv); }, 150);
            lock.appendChild(vers);
        }
        if (ud.PlaybackPositionTicks > 0 && item.RunTimeTicks) lock.appendChild(el('p', 'atv-detail__note', t('left', fmtRuntime(item.RunTimeTicks - ud.PlaybackPositionTicks, 'short'))));
        root.appendChild(lock);

        var people = item.People || [];
        var actors = people.filter(function (p) { return p.Type === 'Actor'; });
        var dirs = people.filter(function (p) { return p.Type === 'Director'; });
        var creators = item.Type === 'Series' && !dirs.length ? people.filter(function (p) { return p.Type === 'Writer' || p.Type === 'Creator'; }) : [];
        if (actors.length || dirs.length || creators.length) {
            var cr2 = el('div', 'atv-detail__credits');
            var a1 = peopleLine(t('with'), actors, 3); if (a1) cr2.appendChild(a1);
            var a2 = peopleLine(dirs.length ? t('director') : t('creator'), dirs.length ? dirs : creators, 2); if (a2) cr2.appendChild(a2);
            root.appendChild(cr2);
        }
        return root;
    }

    function buildAbout(item) {
        var root = el('section', 'atv-about');
        function col(title, rows) {
            if (!rows.length) return;
            var c = el('div', 'atv-about__col'); c.appendChild(el('h3', null, title));
            var dl = el('dl');
            rows.forEach(function (r) { dl.appendChild(el('dt', null, r[0])); var dd = el('dd'); if (r[1] && r[1].nodeType) dd.appendChild(r[1]); else dd.textContent = r[1]; dl.appendChild(dd); });
            c.appendChild(dl); root.appendChild(c);
        }
        var info = [];
        if (item.ProductionYear) info.push([t('released'), String(item.PremiereDate ? new Date(item.PremiereDate).toLocaleDateString(lang() === 'en' ? 'en-US' : 'de-DE') : item.ProductionYear)]);
        if (item.RunTimeTicks) info.push([t('runtime'), fmtRuntime(item.RunTimeTicks, 'short')]);
        if (item.OfficialRating) info.push([t('rating'), item.OfficialRating]);
        if (item.Genres && item.Genres.length) info.push([t('genres'), item.Genres.join(', ')]);
        if (item.Studios && item.Studios.length) {
            // Studio anklickbar: zeigt alles von diesem Studio aus der Mediathek
            var frag = document.createDocumentFragment(), sid0 = ''; try { sid0 = api().serverId(); } catch (e) { /* ohne */ }
            item.Studios.forEach(function (st, i) {
                if (i) frag.appendChild(document.createTextNode(', '));
                if (st.Id) { var a = el('a', 'atv-about__link', st.Name); a.href = '#/list?studioId=' + st.Id + '&serverId=' + sid0; frag.appendChild(a); }
                else frag.appendChild(document.createTextNode(st.Name));
            });
            info.push([t('studio'), frag]);
        }
        col(t('information'), info);
        var streams = [];
        (item.MediaSources || []).forEach(function (ms) { (ms.MediaStreams || []).forEach(function (s) { streams.push(s); }); });
        if (streams.length) {
            var uniq = function (arr) { var o = {}; return arr.filter(function (x) { return x && !o[x] && (o[x] = 1); }); };
            var lname = function (s) { var l = langInfo(s.Language); return l ? l.name : null; };
            var audio = uniq(streams.filter(function (s) { return s.Type === 'Audio'; }).map(lname));
            var subs = uniq(streams.filter(function (s) { return s.Type === 'Subtitle'; }).map(lname));
            col(t('languages'), [[t('audio'), audio.join(', ') || t('none')]]);
            col(t('accessibility'), [[t('subtitles'), subs.join(', ') || t('none')]]);
        }
        return root.children.length ? root : null;
    }

    // ?atvPlay=1 in der Adresse: den nativen Play-/Weiter-Knopf (Jellyfins Wiedergabe-Logik mit Fortsetzen) einmal auslösen
    function autoPlayIfAsked(page) {
        if (!/[?&]atvPlay=1/.test(location.hash)) return;
        try { history.replaceState(history.state, '', location.href.replace(/[?&]atvPlay=1/, '')); } catch (e) { /* ohne Bereinigung */ }
        var tries = 0, iv = setInterval(function () {
            var nb = nativeBtn(page, 'btnPlay'); tries++;
            if (nb) { clearInterval(iv); nb.click(); } else if (tries > 40) clearInterval(iv);
        }, 150);
    }
    function mountDetail(page) {
        var id = hashParam('id');
        if (!id || !apiReady()) return;
        if (!on('detailPage')) { page.classList.add('atv-native'); return; }
        if (page.__atvDetailId === id && (page.querySelector('.atv-detail') || page.__atvDetailDone)) { detailApplyArt(page); autoPlayIfAsked(page); return; }
        page.__atvDetailId = id; page.__atvDetailDone = false;
        var token = {}; page.__atvToken = token;
        page.querySelectorAll('.atv-detail, .atv-person, .atv-shelf, .atv-about').forEach(function (e) { e.remove(); });
        page.classList.remove('atv-detail-on', 'atv-detail-episodes', 'atv-person-on', 'atv-native');
        // Fallback: kommt der Umbau nicht zustande, nach 2,5 s die native Seite zeigen
        clearTimeout(page.__atvFallbackT);
        page.__atvFallbackT = setTimeout(function () {
            if (page.__atvToken === token && !page.querySelector('.atv-detail, .atv-person')) page.classList.add('atv-native');
        }, 2500);
        api().getItem(userId(), id).then(function (item) {
            if (page.__atvToken !== token) return;
            page.__atvDetailDone = true;          // kein erneuter Versuch bei Fehler/fremdem Typ
            if (!item) { page.classList.add('atv-native'); return; }
            if (item.Type === 'Person') { if (!mountPerson(page, item)) page.classList.add('atv-native'); return; }
            if (['Movie', 'Series', 'Episode', 'Season', 'Video', 'BoxSet', 'Playlist'].indexOf(item.Type) < 0) { page.classList.add('atv-native'); return; }   // andere Typen nativ
            var wrapper = page.querySelector('.detailPageWrapperContainer');
            if (!wrapper) { page.classList.add('atv-native'); return; }
            var block = buildDetail(page, item);
            page.insertBefore(block, wrapper);
            block.style.setProperty('--atv-pad', offsetTopOf(block) + 'px');   // bis unter die Kopfzeile ziehen
            page.classList.add('atv-detail-on');
            trimMore(block);
            autoPlayIfAsked(page);
            var secondary = page.querySelector('.detailPageSecondaryContainer') || wrapper;
            // Rand: hat der Container schon Jellyfins Polsterung, kein eigener Rand
            var gutter = parseFloat(getComputedStyle(secondary).paddingLeft) > 20 ? '0px' : null;
            // auch auf der Seite einer einzelnen Folge: dasselbe Folgenregal wie auf der Serienseite (statt Jellyfins „Mehr von Staffel“)
            if (item.Type === 'Series' || item.Type === 'Season' || (item.Type === 'Episode' && item.SeriesId && item.SeasonId)) {
                page.classList.add('atv-detail-episodes');
                var shelf = buildEpisodeShelf(item);
                if (gutter) shelf.style.setProperty('--atv-gutter', gutter);
                secondary.insertBefore(shelf, secondary.firstChild);
            }
            var about = buildAbout(item);
            if (about) { if (gutter) about.style.setProperty('--atv-gutter', gutter); secondary.appendChild(about); }
        }).catch(function (e) { page.__atvDetailDone = true; page.classList.add('atv-native'); console.warn('[atv] Detailseite konnte nicht geladen werden', e); });
    }
    function detailApplyArt(page) {
        var img = page.querySelector('.atv-detail__art'); if (!img) return;
        var want = isMobile() ? img.getAttribute('data-port') : img.getAttribute('data-land');
        if (want && img.getAttribute('src') !== want) progressiveSrc(img, want);
    }
    window.addEventListener('resize', function () { var p = document.querySelector('.itemDetailPage:not(.hide)'); if (p) detailApplyArt(p); });

    /* =====================================================================
       2b. Personenseite (Item-Typ Person): Foto, Name, Geburtsdaten, Biografie;
       die nativen Regale „Movies/Shows“ (#listChildrenCollapsible) bleiben.
       ===================================================================== */
    function fmtDate(iso) {
        if (!iso) return '';
        var d = new Date(iso);
        if (isNaN(d.getTime())) return '';
        try { return d.toLocaleDateString(lang() === 'en' ? 'en-US' : 'de-DE', { year: 'numeric', month: 'long', day: 'numeric' }); }
        catch (e) { return d.toISOString().slice(0, 10); }
    }
    function ageAt(birthIso, deathIso) {
        var b = new Date(birthIso); if (isNaN(b.getTime())) return null;
        var e = deathIso ? new Date(deathIso) : new Date();
        var a = e.getFullYear() - b.getFullYear();
        if (e.getMonth() < b.getMonth() || (e.getMonth() === b.getMonth() && e.getDate() < b.getDate())) a--;
        return a >= 0 ? a : null;
    }
    function buildPerson(item) {
        var root = el('section', 'atv-person');
        var photo = el('div', 'atv-person__photo');
        var src = primaryUrl(item, 600);
        if (src) { var im = el('img'); im.alt = ''; im.src = src; photo.appendChild(im); }
        else photo.appendChild(el('span', null, (item.Name || '?').charAt(0).toUpperCase()));
        root.appendChild(photo);
        var body = el('div', 'atv-person__body');
        body.appendChild(el('h1', 'atv-person__name', item.Name || ''));
        var facts = [];
        if (item.PremiereDate) {
            var born = t('born') + ' ' + fmtDate(item.PremiereDate);
            var age = ageAt(item.PremiereDate, item.EndDate);
            if (age != null && !item.EndDate) born += ' (' + age + ')';
            facts.push(born);
        }
        if (item.EndDate) facts.push(t('died') + ' ' + fmtDate(item.EndDate));
        if (item.ProductionLocations && item.ProductionLocations.length) facts.push(item.ProductionLocations[0]);
        if (facts.length) body.appendChild(el('p', 'atv-person__facts', facts.join(' · ')));
        var ov = item.Overview || '';
        if (ov) {
            var bio = el('p', 'atv-person__bio atv-clamp', ov);
            addMore(bio);
            body.appendChild(bio);
        }
        root.appendChild(body);
        return root;
    }
    function mountPerson(page, item) {
        var wrapper = page.querySelector('.detailPageWrapperContainer');
        if (!wrapper) return false;
        var block = buildPerson(item);
        page.insertBefore(block, wrapper);
        page.classList.add('atv-person-on');
        trimMore(block);
        return true;
    }
    // Schutz: Klick auf eine Personenkarte darf nie Wiedergabe auslösen (nur Personenseite)
    document.addEventListener('click', function (e) {
        var card = e.target.closest && e.target.closest('.personCard');
        if (!card) return;
        var act = e.target.closest('[data-action]');
        if (act && /^(play|resume|playallfromhere|instantmix)$/i.test(act.getAttribute('data-action') || '')) {
            e.preventDefault(); e.stopPropagation();
            var id = card.getAttribute('data-id');
            if (id) showDetails(id);
        }
    }, true);

    /* =====================================================================
       3. Episodenregal mit Staffel-Menü
       ===================================================================== */
    /* =====================================================================
       Herunterladen (wie in Jellyfins eigenem Menü „Herunterladen“): in der App über deren Download-Verwaltung
       (NativeShell), im Browser als normaler Datei-Download. Knopf erscheint nur, wenn der Nutzer laden darf.
       ===================================================================== */
    var ICON_DL = 'M11 4h2v8.17l2.59-2.58L17 11l-5 5-5-5 1.41-1.41L11 12.17zM5 18h14v2H5z';
    var dlAllowed = null;
    function canDownload() {
        if (!on('downloads')) dlAllowed = false;
        if (dlAllowed !== null) return Promise.resolve(dlAllowed);
        return Promise.resolve(api().getCurrentUser()).then(function (u) { dlAllowed = !!(u && u.Policy && u.Policy.EnableContentDownloading); return dlAllowed; }).catch(function () { return false; });
    }
    function toast(msg) {
        var tb = document.querySelector('.atv-toast') || document.body.appendChild(el('div', 'atv-toast'));
        tb.textContent = msg; tb.classList.add('is-on'); clearTimeout(toast.t); toast.t = setTimeout(function () { tb.classList.remove('is-on'); }, 2800);
    }
    // Qualitäten: „Original“ = Datei, wie sie ist. Die anderen lässt der Server beim Laden kleiner rechnen (H.264/AAC als MP4);
    // Größe ≈ Bitrate × Laufzeit. Der Server wandelt dafür während des Downloads um.
    var DLQ = [
        { key: 'orig', de: 'Original', en: 'Original' },
        { key: '1080', de: 'Hoch · 1080p', en: 'High · 1080p', h: 1080, vb: 6000000, ab: 192000 },
        { key: '720', de: 'Mittel · 720p', en: 'Medium · 720p', h: 720, vb: 3000000, ab: 128000 },
        { key: '480', de: 'Sparsam · 480p', en: 'Data saver · 480p', h: 480, vb: 1200000, ab: 128000 }
    ];
    function fmtSize(bytes) { if (!bytes) return ''; var gb = bytes / 1e9; return gb >= 1 ? gb.toFixed(1).replace('.', lang() === 'en' ? '.' : ',') + ' GB' : Math.max(1, Math.round(bytes / 1e6)) + ' MB'; }
    // Herunterladen-Knöpfe nur dort, wo es eine Download-Verwaltung gibt: in der App bzw. auf Handy/Tablet
    // Eigene Desktop-App (WKWebView) mit Download-Verwaltung: Nachrichtenkanal „atvDownload“ (siehe docs/NATIVE-BRIDGE.md)
    function nativeBridge() { if (!on('nativeApp')) return null; try { var h = window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.atvDownload; return h && typeof h.postMessage === 'function' ? h : null; } catch (e) { return null; } }
    /* Wiedergabe im Player der eigenen Desktop-App (JellyDesk). Jellyfin-Web fragt vor jedem Abspielen seine
       „preplayintercept“-Plugins. Als solches meldet sich das Theme an (window.CinemaGlassBridge, vom Server-Plugin in die
       Plugin-Liste der Weboberfläche eingetragen). Bietet die App den Kanal „atvPlay“ an, bekommt sie die Warteschlange,
       die gerade abgespielt werden sollte; übernimmt sie, endet die Wiedergabe im Web an dieser Stelle ohne Fehlermeldung.
       Im Browser und ohne App antwortet das Plugin sofort mit „weiter“ und ändert nichts. */
    function playBridge() { if (!on('nativeApp')) return null; try { var h = window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.atvPlay; return h && typeof h.postMessage === 'function' ? h : null; } catch (e) { return null; } }
    function playable(it) {
        // Titel aus Kanälen (z. B. einer Mediathek) liegen bei einem Sender; der Server reicht ihre Datei durch, die App kann sie
        // also wie jede andere abspielen. Andere entfernte Titel (Trailer von Videoportalen) bleiben im Web-Player.
        var remoteOk = it && it.LocationType === 'Remote' && !!it.ChannelId && it.Type !== 'Trailer';
        return !!it && !!it.Id && it.MediaType === 'Video' && !it.IsPlaceHolder && !it.IsFolder
            && (it.LocationType !== 'Remote' || remoteOk) && it.LocationType !== 'Virtual'
            && it.Type !== 'TvChannel' && it.Type !== 'Program' && it.Type !== 'LiveTvChannel' && it.Type !== 'LiveTvProgram';
    }
    function playMessage(options) {
        var a = api(), first = options.item;
        if (!apiReady() || !playable(first)) return null;
        var list = Array.isArray(options.items) && options.items.length ? options.items : [first];
        var start = typeof options.startIndex === 'number' && list[options.startIndex] ? options.startIndex : Math.max(0, list.indexOf(first));
        // Was die App nicht abspielen kann (Fotos, Live-TV, unvollständige Einträge), fällt aus der Warteschlange.
        list = list.slice(start, start + 200).filter(function (it, i) { return i === 0 || playable(it); });
        // Vorspann: was der Server vor den ersten Film bzw. die erste Folge gestellt hat und selbst keins von beiden ist
        var main = -1;
        for (var i = 0; i < list.length; i++) { if (list[i].Type === 'Movie' || list[i].Type === 'Episode') { main = i; break; } }
        return {
            type: 'play', version: 1,
            serverUrl: typeof a.serverAddress === 'function' ? a.serverAddress() : location.origin,
            serverId: first.ServerId || (typeof a.serverId === 'function' ? a.serverId() : ''),
            userId: userId(), accessToken: a.accessToken(),
            startIndex: 0,
            startPositionTicks: options.startPositionTicks > 0 ? Math.floor(options.startPositionTicks) : 0,
            mediaSourceId: options.mediaSourceId || null,
            audioStreamIndex: typeof options.audioStreamIndex === 'number' ? options.audioStreamIndex : null,
            subtitleStreamIndex: typeof options.subtitleStreamIndex === 'number' ? options.subtitleStreamIndex : null,
            items: list.map(function (it, i) {
                return {
                    itemId: it.Id, title: it.Name || '', itemType: it.Type || 'Video', seriesName: it.SeriesName || '',
                    seasonNumber: typeof it.ParentIndexNumber === 'number' ? it.ParentIndexNumber : null,
                    episodeNumber: typeof it.IndexNumber === 'number' ? it.IndexNumber : null,
                    year: it.ProductionYear || null, runTimeTicks: it.RunTimeTicks || 0,
                    isIntro: main > 0 && i < main
                };
            })
        };
    }
    // Jellyfin reicht seinen Plugins beim Anlegen die Wiedergabe-Verwaltung; damit startet das Theme Titel ohne Umweg.
    function BridgePlugin(deps) {
        this.name = 'Cinema Glass'; this.type = 'preplayintercept'; this.id = 'cinemaglassbridge'; this.priority = 100;
        if (deps && deps.playbackManager && typeof deps.playbackManager.play === 'function') playManager = deps.playbackManager;
    }
    BridgePlugin.prototype.intercept = function (options) {
        var h = playBridge(), msg = null;
        if (!h || !options) return Promise.resolve();
        try { msg = playMessage(options); } catch (e) { msg = null; }
        if (!msg) return Promise.resolve();
        var asked;
        try { asked = h.postMessage(msg); } catch (e) { return Promise.resolve(); }
        if (!asked || typeof asked.then !== 'function') return Promise.resolve();
        // Antwortet die App nicht (ältere Version, Fehler), spielt der Web-Player wie gewohnt.
        return asked.then(function (r) { return !!(r && r.handled); }, function () { return false; })
            .then(function (handled) { if (handled) return Promise.reject(); });
    };
    window.__cinemaGlassBridgeClass = BridgePlugin;
    if (typeof window.__cinemaGlassBridge === 'function') { try { window.__cinemaGlassBridge(BridgePlugin); } catch (e) {} }

    /* Vollbild in der eigenen Desktop-App (JellyDesk). Das Element-Vollbild von WebKit hängt die Web-Ansicht in ein zweites
       Fenster um, und dabei springt das Bild. Bietet die App den Kanal „atvWindow“ an, geht jede Vollbild-Anfrage der Seite
       (Knopf im Player, Doppelklick, Taste F) stattdessen an sie: die App schaltet ihr eigenes Fenster ins Vollbild, die Seite
       bekommt die gewohnten Ereignisse und Zustände. */
    (function () {
        var nw = null;
        if (on('nativeApp')) { try { var h = window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.atvWindow; nw = h && typeof h.postMessage === 'function' ? h : null; } catch (e) { nw = null; } }
        if (!nw) return;
        var fsEl = null, timer = 0, winFs = false, busy = 0, root = document.documentElement;
        // Für das Stylesheet: „atv-native“ = läuft in der App, „atv-native-fs“ = ihr Fenster ist im Vollbild
        // (dann gibt es keine Fensterknöpfe, und das ✕ des Players darf in die Ecke).
        function mark() {
            if (!root.classList.contains('atv-native')) root.classList.add('atv-native');
            if (root.classList.contains('atv-native-fs') !== winFs) root.classList.toggle('atv-native-fs', winFs);
        }
        // Die Seite übernimmt den Zustand des Fensters in beide Richtungen. Sonst hielte sie sich nach dem grünen Fensterknopf
        // für „nicht im Vollbild“, und der erste Druck auf ihren Vollbild-Knopf bewirkte sichtbar nichts.
        function sync() { if (busy) return; ask('state').then(function (r) { if (busy) return; winFs = !!(r && r.fullscreen); mark(); if (winFs !== !!fsEl) set(winFs ? root : null); }).catch(function () {}); }
        function ask(action) { try { return Promise.resolve(nw.postMessage({ type: 'fullscreen', version: 1, action: action })); } catch (e) { return Promise.reject(e); } }
        function set(el) {
            if (fsEl === el) return;
            fsEl = el;
            ['fullscreenchange', 'webkitfullscreenchange'].forEach(function (n) { document.dispatchEvent(new Event(n, { bubbles: true })); });
        }
        function done() { busy--; }
        function enter() { var el = this; busy++; var p = ask('enter').then(function (r) { winFs = !(r && r.fullscreen === false); mark(); set(winFs ? el : null); }); p.then(done, done); return p; }
        function exit() { busy++; var p = ask('exit').then(function () { winFs = false; mark(); set(null); }); p.then(done, done); return p; }
        function prop(name, get) { try { Object.defineProperty(document, name, { configurable: true, get: get }); } catch (e) { /* bleibt beim Original */ } }
        ['requestFullscreen', 'webkitRequestFullscreen', 'webkitRequestFullScreen'].forEach(function (n) { try { Element.prototype[n] = enter; } catch (e) { /* bleibt beim Original */ } });
        ['exitFullscreen', 'webkitExitFullscreen', 'webkitCancelFullScreen'].forEach(function (n) { try { document[n] = exit; } catch (e) { /* bleibt beim Original */ } });
        ['fullscreenElement', 'webkitFullscreenElement', 'webkitCurrentFullScreenElement'].forEach(function (n) { prop(n, function () { return fsEl; }); });
        ['fullscreen', 'webkitIsFullScreen'].forEach(function (n) { prop(n, function () { return !!fsEl; }); });
        ['fullscreenEnabled', 'webkitFullscreenEnabled'].forEach(function (n) { prop(n, function () { return true; }); });
        // Verlässt man das Vollbild am Fenster selbst (grüner Knopf, Menü), erfährt die Seite es über die Größenänderung.
        window.addEventListener('resize', function () { clearTimeout(timer); timer = setTimeout(sync, 250); });
        new MutationObserver(mark).observe(root, { attributes: true, attributeFilter: ['class'] });
        mark(); sync();
        // Seitenverhältnis des laufenden Videos an die App: sie skaliert die Seite beim Wechsel ins Vollbild damit so,
        // dass das Video währenddessen genau so groß ist wie danach.
        function report(v) {
            if (!v || v.tagName !== 'VIDEO' || !v.videoWidth || !v.videoHeight) return;
            try { var p = nw.postMessage({ type: 'video', version: 1, aspect: v.videoWidth / v.videoHeight }); if (p && p.catch) p.catch(function () {}); } catch (e) { /* ältere App */ }
        }
        ['loadedmetadata', 'resize', 'playing'].forEach(function (n) { document.addEventListener(n, function (ev) { report(ev.target); }, true); });
        report(document.querySelector('video'));
        // Esc beendet das Vollbild wie im Browser, statt bei der Seite anzukommen.
        window.addEventListener('keydown', function (ev) { if (ev.key === 'Escape' && fsEl) { ev.preventDefault(); ev.stopImmediatePropagation(); exit(); } }, true);
    })();
    // Qualität wählbar? Nur wo UNSERE Adresse auch geladen wird. Die Jellyfin-iOS/Android-App (NativeShell) baut sich die
    // Download-Adresse selbst aus der Titel-ID und ignoriert unsere – dort entscheidet die Einstellung der App.
    function dlQualityChoice() { return true; }   // auch in der Jellyfin-App anbieten: greift dort nur, wenn deren eigene Umrechnung AUS ist
    function dlPlatform() { return !!nativeBridge() || !!window.NativeShell || isMobile() || (navigator.maxTouchPoints > 1 && /iPad|iPhone|Android|Macintosh/.test(navigator.userAgent || '')); }
    function dlEntry(it, q) {
        var a = api(), name = it.Path ? String(it.Path).replace(/^.*[\\/]/, '') : null;
        if (!name) {
            var base = it.Type === 'Episode' ? (it.SeriesName || '') + ' S' + (it.ParentIndexNumber != null ? it.ParentIndexNumber : '?') + 'E' + (it.IndexNumber != null ? it.IndexNumber : '?') + ' ' + (it.Name || '') : (it.Name || 'video');
            name = base.replace(/[\\/:*?"<>|]+/g, ' ').trim() + '.' + (it.Container ? String(it.Container).split(',')[0] : 'mkv');
        }
        var url = a.getItemDownloadUrl ? a.getItemDownloadUrl(it.Id) : a.getUrl('Items/' + it.Id + '/Download', { api_key: a.accessToken() });
        if (q && q.h) {
            // kleinere Fassung: fortlaufender MP4-Stream, den der Server umrechnet
            url = a.getUrl('Videos/' + it.Id + '/stream.mp4', { static: false, mediaSourceId: it.Id, videoCodec: 'h264', audioCodec: 'aac', maxAudioChannels: 2, videoBitRate: q.vb, audioBitRate: q.ab, maxHeight: q.h,
                deviceId: typeof a.deviceId === 'function' ? a.deviceId() : 'atv', playSessionId: 'atvdl' + Date.now().toString(36) + it.Id.slice(0, 6), api_key: a.accessToken() });
            name = name.replace(/\.[A-Za-z0-9]{2,4}$/, '') + ' [' + q.h + 'p].mp4';
        }
        return { url: url, item: it, itemId: it.Id, serverId: a.serverId(), title: it.Name, filename: name, quality: q && q.h ? q.key : 'orig', estimatedBytes: q && q.h && it.RunTimeTicks ? Math.round((q.vb + q.ab) / 8 * it.RunTimeTicks / 1e7) : ((it.MediaSources || [])[0] || {}).Size || 0 };
    }
    /* ---------------- Download-Fortschritt in der Jellyfin-App ----------------
       Die App meldet ihren Fortschritt nicht an die Weboberfläche. Rechnet der Server die Datei beim Laden um (Einstellung
       „transkodierte Downloads“ der App), kennt er aber den Stand: /Sessions liefert für das Gerät TranscodingInfo mit
       CompletionPercentage. Den zeigt das Theme an – als Ring am Download-Knopf und als kleine Pille. Lädt die App die
       Originaldatei, gibt es diese Angabe nicht; dann erscheint nur ein kurzer Hinweis. */
    var dlp = { queue: [], iv: null, seen: false, t0: 0, last: -1, lastChange: 0, idle: 0 };
    function dlpSave() { try { sessionStorage.setItem('atvDlQueue', JSON.stringify(dlp.queue.slice(0, 60))); } catch (e) { /* egal */ } }
    function dlpPill() {
        var p = document.querySelector('.atv-dlp');
        if (!p) { p = el('div', 'atv-dlp'); p.setAttribute('role', 'status'); var ring = el('span', 'atv-dlp__ring'); p.appendChild(ring); var tx = el('span', 'atv-dlp__text'); tx.appendChild(el('strong')); tx.appendChild(el('small')); p.appendChild(tx); document.body.appendChild(p); }
        return p;
    }
    function dlpShow(pct, note) {
        var cur = dlp.queue[0], p = dlpPill(), has = pct != null;
        p.classList.toggle('is-unknown', !has);
        p.querySelector('.atv-dlp__ring').style.setProperty('--p', has ? Math.max(2, pct) : 0);
        p.querySelector('strong').textContent = (cur && cur.title) || (lang() === 'en' ? 'Download' : 'Download');
        var rest = dlp.queue.length > 1 ? (lang() === 'en' ? ' · ' + (dlp.queue.length - 1) + ' more' : ' · ' + (dlp.queue.length - 1) + ' weitere') : '';
        p.querySelector('small').textContent = (note || (has ? Math.round(pct) + ' %' : '')) + rest;
        p.classList.add('is-on');
        // Ring am Download-Knopf der Seite, auf der der Titel (oder seine Serie) offen ist
        document.querySelectorAll('.atv-circle--dl').forEach(function (b) {
            var pg = b.closest('.itemDetailPage'), id = pg && pg.__atvDetailId, on = !!cur && has && (id === cur.id || id === cur.seriesId || id === cur.seasonId);
            b.classList.toggle('is-loading', on); if (on) b.style.setProperty('--p', Math.max(2, pct)); else b.style.removeProperty('--p');
        });
    }
    function dlpHide(msg) {
        clearInterval(dlp.iv); dlp.iv = null; dlp.queue = []; dlpSave();
        document.querySelectorAll('.atv-circle--dl.is-loading').forEach(function (b) { b.classList.remove('is-loading'); });
        var p = document.querySelector('.atv-dlp'); if (!p) return;
        if (msg) { p.classList.add('is-unknown'); p.querySelector('small').textContent = msg; setTimeout(function () { p.classList.remove('is-on'); }, 4500); } else p.classList.remove('is-on');
    }
    function dlpTick() {
        var a = api(), did = typeof a.deviceId === 'function' ? a.deviceId() : null; if (!did || typeof a.getSessions !== 'function') { dlpHide(); return; }
        Promise.resolve(a.getSessions({ deviceId: did })).then(function (list) {
            var ti = null; (list || []).forEach(function (se) { if (!ti && se.DeviceId === did && se.TranscodingInfo && !se.NowPlayingItem) ti = se.TranscodingInfo; });
            var now = Date.now(), pct = ti && typeof ti.CompletionPercentage === 'number' ? ti.CompletionPercentage : null;
            if (pct != null) {
                dlp.seen = true; dlp.idle = 0;
                if (dlp.last > 20 && pct < dlp.last - 15 && dlp.queue.length > 1) { dlp.queue.shift(); dlpSave(); }     // neuer Titel begonnen
                if (pct !== dlp.last) dlp.lastChange = now;
                dlp.last = pct;
                if (pct >= 99.5 || now - dlp.lastChange > 45000) { dlp.queue.shift(); dlpSave(); dlp.last = -1; if (!dlp.queue.length) { dlpHide(lang() === 'en' ? 'Finished' : 'Fertig'); return; } }
                dlpShow(pct);
            } else if (dlp.seen) {
                // Umrechnung beendet: Titel fertig
                if (++dlp.idle >= 3) { dlp.queue.shift(); dlpSave(); dlp.idle = 0; dlp.last = -1; if (!dlp.queue.length) dlpHide(lang() === 'en' ? 'Finished' : 'Fertig'); else dlpShow(null, lang() === 'en' ? 'Waiting …' : 'Wartet …'); }
            } else if (now - dlp.t0 > 25000) {
                dlpHide(lang() === 'en' ? 'Loading in the app – progress not available' : 'Lädt in der App – Fortschritt hier nicht verfügbar');
            } else dlpShow(null, lang() === 'en' ? 'Starting …' : 'Wird gestartet …');
        }).catch(function () { /* nächster Versuch */ });
    }
    function dlpTrack(items) {
        if (!on('downloadProgress') || !window.NativeShell || nativeBridge()) return; // nur in der Jellyfin-App nötig
        items.forEach(function (it) { dlp.queue.push({ id: it.Id, title: it.Type === 'Episode' && it.SeriesName ? it.SeriesName + ' · ' + (it.ParentIndexNumber != null ? 'S' + it.ParentIndexNumber : '') + (it.IndexNumber != null ? 'F' + it.IndexNumber : '') : it.Name, seriesId: it.SeriesId || null, seasonId: it.SeasonId || null }); });
        dlpSave();
        if (!dlp.iv) { dlp.seen = false; dlp.t0 = Date.now(); dlp.last = -1; dlp.lastChange = Date.now(); dlp.idle = 0; dlp.iv = setInterval(dlpTick, 2000); dlpTick(); }
    }
    // nach einem Neuladen der Seite: laufenden Download wieder aufgreifen
    function dlpResume() {
        if (!on('downloadProgress') || dlp.iv || dlpResume.done || !apiReady() || !window.NativeShell) return; dlpResume.done = true;
        try { var q = JSON.parse(sessionStorage.getItem('atvDlQueue') || '[]'); if (q.length) { dlp.queue = q; dlp.seen = false; dlp.t0 = Date.now(); dlp.last = -1; dlp.lastChange = Date.now(); dlp.iv = setInterval(dlpTick, 2000); } } catch (e) { /* leer */ }
    }
    // ids: Liste von Item-IDs (Film/Folge). Holt je Titel die vollen Daten (Dateiname) und übergibt nacheinander.
    function downloadIds(ids, q) {
        if (!ids.length) return Promise.resolve(0);
        var n = 0, shell = window.NativeShell, i = 0, a0 = api();
        toast(ids.length === 1 ? (lang() === 'en' ? 'Starting download …' : 'Download wird gestartet …') : (lang() === 'en' ? 'Starting ' + ids.length + ' downloads …' : ids.length + ' Downloads werden gestartet …'));
        function next() {
            if (i >= ids.length) { toast(n === 1 ? (lang() === 'en' ? 'Download started' : 'Download gestartet') : (lang() === 'en' ? n + ' downloads started' : n + ' Downloads gestartet')); return n; }
            var id = ids[i++];
            return api().getItem(userId(), id).then(function (it) {
                var e = dlEntry(it, q), nb = nativeBridge();
                if (nb) {
                    // eigene App: alles, was sie zum Laden, Anzeigen und späteren Abgleich braucht (reine Daten, kein DOM)
                    nb.postMessage({ type: 'download', version: 1, url: e.url, filename: e.filename, title: it.Name || '', itemId: it.Id, itemType: it.Type || '', serverId: e.serverId,
                        serverUrl: typeof a0.serverAddress === 'function' ? a0.serverAddress() : location.origin, userId: userId(), accessToken: a0.accessToken(),
                        runTimeTicks: it.RunTimeTicks || 0, quality: e.quality, estimatedBytes: e.estimatedBytes, container: e.quality === 'orig' ? (it.Container || '') : 'mp4',
                        seriesName: it.SeriesName || '', seasonNumber: it.ParentIndexNumber != null ? it.ParentIndexNumber : null, episodeNumber: it.IndexNumber != null ? it.IndexNumber : null, year: it.ProductionYear || null,
                        imageUrl: primaryUrl(it, 600) || '', backdropUrl: backdropUrl(it) || '', positionTicks: (it.UserData && it.UserData.PlaybackPositionTicks) || 0 });
                }
                else if (shell && typeof shell.downloadFile === 'function') { shell.downloadFile(e); dlpTrack([it]); }
                else if (shell && typeof shell.downloadFiles === 'function') { shell.downloadFiles([e]); dlpTrack([it]); }
                else { var a = document.createElement('a'); a.href = e.url; a.download = e.filename; a.style.display = 'none'; document.body.appendChild(a); a.click(); setTimeout(function () { a.remove(); }, 1000); }
                n++;
            }).catch(function () { /* diesen Titel überspringen */ }).then(function () { return new Promise(function (r) { setTimeout(r, shell ? 150 : 900); }); }).then(next);
        }
        return next();
    }
    // Menü „Welche Qualität?“ am Knopf. info: { ticks: Gesamtlaufzeit, size: Originalgröße in Bytes (falls bekannt), count }
    function askQuality(anchor, info, cb) {
        closeMenus();
        if (!dlQualityChoice()) { cb(null); return; }                        // Jellyfin-App: keine Wahl möglich, direkt laden
        var menu = el('div', 'atv-menu atv-menu--float atv-menu--actions atv-menu--quality'); menu.setAttribute('role', 'menu');
        if (info.title) menu.appendChild(el('div', 'atv-menu__head', info.title));
        var secs = (info.ticks || 0) / 1e7;
        DLQ.forEach(function (q) {
            var size = q.h ? (secs ? (q.vb + q.ab) / 8 * secs : 0) : (info.size || 0);
            if (q.h && info.maxH && q.h > info.maxH) return;                 // nicht größer rechnen als die Quelle
            if (q.h && info.size && size >= info.size * .95) return;         // lohnt nicht: kaum kleiner als das Original
            var it = el('button', 'atv-menu__item'); it.type = 'button'; it.setAttribute('role', 'menuitem');
            it.appendChild(svg(ICON_DL)); it.appendChild(el('span', null, lang() === 'en' ? q.en : q.de));
            if (size) it.appendChild(el('small', null, (q.h ? '≈ ' : '') + fmtSize(size)));
            it.addEventListener('click', function (ev) { ev.stopPropagation(); closeMenus(); cb(q.h ? q : null); });
            menu.appendChild(it);
        });
        document.body.appendChild(menu);
        var r = anchor.getBoundingClientRect(), mw = menu.offsetWidth, mh = menu.offsetHeight;
        var left = Math.min(Math.max(8, r.right - mw), window.innerWidth - mw - 8), top = r.bottom + 6; if (top + mh > window.innerHeight - 8) top = Math.max(8, r.top - mh - 6);
        menu.style.left = Math.round(left) + 'px'; menu.style.top = Math.round(top) + 'px';
    }
    function srcInfo(it) {
        var ms = (it.MediaSources || [])[0] || {}, v = (ms.MediaStreams || []).filter(function (x) { return x.Type === 'Video'; })[0] || {};
        return { ticks: it.RunTimeTicks || ms.RunTimeTicks || 0, size: ms.Size || 0, maxH: v.Height || 0 };
    }
    function buildEpisodeShelf(item) {
        var seriesId = item.Type === 'Season' || item.Type === 'Episode' ? item.SeriesId : item.Id;
        var currentEp = item.Type === 'Episode' ? item.Id : null, firstRender = true;
        var shelf = el('section', 'atv-shelf atv-episodes');
        var head = el('div', 'atv-shelf__head');
        var btn = el('button', 'atv-season'); btn.type = 'button'; btn.setAttribute('aria-haspopup', 'menu'); btn.setAttribute('aria-expanded', 'false');
        var label = el('span', null, t('season') + ' …'); btn.appendChild(label); btn.appendChild(svg(ICON.updown));
        head.appendChild(btn);
        var count = el('span', 'atv-shelf__count'); head.appendChild(count);
        shelf.appendChild(head);
        var wrap = el('div', 'atv-shelf__wrap'); shelf.appendChild(wrap);
        var scroller = el('div', 'atv-shelf__scroller'); wrap.appendChild(scroller);
        var state = { seasons: [], current: null };
        // Pfeile (Maus/Trackpad ohne Seitwärts-Scrollen): blättern um eine Regalbreite; mittig am linken/rechten Rand
        var arrows = el('div', 'atv-shelf__arrows');
        var bPrev = el('button', 'atv-shelf__arrow'); bPrev.type = 'button'; bPrev.setAttribute('aria-label', t('prev')); bPrev.appendChild(svg(ICON.prev));
        var bNext = el('button', 'atv-shelf__arrow'); bNext.type = 'button'; bNext.setAttribute('aria-label', t('next')); bNext.appendChild(svg(ICON.next));
        arrows.appendChild(bPrev); arrows.appendChild(bNext); wrap.appendChild(arrows);
        function syncArrows() {
            var max = scroller.scrollWidth - scroller.clientWidth;
            bPrev.disabled = scroller.scrollLeft <= 2; bNext.disabled = scroller.scrollLeft >= max - 2;
            arrows.classList.toggle('atv-hide', max <= 4);
        }
        function page(dir) { scroller.scrollBy({ left: dir * Math.max(240, scroller.clientWidth * .8), behavior: 'smooth' }); }
        bPrev.addEventListener('click', function (e) { e.stopPropagation(); page(-1); });
        bNext.addEventListener('click', function (e) { e.stopPropagation(); page(1); });
        scroller.addEventListener('scroll', function () { clearTimeout(syncArrows.t); syncArrows.t = setTimeout(syncArrows, 80); }, { passive: true });
        window.addEventListener('resize', function () { if (shelf.isConnected) syncArrows(); });

        function render(episodes) {
            scroller.replaceChildren();
            count.textContent = episodes.length ? t('episodes', episodes.length) : '';
            if (!episodes.length) { scroller.appendChild(el('div', 'atv-shelf__empty', '–')); return; }
            var target = null;
            episodes.forEach(function (ep) {
                var c = episodeCard(ep); scroller.appendChild(c);
                var ud = ep.UserData || {};
                // erste angefangene bzw. erste ungesehene Folge: dorthin blättern
                if (currentEp && firstRender) { if (ep.Id === currentEp) { target = c; c.classList.add('is-next', 'is-current'); } }
                else if (!target && !ud.Played) { target = c; c.classList.add('is-next'); }
            });
            scroller.scrollLeft = 0;
            if (target && target !== scroller.firstChild) {
                requestAnimationFrame(function () {
                    var pad = parseFloat(getComputedStyle(scroller).paddingLeft) || 0;
                    scroller.scrollLeft = Math.max(0, target.offsetLeft - pad);
                    syncArrows();
                });
            }
            firstRender = false;
            requestAnimationFrame(syncArrows);
        }
        function load(season) {
            state.current = season; shelf.__atvSeason = season;
            label.textContent = season ? (season.Name || (t('season') + ' ' + season.IndexNumber)) : t('season');
            api().getEpisodes(seriesId, { seasonId: season ? season.Id : undefined, userId: userId(), Fields: 'Overview', EnableImageTypes: 'Primary' })
                .then(function (res) { render((res && res.Items) || []); })
                .catch(function () { render([]); });
        }
        btn.addEventListener('click', function (e) {
            e.stopPropagation();
            if (btn.getAttribute('aria-expanded') === 'true') { closeMenus(); return; }
            closeMenus();
            if (!state.seasons.length) return;
            btn.setAttribute('aria-expanded', 'true');
            var menu = el('div', 'atv-menu'); menu.setAttribute('role', 'menu');
            state.seasons.forEach(function (s) {
                var it = el('button', 'atv-menu__item' + (state.current && s.Id === state.current.Id ? ' is-selected' : '')); it.type = 'button'; it.setAttribute('role', 'menuitem');
                it.appendChild(svg(ICON.check)); it.appendChild(el('span', null, s.Name || (t('season') + ' ' + s.IndexNumber)));
                it.addEventListener('click', function () { closeMenus(); load(s); });
                menu.appendChild(it);
            });
            head.appendChild(menu);
            var first = menu.querySelector('.is-selected') || menu.firstChild; if (first) first.focus();
        });
        api().getSeasons(seriesId, { userId: userId(), Fields: 'ItemCounts' }).then(function (res) {
            state.seasons = ((res && res.Items) || []).filter(function (s) { return s.IndexNumber !== 0 || res.Items.length === 1; });
            if (!state.seasons.length && res && res.Items) state.seasons = res.Items;
            var start = null;
            if (item.Type === 'Season') start = state.seasons.filter(function (s) { return s.Id === item.Id; })[0] || item;
            if (item.Type === 'Episode') start = state.seasons.filter(function (s) { return s.Id === item.SeasonId; })[0] || null;
            var pick = function () { load(start || state.seasons[0] || null); };
            if (!start) {
                api().getNextUpEpisodes({ SeriesId: seriesId, UserId: userId(), Limit: 1 }).then(function (nu) {
                    var e = nu && nu.Items && nu.Items[0];
                    if (e && e.SeasonId) start = state.seasons.filter(function (s) { return s.Id === e.SeasonId; })[0] || null;
                    pick();
                }).catch(pick);
            } else pick();
        }).catch(function () { load(null); });
        return shelf;
    }

    var EP_TXT = {
        de: { fromStart: 'Von Anfang an', markSeen: 'Als gesehen markieren', markUnseen: 'Als ungesehen markieren', addList: 'Zur Merkliste', removeList: 'Von der Merkliste entfernen', details: 'Details', seasonUp: 'STAFFEL', aired: 'Erstausstrahlung' },
        en: { fromStart: 'From the beginning', markSeen: 'Mark as watched', markUnseen: 'Mark as unwatched', addList: 'Add to watchlist', removeList: 'Remove from watchlist', details: 'Details', seasonUp: 'SEASON', aired: 'First aired' }
    };
    function et(k) { return (EP_TXT[lang()] || EP_TXT.de)[k]; }
    var ICON_REPLAY = 'M12 5V1L7 6l5 5V7c3.31 0 6 2.69 6 6s-2.69 6-6 6-6-2.69-6-6H4c0 4.42 3.58 8 8 8s8-3.58 8-8-3.58-8-8-8z';
    // Karte nach einer Änderung (gesehen/Merkliste) an Ort und Stelle neu zeichnen
    function redrawEp(ep, card) {
        var nc = episodeCard(ep);
        ['is-next', 'is-current'].forEach(function (c) { if (card.classList.contains(c)) nc.classList.add(c); });
        if (card.parentNode) card.parentNode.replaceChild(nc, card);
        return nc;
    }
    function epTogglePlayed(ep) {
        var a = api(), ud = ep.UserData || {}, was = !!ud.Played;
        return (was ? a.markUnplayed(userId(), ep.Id) : a.markPlayed(userId(), ep.Id)).then(function (nud) {
            ep.UserData = nud || ud; if (!nud) ep.UserData.Played = !was;
            if (ep.UserData.Played) { ep.UserData.PlaybackPositionTicks = 0; ep.UserData.PlayedPercentage = 0; }
        });
    }
    function epToggleFav(ep) {
        var a = api(), ud = ep.UserData || {}, was = !!ud.IsFavorite;
        return (was ? a.unmarkFavorite(userId(), ep.Id) : a.markFavorite(userId(), ep.Id)).then(function (nud) { ep.UserData = nud || ud; if (!nud) ep.UserData.IsFavorite = !was; });
    }
    function openEpMenu(ep, card, anchor) {
        closeMenus(); canDownload();
        var ud = ep.UserData || {}, resume = ud.PlaybackPositionTicks > 0;
        var menu = el('div', 'atv-menu atv-menu--float atv-menu--actions'); menu.setAttribute('role', 'menu');
        function item(icon, label, fn) {
            var it = el('button', 'atv-menu__item'); it.type = 'button'; it.setAttribute('role', 'menuitem');
            it.appendChild(svg(icon)); it.appendChild(el('span', null, label));
            it.addEventListener('click', function (e) { e.stopPropagation(); closeMenus(); fn(); });
            menu.appendChild(it);
        }
        item(ICON.play, resume ? t('resume') : t('play'), function () { playItem(ep, ud.PlaybackPositionTicks || 0); });
        if (resume) item(ICON_REPLAY, et('fromStart'), function () { playItem(ep, 0); });
        item(ICON.check, ud.Played ? et('markUnseen') : et('markSeen'), function () { epTogglePlayed(ep).then(function () { redrawEp(ep, card); }).catch(function () {}); });
        item(ICON.plus, ud.IsFavorite ? et('removeList') : et('addList'), function () { epToggleFav(ep).then(function () { redrawEp(ep, card); }).catch(function () {}); });
        if (dlAllowed && dlPlatform()) item(ICON_DL, lang() === 'en' ? 'Download …' : 'Herunterladen …', function () {
            api().getItem(userId(), ep.Id).then(function (full) { var inf = srcInfo(full); inf.title = ep.Name; askQuality(anchor, inf, function (q) { downloadIds([ep.Id], q); }); }).catch(function () { downloadIds([ep.Id]); });
        });
        item(ICON.info, et('details'), function () { openEpInfo(ep, card); });
        document.body.appendChild(menu);
        // an den Knopf heften: bevorzugt darunter, rechtsbündig; sonst darüber
        var r = anchor.getBoundingClientRect(), mw = menu.offsetWidth, mh = menu.offsetHeight, vw = window.innerWidth, vh = window.innerHeight;
        var left = Math.min(Math.max(8, r.right - mw), vw - mw - 8), top = r.bottom + 6;
        if (top + mh > vh - 8) top = Math.max(8, r.top - mh - 6);
        menu.style.left = Math.round(left) + 'px'; menu.style.top = Math.round(top) + 'px';
        var first = menu.querySelector('button'); if (first && !isMobile()) first.focus();
        var onScroll = function () { closeMenus(); window.removeEventListener('scroll', onScroll, true); };
        window.addEventListener('scroll', onScroll, true);
    }
    // Infobox zur Folge: Bild, Staffel/Folge, Titel, ganzer Beschreibungstext, Eckdaten – ohne die Seite zu verlassen
    function openEpInfo(ep, card) {
        var old = document.querySelector('.atv-sheet'); if (old) old.remove();
        var build = function (full) {
            var it = full || ep, ud = ep.UserData || {};
            var wrap = el('div', 'atv-sheet'); wrap.setAttribute('role', 'dialog'); wrap.setAttribute('aria-modal', 'true'); wrap.setAttribute('aria-label', ep.Name || '');
            var box = el('div', 'atv-sheet__box'); wrap.appendChild(box);
            var x = el('button', 'atv-sheet__close'); x.type = 'button'; x.setAttribute('aria-label', t('close')); x.appendChild(svg(ICON.close)); box.appendChild(x);
            var src = ep.ImageTags && ep.ImageTags.Primary ? imgUrl(ep.Id, { type: 'Primary', maxWidth: 960, tag: ep.ImageTags.Primary }) : backdropUrl(it);
            if (src) { var im = el('img', 'atv-sheet__img'); im.alt = ''; im.src = src; box.appendChild(im); }
            var body = el('div', 'atv-sheet__body'); box.appendChild(body);
            var tag = [];
            if (it.ParentIndexNumber != null) tag.push(et('seasonUp') + ' ' + it.ParentIndexNumber);
            if (it.IndexNumber != null) tag.push(t('episode') + ' ' + it.IndexNumber);
            if (tag.length) body.appendChild(el('div', 'atv-sheet__tag', tag.join(' · ')));
            body.appendChild(el('h2', 'atv-sheet__title', ep.Name || ''));
            if (it.SeriesName) body.appendChild(el('div', 'atv-sheet__series', it.SeriesName));
            var facts = el('div', 'atv-sheet__facts'), parts = [];
            if (it.PremiereDate) parts.push(fmtDate(it.PremiereDate));
            if (it.RunTimeTicks) parts.push(fmtRuntime(it.RunTimeTicks, 'short'));
            if (parts.length) facts.appendChild(el('span', null, parts.join(' · ')));
            if (it.CommunityRating) { var st = el('span', 'atv-badge atv-badge--star'); st.appendChild(svg(ICON.star)); st.appendChild(document.createTextNode(it.CommunityRating.toFixed(1).replace('.', lang() === 'en' ? '.' : ','))); facts.appendChild(st); }
            if (full) {
                mediaBadges(full).forEach(function (b) { facts.appendChild(el('span', 'atv-badge', b)); });
                langBadges(full).forEach(function (b) { var sp = el('span', 'atv-badge atv-badge--lang'); sp.title = b.title; sp.appendChild(el('span', 'atv-badge__pre', b.prefix)); sp.appendChild(document.createTextNode(b.text)); facts.appendChild(sp); });
            }
            if (facts.childNodes.length) body.appendChild(facts);
            var act = el('div', 'atv-sheet__actions');
            var play = el('button', 'atv-pill'); play.type = 'button'; play.appendChild(svg(ICON.play)); play.appendChild(el('span', null, ud.PlaybackPositionTicks > 0 ? t('resume') : t('play')));
            play.addEventListener('click', function () { close(); playItem(ep, ud.PlaybackPositionTicks || 0); });
            act.appendChild(play);
            var seen = el('button', 'atv-circle' + (ud.Played ? ' is-on' : '')); seen.type = 'button'; seen.setAttribute('aria-label', t('watched')); seen.appendChild(svg(ICON.check));
            seen.addEventListener('click', function () { epTogglePlayed(ep).then(function () { seen.classList.toggle('is-on', !!ep.UserData.Played); if (card) card = redrawEp(ep, card); }).catch(function () {}); });
            act.appendChild(seen);
            var fav = el('button', 'atv-circle'); fav.type = 'button'; fav.setAttribute('aria-label', t('watchlist')); setFavButton(fav, ud.IsFavorite);
            fav.addEventListener('click', function () { epToggleFav(ep).then(function () { setFavButton(fav, ep.UserData.IsFavorite); if (card) card = redrawEp(ep, card); }).catch(function () {}); });
            act.appendChild(fav);
            body.appendChild(act);
            if (it.Overview) body.appendChild(el('p', 'atv-sheet__text', it.Overview));
            var people = (full && full.People) || [], dirs = people.filter(function (p) { return p.Type === 'Director'; }), guests = people.filter(function (p) { return p.Type === 'GuestStar'; });
            if (dirs.length || guests.length) {
                var cr = el('div', 'atv-sheet__credits');
                var line = function (label, list, max) { if (!list.length) return; var p = el('p'); p.appendChild(el('span', 'atv-label', label + ' ')); p.appendChild(document.createTextNode(list.slice(0, max).map(function (q) { return q.Name; }).join(', '))); cr.appendChild(p); };
                line(t('director'), dirs, 2); line(lang() === 'en' ? 'Guest stars' : 'Gäste', guests, 5);
                body.appendChild(cr);
            }
            var prevOv = document.documentElement.style.overflow;
            function onKey(e) { if (e.key === 'Escape') { e.stopPropagation(); close(); } }
            function close() { document.removeEventListener('keydown', onKey, true); document.documentElement.style.overflow = prevOv; wrap.classList.add('is-out'); setTimeout(function () { wrap.remove(); }, 180); }
            x.addEventListener('click', close);
            wrap.addEventListener('click', function (e) { if (e.target === wrap) close(); });
            document.addEventListener('keydown', onKey, true);
            document.documentElement.style.overflow = 'hidden';
            document.body.appendChild(wrap);
        };
        // erst die vollständigen Daten holen, dann in einem Stück zeigen (höchstens 700 ms warten)
        var done = false, tmr = setTimeout(function () { if (!done) { done = true; build(null); } }, 700);
        api().getItem(userId(), ep.Id).then(function (full) { if (done) return; done = true; clearTimeout(tmr); build(full); }).catch(function () { if (done) return; done = true; clearTimeout(tmr); build(null); });
    }
    function episodeCard(ep) {
        var card = el('article', 'atv-ep'); card.tabIndex = 0;
        card.setAttribute('aria-label', (ep.IndexNumber != null ? t('episode') + ' ' + ep.IndexNumber + ': ' : '') + ep.Name);
        var img = el('img', 'atv-ep__img'); img.alt = ''; img.loading = 'lazy';
        var src = ep.ImageTags && ep.ImageTags.Primary ? imgUrl(ep.Id, { type: 'Primary', maxWidth: 640, tag: ep.ImageTags.Primary }) : backdropUrl(ep) || (ep.SeriesId && ep.SeriesThumbImageTag ? imgUrl(ep.SeriesId, { type: 'Thumb', maxWidth: 640, tag: ep.SeriesThumbImageTag }) : null);
        if (src) img.src = src;
        card.appendChild(img);
        card.appendChild(el('div', 'atv-ep__grad'));
        card.appendChild(el('div', 'atv-ep__scrim'));
        var body = el('div', 'atv-ep__body');
        if (ep.IndexNumber != null) body.appendChild(el('div', 'atv-ep__tag', t('episode') + ' ' + ep.IndexNumber));
        body.appendChild(el('div', 'atv-ep__title', ep.Name));
        if (ep.Overview) body.appendChild(el('div', 'atv-ep__desc atv-clamp atv-clamp--2', ep.Overview));
        var foot = el('div', 'atv-ep__foot'), ud = ep.UserData || {};
        if (ud.Played) { card.classList.add('is-played'); foot.appendChild(svg(ICON.check)); foot.appendChild(el('span', null, fmtRuntime(ep.RunTimeTicks))); }
        else if (ud.PlaybackPositionTicks > 0 && ep.RunTimeTicks) {
            var bar = el('span', 'atv-ep__bar'), fill = el('i'); fill.style.width = Math.min(100, ud.PlayedPercentage || 0) + '%'; bar.appendChild(fill);
            foot.appendChild(bar); foot.appendChild(el('span', null, t('left', fmtRuntime(ep.RunTimeTicks - ud.PlaybackPositionTicks))));
        } else if (ep.RunTimeTicks) { var go0 = svg(ICON.play); go0.setAttribute('class', 'atv-ep__go'); foot.appendChild(go0); foot.appendChild(el('span', null, fmtRuntime(ep.RunTimeTicks))); }
        if (foot.childNodes.length) body.appendChild(foot);
        card.appendChild(body);
        var more = el('button', 'atv-ep__more'); more.type = 'button'; more.setAttribute('aria-label', t('info')); more.appendChild(svg(ICON.more));
        // ··· öffnet ein kleines Menü an Ort und Stelle (kein Seitenwechsel); „Details“ darin eine Infobox
        more.addEventListener('pointerdown', function () { more.__had = !!document.querySelector('.atv-menu--float'); }, true);
        more.addEventListener('click', function (e) { e.stopPropagation(); e.preventDefault(); if (more.__had) { more.__had = false; return; } if (!on('episodeMenu')) { showDetails(ep.Id); return; } openEpMenu(ep, card, more); });
        card.appendChild(more);
        var go = function () { playItem(ep, ud.PlaybackPositionTicks || 0); };
        card.addEventListener('click', go);
        card.addEventListener('keydown', function (e) { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); go(); } });
        return card;
    }

    /* =====================================================================
       4. Profilauswahl „Wer schaut?“ (Anmeldeseite)
       ===================================================================== */
    var PASTEL = ['#f5c3d1', '#c4dff5', '#c6ecc9', '#f6e6a7', '#e4d2f5', '#ffd9b8'];
    function mountLogin(page) {
        if (page.__atvWho) return;
        var a = api();
        if (!a || typeof a.getPublicUsers !== 'function') return;
        var form = page.querySelector('form.manualLoginForm');
        if (!form) return;
        page.__atvWho = true;
        // Erst die öffentlichen Benutzer holen: ohne sichtbare Benutzer (Jellyfin-Standard:
        // „auf Anmeldeseiten verbergen“) bleibt die native Anmeldeseite unangetastet.
        a.getPublicUsers().then(function (users) {
            users = users || [];
            if (!users.length || !page.isConnected) return;
            buildWho(page, form, users);
        }).catch(function () {});
    }
    function buildWho(page, form, users) {
        var a = api();
        var who = el('div', 'atv-who');
        who.appendChild(el('h1', 'atv-who__title', t('who')));
        var row = el('div', 'atv-who__row'); who.appendChild(row);
        var scrim = el('div', 'atv-who__scrim'); scrim.addEventListener('click', function () { page.classList.remove('atv-who--form'); });
        who.appendChild(scrim);
        page.insertBefore(who, page.firstChild);
        page.classList.add('atv-who-on');
        var wrap = form.parentNode;
        if (wrap && !wrap.querySelector('.atv-who__close')) {
            var x = el('button', 'atv-who__close'); x.type = 'button'; x.setAttribute('aria-label', t('close')); x.appendChild(svg(ICON.close));
            x.addEventListener('click', function () { page.classList.remove('atv-who--form'); });
            wrap.insertBefore(x, wrap.firstChild);
        }
        function showManual() {
            if (form.classList.contains('hide')) { var bm = page.querySelector('.btnManual'); if (bm) bm.click(); }
        }
        function openForm(name, focusPw) {
            page.classList.add('atv-who--form');
            showManual();
            var u = page.querySelector('#txtManualName'), p = page.querySelector('#txtManualPassword');
            if (u && name != null) { u.value = name; u.dispatchEvent(new Event('input', { bubbles: true })); }
            setTimeout(function () { var f = focusPw ? p : u; if (f) f.focus(); }, 50);
        }
        function userTile(u, i) {
            var b = el('button', 'atv-who__user'); b.type = 'button';
            var av = el('div', 'atv-who__avatar'); av.style.background = PASTEL[i % PASTEL.length];
            if (u.PrimaryImageTag) { var im = el('img'); im.alt = ''; im.src = a.getUserImageUrl(u.Id, { type: 'Primary', tag: u.PrimaryImageTag, maxWidth: 400 }); av.appendChild(im); }
            else av.appendChild(el('span', null, (u.Name || '?').charAt(0).toUpperCase()));
            b.appendChild(av); b.appendChild(el('div', 'atv-who__name', u.Name));
            b.addEventListener('click', function () {
                var un = page.querySelector('#txtManualName'), pw = page.querySelector('#txtManualPassword');
                if (u.HasPassword === false && un && pw) {
                    showManual();
                    un.value = u.Name; pw.value = '';
                    un.dispatchEvent(new Event('input', { bubbles: true }));
                    if (typeof form.requestSubmit === 'function') form.requestSubmit(); else form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
                } else openForm(u.Name, true);
            });
            return b;
        }
        users.forEach(function (u, i) { row.appendChild(userTile(u, i)); });
        var add = el('button', 'atv-who__user'); add.type = 'button'; add.setAttribute('aria-label', t('other'));
        var av = el('div', 'atv-who__avatar atv-who__avatar--add'); av.appendChild(svg(ICON.plus)); add.appendChild(av);
        add.appendChild(el('div', 'atv-who__name', ''));
        add.addEventListener('click', function () { openForm(null, false); });
        row.appendChild(add);
    }

    /* =====================================================================
       5. Glas-Tableiste (Mobil)
       ===================================================================== */
    var tabbar = { root: null, views: null, viewsUser: null };
    function tabLink(href, icon, label) {
        var a = el('a'); a.href = href; a.appendChild(svg(icon)); a.appendChild(el('span', null, label)); return a;
    }
    var MB_ICON = {
        back: 'M15.41 7.41 14 6l-6 6 6 6 1.41-1.41L10.83 12z',
        user: 'M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z'
    };
    function mbBtn(cls, label, path) {
        var b = el('button', 'atv-mb__btn ' + cls); b.type = 'button'; b.setAttribute('aria-label', label); b.appendChild(svg(path)); return b;
    }
    function ensureMobileBar() {
        var bar = document.getElementById('atv-mbar');
        if (!bar) {
            bar = el('div', 'atv-mb'); bar.id = 'atv-mbar';
            var back = mbBtn('atv-mb__back', 'Zurück', MB_ICON.back);
            back.addEventListener('click', function () { goBack(); });
            var right = el('div', 'atv-mb__right');
            var search = el('a', 'atv-mb__btn atv-mb__search'); search.href = '#/search.html'; search.setAttribute('aria-label', t('search')); search.appendChild(svg(ICON.search));
            var prof = mbBtn('atv-mb__profile', 'Profil', MB_ICON.user);
            prof.addEventListener('click', function () {
                var n = document.querySelector('header button[aria-label="User Menu"], header [aria-label*="Benutzer"], header .MuiBox-root:last-child button');
                if (n) n.click();
            });
            right.appendChild(search); right.appendChild(prof);
            bar.appendChild(back); bar.appendChild(right); document.body.appendChild(bar);
            try {
                var url = api().getUserImageUrl(userId(), { type: 'Primary', maxHeight: 96, quality: 80 });
                if (url) { var im = new Image(); im.onload = function () { prof.textContent = ''; var i2 = el('img', 'atv-mb__avatar'); i2.alt = ''; i2.src = url; prof.appendChild(i2); }; im.src = url; }
            } catch (e) { /* kein Profilbild: Standard-Symbol bleibt */ }
        }
    }
    // Zurück = zur vorigen SEITE. Wechselt man in einer Liste oben die Ansicht (Sammlungen → Genres …), legt Jellyfin
    // dafür Verlaufseinträge an; „Zurück“ überspringt sie, statt nur die Ansicht zurückzuschalten.
    function pageKey() {
        var m = location.hash.match(/^#\/([^?]*)/), path = m ? m[1].replace(/\.html$/, '') : '';
        if (path === 'search') return 'search';
        return path + '|' + (hashParam('topParentId') || hashParam('parentId') || hashParam('id') || '');
    }
    function goBack() {
        var key = pageKey(), n = 0;
        function step() {
            if (window.history.length <= 1) { location.hash = '#/home'; return; }
            var before = location.href, done = false;
            function after() {
                if (done) return; done = true; window.removeEventListener('popstate', after);
                if (location.href !== before && pageKey() === key && ++n < 10) step();
            }
            window.addEventListener('popstate', after);
            setTimeout(after, 350);
            window.history.back();
        }
        step();
    }
    // Zurück-Wischgeste (nur in der App; Safari und Android-Browser bringen sie selbst mit).
    // Aufbau: zwei EIGENE bildschirmfüllende Ebenen am <body>, unabhängig davon, wie Jellyfin seine Seiten-Kästen baut
    // (in der iOS-App sind die nur schmale Streifen – Zuschnitt/Hintergrund direkt an der Seite ging dort schief):
    //   unten  = Abbild der vorigen Seite (beim Verlassen gemerkt, Jellyfin baut Start/Listen sonst ab)
    //   oben   = Abbild der aktuellen Seite, folgt dem Finger 1:1, deckend, mit runden Ecken
    // Nach dem Loslassen bleibt das untere Abbild stehen, bis die echte Seite fertig darunter steht.
    function swipeBackWanted() {
        var ua = navigator.userAgent || '';
        return on('swipeBack') && (!!window.NativeShell || (/iPhone|iPad|iPod/.test(ua) && !/Safari\//.test(ua)));
    }
    var ghosts = [];
    function buildGhost(page) {
        var g = el('div', 'atv-ghost'); g.setAttribute('aria-hidden', 'true');
        var srcs = [page], hd = document.querySelector('header.MuiAppBar-root');
        if (hd) srcs.push(hd);                                             // ganze Kopfzeile: nur so greifen ihre Stil-Regeln auch im Abbild
        srcs.forEach(function (src) {
            var r = src.getBoundingClientRect(), box = el('div', 'atv-ghost__box'), c = src.cloneNode(true);
            box.style.cssText = 'position:absolute;left:' + Math.round(r.left) + 'px;top:' + Math.round(r.top) + 'px;width:' + Math.round(r.width) + 'px;';
            c.style.transform = ''; c.style.opacity = ''; c.style.transition = '';
            if (src === hd) c.style.cssText += ';position:absolute!important;left:0;top:0;width:100%;';
            c.querySelectorAll('video, audio, iframe, script').forEach(function (n) { n.remove(); });
            // Bezugspunkt merken: viele Abstände hängen an Klassen am <html> (Start/Liste/Detail). Liegt das Abbild später auf einer
            // anderen Seite, rutscht sein Inhalt sonst um ein paar Dutzend Pixel – fixGhost() gleicht das wieder aus.
            var ref = null, cands = src === hd ? src.querySelectorAll('.MuiToolbar-root + .MuiToolbar-root') : src.querySelectorAll('.atv-hero, .atv-detail, .atv-person, .itemsContainer, .verticalSection, .card');
            for (var i = 0; i < cands.length && !ref; i++) { var cr = cands[i].getBoundingClientRect(); if (cr.height > 0 && cr.width > 0) ref = cands[i]; }
            if (ref) {
                var path = [], n = ref;
                while (n && n !== src) { path.unshift(Array.prototype.indexOf.call(n.parentNode.children, n)); n = n.parentNode; }
                box.__ref = { path: path, top: ref.getBoundingClientRect().top }; box.__m = 0;
            }
            box.appendChild(c); g.appendChild(box);
        });
        return g;
    }
    function fixGhost(g) {
        if (!g || !g.isConnected) return;
        for (var i = 0; i < g.children.length; i++) {
            var box = g.children[i], rf = box.__ref; if (!rf) continue;
            var n = box.firstChild;
            for (var k = 0; n && k < rf.path.length; k++) n = n.children[rf.path[k]];
            if (!n) continue;
            var d = Math.round(rf.top - (n.getBoundingClientRect().top - box.__m));
            if (d !== box.__m && Math.abs(d) < 400) { box.__m = d; box.style.marginTop = d + 'px'; }
        }
    }
    function snapPage() {
        try {
            if (!isMobile() || !swipeBackWanted()) return;
            var page = visiblePage(), key = pageKey();
            if (!page || /^(video|login|selectserver|wizard)/.test(key) || document.querySelector('.atv-xd')) return;
            var entry = { key: key, node: buildGhost(page), y: Math.round(window.scrollY) }, top = ghosts[ghosts.length - 1];
            if (top && top.key === key) ghosts[ghosts.length - 1] = entry; else { ghosts.push(entry); if (ghosts.length > 4) ghosts.shift(); }
        } catch (e) { /* Abbild ist nur Zierde */ }
    }
    function ghostFor() { var top = ghosts[ghosts.length - 1]; return top && top.key !== pageKey() ? top : null; }
    if (!window.__atvSnap) {
        window.__atvSnap = true;
        // Jellyfins Navigation läuft über history.pushState: Abbild unmittelbar davor (die alte Seite steht dann noch)
        try {
            var origPush = window.history.pushState;
            window.history.pushState = function () { if (!(arguments[0] && arguments[0].atvXd)) snapPage(); return origPush.apply(this, arguments); };
        } catch (e) { /* ohne Abbild */ }
        document.addEventListener('click', function (e) {
            var a = e.target && e.target.closest ? e.target.closest('a[href^="#/"]') : null;
            if (a && !e.defaultPrevented) snapPage();
        }, true);
    }
    // echte Seite fertig? (sichtbare Bilder geladen, Grundgerüst da)
    function pageReady(np) {
        if (!np.querySelector('.card, .atv-detail, .atv-hero, .atv-person, .atv-search')) return false;
        // Grundgerüst des Themes muss stehen – sonst schiebt z. B. der später eingesetzte Hero die Startseite nach dem Tausch nach unten
        if (!np.classList.contains('atv-native')) {
            if (np.classList.contains('homePage') && (!np.querySelector('.atv-hero') || !np.classList.contains('atv-hero-on') || !np.querySelector('.homeSectionsContainer .card') || !homeChipsReady(np))) return false;
            if (np.classList.contains('itemDetailPage')) {
                if (np.__atvDetailId !== hashParam('id') || !np.querySelector('.atv-detail, .atv-person')) return false;
                if (np.querySelector('.atv-episodes') && !np.querySelector('.atv-ep, .atv-shelf__empty')) return false;
            }
        }
        var vh = window.innerHeight, vw = window.innerWidth, ok = true;
        var inView = function (n) { var r = n.getBoundingClientRect(); return r.width > 0 && r.bottom > 0 && r.top < vh && r.right > 0 && r.left < vw; };
        var imgOk = function (im) { return !im.getAttribute('src') || (im.complete && im.naturalWidth > 0 && parseFloat(getComputedStyle(im).opacity) > .9); };
        np.querySelectorAll('img.atv-hero__art, img.atv-detail__art, img.atv-ep__img').forEach(function (im) {
            if (!ok || !inView(im) || (im.closest('.atv-hero__slide') && !im.closest('.is-active'))) return;
            if (!imgOk(im)) ok = false;
        });
        np.querySelectorAll('.cardImageContainer').forEach(function (c) {
            if (!ok || !inView(c)) return;
            if (c.classList.contains('lazy')) { if (!c.style.backgroundImage && c.hasAttribute('data-src')) ok = false; return; }
            var im = c.querySelector('img');
            if (im) { if (!imgOk(im)) ok = false; }
            else if (c.classList.contains('coveredImage') && !c.querySelector('svg, .material-icons, canvas')) ok = false;
        });
        return ok;
    }
    if (!window.__atvSwipe) {
        window.__atvSwipe = true;
        var sw = null;
        var H = document.documentElement.classList;
        var layerOff = function (n) { if (!n) return; n.remove(); n.style.transform = ''; n.style.transition = ''; n.style.filter = ''; n.classList.remove('atv-ghost--top', 'atv-ghost--under', 'atv-ghost--hold'); };
        document.addEventListener('touchstart', function (e) {
            sw = null;
            if (!isMobile() || !swipeBackWanted() || e.touches.length !== 1) return;
            var t0 = e.touches[0];
            if (t0.clientX > 28) return;                                   // nur vom Rand (Regale wischen weiter innen)
            if (H.contains('atv-page-home') || H.contains('atv-swap') || /#\/(home|login|video)/.test(location.hash)) return;
            if (document.querySelector('.dialogContainer, .MuiModal-root:not(.MuiModal-hidden), .atv-sheet, .atv-trailer')) return;
            sw = { x: t0.clientX, y: t0.clientY, t: Date.now(), on: false, dx: 0, top: null, under: null, ghost: null, xd: document.querySelector('.atv-xd') };
        }, { passive: true, capture: true });
        document.addEventListener('touchmove', function (e) {
            if (!sw) return;
            var t1 = e.touches[0], dx = t1.clientX - sw.x, dy = Math.abs(t1.clientY - sw.y);
            if (!sw.on) {
                if (dy > 14 && dy > dx) { sw = null; return; }             // senkrecht: normales Scrollen
                if (dx < 10) return;
                sw.on = true;
                try {
                    if (sw.xd) { sw.top = sw.xd; sw.top.style.transition = 'none'; }           // Seerr-Detailansicht: eigenes Vollbild-Element, direkt verschieben
                    else {
                        var page = visiblePage(); if (!page) { sw = null; return; }
                        sw.ghost = ghostFor();
                        sw.under = sw.ghost ? sw.ghost.node : el('div', 'atv-ghost');
                        sw.under.classList.add('atv-ghost--under');
                        sw.top = buildGhost(page); sw.top.classList.add('atv-ghost--top');
                        document.body.appendChild(sw.under); document.body.appendChild(sw.top);
                        fixGhost(sw.under);
                    }
                } catch (e2) { sw = null; return; }
                H.add('atv-swiping', 'atv-swipe-ui');
            }
            if (e.cancelable) e.preventDefault();
            sw.dx = Math.max(0, dx);
            var pr = Math.min(1, sw.dx / window.innerWidth);
            sw.top.style.transform = 'translate3d(' + Math.round(sw.dx) + 'px,0,0)';
            if (sw.under) { sw.under.style.transform = 'translate3d(' + Math.round(-(1 - pr) * window.innerWidth * .25) + 'px,0,0)'; sw.under.style.filter = 'brightness(' + (.6 + pr * .4).toFixed(2) + ')'; }
        }, { passive: false, capture: true });
        var swEnd = function () {
            if (!sw) return;
            var s0 = sw; sw = null;
            if (!s0.on) return;
            var w = window.innerWidth, dt = Date.now() - s0.t, top = s0.top, under = s0.under, gh = s0.ghost;
            var go = s0.dx > w * .33 || (s0.dx > 60 && dt < 320);
            var ease = 'transform .24s cubic-bezier(.2,.8,.2,1)';
            top.style.transition = ease; if (under) under.style.transition = ease + ', filter .22s linear';
            if (!go) {
                top.style.transform = 'translate3d(0,0,0)';
                if (under) under.style.transform = 'translate3d(' + Math.round(-w * .25) + 'px,0,0)';
                setTimeout(function () { H.remove('atv-swiping', 'atv-swipe-ui'); if (s0.xd) { top.style.transition = ''; top.style.transform = ''; } else { layerOff(top); layerOff(under); } }, 260);
                return;
            }
            top.style.transform = 'translate3d(' + (w + 40) + 'px,0,0)';
            if (under) { under.style.transform = 'translate3d(0,0,0)'; under.style.filter = 'brightness(1)'; }
            setTimeout(function () {
                var oldPage = visiblePage();
                H.remove('atv-swiping');
                if (s0.xd) { H.remove('atv-swipe-ui'); goBack(); return; }                     // schließt die Detailansicht
                if (!gh) {                                                  // kein Abbild der vorigen Seite: normal laden (mit Ladekreis)
                    H.remove('atv-swipe-ui'); goBack();
                    setTimeout(function () { layerOff(top); layerOff(under); }, 80);
                    return;
                }
                H.add('atv-swap');
                goBack();
                layerOff(top); fixGhost(under);
                // Abbild der vorigen Seite bleibt, bis die echte Seite darunter fertig ist (gleiche Stelle, Bilder geladen)
                var t0 = Date.now(), okTicks = 0, lastH = 0;
                under.classList.add('atv-ghost--hold');                       // fängt Berührungen ab, bis die echte Seite steht
                var iv = setInterval(function () {
                    fixGhost(under);                                         // Klassen am <html> wechseln beim Zurückgehen: Abbild an Ort und Stelle halten
                    var np = visiblePage(), age = Date.now() - t0, late = age > 3000;
                    var there = np && pageKey() === gh.key && (np !== oldPage || age > 500);
                    if (!there && !late) return;
                    if (there && !late) {
                        if (Math.abs(window.scrollY - gh.y) > 2) { window.scrollTo(0, gh.y); okTicks = 0; if (Math.abs(window.scrollY - gh.y) > 2) return; }
                        if (!pageReady(np)) { okTicks = 0; return; }
                        // Seitenhöhe muss zur Ruhe gekommen sein (nachrückende Regale)
                        var dh = document.documentElement.scrollHeight; if (dh !== lastH) { lastH = dh; okTicks = 0; return; }
                        if (++okTicks < 4) return;
                    }
                    if (there && Math.abs(window.scrollY - gh.y) > 2) window.scrollTo(0, gh.y);   // auch im Zeitlimit: wenigstens an die alte Stelle
                    clearInterval(iv);
                    layerOff(under);
                    H.remove('atv-swipe-ui');
                    setTimeout(function () { H.remove('atv-swap'); }, 250);
                }, 50);
            }, 250);
        };
        document.addEventListener('touchend', swEnd, { passive: true, capture: true });
        document.addEventListener('touchcancel', swEnd, { passive: true, capture: true });
    }
    // Kategorie-Chips unter dem Hero: Movies · Shows · Sammlungen (aus den Bibliotheken des Benutzers)
    var chipViews = null, chipUser = null;
    var ICON_COLL = 'M4 6H2v14c0 1.1.9 2 2 2h14v-2H4V6zm16-4H8c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2zm-1 9H9V9h10v2zm-4 4H9v-2h6v2zm4-8H9V5h10v2z';
    function viewHref(v) {
        var sid = api().serverId(), q = 'topParentId=' + v.Id + '&serverId=' + sid;
        if (v.CollectionType === 'tvshows') return '#/tv?' + q + '&collectionType=tvshows';
        if (v.CollectionType === 'boxsets') return '#/boxsets?' + q + '&collectionType=boxsets';
        if (v.CollectionType === 'movies') return '#/movies?' + q + '&collectionType=movies';
        return '#/list?parentId=' + v.Id + '&serverId=' + sid;
    }
    function fillChips(nav, views) {
        nav.textContent = '';
        views.forEach(function (v) {
            var a = el('a', 'atv-chip'); a.href = viewHref(v);
            a.appendChild(svg(v.CollectionType === 'tvshows' ? ICON.tv : v.CollectionType === 'boxsets' ? ICON_COLL : ICON.film));
            a.appendChild(el('span', null, v.Name)); nav.appendChild(a);
        });
        nav.hidden = !views.length;
    }
    // Chips der Handy-Startseite stehen? (sonst rücken sie nach dem Erscheinen der Seite nach und alles springt)
    function homeChipsReady(page) { return !isMobile() || !!(page.querySelector('.atv-chips') && chipViews && chipUser === userId()); }
    /* ---------------- Update-Hinweis (nur für Admins, höchstens ein Abruf pro Tag) ---------------- */
    function verNewer(a, b) { var x = String(a).split('.'), y = String(b).split('.'); for (var i = 0; i < 3; i++) { var d = (parseInt(x[i], 10) || 0) - (parseInt(y[i], 10) || 0); if (d) return d > 0; } return false; }
    function updShow(info) {
        if (document.querySelector('.atv-update') || !info || !verNewer(info.version, VERSION)) return;
        var st = {}; try { st = JSON.parse(localStorage.getItem('atvUpdate') || '{}'); } catch (e) { /* leer */ }
        if (st.dismissed === info.version) return;
        var box = el('div', 'atv-update'); box.setAttribute('role', 'status');
        var a = el('a', 'atv-update__link'); a.href = 'https://github.com/' + CFG.repo; a.target = '_blank'; a.rel = 'noopener';
        a.appendChild(el('strong', null, lang() === 'en' ? 'Theme update available' : 'Theme-Update verfügbar'));
        a.appendChild(el('span', null, VERSION + ' → ' + info.version + (info.notes ? ' · ' + info.notes : '')));
        var x = el('button', 'atv-update__close'); x.type = 'button'; x.setAttribute('aria-label', t('close')); x.appendChild(svg(ICON.close));
        x.addEventListener('click', function () { st.dismissed = info.version; try { localStorage.setItem('atvUpdate', JSON.stringify(st)); } catch (e) { /* egal */ } box.remove(); });
        box.appendChild(a); box.appendChild(x); document.body.appendChild(box);
    }
    var updDone = false;
    function updCheck() {
        if (updDone || !CFG.updateCheck || !CFG.repo || !apiReady()) return; updDone = true;
        Promise.resolve(api().getCurrentUser()).then(function (u) {
            if (!u || !u.Policy || !u.Policy.IsAdministrator) return;
            var st = {}; try { st = JSON.parse(localStorage.getItem('atvUpdate') || '{}'); } catch (e) { /* leer */ }
            if (st.at && Date.now() - st.at < 86400000 && st.info) { updShow(st.info); return; }
            return fetch('https://raw.githubusercontent.com/' + CFG.repo + '/main/version.json', { credentials: 'omit', cache: 'no-store' })
                .then(function (r) { return r.ok ? r.json() : null; })
                .then(function (info) { if (!info || !info.version) return; st.at = Date.now(); st.info = { version: String(info.version), notes: String(info.notes || '').slice(0, 80) }; try { localStorage.setItem('atvUpdate', JSON.stringify(st)); } catch (e) { /* egal */ } updShow(st.info); });
        }).catch(function () { /* kein Netz zu GitHub: kein Hinweis */ });
    }
    function mountHomeChips(page) {
        var hero = page.querySelector('.atv-hero'); if (!hero || !hero.parentNode) return;
        var nav = page.querySelector('.atv-chips');
        if (nav && nav.previousElementSibling === hero) return;
        if (nav) nav.remove();
        nav = el('nav', 'atv-chips'); nav.setAttribute('aria-label', 'Bibliotheken'); nav.hidden = true;
        hero.parentNode.insertBefore(nav, hero.nextSibling);
        var uid = userId();
        if (chipViews && chipUser === uid) { fillChips(nav, chipViews); return; }
        api().getUserViews({}, uid).then(function (res) {
            chipUser = uid;
            chipViews = ((res && res.Items) || []).filter(function (v) { return ['movies', 'tvshows', 'boxsets', 'homevideos', 'musicvideos'].indexOf(v.CollectionType) >= 0; });
            if (nav.isConnected) fillChips(nav, chipViews);
        }).catch(function () {});
    }
    function mountTabbar(visiblePage) {
        var html = document.documentElement;
        var on = isMobile() && apiReady() && visiblePage && !visiblePage.classList.contains('standalonePage') && !visiblePage.classList.contains('videoOsdPage') && !visiblePage.classList.contains('type-interior') && !/#\/(login|selectserver|wizard|video|queue)/.test(location.hash);
        // Eigene mobile Kopfzeile (Zurück links, Suche + Profil rechts): unabhängig von den Kopfzeilen-Knöpfen der Jellyfin-App
        if (on) ensureMobileBar();
        html.classList.toggle('atv-mbar-on', !!on);
        // Keine eigene Tab-Leiste mehr (Navigation = Kategorie-Chips auf der Startseite + Suche)
        if (on && visiblePage.classList.contains('homePage')) mountHomeChips(visiblePage);
        html.classList.remove('atv-tabbar-on'); if (tabbar.root) { tabbar.root.remove(); tabbar.root = null; }
        return;
        // Hat das Branding-CSS Jellyfins Kopfzeilen-Tabs schon als Leiste nach unten gelegt, keine zweite Leiste
        if (on) {
            ['header .MuiStack-root', '.mainDrawer', '.navDrawer', '.headerTabs', '.sectionTabs'].forEach(function (sel) {
                var e = document.querySelector(sel); if (!e) return;
                var r = e.getBoundingClientRect(), cs = getComputedStyle(e);
                if (cs.position === 'fixed' && cs.visibility !== 'hidden' && r.height > 0 && r.height < 120) on = false;   // Branding-CSS liefert die Leiste (jetzt oben)
            });
        }
        html.classList.toggle('atv-tabbar-on', !!on);
        if (!on) return;
        if (!tabbar.root) {
            tabbar.root = el('nav', 'atv-tabbar'); tabbar.root.setAttribute('aria-label', 'Tabs');
            var caps = el('div', 'atv-tabbar__caps');
            caps.appendChild(tabLink('#/home.html', ICON.home, t('home')));
            caps.appendChild(tabLink('#/home.html', ICON.film, t('movies')));
            caps.appendChild(tabLink('#/home.html', ICON.tv, t('shows')));
            tabbar.root.appendChild(caps);
            var s = el('div', 'atv-tabbar__search'); s.appendChild(tabLink('#/search.html', ICON.search, ''));
            tabbar.root.appendChild(s);
            document.body.appendChild(tabbar.root);
        }
        var links = tabbar.root.querySelectorAll('a');
        var uid = userId();
        if (tabbar.viewsUser !== uid) {
            tabbar.viewsUser = uid;
            api().getUserViews({}, uid).then(function (res) {
                var views = (res && res.Items) || [];
                var mv = views.filter(function (v) { return v.CollectionType === 'movies'; })[0];
                var tv = views.filter(function (v) { return v.CollectionType === 'tvshows'; })[0];
                var sid = api().serverId();
                links[1].href = mv ? '#/movies.html?topParentId=' + mv.Id + '&serverId=' + sid : '#/home.html';
                links[2].href = tv ? '#/tv.html?topParentId=' + tv.Id + '&serverId=' + sid : '#/home.html';
                tabbar.views = { movies: mv, tv: tv };
                markActive(links);
            }).catch(function () {});
        }
        markActive(links);
    }
    function markActive(links) {
        var h = location.hash;
        links.forEach(function (l) { l.classList.remove('is-active'); });
        if (/#\/search/.test(h)) links[3].classList.add('is-active');
        else if (/#\/movies\.html/.test(h) || (tabbar.views && tabbar.views.movies && h.indexOf(tabbar.views.movies.Id) >= 0)) links[1].classList.add('is-active');
        else if (/#\/tv\.html/.test(h) || (tabbar.views && tabbar.views.tv && h.indexOf(tabbar.views.tv.Id) >= 0)) links[2].classList.add('is-active');
        else if (/#\/home/.test(h) || h === '' || h === '#/' || h === '#') links[0].classList.add('is-active');
    }

    /* =====================================================================
       6. Suche (#/search): leeres Feld → Vorschlags-Pillen + Regal „Zuletzt
       hinzugefügt“ (statt Jellyfins Namensliste); Treffer-Anzahl je Abschnitt;
       „Keine Ergebnisse“. Live-Suche beim Tippen macht Jellyfin selbst.
       ===================================================================== */
    var searchCache = { user: null, time: 0, items: null };
    function searchLoad() {
        var uid = userId(), c = searchCache;
        if (c.items && c.user === uid && Date.now() - c.time < CFG.cacheMs) return Promise.resolve(c.items);
        return api().getItems(uid, { IncludeItemTypes: 'Movie,Series', Recursive: true, SortBy: 'DateCreated', SortOrder: 'Descending', Limit: 18, ImageTypes: 'Primary', EnableImageTypes: 'Primary', Fields: 'ProductionYear' })
            .then(function (res) { var items = (res && res.Items) || []; searchCache = { user: uid, time: Date.now(), items: items }; return items; });
    }
    function searchCard(item) {
        var a = el('a', 'atv-search__card'); a.href = '#/details?id=' + item.Id + '&serverId=' + api().serverId();
        a.setAttribute('aria-label', item.Name);
        var im = el('img'); im.alt = ''; im.loading = 'lazy'; var src = primaryUrl(item, 400); if (src) im.src = src;
        a.appendChild(im); a.appendChild(el('span', null, item.Name));
        return a;
    }
    function mountSearch(page) {
        var input = page.querySelector('.searchfields-txtSearch');
        var fields = page.querySelector('.searchFields');
        if (!input || !fields || !apiReady()) return;
        if (page.__atvSearch) { searchUpdate(page); return; }
        page.__atvSearch = true;
        var box = el('section', 'atv-search');
        box.appendChild(el('h2', 'atv-search__title', t('suggestions')));
        var pills = el('div', 'atv-search__pills'); box.appendChild(pills);
        box.appendChild(el('h2', 'atv-search__title', t('recentlyAdded')));
        var grid = el('div', 'atv-search__grid'); box.appendChild(grid);
        var empty = el('p', 'atv-search__empty'); empty.hidden = true;
        fields.parentNode.insertBefore(box, fields.nextSibling);
        fields.parentNode.insertBefore(empty, box.nextSibling);
        page.__atvSearchEls = { box: box, empty: empty, input: input };
        searchLoad().then(function (items) {
            items.slice(0, 8).forEach(function (it) {
                var b = el('button', 'atv-search__pill'); b.type = 'button';
                b.appendChild(svg(ICON.search)); b.appendChild(el('span', null, it.Name));
                b.addEventListener('click', function () {
                    input.value = it.Name;
                    input.dispatchEvent(new Event('input', { bubbles: true }));
                    input.dispatchEvent(new Event('change', { bubbles: true }));
                    input.focus();
                });
                pills.appendChild(b);
            });
            items.forEach(function (it) { grid.appendChild(searchCard(it)); });
        }).catch(function () {});
        input.addEventListener('input', function () { searchUpdate(page); });
        var results = page.querySelector('.searchResults');
        if (results) {
            var mo = new MutationObserver(function () { clearTimeout(page.__atvSearchT); page.__atvSearchT = setTimeout(function () { searchUpdate(page); }, 400); });
            mo.observe(results, { childList: true, subtree: true, attributes: true, attributeFilter: ['class'] });
        }
        searchUpdate(page);
    }
    // Treffer aus der Mediathek unter denen der eigenen Bibliothek (nur mit dem Plugin „Mediathek“)
    function searchMediathek(page, q) {
        var els = page.__atvSearchEls; if (!els) return;
        tvProbe();
        if (!els.media) { els.media = el('section', 'atv-tv__results atv-search__media'); els.empty.parentNode.appendChild(els.media); }
        if (els.mediaQuery === q) return;
        els.mediaQuery = q;
        clearTimeout(els.mediaTimer);
        els.media.textContent = '';
        page.classList.remove('atv-search-hasmedia');
        if (!tv.ok || q.length < 2) return;
        els.mediaTimer = setTimeout(function () {
            var a = api();
            a.getJSON(a.getUrl('Mediathek/search', { q: q })).then(function (r) {
                if (els.mediaQuery !== q || !els.media.isConnected) return;
                var items = (r && r.Items) || [];
                if (!items.length) return;
                els.media.appendChild(el('h2', 'atv-tv__title', tvText('Aus der Mediathek', 'From the Mediathek')));
                var grid = el('div', 'atv-tv__grid');
                items.forEach(function (it) { grid.appendChild(tvCard(it)); });
                els.media.appendChild(grid);
                page.classList.add('atv-search-hasmedia');
                searchUpdate(page);
            }).catch(function () { /* ohne Mediathek-Treffer bleibt die Suche, wie sie ist */ });
        }, 600);
    }
    function searchUpdate(page) {
        var els = page.__atvSearchEls; if (!els) return;
        var q = (els.input.value || '').trim();
        els.box.hidden = !!q;
        searchMediathek(page, q);
        var results = page.querySelector('.searchResults');
        var visible = 0;
        if (results) {
            results.querySelectorAll('.verticalSection').forEach(function (sec) {
                var title = sec.querySelector('.sectionTitle');
                var n = sec.querySelectorAll('.card').length;
                if (title) {
                    var c = title.querySelector('.atv-search-count');
                    if (!c) { c = el('span', 'atv-search-count'); title.appendChild(c); }
                    c.textContent = n ? String(n) : '';
                }
                if (n && !sec.classList.contains('hide') && sec.offsetParent !== null) visible++;
            });
        }
        var searching = results && !results.classList.contains('hide') && results.querySelector('.card');
        els.empty.hidden = !(q && q.length >= 2 && !visible && !searching && page.__atvSearchSettled === q);
        // „Keine Ergebnisse“ erst, wenn Jellyfin für diesen Text fertig ist (400 ms Ruhe nach letzter Änderung)
        clearTimeout(page.__atvSearchSettleT);
        page.__atvSearchSettleT = setTimeout(function () {
            page.__atvSearchSettled = q;
            var vis = 0; if (results) results.querySelectorAll('.verticalSection').forEach(function (s) { if (s.querySelectorAll('.card').length && !s.classList.contains('hide')) vis++; });
            els.empty.textContent = t('noResults', q);
            els.empty.hidden = !(q.length >= 2 && !vis);
        }, 900);
    }

    /* =====================================================================
       Weiterschauen (Startseite): pro Serie nur die zuletzt gesehene Folge;
       Klicks regelt der Abschnitt „Startseite: Klick auf das Bild …“ weiter oben:
       Bild und ▶-Zeile = abspielen, Titelzeile mit „›“ = Serie bzw. Film öffnen.
       ===================================================================== */
    function cardLines(card) {
        var a = card.querySelectorAll('.cardText a.textActionButton');
        return a;
    }
    function tidyShelves(page) {
        var seen = {};
        page.querySelectorAll('.card[data-type="Episode"]').forEach(function (card) {
            var sa = card.querySelector('.cardText-first a[data-type="Series"]');
            var sid = sa && sa.getAttribute('data-id');
            if (!sid || !card.querySelector('.itemProgressBar')) return;     // nur Fortsetzen-Karten (mit Fortschritt)
            var sc = card.closest('.itemsContainer') || card.parentNode;
            var key = (sc.__atvId = sc.__atvId || ('s' + Math.random().toString(36).slice(2))) + sid;
            if (seen[key]) card.style.display = 'none'; else { seen[key] = 1; card.style.display = ''; }
        });
    }
    /* =====================================================================
       Router: Seite erkennen, Module anstoßen, Aufräumen
       ===================================================================== */
    var lastHash = null;
    function visiblePage() {
        var pages = document.querySelectorAll('.mainAnimatedPage, .page.standalonePage');
        for (var i = pages.length - 1; i >= 0; i--) if (!pages[i].classList.contains('hide') && !pages[i].closest('.atv-ghost')) return pages[i];
        return null;
    }
    function ensureRootClass() { var h = document.documentElement; if (!h.classList.contains('atv-js')) h.classList.add('atv-js'); }
    /* =====================================================================
       Player: Titelblock über dem Zeitbalken (klein: Folge, groß: Serie bzw. Film)
       ===================================================================== */
    var osd = { key: '', busy: false };
    // Während der Vorspann läuft, den folgenden Titel „anwärmen“: Abspiel-Infos abrufen und die ersten Megabyte der Datei
    // lesen. Das weckt schlafende Festplatten und füllt den Cache des Servers, sodass der Film nach dem Vorspann schneller
    // startet. (Den eigentlichen Stream baut Jellyfin danach selbst auf; der lässt sich nicht vorab starten.)
    // Bild des folgenden Films für die Lücke nach dem Vorspann (statt Schwarz): während des Vorspanns schon laden
    var gapArt = { id: null, url: null };
    function gapPrepare(id) {
        if (gapArt.id === id) return; gapArt = { id: id, url: null };
        api().getItem(userId(), id).then(function (it) {
            var u = backdropUrl(it) || primaryUrl(it, 1200); if (!u || gapArt.id !== id) return;
            var im = new Image(); im.decoding = 'async'; im.onload = function () { if (gapArt.id === id) gapArt.url = u; }; im.src = u;
        }).catch(function () {});
    }
    var warmed = {};
    function warmUp(id) {
        if (!id || warmed[id]) return; warmed[id] = 1;
        try {
            var a = api(), hd = { 'Authorization': 'MediaBrowser Token="' + a.accessToken() + '"' };
            fetch(a.getUrl('Items/' + id + '/PlaybackInfo', { UserId: userId() }), { headers: hd, credentials: 'omit' }).catch(function () {});
            var h2 = { 'Authorization': hd.Authorization, 'Range': 'bytes=0-4194303' };
            fetch(a.getUrl('Videos/' + id + '/stream', { static: true, mediaSourceId: id }), { headers: h2, credentials: 'omit' })
                .then(function (r) { return r.ok ? r.arrayBuffer() : null; }).catch(function () {});
        } catch (e) { /* nur eine Beschleunigung */ }
    }
    function osdTitleTick() {
        var ctl = document.querySelector('.videoOsdBottom .osdControls');
        var onVideo = !!ctl && /#\/video/.test(location.hash);
        document.documentElement.classList.toggle('atv-osd-on', onVideo);
        if (!onVideo) { osd.key = ''; return; }
        var hdr = document.querySelector('.videoOsd-appBar p, .osdHeader .pageTitle'), key = hdr ? hdr.textContent : '';
        var box = ctl.querySelector('.atv-osd-title');
        if (box && key === osd.key) return;
        if (osd.busy || !apiReady()) return;
        osd.busy = true;
        var a = api(), did = typeof a.deviceId === 'function' ? a.deviceId() : null;
        var p = typeof a.getSessions === 'function' ? a.getSessions(did ? { deviceId: did } : {}) : Promise.resolve([]);
        Promise.resolve(p).then(function (list) {
            osd.busy = false;
            var se0 = null; (list || []).forEach(function (se) { if (!se0 && se.NowPlayingItem && (!did || se.DeviceId === did)) se0 = se; });
            var it = se0 ? se0.NowPlayingItem : null;
            if (it && it.Id) skipLoad(it.Id);
            var render = function (it) {
                var c2 = document.querySelector('.videoOsdBottom .osdControls'); if (!c2) return;
                var big = '', small = '';
                if (it && it.Type === 'Episode') {
                    big = it.SeriesName || it.Name;
                    small = (it.ParentIndexNumber != null ? t('s') + it.ParentIndexNumber + ' · ' : '') + (it.IndexNumber != null ? t('ep') + it.IndexNumber + ' · ' : '') + it.Name;
                } else if (it) { big = it.Name; small = it.ProductionYear ? String(it.ProductionYear) : ''; }
                else { big = key.replace(/\s*\(\d{4}\)\s*$/, ''); }
                var b2 = c2.querySelector('.atv-osd-title') || c2.insertBefore(el('div', 'atv-osd-title'), c2.firstChild);
                b2.replaceChildren(); if (small) b2.appendChild(el('div', 'atv-osd-title__sub', small)); if (big) b2.appendChild(el('div', 'atv-osd-title__main', big));
                osd.key = key; document.documentElement.classList.add('atv-osd-titled');
            };
            // Vorspann (Local Intros u. ä.: Typ „Video“ vor dem eigentlichen Titel): keinen Titel zeigen
            var intro = on('introTransition') && it && it.Type !== 'Movie' && it.Type !== 'Episode';
            document.documentElement.classList.toggle('atv-osd-intro', !!intro);
            if (intro) {
                // Welcher Titel folgt? Zuerst der, den das Theme gerade gestartet hat (sicher). Die Warteschlange, die der Server
                // für das Gerät führt, kann von einer früheren Wiedergabe stammen – nur als Rückfall und nur bei exaktem Treffer.
                var nextId = lastPlay && lastPlay.id !== it.Id && Date.now() - lastPlay.at < 600000 ? lastPlay.id : null;
                if (!nextId && se0.PlaylistItemId) {
                    var q = se0.NowPlayingQueue || [];
                    for (var i = 0; i < q.length - 1; i++) if (q[i].PlaylistItemId === se0.PlaylistItemId && q[i].Id === it.Id) { nextId = q[i + 1].Id; break; }
                }
                // während des Vorspanns steht unten nichts (weder der Dateiname des Vorspanns noch der kommende Titel)
                if (nextId) { warmUp(nextId); gapPrepare(nextId); }
                render({ Name: '' });
                return;
            }
            render(it);
        }).catch(function () { osd.busy = false; });
    }
    setInterval(osdTitleTick, 1200);

    /* Überspringen-Knopf (Vorspann/Abspann): Jellyfin zeigt ihn nur, solange die Steuerleiste offen ist, und setzt ihn auf eine
       feste Höhe – im Querformat am Handy liegt er dann auf den Knöpfen. Hier: sichtbar, solange der Abschnitt läuft, und
       immer knapp über der Steuerleiste (ohne Leiste unten in der Ecke). */
    var skip = { id: null, segs: [] };
    function skipLoad(id) {
        if (skip.id === id) return; skip.id = id; skip.segs = [];
        Promise.resolve(api().getJSON(api().getUrl('MediaSegments/' + id))).then(function (r) {
            if (skip.id !== id) return;
            skip.segs = ((r && r.Items) || []).filter(function (x) { return x.Type === 'Intro' || x.Type === 'Outro'; });
        }).catch(function () { /* ohne Abschnitte bleibt es bei Jellyfins Verhalten */ });
    }
    function skipTick() {
        var H = document.documentElement;
        if (!/#\/video/.test(location.hash)) { if (H.classList.contains('atv-skip-on')) H.classList.remove('atv-skip-on'); return; }
        var osdB = document.querySelector('.videoOsdBottom'), ctl = osdB && !osdB.classList.contains('hide') ? osdB.querySelector('.osdControls') : null, bottom = 28;
        if (ctl) { var r = ctl.getBoundingClientRect(); if (r.height) bottom = Math.max(28, Math.round(window.innerHeight - r.top + 12)); }
        if (skip.bottom !== bottom) { skip.bottom = bottom; H.style.setProperty('--atv-skip-bottom', bottom + 'px'); }
        var v = document.querySelector('video.htmlvideoplayer, .videoPlayerContainer video'), t = v ? v.currentTime * 1e7 : -1, inSeg = false;
        for (var i = 0; i < skip.segs.length; i++) if (t >= skip.segs[i].StartTicks && t < skip.segs[i].EndTicks - 1e7) { inSeg = true; break; }
        var btn = document.querySelector('.skip-button-container .skip-button');
        H.classList.toggle('atv-skip-on', inSeg && !!btn && !!btn.textContent);
    }
    setInterval(skipTick, 400);
    // Zwischen zwei Titeln (Vorspann → Film) blieb das letzte Bild des Vorspanns stehen: Video ausblenden, sobald ein
    // Titel endet, und erst wieder zeigen, wenn der nächste wirklich läuft (dazwischen Schwarz bzw. Jellyfins Filmbild).
    if (!window.__atvVidGap) {
        window.__atvVidGap = true;
        var gapT = null, gapOff = function () {
            clearTimeout(gapT); document.documentElement.classList.remove('atv-vid-gap');
            document.querySelectorAll('.atv-gap-art').forEach(function (n) { n.classList.add('is-out'); setTimeout(function () { n.remove(); }, 450); });
            gapArt = { id: null, url: null };
        };
        document.addEventListener('ended', function (e) {
            if (!e.target || e.target.tagName !== 'VIDEO' || !e.target.closest('.videoPlayerContainer')) return;
            document.documentElement.classList.add('atv-vid-gap'); clearTimeout(gapT); gapT = setTimeout(gapOff, 8000);
            // Vorspann zu Ende: Bild des Films zeigen, bis er läuft
            if (gapArt.url && document.documentElement.classList.contains('atv-osd-intro') && !document.querySelector('.atv-gap-art')) {
                var ga = el('div', 'atv-gap-art'); ga.style.backgroundImage = 'url("' + gapArt.url + '")';
                (e.target.closest('.videoPlayerContainer') || document.body).appendChild(ga);
            }
        }, true);
        ['playing', 'timeupdate'].forEach(function (ev) {
            document.addEventListener(ev, function (e) { if (e.target && e.target.tagName === 'VIDEO' && !e.target.ended && e.target.currentTime > 0.05 && document.documentElement.classList.contains('atv-vid-gap')) gapOff(); }, true);
        });
    }

    /* =====================================================================
       Player verlassen: zurück auf die Seite, von der aus gestartet wurde.
       In der App folgt auf das Schließen manchmal ein zweites „Zurück“ (Schließen-Knopf
       und Wiedergabe-Ende lösen beide eines aus) – dann landet man eine Seite zu weit
       (Startseite statt Serie). Dieses zweite Zurück wird rückgängig gemacht.
       ===================================================================== */
    if (!window.__atvPlayBack) {
        window.__atvPlayBack = true;
        var pb = { origin: null, prev: location.hash, armed: 0, touch: 0 };
        var pbBase = function (h) { return (h || '').split('&atvPlay')[0]; };
        try {
            var pbPush = window.history.pushState;
            window.history.pushState = function () {
                var before = location.hash, r = pbPush.apply(this, arguments);
                if (/^#\/video/.test(location.hash) && !/^#\/video/.test(before)) pb.origin = pbBase(before);
                pb.prev = location.hash;
                return r;
            };
        } catch (e) { /* ohne Schutz */ }
        document.addEventListener('touchstart', function () { pb.touch = Date.now(); }, { passive: true, capture: true });
        document.addEventListener('mousedown', function () { pb.touch = Date.now(); }, true);
        window.addEventListener('popstate', function () {
            var now = Date.now(), cur = location.hash, was = pb.prev; pb.prev = cur;
            if (/^#\/video/.test(was) && !/^#\/video/.test(cur)) {
                if (pb.origin && pbBase(cur) === pb.origin) pb.armed = now;           // richtig gelandet: kurz aufpassen
                return;
            }
            if (pb.armed && now - pb.armed < 1500 && pb.touch < pb.armed && pb.origin && pbBase(was) === pb.origin && pbBase(cur) !== pb.origin) {
                pb.armed = 0;
                try { window.history.forward(); } catch (e) { /* dann eben nicht */ }
                return;
            }
            if (now - pb.armed >= 1500) pb.armed = 0;
        });
    }

    /* =====================================================================
       Seitenwechsel: neue Seite erst zeigen, wenn sie fertig aufgebaut ist
       (Hero, Regale, Folgen, Infoblock stehen) – bis dahin Ladekreis. So klappt
       nach dem Erscheinen nichts mehr nach. Bilder dürfen nachladen.
       ===================================================================== */
    var gate = { key: null, on: false, t0: 0, lastMut: 0, old: null, iv: null };
    function gateSkip() {
        var h = document.documentElement.classList;
        return h.contains('atv-swiping') || h.contains('atv-swap') || /^#\/(video|queue)/.test(location.hash) || !!document.querySelector('.atv-xd');
    }
    function gateReady() {
        var now = Date.now(), age = now - gate.t0;
        if (age > 3000) return true;                                        // nie länger als 3 s zurückhalten
        var page = visiblePage(); if (!page) return false;
        if (page === gate.old && age < 700) return false;                   // noch die alte Seite
        if (page.classList.contains('itemDetailPage')) {
            if (!page.classList.contains('atv-native')) {
                if (page.__atvDetailId !== hashParam('id')) return false;
                if (!page.querySelector('.atv-detail, .atv-person')) return false;
                if (page.querySelector('.atv-episodes') && !page.querySelector('.atv-ep, .atv-shelf__empty')) return false;
            }
        } else if (page.classList.contains('homePage')) {
            if (!page.classList.contains('atv-native') && (!page.querySelector('.atv-hero') || !homeChipsReady(page))) return false;
            if (!page.querySelector('.homeSectionsContainer .card') && age < 1500) return false;
        } else if (page.classList.contains('withTabs') || /^(movies|tv|boxsets|list|music)/.test(pageKey())) {
            if (!page.querySelector('.card, .listItem') && age < 1500) return false;
        }
        // und der Aufbau ist zur Ruhe gekommen (Detailseiten: Grundgerüst ist oben schon geprüft, kurze Ruhe genügt)
        return now - gate.lastMut > (page.classList.contains('itemDetailPage') ? 150 : 320);
    }
    function gateEnd() {
        if (!gate.on) return;
        gate.on = false; clearInterval(gate.iv); gate.old = null;
        var h = document.documentElement.classList;
        h.remove('atv-gate'); h.add('atv-gate-out');
        setTimeout(function () { h.remove('atv-gate-out'); }, 260);
    }
    function gateCheck() {
        var key = pageKey();
        if (key === gate.key) return;
        gate.key = key;
        if (gateSkip()) { gateEnd(); return; }
        if (!document.getElementById('atv-gate-spin') && document.body) { var sp = el('div'); sp.id = 'atv-gate-spin'; sp.setAttribute('aria-hidden', 'true'); document.body.appendChild(sp); }
        gate.old = gate.on ? gate.old : visiblePage();
        gate.on = true; gate.t0 = gate.lastMut = Date.now();
        document.documentElement.classList.remove('atv-gate-out'); document.documentElement.classList.add('atv-gate');
        clearInterval(gate.iv);
        gate.iv = setInterval(function () { if (gateSkip() || gateReady()) gateEnd(); }, 60);
    }
    if (!window.__atvGate && on('pageGate')) {
        window.__atvGate = true;
        try {
            var gPush = window.history.pushState, gRepl = window.history.replaceState;
            window.history.pushState = function () { var r = gPush.apply(this, arguments); gateCheck(); return r; };
            window.history.replaceState = function () { var r = gRepl.apply(this, arguments); gateCheck(); return r; };
        } catch (e) { /* ohne Verlaufshaken: nur hashchange/popstate */ }
        window.addEventListener('popstate', gateCheck); window.addEventListener('hashchange', gateCheck);
        var gateMo = new MutationObserver(function (muts) {
            if (!gate.on) return;
            for (var i = 0; i < muts.length; i++) { var tg = muts[i].target; if (tg.nodeType === 1 && tg.id !== 'atv-gate-spin' && tg.closest && tg.closest('.mainAnimatedPage, header.MuiAppBar-root')) { gate.lastMut = Date.now(); return; } }
        });
        var gateStart = function () { if (!document.body) { setTimeout(gateStart, 50); return; } gateMo.observe(document.body, { childList: true, subtree: true }); gateCheck(); };
        gateStart();
    }

    // Seitentyp als Klasse am <html>: sofort beim Seitenwechsel (nicht erst nach der Entprellung), sonst steht die neue
    // Seite ~150–300 ms mit den Regeln der alten da und springt dann um („Zittern“ beim Wechsel Start → Sammlungen).
    function syncClasses() {
        var page = visiblePage(), h = document.documentElement;
        if (page && ghosts.length && !document.documentElement.classList.contains('atv-swiping')) { var k0 = pageKey(); while (ghosts.length && ghosts[ghosts.length - 1].key === k0 && !ghosts[ghosts.length - 1].node.isConnected) ghosts.pop(); }
        h.classList.toggle('atv-page-home', !!(page && page.classList.contains('homePage')));
        h.classList.toggle('atv-page-detail', !!(page && page.classList.contains('itemDetailPage')));
        h.classList.toggle('atv-pg-list', !!(page && !page.classList.contains('homePage') && !page.classList.contains('itemDetailPage') && page.classList.contains('withTabs')));
        h.classList.toggle('atv-pg-search', !!(page && page.id === 'searchPage'));
        h.classList.toggle('atv-pg-settings', !!(page && /userPreferencesPage|myPreferences/.test(page.className + ' ' + page.id)));
        return page;
    }
    /* Regale von Jellyfin-Web (emby-scroller) mit dem Trackpad. Außer in Firefox verschiebt Jellyfin sie per Skript:
       jedes Wischereignis zählt doppelt und startet eine neue Übergangsanimation, ein Teil der Bewegung kommt zusätzlich bei
       der Seite an. Das Ergebnis ruckt, zieht nach und wirkt doppelt. Hier folgt das Regal stattdessen dem Finger eins zu
       eins und ohne Übergang; Jellyfins eigene Behandlung bekommt die Ereignisse nicht mehr. Pfeilknöpfe und Tastatur
       laufen weiter über Jellyfin. */
    function onShelfWheel(ev) {
        var el = ev.currentTarget, s = el.scroller, slider = el.querySelector('.scrollSlider');
        if (ev.ctrlKey || !s || !s._pos || !slider || !slider.classList.contains('animatedScrollX') || document.dir === 'rtl') return;
        var ax = Math.abs(ev.deltaX), ay = Math.abs(ev.deltaY);
        if (ax <= ay) { if (ax) ev.stopImmediatePropagation(); return; }   // senkrecht: die Seite scrollt, das Regal bleibt stehen
        var end = Math.max(Math.max(slider.offsetWidth, slider.scrollWidth) - el.offsetWidth, s._pos.end || 0, 0);   // Jellyfins eigenes Ende gilt mit
        if (!end) return;
        ev.stopImmediatePropagation();
        var cur = s._pos.cur || 0, pos = Math.min(Math.max(cur + (ev.deltaMode === 1 ? ev.deltaX * 40 : ev.deltaX), 0), end);
        // Am Rand bleibt das Ereignis im Browser unverbraucht (Zurückwischen). In der App gibt es das nicht; dort würde nur
        // die ganze Seite seitlich nachfedern.
        if (pos === cur) { if (ev.cancelable && document.documentElement.classList.contains('atv-native')) ev.preventDefault(); return; }
        if (ev.cancelable) ev.preventDefault();
        s._pos.cur = s._pos.dest = pos;
        slider.style.transition = 'none';
        slider.style.transform = 'translateX(' + (-Math.round(pos)) + 'px)';
        el.dispatchEvent(new CustomEvent('scrollanimate', { bubbles: true, cancelable: false }));
    }
    /* Die Seite selbst darf nie seitlich verschoben sein: Regale stehen weit über den rechten Rand hinaus, und der Browser
       rollt dorthin, wenn etwa eine verdeckte Karte den Fokus bekommt. Dann wäre alles – auch das Hero-Banner – nach links
       weggeschoben. Passiert es, geht es sofort zurück. */
    function keepPageLeft() {
        var d = document.scrollingElement || document.documentElement, b = document.body;
        if (window.scrollX || (d && d.scrollLeft)) window.scrollTo(0, window.scrollY);
        if (b && b.scrollLeft) b.scrollLeft = 0;
    }
    window.addEventListener('scroll', keepPageLeft, { passive: true, capture: true });
    function bindShelfWheel() {
        if (!on('shelfWheel')) return;
        var list = document.querySelectorAll('.emby-scroller:not([data-atv-wheel])');
        for (var i = 0; i < list.length; i++) {
            list[i].setAttribute('data-atv-wheel', '1');
            list[i].addEventListener('wheel', onShelfWheel, { capture: true, passive: false });
        }
    }
    /* =====================================================================
       TV: laufendes Programm und Mediathek auf einer Seite
       Gibt es auf dem Server das Plugin „Mediathek“, ersetzt diese Seite die Startseite des Live-TV und die oberste Ebene des
       Mediathek-Kanals: oben das laufende Programm der Sender, darunter die Reihen der Mediathek, dazu eine Suche. Ein Klick
       auf einen Sender oder ein Video spielt sofort ab; nur Serien öffnen ihre Seite. Ohne das Plugin bleibt alles, wie
       Jellyfin es zeigt.
       ===================================================================== */
    var tv = { probed: false, ok: false, channelId: null, rows: [], root: null, timer: null, searchSeq: 0 };
    function tvText(de, en) { return lang() === 'en' ? en : de; }
    function tvProbe() {
        if (tv.probed || !apiReady()) return;
        tv.probed = true;
        var a = api();
        a.getJSON(a.getUrl('Mediathek/home')).then(function (d) {
            tv.rows = (d && d.rows) || [];
            return a.getJSON(a.getUrl('Channels', { userId: userId() }));
        }).then(function (r) {
            var c = ((r && r.Items) || []).filter(function (i) { return i.Name === 'Mediathek'; })[0];
            tv.channelId = c ? c.Id : null;
            tv.ok = !!c;
            schedule();
        }).catch(function () { tv.ok = false; });
    }
    function tvHubHash() { return tv.channelId ? '#/list?parentId=' + tv.channelId + '&serverId=' + api().serverId() : '#/livetv?collectionType=livetv'; }
    // Die TV-Seite liegt auf der Startseite des Live-TV (erster Reiter) und auf der obersten Ebene des Mediathek-Kanals.
    function tvIsHub() {
        var h = location.hash;
        if (/^#\/livetv/.test(h)) return !/[?&]tab=[1-9]/.test(h);
        return !!tv.channelId && h.indexOf('parentId=' + tv.channelId) >= 0;
    }
    // Kopfzeile: ein Eintrag „TV“ hinter den Bibliotheken statt „Live TV“ und „Mediathek“ nebeneinander.
    function tvNav() {
        var live = document.querySelectorAll('header .MuiStack-root a[href*="#/livetv"]');
        var media = tv.channelId ? document.querySelectorAll('header .MuiStack-root a[href*="parentId=' + tv.channelId + '"]') : [];
        var active = tv.ok && tvIsHub();
        [].forEach.call(live, function (l) { l.classList.toggle('atv-nav-hide', tv.ok && media.length > 0); });
        [].forEach.call(media, function (l) { l.classList.toggle('atv-nav-tv', tv.ok); l.classList.toggle('atv-nav-on', active); });
        document.documentElement.classList.toggle('atv-pg-tv', active);
    }
    function tvTime(iso) { var d = new Date(iso); return isNaN(d) ? '' : ('0' + d.getHours()).slice(-2) + ':' + ('0' + d.getMinutes()).slice(-2); }
    function tvPlay(item) {
        if (playDirect(item, 0)) return;
        if (window.Emby && window.Emby.Page) window.Emby.Page.show('/details?id=' + item.Id + '&serverId=' + api().serverId());
    }
    function tvOpen(item) {
        var folder = item.Type === 'Series' || item.Type === 'Season' || item.IsFolder;
        if (!folder) { tvPlay(item); return; }
        tvSeries(item);
    }
    // Inhalt eines Kanal-Ordners (Serie, Staffel). Jellyfins Serienseite kennt nur Serien der Bibliothek; Serien aus einem
    // Kanal haben ihre Staffeln und Folgen als Ordnerinhalt.
    function tvChildren(id) {
        var a = api();
        return a.getJSON(a.getUrl('Channels/' + tv.channelId + '/Items', { userId: userId(), folderId: id, limit: 400, fields: 'Overview,PrimaryImageAspectRatio' }))
            .then(function (r) { return (r && r.Items) || []; });
    }
    function tvEpisodeOrder(x, y) {
        var a1 = x.ParentIndexNumber || 0, b1 = y.ParentIndexNumber || 0; if (a1 !== b1) return a1 - b1;
        if (x.IndexNumber != null && y.IndexNumber != null && x.IndexNumber !== y.IndexNumber) return x.IndexNumber - y.IndexNumber;
        return String(y.PremiereDate || '').localeCompare(String(x.PremiereDate || ''));   // ohne Folgennummer: neueste zuerst
    }
    // Serienansicht innerhalb der TV-Seite: Plakat, Beschreibung, Staffeln zur Wahl, Folgen als Karten.
    function tvSeries(item) {
        var hub = tv.root;
        if (!hub || !hub.isConnected) {   // von der Suchseite aus: erst zur TV-Seite, dort öffnet sich die Serie
            tv.pendingSeries = item;
            if (window.Emby && window.Emby.Page) window.Emby.Page.show(tvHubHash().slice(1)); else location.hash = tvHubHash();
            return;
        }
        var d = hub.detail; d.textContent = '';
        hub.classList.add('is-detail');
        window.scrollTo(0, 0);
        var head = el('div', 'atv-tv__dhead');
        var back = el('button', 'atv-pill atv-tv__back'); back.type = 'button'; back.appendChild(svg(ICON.prev)); back.appendChild(el('span', null, tvText('Zurück', 'Back')));
        back.addEventListener('click', function () { hub.classList.remove('is-detail'); d.textContent = ''; });
        var tag = item.ImageTags && item.ImageTags.Primary;
        if (tag) { var img = el('img', 'atv-tv__dposter'); img.alt = ''; img.src = imgUrl(item.Id, { type: 'Primary', maxWidth: 480, tag: tag, quality: 88 }); head.appendChild(img); }
        var info = el('div', 'atv-tv__dinfo');
        info.appendChild(back);
        info.appendChild(el('h1', 'atv-tv__herotitle', item.Name));
        if (item.Overview) info.appendChild(el('p', 'atv-tv__dtext', item.Overview));
        var actions = el('div', 'atv-tv__heroactions'); info.appendChild(actions);
        head.appendChild(info); d.appendChild(head);
        var tabs = el('div', 'atv-tv__tabs'); d.appendChild(tabs);
        var grid = el('div', 'atv-tv__grid atv-tv__grid--wide'); d.appendChild(grid);
        var hint = el('p', 'atv-tv__hint', tvText('Folgen werden geladen …', 'Loading episodes …')); d.appendChild(hint);
        function show(list) {
            grid.textContent = '';
            list.slice().sort(tvEpisodeOrder).forEach(function (ep) { grid.appendChild(tvCard(ep)); });
            hint.hidden = list.length > 0;
            if (!list.length) hint.textContent = tvText('Keine Folgen verfügbar.', 'No episodes available.');
            actions.textContent = '';
            var first = list.slice().sort(tvEpisodeOrder)[0];
            if (first) {
                var play = el('button', 'atv-pill atv-pill--primary'); play.type = 'button'; play.appendChild(svg(ICON.play)); play.appendChild(el('span', null, tvText('Abspielen', 'Play')));
                play.addEventListener('click', function () { tvPlay(first); });
                actions.appendChild(play);
            }
        }
        tvChildren(item.Id).then(function (kids) {
            if (!d.isConnected) return;
            var seasons = kids.filter(function (k) { return k.Type === 'Season' || k.IsFolder; });
            if (!seasons.length) { show(kids); return; }
            seasons.sort(function (x, y) { return (x.IndexNumber || 0) - (y.IndexNumber || 0); });
            function pick(season, button) {
                [].forEach.call(tabs.children, function (b) { b.classList.toggle('is-on', b === button); });
                grid.textContent = ''; hint.hidden = false; hint.textContent = tvText('Folgen werden geladen …', 'Loading episodes …');
                // Jellyfin legt eine Staffel beim ersten Öffnen der Serie an; fragt man sofort nach ihren Folgen, ist sie
                // manchmal noch leer. Dann kurz warten und noch einmal fragen.
                var tries = 0;
                (function load() {
                    tvChildren(season.Id).then(function (eps) {
                        if (!button.classList.contains('is-on') || !d.isConnected) return;
                        if (!eps.length && tries++ < 4) { setTimeout(load, 1500); return; }
                        show(eps);
                    }).catch(function () { hint.textContent = tvText('Die Folgen ließen sich nicht laden.', 'Could not load episodes.'); });
                })();
            }
            seasons.forEach(function (season, i) {
                var b = el('button', 'atv-tv__tab', season.Name); b.type = 'button';
                b.addEventListener('click', function () { pick(season, b); });
                tabs.appendChild(b);
                if (i === 0) pick(season, b);
            });
        }).catch(function () { hint.textContent = tvText('Die Folgen ließen sich nicht laden.', 'Could not load episodes.'); });
    }
    function tvShelf(title) {
        var sec = el('section', 'atv-tv__shelf');
        if (title) sec.appendChild(el('h2', 'atv-tv__title', title));
        var row = el('div', 'atv-tv__row'); sec.appendChild(row);
        return { root: sec, row: row };
    }
    function tvCard(item) {
        var poster = item.Type === 'Series' || item.Type === 'Movie';
        var b = el('button', 'atv-tv__card' + (poster ? ' atv-tv__card--poster' : '')); b.type = 'button';
        var pic = el('span', 'atv-tv__pic');
        var tag = item.ImageTags && item.ImageTags.Primary;
        if (tag) { var img = el('img'); img.loading = 'lazy'; img.decoding = 'async'; img.alt = ''; img.src = imgUrl(item.Id, { type: 'Primary', maxWidth: poster ? 400 : 560, tag: tag, quality: 85 }); pic.appendChild(img); }
        else pic.appendChild(el('span', 'atv-tv__blank', item.Name));
        if (!poster && item.RunTimeTicks) pic.appendChild(el('span', 'atv-tv__len', fmtRuntime(item.RunTimeTicks, 'short')));
        b.appendChild(pic);
        b.appendChild(el('span', 'atv-tv__name', item.Name));
        var sub = item.Type === 'Series' ? tvText('Serie', 'Series') : (item.SeriesName && item.Name.indexOf(item.SeriesName) < 0 ? item.SeriesName : (item.ProductionYear ? String(item.ProductionYear) : (item.RunTimeTicks > 0 ? fmtRuntime(item.RunTimeTicks, 'short') : '')));
        if (item.Type === 'Episode' && item.IndexNumber != null) sub = (item.ParentIndexNumber != null ? tvText('Staffel ', 'Season ') + item.ParentIndexNumber + ' · ' : '') + tvText('Folge ', 'Episode ') + item.IndexNumber;
        if (sub) b.appendChild(el('span', 'atv-tv__sub', sub));
        b.title = item.Name;
        b.addEventListener('click', function () { tvOpen(item); });
        return b;
    }
    function tvLiveCard(ch) {
        var p = ch.CurrentProgram || {};
        var b = el('button', 'atv-tv__card atv-tv__card--live'); b.type = 'button';
        var pic = el('span', 'atv-tv__pic');
        var tag = p.ImageTags && p.ImageTags.Primary;
        if (tag) { var img = el('img'); img.loading = 'lazy'; img.decoding = 'async'; img.alt = ''; img.src = imgUrl(p.Id, { type: 'Primary', maxWidth: 560, tag: tag, quality: 85 }); pic.appendChild(img); }
        else pic.appendChild(el('span', 'atv-tv__blank', ch.Name));
        pic.appendChild(el('span', 'atv-tv__chan', ch.Name));
        if (p.StartDate && p.EndDate) {
            var s0 = +new Date(p.StartDate), e0 = +new Date(p.EndDate), pct = e0 > s0 ? Math.min(100, Math.max(0, (Date.now() - s0) / (e0 - s0) * 100)) : 0;
            var bar = el('span', 'atv-tv__bar'); var fill = el('i'); fill.style.width = pct.toFixed(1) + '%'; bar.appendChild(fill); pic.appendChild(bar);
        }
        b.appendChild(pic);
        b.appendChild(el('span', 'atv-tv__name', p.Name || ch.Name));
        if (p.StartDate) b.appendChild(el('span', 'atv-tv__sub', tvTime(p.StartDate) + ' – ' + tvTime(p.EndDate)));
        b.title = ch.Name + (p.Name ? ' · ' + p.Name : '');
        b.addEventListener('click', function () { tvPlay({ Id: ch.Id, Type: 'TvChannel', MediaType: 'Video' }); });
        return b;
    }
    function tvHero(ch) {
        var p = ch.CurrentProgram || {}, hero = el('section', 'atv-tv__hero');
        var tag = p.ImageTags && p.ImageTags.Primary;
        if (tag) hero.style.backgroundImage = 'url("' + imgUrl(p.Id, { type: 'Primary', maxWidth: 1600, tag: tag, quality: 88 }) + '")';
        var box = el('div', 'atv-tv__herobox');
        var live = el('span', 'atv-tv__livetag'); live.appendChild(el('i')); live.appendChild(document.createTextNode('LIVE · ' + ch.Name));
        box.appendChild(live);
        box.appendChild(el('h1', 'atv-tv__herotitle', p.Name || ch.Name));
        var line = [p.StartDate ? tvTime(p.StartDate) + ' – ' + tvTime(p.EndDate) : '', p.EpisodeTitle || ''].filter(Boolean).join(' · ');
        if (line) box.appendChild(el('p', 'atv-tv__heroline', line));
        if (p.Overview) box.appendChild(el('p', 'atv-tv__herotext', p.Overview));
        var actions = el('div', 'atv-tv__heroactions');
        var play = el('button', 'atv-pill atv-pill--primary'); play.type = 'button'; play.appendChild(svg(ICON.play)); play.appendChild(el('span', null, tvText('Jetzt ansehen', 'Watch live')));
        play.addEventListener('click', function () { tvPlay({ Id: ch.Id, Type: 'TvChannel', MediaType: 'Video' }); });
        actions.appendChild(play);
        var guide = el('a', 'atv-pill'); guide.href = '#/livetv?collectionType=livetv&tab=1'; guide.appendChild(el('span', null, tvText('Programmübersicht', 'Guide')));
        actions.appendChild(guide);
        box.appendChild(actions);
        hero.appendChild(box);
        return hero;
    }
    function tvLoadLive(hub) {
        var a = api();
        a.getJSON(a.getUrl('LiveTv/Channels', { userId: userId(), addCurrentProgram: true, limit: 80, enableImages: true, enableUserData: false, sortBy: 'SortName', fields: 'Overview' })).then(function (r) {
            var list = ((r && r.Items) || []).slice().sort(function (x, y) { return (parseFloat(x.ChannelNumber) || 999) - (parseFloat(y.ChannelNumber) || 999); });
            if (!hub.isConnected) return;
            hub.live.row.textContent = '';
            list.forEach(function (ch) { hub.live.row.appendChild(tvLiveCard(ch)); });
            hub.live.root.hidden = !list.length;
            var first = list.filter(function (c) { return c.CurrentProgram && c.CurrentProgram.ImageTags && c.CurrentProgram.ImageTags.Primary; })[0] || list[0];
            hub.heroSlot.textContent = '';
            if (first) hub.heroSlot.appendChild(tvHero(first));
        }).catch(function () { hub.live.root.hidden = true; });
    }
    function tvLoadRows(hub) {
        var a = api(), queue = tv.rows.slice(0, 12), running = 0;
        function next() {
            if (!hub.isConnected || !queue.length || running >= 2) return;
            var row = queue.shift(), shelf = tvShelf(row.title);
            shelf.root.classList.add('is-loading');
            hub.rows.appendChild(shelf.root);
            running++;
            a.getJSON(a.getUrl('Mediathek/row', { id: row.id })).then(function (r) {
                var items = (r && r.Items) || [];
                if (!items.length) { shelf.root.remove(); return; }
                items.forEach(function (it) { shelf.row.appendChild(tvCard(it)); });
                shelf.root.classList.remove('is-loading');
            }).catch(function () { shelf.root.remove(); }).then(function () { running--; next(); });
            next();
        }
        next();
    }
    function tvSearch(hub, text) {
        var q = text.trim(), seq = ++tv.searchSeq;
        hub.classList.toggle('is-searching', q.length >= 2);
        if (q.length < 2) { hub.results.textContent = ''; return; }
        hub.results.textContent = '';
        hub.results.appendChild(el('p', 'atv-tv__hint', tvText('Suche in der Mediathek …', 'Searching …')));
        var a = api();
        a.getJSON(a.getUrl('Mediathek/search', { q: q })).then(function (r) {
            if (seq !== tv.searchSeq || !hub.isConnected) return;
            var items = (r && r.Items) || [];
            hub.results.textContent = '';
            if (!items.length) { hub.results.appendChild(el('p', 'atv-tv__hint', tvText('Nichts gefunden für „' + q + '“.', 'Nothing found for “' + q + '”.'))); return; }
            hub.results.appendChild(el('h2', 'atv-tv__title', tvText('Aus der Mediathek', 'From the Mediathek')));
            var grid = el('div', 'atv-tv__grid');
            items.forEach(function (it) { grid.appendChild(tvCard(it)); });
            hub.results.appendChild(grid);
        }).catch(function () {
            if (seq !== tv.searchSeq) return;
            hub.results.textContent = '';
            hub.results.appendChild(el('p', 'atv-tv__hint', tvText('Die Suche ist gerade nicht erreichbar.', 'Search is unavailable right now.')));
        });
    }
    function mountTvHub(page) {
        tvProbe();
        tvNav();
        var wanted = tv.ok && tvIsHub() && !page.classList.contains('itemDetailPage');
        if (!wanted) {
            if (page.__atvTv) { page.__atvTv.remove(); page.__atvTv = null; page.classList.remove('atv-tv-on'); }
            return;
        }
        if (page.__atvTv && page.__atvTv.isConnected) return;
        var hub = el('div', 'atv-tvhub');
        hub.heroSlot = el('div', 'atv-tv__heroslot'); hub.appendChild(hub.heroSlot);
        var bar = el('div', 'atv-tv__searchbar');
        var field = el('label', 'atv-tv__field'); field.appendChild(svg(ICON.search));
        var input = el('input'); input.type = 'search'; input.placeholder = tvText('Mediathek durchsuchen', 'Search the Mediathek'); input.autocomplete = 'off'; input.spellcheck = false;
        field.appendChild(input); bar.appendChild(field); hub.appendChild(bar);
        var debounce = null;
        input.addEventListener('input', function () { clearTimeout(debounce); debounce = setTimeout(function () { tvSearch(hub, input.value); }, 450); });
        input.addEventListener('keydown', function (e) { if (e.key === 'Enter') { clearTimeout(debounce); tvSearch(hub, input.value); } e.stopPropagation(); });
        hub.results = el('div', 'atv-tv__results'); hub.appendChild(hub.results);
        hub.body = el('div', 'atv-tv__body');
        hub.live = tvShelf(tvText('Jetzt im TV', 'On now')); hub.live.root.classList.add('atv-tv__shelf--live'); hub.body.appendChild(hub.live.root);
        hub.rows = el('div', 'atv-tv__rows'); hub.body.appendChild(hub.rows);
        hub.appendChild(hub.body);
        hub.detail = el('div', 'atv-tv__detail'); hub.appendChild(hub.detail);
        page.appendChild(hub);
        page.classList.add('atv-tv-on');
        page.__atvTv = hub;
        tv.root = hub;
        tvLoadLive(hub);
        tvLoadRows(hub);
        if (tv.pendingSeries) { var wanted0 = tv.pendingSeries; tv.pendingSeries = null; tvSeries(wanted0); }
        // Das laufende Programm wechselt; alle zwei Minuten nachsehen, solange die Seite offen ist.
        clearInterval(tv.timer);
        tv.timer = setInterval(function () { if (hub.isConnected && !document.hidden) tvLoadLive(hub); else if (!hub.isConnected) clearInterval(tv.timer); }, 120000);
    }

    function refresh() {
        bindShelfWheel();
        ensureRootClass();
        cssInject();
        if (location.hash !== lastHash) { lastHash = location.hash; collapseAll(); closeMenus(); var tw0 = document.querySelector('.atv-trailer'); if (tw0) { tw0.remove(); document.documentElement.style.overflow = ''; } var sh0 = document.querySelector('.atv-sheet'); if (sh0) { sh0.remove(); document.documentElement.style.overflow = ''; } }
        var page = syncClasses();
        if (!page) return;
        if (dlAllowed === null && apiReady()) canDownload();
        dlpResume();
        if (page.classList.contains('homePage')) { mountHero(page); tidyShelves(page); updCheck(); }
        else if (page.classList.contains('itemDetailPage')) mountDetail(page);
        else if (page.id === 'loginPage') mountLogin(page);
        else if (page.id === 'searchPage' || page.classList.contains('searchPage')) mountSearch(page);
        var upd0 = document.querySelector('.atv-update'); if (upd0) upd0.style.display = page.classList.contains('homePage') ? '' : 'none';
        mountTvHub(page);
        mountTabbar(page);
    }
    function schedule() { clearTimeout(schedule.t); schedule.t = setTimeout(refresh, 120); }
    document.addEventListener('viewshow', schedule, true);
    document.addEventListener('pageshow', schedule, true);
    window.addEventListener('hashchange', schedule);
    window.addEventListener('popstate', schedule);
    window.addEventListener('resize', schedule);
    var mo = new MutationObserver(function (muts) {
        for (var i = 0; i < muts.length; i++) {
            var tg = muts[i].target;
            if (tg.nodeType !== 1) continue;
            if (tg.classList.contains('mainAnimatedPage')) syncClasses();
            if (tg.classList.contains('mainAnimatedPage') || tg.classList.contains('mainAnimatedPages') || tg.classList.contains('skinBody') || tg.classList.contains('homeSectionsContainer') || tg.classList.contains('mainDetailButtons') || tg.tagName === 'MAIN' || tg.id === 'loginPage' || tg.tagName === 'BODY') { schedule(); return; }
        }
    });
    function start() {
        if (!document.body) { setTimeout(start, 50); return; }
        cssInject();
        ensureRootClass();
        mo.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['class'] });
        new MutationObserver(ensureRootClass).observe(document.documentElement, { attributes: true, attributeFilter: ['class'] });
        schedule();
    }
    start();
    var wait = setInterval(function () { if (window.ApiClient) { clearInterval(wait); schedule(); } }, 300);
    setTimeout(function () { clearInterval(wait); }, 60000);

})();
