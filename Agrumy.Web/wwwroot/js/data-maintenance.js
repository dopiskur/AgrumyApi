// Roadmap #126: "Optimize Old Data" / "Purge Old Data" on the Server Settings page. Both actions
// dispatch a background job server-side and return immediately (202) - this script's job ends at
// "request accepted", it does not poll for completion. Purge additionally needs, in order: (1) a
// typed-phrase confirmation (same "at least as strict as #92" gate the API itself re-checks), then
// (2) on MariaDB only, a separate "shrink files on disk?" dialog - drop_chunks() on Postgres/
// TimescaleDB always reclaims space with no extra step, so that dialog never shows there.
document.addEventListener('DOMContentLoaded', function () {
    const optimizeButton = document.getElementById('dataMaintenanceOptimize');
    const purgeButton = document.getElementById('dataMaintenancePurge');
    if (!optimizeButton || !purgeButton) {
        return;
    }

    const thresholdSelect = document.getElementById('dataMaintenanceThreshold');
    const status = document.getElementById('dataMaintenanceStatus');
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';

    const confirmModalEl = document.getElementById('purgeConfirmModal');
    const confirmPhraseInput = document.getElementById('purgeConfirmPhrase');
    const confirmSubmitButton = document.getElementById('purgeConfirmSubmit');
    const confirmModal = new bootstrap.Modal(confirmModalEl);

    const shrinkModalEl = document.getElementById('purgeShrinkModal');
    const shrinkModal = new bootstrap.Modal(shrinkModalEl);
    const shrinkYesButton = document.getElementById('purgeShrinkYes');
    const shrinkNoButton = document.getElementById('purgeShrinkNo');

    // Fetched once, up front, so the shrink dialog can be skipped entirely on Postgres without an
    // extra round trip in the middle of the confirm flow.
    let isMySql = false;
    fetch('/ServerConfig/DataMaintenanceProvider')
        .then(r => r.ok ? r.json() : null)
        .then(info => { if (info) { isMySql = info.isMySql; } })
        .catch(() => { /* falls back to isMySql=false - worst case, a skippable dialog is missed, not shown wrongly */ });

    function setStatus(text, isError) {
        status.textContent = text;
        status.className = isError ? 'mt-2 text-danger' : 'mt-2 text-success';
    }

    async function postJson(url, body) {
        const response = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
            body: JSON.stringify(body),
        });
        if (!response.ok) {
            throw new Error((await response.text()) || ('HTTP ' + response.status));
        }
    }

    optimizeButton.addEventListener('click', async function () {
        optimizeButton.disabled = true;
        try {
            await postJson('/ServerConfig/DataMaintenanceOptimize', { olderThanDays: Number(thresholdSelect.value) });
            setStatus('Optimize started in the background - check back later; this page does not wait for it to finish.', false);
        } catch (err) {
            setStatus('Optimize failed to start: ' + err.message, true);
        } finally {
            optimizeButton.disabled = false;
        }
    });

    purgeButton.addEventListener('click', function () {
        confirmPhraseInput.value = '';
        confirmSubmitButton.disabled = true;
        confirmModal.show();
    });

    confirmPhraseInput.addEventListener('input', function () {
        confirmSubmitButton.disabled = confirmPhraseInput.value !== 'PURGE';
    });

    confirmSubmitButton.addEventListener('click', function () {
        confirmModal.hide();
        if (isMySql) {
            shrinkModal.show();
        } else {
            submitPurge(false);
        }
    });

    shrinkYesButton.addEventListener('click', function () { shrinkModal.hide(); submitPurge(true); });
    shrinkNoButton.addEventListener('click', function () { shrinkModal.hide(); submitPurge(false); });

    async function submitPurge(shrinkAfterPurge) {
        purgeButton.disabled = true;
        try {
            await postJson('/ServerConfig/DataMaintenancePurge', {
                olderThanDays: Number(thresholdSelect.value),
                confirmationPhrase: 'PURGE',
                shrinkAfterPurge: shrinkAfterPurge,
            });
            setStatus('Purge started in the background - check back later; this page does not wait for it to finish.', false);
        } catch (err) {
            setStatus('Purge failed to start: ' + err.message, true);
        } finally {
            purgeButton.disabled = false;
        }
    }
});
