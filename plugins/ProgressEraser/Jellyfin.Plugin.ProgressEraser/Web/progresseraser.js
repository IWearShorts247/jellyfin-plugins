// Progress Eraser — injected into jellyfin-web by the plugin.
//
//  * Eraser button in card hover menus (next to Favorite / Mark Played) and in
//    the item page button row. Clears the user's playback position, which
//    removes the item from Continue Watching.
//  * "Hide from Next Up" button on Next Up cards, and a show/hide toggle on
//    series, season and episode pages. The server filters hidden shows out of
//    Next Up for every client; this script just provides the buttons and
//    removes the card right away.
(function () {
    'use strict';

    // Replaced with the plugin configuration when the server serves this file.
    var CONFIG = { ShowOnAllCards: false, ShowOnDetailPage: true, NextUpButton: true };
    try { CONFIG = JSON.parse('__PROGRESSERASER_CONFIG__'); } catch (e) { /* dev fallback */ }

    var RESET_TITLE = 'Reset progress';
    var HIDE_TITLE = 'Hide from Next Up';
    var SHOW_TITLE = 'Show in Next Up';

    // Inline SVGs so nothing depends on the icon font version.
    // Material Symbols "ink_eraser"; Material Icons "visibility_off" / "visibility".
    var ERASER_SVG = '<svg viewBox="0 -960 960 960" width="1em" height="1em" fill="currentColor" aria-hidden="true">'
        + '<path d="M690-240h190v80H610l80-80Zm-500 80-85-85q-23-23-23.5-57t22.5-58l440-456q23-24 56.5-24t56.5 23l199 199'
        + 'q23 23 23 57t-23 57L520-160H190Zm296-80 314-322-198-198-442 456 64 64h262Zm-6-240Z"/></svg>';
    var EYE_OFF_SVG = '<svg viewBox="0 0 24 24" width="1em" height="1em" fill="currentColor" aria-hidden="true">'
        + '<path d="M12 7c2.76 0 5 2.24 5 5 0 .65-.13 1.26-.36 1.83l2.92 2.92c1.51-1.26 2.7-2.89 3.43-4.75-1.73-4.39-6-7.5-11-7.5'
        + '-1.4 0-2.74.25-3.98.7l2.16 2.16C10.74 7.13 11.35 7 12 7zM2 4.27l2.28 2.28.46.46C3.08 8.3 1.78 10.02 1 12c1.73 4.39 6 7.5 11 7.5'
        + ' 1.55 0 3.03-.3 4.38-.84l.42.42L19.73 22 21 20.73 3.27 3 2 4.27zM7.53 9.8l1.55 1.55c-.05.21-.08.43-.08.65 0 1.66 1.34 3 3 3'
        + ' .22 0 .44-.03.65-.08l1.55 1.55c-.67.33-1.41.53-2.2.53-2.76 0-5-2.24-5-5 0-.79.2-1.53.53-2.2zm4.31-.78l3.15 3.15.02-.16'
        + 'c0-1.66-1.34-3-3-3l-.17.01z"/></svg>';
    var EYE_SVG = '<svg viewBox="0 0 24 24" width="1em" height="1em" fill="currentColor" aria-hidden="true">'
        + '<path d="M12 4.5C7 4.5 2.73 7.61 1 12c1.73 4.39 6 7.5 11 7.5s9.27-3.11 11-7.5c-1.73-4.39-6-7.5-11-7.5zM12 17'
        + 'c-2.76 0-5-2.24-5-5s2.24-5 5-5 5 2.24 5 5-2.24 5-5 5zm0-8c-1.66 0-3 1.34-3 3s1.34 3 3 3 3-1.34 3-3-1.34-3-3-3z"/></svg>';

    // ---- API ----

    function api(method, path) {
        var apiClient = window.ApiClient;
        if (!apiClient || !apiClient.accessToken || !apiClient.accessToken()) {
            return Promise.reject(new Error('not signed in'));
        }
        return fetch(apiClient.getUrl('ProgressEraser/' + path), {
            method: method,
            headers: { 'Authorization': 'MediaBrowser Token="' + apiClient.accessToken() + '"' }
        }).then(function (res) {
            if (!res.ok) {
                throw new Error('HTTP ' + res.status);
            }
            return res.status === 204 ? null : res.json();
        });
    }

    function resetProgress(itemId) { return api('POST', itemId); }
    function hideFromNextUp(itemId) { return api('POST', 'NextUp/Hide/' + itemId); }
    function showInNextUp(seriesId) { return api('DELETE', 'NextUp/Hide/' + seriesId); }
    function nextUpStatus(itemId) { return api('GET', 'NextUp/Status/' + itemId); }

    function makeIcon(className, svg) {
        var span = document.createElement('span');
        span.className = className;
        span.innerHTML = svg;
        return span;
    }

    function busy(btn, on) {
        btn.disabled = on;
        btn.style.opacity = on ? '.4' : '';
    }

    // ---- cards ----

    function cardHasProgress(card) {
        // cardBuilder renders .itemProgressBar inside .innerCardFooter and keeps
        // the raw position in data-positionticks; either is a reliable signal.
        if (card.querySelector('.itemProgressBar')) {
            return true;
        }
        var ticks = parseInt(card.getAttribute('data-positionticks') || '0', 10);
        return ticks > 0;
    }

    function isNextUpCard(card) {
        if (card.closest('#nextUpItems')) {                 // Shows library, Next Up tab
            return true;
        }
        if (/type=nextup/i.test(window.location.hash)) {    // the full Next Up list page
            return true;
        }
        var section = card.closest('.verticalSection');     // home screen section
        return !!(section && section.querySelector('a.sectionTitleTextButton[href*="type=nextup" i]'));
    }

    function makeCardButton(title, svg) {
        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'cardOverlayButton cardOverlayButton-hover paper-icon-button-light';
        btn.title = title;
        btn.setAttribute('aria-label', title);
        btn.appendChild(makeIcon('cardOverlayButtonIcon cardOverlayButtonIcon-hover', svg));
        return btn;
    }

    function insertCardButton(row, btn) {
        // Sit with Favorite / Mark Played, ahead of the "more" menu button.
        var menu = row.querySelector('[data-action="menu"]');
        row.insertBefore(btn, menu || null);
    }

    function removeCard(card) {
        var container = card.closest('.itemsContainer');
        var section = card.closest('.verticalSection');
        card.style.transition = 'opacity .3s';
        card.style.opacity = '0';
        setTimeout(function () {
            card.remove();
            if (container && !container.querySelector('.card') && section) {
                section.classList.add('hide');
            }
        }, 300);
    }

    function decorateCard(card) {
        var itemId = card.getAttribute('data-id');
        var row = card.querySelector('.cardOverlayButton-br');
        if (!itemId || !row) {
            return;
        }

        if (!card.querySelector('.progressEraserBtn') && (CONFIG.ShowOnAllCards || cardHasProgress(card))) {
            var eraser = makeCardButton(RESET_TITLE, ERASER_SVG);
            eraser.classList.add('progressEraserBtn');
            eraser.addEventListener('click', function (e) {
                // Keep the card's own link/menu handlers from firing.
                e.preventDefault();
                e.stopPropagation();
                busy(eraser, true);
                resetProgress(itemId).then(function () {
                    card.querySelectorAll('.itemProgressBar').forEach(function (el) { el.remove(); });
                    card.setAttribute('data-positionticks', '0');
                    eraser.remove();
                }).catch(function () {
                    busy(eraser, false);
                });
            });
            insertCardButton(row, eraser);
        }

        if (CONFIG.NextUpButton && !card.querySelector('.peNextUpBtn') && isNextUpCard(card)) {
            var hide = makeCardButton(HIDE_TITLE, EYE_OFF_SVG);
            hide.classList.add('peNextUpBtn');
            hide.addEventListener('click', function (e) {
                e.preventDefault();
                e.stopPropagation();
                busy(hide, true);
                hideFromNextUp(itemId).then(function () {
                    removeCard(card);
                }).catch(function () {
                    busy(hide, false);
                });
            });
            insertCardButton(row, hide);
        }
    }

    // ---- item details page ----

    function currentItemId() {
        var match = /[?&]id=([0-9a-fA-F-]{32,36})/.exec(window.location.hash || window.location.search || '');
        return match ? match[1] : null;
    }

    function makeDetailButton(title, svg) {
        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'button-flat detailButton emby-button';
        btn.title = title;
        btn.setAttribute('aria-label', title);
        var content = document.createElement('div');
        content.className = 'detailButton-content';
        content.appendChild(makeIcon('detailButton-icon', svg));
        btn.appendChild(content);
        return btn;
    }

    function insertDetailButton(row, btn) {
        var anchor = row.querySelector('.peNextUpBtn') || row.querySelector('.progressEraserBtn') || row.querySelector('.btnPlaystate');
        if (anchor && anchor.parentNode === row) {
            row.insertBefore(btn, anchor.nextSibling);
        } else {
            row.appendChild(btn);
        }
    }

    function decorateDetailEraser(row, itemId) {
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
        if (existing) {
            return;
        }

        var btn = makeDetailButton(RESET_TITLE, ERASER_SVG);
        btn.classList.add('progressEraserBtn');
        btn.addEventListener('click', function (e) {
            e.preventDefault();
            e.stopPropagation();
            busy(btn, true);
            resetProgress(itemId).then(function () {
                btn.remove();
                // The page refreshes its own buttons from UserDataChanged; nudge
                // the visible state now so the click feels immediate.
                replay.classList.add('hide');
                row.querySelectorAll('.btnPlay').forEach(function (play) { play.title = 'Play'; });
            }).catch(function () {
                busy(btn, false);
            });
        });
        insertDetailButton(row, btn);
    }

    function decorateDetailNextUp(row, itemId) {
        if (!CONFIG.NextUpButton || row.getAttribute('data-pe-nextup') === itemId) {
            return;
        }
        row.setAttribute('data-pe-nextup', itemId);

        var apiClient = window.ApiClient;
        if (!apiClient || !apiClient.getItem) {
            return;
        }
        apiClient.getItem(apiClient.getCurrentUserId(), itemId).then(function (item) {
            if (!item || ['Series', 'Season', 'Episode'].indexOf(item.Type) === -1) {
                return null;
            }
            return nextUpStatus(itemId);
        }).then(function (status) {
            if (!status || row.getAttribute('data-pe-nextup') !== itemId || row.querySelector('.peNextUpBtn')) {
                return;
            }
            var hidden = !!status.Hidden;
            var btn = makeDetailButton(hidden ? SHOW_TITLE : HIDE_TITLE, hidden ? EYE_SVG : EYE_OFF_SVG);
            btn.classList.add('peNextUpBtn');
            btn.addEventListener('click', function (e) {
                e.preventDefault();
                e.stopPropagation();
                busy(btn, true);
                var action = hidden ? showInNextUp(status.SeriesId) : hideFromNextUp(itemId);
                action.then(function () {
                    hidden = !hidden;
                    btn.title = hidden ? SHOW_TITLE : HIDE_TITLE;
                    btn.setAttribute('aria-label', btn.title);
                    btn.querySelector('.detailButton-icon').innerHTML = hidden ? EYE_SVG : EYE_OFF_SVG;
                }).catch(function () { /* leave as is */ }).then(function () {
                    busy(btn, false);
                });
            });
            insertDetailButton(row, btn);
        }).catch(function () { /* not an item we handle */ });
    }

    function decorateDetailButtons(row) {
        var itemId = currentItemId();
        if (!itemId) {
            return;
        }
        if (row.getAttribute('data-pe-item') !== itemId) {
            // The view is reused across items; start clean for the new one.
            row.querySelectorAll('.progressEraserBtn, .peNextUpBtn').forEach(function (el) { el.remove(); });
            row.setAttribute('data-pe-item', itemId);
            row.removeAttribute('data-pe-nextup');
        }
        decorateDetailEraser(row, itemId);
        decorateDetailNextUp(row, itemId);
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
        window.addEventListener('hashchange', function () {
            document.querySelectorAll('.mainDetailButtons').forEach(decorateDetailButtons);
        });
    }

    if (document.body) {
        start();
    } else {
        document.addEventListener('DOMContentLoaded', start);
    }
})();
