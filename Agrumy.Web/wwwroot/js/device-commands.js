// Roadmap #34: wires every [data-device-command] button (see Views/Shared/_DeviceCommandButtons)
// to POST /Device/IssueCommand. Attached once here (not per-partial) so a page with several
// buttons - e.g. one command group per zone - never ends up with duplicate listeners from a
// partial being rendered more than once.
document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('[data-device-command]').forEach(function (button) {
        button.addEventListener('click', async function () {
            const originalText = button.innerHTML;
            button.disabled = true;
            button.innerHTML = 'Sending...';

            const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
            const params = new URLSearchParams({
                targetType: button.dataset.targetType,
                targetId: button.dataset.targetId,
                actionType: button.dataset.actionType,
            });

            try {
                const response = await fetch('/Device/IssueCommand?' + params.toString(), {
                    method: 'POST',
                    headers: { 'RequestVerificationToken': token },
                });
                if (response.ok) {
                    // Left disabled - the command is now queued server-side; a fresh page load is
                    // the way to issue another one, same as every other action button on this page.
                    button.innerHTML = 'Queued';
                } else {
                    const body = await response.text();
                    button.disabled = false;
                    button.innerHTML = originalText;
                    alert(body || 'Command failed.');
                }
            } catch {
                button.disabled = false;
                button.innerHTML = originalText;
                alert('Network error - command not sent.');
            }
        });
    });
});
