(function () {
    'use strict';

    var buttonId = 'wrestling-show-results-button';
    var resultsId = 'wrestling-results-panel';

    function getItemId() {
        var hash = window.location.hash || '';
        var match = /[?&]id=([a-f0-9-]+)/i.exec(hash);
        return match ? match[1] : null;
    }

    function getApiClient() {
        return window.ApiClient || (window.Dashboard && window.Dashboard.apiClient);
    }

    function buildUrl(itemId, includeResults) {
        var base = getApiClient() && getApiClient().getUrl
            ? getApiClient().getUrl('Wrestling/Items/' + itemId + '/Matches')
            : 'Wrestling/Items/' + itemId + '/Matches';

        return base + '?includeResults=' + (includeResults ? 'true' : 'false');
    }

    function fetchMatches(itemId, includeResults) {
        var client = getApiClient();
        if (client && client.ajax) {
            return client.ajax({
                type: 'GET',
                url: buildUrl(itemId, includeResults),
                dataType: 'json'
            });
        }

        return fetch(buildUrl(itemId, includeResults), { credentials: 'same-origin' }).then(function (response) {
            if (!response.ok) {
                throw new Error('No cached wrestling matches');
            }

            return response.json();
        });
    }

    function renderResults(panel, data) {
        panel.innerHTML = '';
        var list = document.createElement('ol');
        (data.Matches || data.matches || []).forEach(function (match) {
            var result = match.Result || match.result || 'Result unavailable';
            var participants = match.Participants || match.participants || '';
            var item = document.createElement('li');
            item.textContent = participants + ': ' + result;
            list.appendChild(item);
        });
        panel.appendChild(list);
    }

    function ensureButton() {
        if (document.getElementById(buttonId)) {
            return;
        }

        var itemId = getItemId();
        if (!itemId) {
            return;
        }

        fetchMatches(itemId, false).then(function (data) {
            var matches = data.Matches || data.matches || [];
            if (!matches.length || document.getElementById(buttonId)) {
                return;
            }

            var overview = document.querySelector('.overview, .itemOverview, [data-role="overview"]') || document.querySelector('.detailPageContent');
            if (!overview) {
                return;
            }

            var button = document.createElement('button');
            button.id = buttonId;
            button.className = 'raised emby-button';
            button.type = 'button';
            button.textContent = 'Show Results';

            var panel = document.createElement('div');
            panel.id = resultsId;
            panel.hidden = true;

            button.addEventListener('click', function () {
                fetchMatches(itemId, true).then(function (results) {
                    renderResults(panel, results);
                    panel.hidden = false;
                    button.hidden = true;
                });
            });

            overview.insertAdjacentElement('afterend', panel);
            overview.insertAdjacentElement('afterend', button);
        }).catch(function () {
            return null;
        });
    }

    var observer = new MutationObserver(function () {
        ensureButton();
    });

    observer.observe(document.documentElement, { childList: true, subtree: true });
    window.addEventListener('hashchange', ensureButton);
    ensureButton();
}());
