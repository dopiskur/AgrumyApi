// File System Access API is Chromium-only and HTTPS-only (button disabled elsewhere when unavailable).
// Files come through this app's OfflineFile proxy, not straight from GitHub - a release asset's
// redirect target does not answer cross-origin fetches.
document.addEventListener('DOMContentLoaded', function () {
    const button = document.getElementById('buildOfflineRepo');
    if (!button) {
        return;
    }
    const progress = document.getElementById('offlineRepoProgress');
    const bar = progress.querySelector('.progress-bar');
    const status = document.getElementById('offlineRepoStatus');

    if (typeof window.showDirectoryPicker !== 'function') {
        button.disabled = true;
        button.title = 'Needs Chrome, Edge or Opera over HTTPS - use the tools/offline-repo script instead.';
        // A disabled button's hover title is easy to miss - show the reason as visible text too.
        const hint = document.getElementById('offlineRepoUnsupportedHint');
        if (hint) { hint.hidden = false; }
        return;
    }

    async function sha256Hex(buffer) {
        const hash = await crypto.subtle.digest('SHA-256', buffer);
        return Array.from(new Uint8Array(hash)).map(b => b.toString(16).padStart(2, '0')).join('');
    }

    function setProgress(done, total, text) {
        bar.style.width = total ? Math.round(done * 100 / total) + '%' : '0%';
        status.textContent = text;
    }

    button.addEventListener('click', async function () {
        let dir;
        try {
            dir = await window.showDirectoryPicker({ mode: 'readwrite' });
        } catch {
            return; // picker cancelled
        }

        button.disabled = true;
        progress.hidden = false;
        setProgress(0, 1, 'Reading catalog…');

        try {
            const manifest = await (await fetch(button.dataset.manifestUrl, { headers: { 'X-Requested-With': 'XMLHttpRequest' } })).json();
            const files = manifest.releases.flatMap(r => r.files);
            let done = 0;
            const failures = [];

            for (const release of manifest.releases) {
                for (const file of release.files) {
                    setProgress(done, files.length, `Downloading ${file.fileName}…`);
                    // The file name is the key the API resolves (the manifest carries no catalog ids).
                    const response = await fetch(`${button.dataset.fileUrl}?fileName=${encodeURIComponent(file.fileName)}`);
                    if (!response.ok) {
                        failures.push(`${file.fileName}: HTTP ${response.status}`);
                        done++;
                        continue;
                    }
                    const bytes = await response.arrayBuffer();
                    const actual = await sha256Hex(bytes);
                    if (file.sha256 && file.sha256.toLowerCase() !== actual) {
                        failures.push(`${file.fileName}: checksum mismatch after download`);
                        done++;
                        continue;
                    }
                    file.sha256 = actual;
                    file.sizeBytes = bytes.byteLength;
                    file.url = null; // USB layout: the file sits next to the manifest

                    const handle = await dir.getFileHandle(file.fileName, { create: true });
                    const writable = await handle.createWritable();
                    await writable.write(bytes);
                    await writable.close();
                    done++;
                }
            }

            manifest.source = 'agrumy-offline-usb';
            manifest.generatedAt = new Date().toISOString();
            const manifestHandle = await dir.getFileHandle('manifest.json', { create: true });
            const manifestWritable = await manifestHandle.createWritable();
            await manifestWritable.write(JSON.stringify(manifest, null, 2));
            await manifestWritable.close();

            setProgress(files.length, files.length, failures.length === 0
                ? `Done - ${done} file(s) + manifest.json written.`
                : `Finished with ${failures.length} problem(s): ${failures.join('; ')}`);
        } catch (err) {
            setProgress(0, 1, 'Failed: ' + (err && err.message ? err.message : err));
        } finally {
            button.disabled = false;
        }
    });
});
