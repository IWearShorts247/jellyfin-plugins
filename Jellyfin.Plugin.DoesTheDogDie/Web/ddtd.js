// Does the Dog Die overlay — injected into jellyfin-web by the plugin.
// When playback starts (and on every item change, e.g. auto-next episode),
// shows community-sourced content warnings for a configurable duration.
(function () {
    'use strict';

    var POLL_MS = 3000;
    var IDLE_MS = 2000;

    // Replaced with the plugin configuration when the server serves this file.
    var CONFIG = { OverlaySeconds: 12 };
    try { CONFIG = JSON.parse('__DDTD_CONFIG__'); } catch (e) { /* dev fallback */ }

    var lastItemId = null;
    var overlayEl = null;
    var overlayTimer = null;

    function isWatching() {
        return !!document.querySelector('.videoPlayerContainer video, video.htmlvideoplayer');
    }

    function hideOverlay() {
        if (overlayTimer) {
            clearTimeout(overlayTimer);
            overlayTimer = null;
        }
        if (overlayEl) {
            overlayEl.remove();
            overlayEl = null;
        }
    }

    function showOverlay(lookup) {
        hideOverlay();

        overlayEl = document.createElement('div');
        overlayEl.className = 'ddtdOverlay';
        overlayEl.style.cssText =
            'position:fixed;top:1.5em;left:1.5em;z-index:999998;max-width:26em;'
            + 'background:rgba(15,15,20,.92);color:#fff;border-radius:.5em;'
            + 'padding:1em 1.25em;cursor:pointer;font-size:.95em;'
            + 'box-shadow:0 .2em 1em rgba(0,0,0,.6);transition:opacity .6s;';

        var title = document.createElement('div');
        title.style.cssText = 'font-weight:600;margin-bottom:.5em;letter-spacing:.04em;';
        title.textContent = '⚠ Content notes — ' + lookup.MatchedName
            + (lookup.MatchedYear ? ' (' + lookup.MatchedYear + ')' : '');
        overlayEl.appendChild(title);

        var list = document.createElement('ul');
        list.style.cssText = 'margin:0;padding-left:1.2em;';
        lookup.Warnings.forEach(function (w) {
            var li = document.createElement('li');
            li.style.cssText = 'margin:.15em 0;';
            li.textContent = w.Name;
            var votes = document.createElement('span');
            votes.style.cssText = 'opacity:.55;font-size:.85em;margin-left:.4em;';
            votes.textContent = w.YesVotes + '✓ ' + w.NoVotes + '✗';
            li.appendChild(votes);
            list.appendChild(li);
        });
        overlayEl.appendChild(list);

        var footer = document.createElement('div');
        footer.style.cssText = 'opacity:.5;font-size:.8em;margin-top:.6em;';
        footer.textContent = 'via DoesTheDogDie.com · click to dismiss';
        overlayEl.appendChild(footer);

        overlayEl.addEventListener('click', hideOverlay);
        document.body.appendChild(overlayEl);

        overlayTimer = setTimeout(function () {
            if (overlayEl) {
                overlayEl.style.opacity = '0';
                setTimeout(hideOverlay, 700);
            }
        }, Math.max(3, CONFIG.OverlaySeconds) * 1000);
    }

    function tick() {
        var apiClient = window.ApiClient;
        if (!isWatching() || !apiClient || !apiClient.accessToken || !apiClient.accessToken()) {
            lastItemId = null;
            hideOverlay();
            setTimeout(tick, IDLE_MS);
            return;
        }

        var url = apiClient.getUrl('DoesTheDogDie/Current', { deviceId: apiClient.deviceId() });
        fetch(url, {
            headers: {
                'Authorization': 'MediaBrowser Token="' + apiClient.accessToken() + '"',
                'X-Emby-Token': apiClient.accessToken()
            }
        }).then(function (res) {
            return res.ok ? res.json() : null;
        }).then(function (data) {
            if (data && data.ItemId && data.ItemId !== lastItemId) {
                lastItemId = data.ItemId;
                if (data.Lookup && data.Lookup.Warnings && data.Lookup.Warnings.length > 0) {
                    showOverlay(data.Lookup);
                }
            }
        }).catch(function () {
            /* transient network error — try again next tick */
        }).then(function () {
            setTimeout(tick, POLL_MS);
        });
    }

    setTimeout(tick, IDLE_MS);
})();
