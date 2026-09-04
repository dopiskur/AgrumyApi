// Patches applyHtml into the page instead of a full reload, so scroll position and (for a
// DataTables-backed caller) paging/search state survive a refresh. Pauses via the Page Visibility
// API while the tab is hidden, and refreshes immediately once it becomes visible again.
function startLiveRefresh({ url, applyHtml, intervalMs = 10000 }) {
    let timerId = null;

    async function refresh() {
        let html;
        try {
            const response = await fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            if (!response.ok) {
                return; // transient server/network hiccup - next tick tries again
            }
            html = await response.text();
        } catch {
            return; // offline - same reasoning as above
        }
        applyHtml(html);
    }

    function start() {
        if (timerId === null) {
            timerId = setInterval(refresh, intervalMs);
        }
    }

    function stop() {
        if (timerId !== null) {
            clearInterval(timerId);
            timerId = null;
        }
    }

    document.addEventListener('visibilitychange', function () {
        if (document.hidden) {
            stop();
        } else {
            refresh();
            start();
        }
    });

    if (!document.hidden) {
        start();
    }
}
