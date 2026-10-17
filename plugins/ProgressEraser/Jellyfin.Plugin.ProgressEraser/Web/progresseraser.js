// Progress Eraser — injected into jellyfin-web by the plugin.
// Adds an eraser button to card hover menus (next to Favorite / Mark Played)
// and to the item page button row. Clicking it clears the current user's
// playback position for that item, which removes it from Continue Watching.
(function () {
    'use strict';

    // Replaced with the plugin configuration when the server serves this file.
    var CONFIG = { ShowOnAllCards: false, ShowOnDetailPage: true };
    try { CONFIG = JSON.parse('__PROGRESSERASER_CONFIG__'); } catch (e) { /* dev fallback */ }

    var TITLE = 'Reset progress';
    // Material Symbols "ink_eraser", inlined so it does not depend on the icon font.
    var ERASER_SVG = '<svg viewBox="0 -960 960 960" width="1em" height="1em" fill="currentColor" aria-hidden="true">'
        + '<path d="M690-240h190v80H610l80-80Zm-500 80-85-85q-23-23-23.5-57t22.5-58l440-456q23-24 56.5-24t56.5 23l199 199'
        + 'q23 23 23 57t-23 57L520-160H190Zm296-80 314-322-198-198-442 456 64 64h262Zm-6-240Z"/></svg>';

    function resetProgress(itemId) {
        var apiClient = window.ApiClient;
        if (!apiClient || !apiClient.accessToken || !apiClient.accessToken()) {
            return Promise.reject(new Error('not signed in'));
        }
        return fetch(apiClient.getUrl('ProgressEraser/' + itemId), {
            method: 'POST',
            headers: { 'Authorization': 'MediaBrowser Token="' + apiClient.accessToken() + '"' }
        }).then(function (res) {
            if (!res.ok) {
                throw new Error('HTTP ' + res.status);
            }
            return res.json();
        });
    }

    function makeIcon(className) {
        var span = document.createElement('span');
        span.className = className;
        span.innerHTML = ERASER_SVG;
        return span;
    }

    // ---- card hover menu ----

    function cardHasProgress(card) {
        // cardBuilder renders .itemProgressBar inside .innerCardFooter and keeps
        // the raw position in data-positionticks; either is a reliable signal.
        if (card.querySelector('.itemProgressBar')) {
            return true;
        }
        var ticks = parseInt(card.getAttribute('data-positionticks') || '0', 10);
        return ticks > 0;
    }

    function decorateCard(card) {
        if (card.querySelector('.progressEraserBtn')) {
            return;
        }
        var itemId = card.getAttribute('data-id');
        var row = card.querySelector('.cardOverlayButton-br');
        if (!itemId || !row) {
            return;
        }
        if (!CONFIG.ShowOnAllCards && !cardHasProgress(card)) {
            return;
        }

        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'cardOverlayButton cardOverlayButton-hover paper-icon-button-light progressEraserBtn';
        btn.title = TITLE;
        btn.setAttribute('aria-label', TITLE);
        btn.appendChild(makeIcon('cardOverlayButtonIcon cardOverlayButtonIcon-hover'));
        btn.addEventListener('click', function (e) {
            // Keep the card's own link/menu handlers from firing.
            e.preventDefault();
            e.stopPropagation();
            btn.disabled = true;
            btn.style.opacity = '.4';
            resetProgress(itemId).then(function () {
                card.querySelectorAll('.itemProgressBar').forEach(function (el) { el.remove(); });
                card.setAttribute('data-positionticks', '0');
                btn.remove();
            }).catch(function () {
                btn.disabled = false;
                btn.style.opacity = '';
            });
        });

        // Sit with Favorite / Mark Played, ahead of the "more" menu button.
        var menu = row.querySelector('[data-action="menu"]');
        row.insertBefore(btn, menu || null);
    }

    // ---- item details page ----

    function currentItemId() {
        var match = /[?&]id=([0-9a-fA-F-]{32,36})/.exec(window.location.hash || window.location.search || '');
        return match ? match[1] : null;
    }

    function decorateDetailButtons(row) {
        // jellyfin-web shows .btnReplay (and relabels .btnPlay "Resume") exactly
        // when the item has a playback position, and toggles it from the
        // server's UserDataChanged event. Follow that signal.
        var replay = row.querySelector('.btnReplay');
        var resumable = !!replay && !replay.classList.contains('hide');
        var existing = row.querySelector('.progressEraserBtn');
        if (!CONFIG.ShowOnDetailPage || !resumable) {
            if (existing) {
                existing.remove();
            }
            return;
        }
        var itemId = currentItemId();
        if (existing || !itemId) {
            return;
        }

        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'button-flat detailButton emby-button progressEraserBtn';
        btn.title = TITLE;
        btn.setAttribute('aria-label', TITLE);
        var content = document.createElement('div');
        content.className = 'detailButton-content';
        content.appendChild(makeIcon('detailButton-icon'));
        btn.appendChild(content);
        btn.addEventListener('click', function (e) {
            e.preventDefault();
            e.stopPropagation();
            btn.disabled = true;
            resetProgress(itemId).then(function () {
                btn.remove();
                // The page refreshes its own buttons from UserDataChanged; nudge
                // the visible state now so the click feels immediate.
                replay.classList.add('hide');
                row.querySelectorAll('.btnPlay').forEach(function (play) { play.title = 'Play'; });
            }).catch(function () {
                btn.disabled = false;
            });
        });

        var playstate = row.querySelector('.btnPlaystate');
        if (playstate && playstate.parentNode === row) {
            row.insertBefore(btn, playstate.nextSibling);
        } else {
            row.appendChild(btn);
        }
    }

    // ---- wiring ----

    function scan(root) {
        if (!(root instanceof Element)) {
            return;
        }
        if (root.matches('.cardOverlayContainer, .cardOverlayButton-br')) {
            var card = root.closest('.card');
            if (card) {
                decorateCard(card);
            }
        }
        root.querySelectorAll('.cardOverlayButton-br').forEach(function (row) {
            var card = row.closest('.card');
            if (card) {
                decorateCard(card);
            }
        });
        if (root.matches('.mainDetailButtons')) {
            decorateDetailButtons(root);
        }
        root.querySelectorAll('.mainDetailButtons').forEach(decorateDetailButtons);
    }

    var observer = new MutationObserver(function (mutations) {
        for (var i = 0; i < mutations.length; i++) {
            var m = mutations[i];
            if (m.type === 'attributes') {
                // .btnReplay toggles its "hide" class as user data loads or changes.
                var row = m.target.closest && m.target.closest('.mainDetailButtons');
                if (row) {
                    decorateDetailButtons(row);
                }
                continue;
            }
            for (var j = 0; j < m.addedNodes.length; j++) {
                scan(m.addedNodes[j]);
            }
        }
    });

    function start() {
        scan(document.body);
        observer.observe(document.body, {
            childList: true,
            subtree: true,
            attributes: true,
            attributeFilter: ['class']
        });
    }

    if (document.body) {
        start();
    } else {
        document.addEventListener('DOMContentLoaded', start);
    }
})();
