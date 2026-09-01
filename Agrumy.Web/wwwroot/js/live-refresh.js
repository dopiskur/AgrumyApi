// Roadmap #90: polls a server endpoint on a fixed interval and hands the raw HTML response to
// applyHtml to patch into the page - deliberately not a full page reload, so scroll position (and,
// for a DataTables-backed caller, its paging/search state) survives a refresh. Pauses while the
// tab is hidden (Page Visibility API) - no point polling a screen nobody is looking at - and
// refreshes immediately the moment it becomes visible again instead of waiting out a stale interval.
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
