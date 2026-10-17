// Pause Tracker overlay — injected into jellyfin-web by the Pause Tracker plugin.
// While media is playing: polls the plugin API and shows
// "⏸ <count> · <accumulated paused time>" next to the OSD time display.
// When playback ends: shows a fullscreen summary of pauses (if enabled).
(function () {
    'use strict';

    var POLL_MS = 1000;             // while media is loaded
    var IDLE_MS = 2000;             // while browsing / logged out
    var SUMMARY_TIMEOUT_MS = 15000; // fullscreen summary auto-dismiss
    var SUMMARY_MAX_ROWS = 12;

    // Replaced with the plugin configuration when the server serves this file.
    var CONFIG = { ShowExitSummary: true };
    try { CONFIG = JSON.parse('__PAUSETRACKER_CONFIG__'); } catch (e) { /* dev fallback */ }

    var lastStats = null;    // latest snapshot seen during playback
    var wasWatching = false;
    var summaryEl = null;
    var summaryTimer = null;

    function formatDuration(ms) {
        var totalSec = Math.floor(ms / 1000);
        var h = Math.floor(totalSec / 3600);
        var m = Math.floor((totalSec % 3600) / 60);
        var s = totalSec % 60;
        var pad = function (n) { return String(n).padStart(2, '0'); };
        return h > 0 ? h + ':' + pad(m) + ':' + pad(s) : m + ':' + pad(s);
    }

    function isWatching() {
        // The player's <video> element exists exactly while media is loaded,
        // whether or not the OSD controls are currently shown. (Checking OSD
        // visibility is wrong: the controls auto-hide during playback, which
        // made the exit summary fire mid-viewing.)
        return !!document.querySelector('.videoPlayerContainer video, video.htmlvideoplayer');
    }

    // ---- in-player OSD line ----

    function ensureOverlay() {
        var osd = document.querySelector('.videoOsdBottom');
        if (!osd) {
            return null;
        }
        // Always find our element in the DOM rather than caching it —
        // jellyfin-web can rebuild OSD contents, and a stale reference led to
        // duplicate entries piling up in the control bar.
        var existing = osd.querySelectorAll('.pauseTrackerOsd');
        if (existing.length > 0) {
            for (var i = 1; i < existing.length; i++) {
                existing[i].remove();
            }
            return existing[0];
        }
        var el = document.createElement('span');
        el.className = 'pauseTrackerOsd';
        el.style.cssText = 'margin-left:1.25em;opacity:.85;white-space:nowrap;';
        var timeText = osd.querySelector('.osdTimeText');
        (timeText || osd).appendChild(el);
        return el;
    }

    function render(stats) {
        var el = ensureOverlay();
        if (!el) {
            return;
        }
        if (!stats || !stats.PauseCount) {
            el.textContent = '';
            return;
        }
        el.textContent = '⏸ ' + stats.PauseCount + ' · ' + formatDuration(stats.TotalPausedMs);
    }

    // ---- fullscreen exit summary ----

    function dismissSummary() {
        if (summaryTimer) {
            clearTimeout(summaryTimer);
            summaryTimer = null;
        }
        if (summaryEl) {
            document.removeEventListener('keydown', dismissSummary, true);
            summaryEl.remove();
            summaryEl = null;
        }
    }

    function showSummary(stats) {
        if (!CONFIG.ShowExitSummary || !stats || !stats.PauseCount) {
            return;
        }

        // The interval list only holds *completed* pauses; if playback was
        // stopped while paused, count the ongoing pause as the last interval.
        var intervals = (stats.IntervalsMs || []).slice();
        if (stats.IsPaused && stats.CurrentPauseMs > 0) {
            intervals.push(stats.CurrentPauseMs);
        }

        dismissSummary();
        summaryEl = document.createElement('div');
        summaryEl.className = 'pauseTrackerSummary';
        summaryEl.style.cssText =
            'position:fixed;inset:0;z-index:999999;display:flex;align-items:center;justify-content:center;' +
            'background:rgba(0,0,0,.88);color:#fff;cursor:pointer;';

        var rows = intervals.slice(0, SUMMARY_MAX_ROWS).map(function (ms, i) {
            return '<tr><td style="padding:.15em 1.5em .15em 0;opacity:.7;text-align:left;">Pause ' + (i + 1) + '</td>'
                + '<td style="text-align:right;">' + formatDuration(ms) + '</td></tr>';
        }).join('');
        if (intervals.length > SUMMARY_MAX_ROWS) {
            rows += '<tr><td colspan="2" style="opacity:.7;padding-top:.3em;">… and '
                + (intervals.length - SUMMARY_MAX_ROWS) + ' more</td></tr>';
        }

        summaryEl.innerHTML =
            '<div style="text-align:center;max-height:90vh;overflow-y:auto;padding:2em;">'
            + '<div style="font-size:1.3em;letter-spacing:.15em;text-transform:uppercase;opacity:.7;margin-bottom:1em;">Playback summary</div>'
            + '<div style="font-size:3em;font-weight:600;margin-bottom:.2em;">⏸ ' + stats.PauseCount
            + (stats.PauseCount === 1 ? ' pause' : ' pauses') + '</div>'
            + '<div style="font-size:1.8em;margin-bottom:1.2em;">' + formatDuration(stats.TotalPausedMs) + ' paused in total</div>'
            + '<table style="margin:0 auto;border-collapse:collapse;font-size:1.1em;">' + rows + '</table>'
            + '<div style="margin-top:1.8em;opacity:.55;font-size:.9em;">Click anywhere or press any key to close</div>'
            + '</div>';

        summaryEl.addEventListener('click', dismissSummary);
        document.addEventListener('keydown', dismissSummary, true);
        summaryTimer = setTimeout(dismissSummary, SUMMARY_TIMEOUT_MS);
        document.body.appendChild(summaryEl);
    }

    // ---- polling loop ----

    function fetchStats(apiClient) {
        var url = apiClient.getUrl('PauseTracker/Stats', { deviceId: apiClient.deviceId() });
        return fetch(url, {
            headers: {
                'Authorization': 'MediaBrowser Token="' + apiClient.accessToken() + '"'
            }
        }).then(function (res) {
            return res.ok ? res.json() : null;
        });
    }

    function tick() {
        var apiClient = window.ApiClient;
        var watching = isWatching();

        if (watching && apiClient && apiClient.accessToken && apiClient.accessToken()) {
            wasWatching = true;
            dismissSummary();
            fetchStats(apiClient)
                .then(function (stats) {
                    if (stats) {
                        lastStats = stats;
                    }
                    render(stats);
                })
                .catch(function () { /* transient network error — try again next tick */ })
                .then(function () { setTimeout(tick, POLL_MS); });
            return;
        }

        if (!watching) {
            if (wasWatching) {
                wasWatching = false;
                showSummary(lastStats);
                lastStats = null;
            }
        }
        setTimeout(tick, IDLE_MS);
    }

    setTimeout(tick, IDLE_MS);
})();
