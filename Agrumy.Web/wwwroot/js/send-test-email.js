// Sends through the SAVED Email settings (Save first, then test) - see ServerConfigController.TestEmail.
document.addEventListener('DOMContentLoaded', function () {
    const button = document.getElementById('testEmailButton');
    const recipientInput = document.getElementById('testEmailRecipient');
    const status = document.getElementById('testEmailStatus');
    if (!button || !recipientInput || !status) {
        return;
    }

    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';

    function setStatus(text, isError) {
        status.textContent = text;
        status.className = isError ? 'mt-2 text-danger' : 'mt-2 text-success';
    }

    button.addEventListener('click', async function () {
        const toEmail = recipientInput.value.trim();
        if (!toEmail) {
            setStatus('Enter a recipient address first.', true);
            return;
        }

        button.disabled = true;
        setStatus('Sending...', false);
        try {
            const response = await fetch('/ServerConfig/TestEmail?' + new URLSearchParams({ toEmail }), {
                method: 'POST',
                headers: { 'RequestVerificationToken': token },
            });
            if (response.ok) {
                setStatus('Sent - check the recipient inbox.', false);
            } else {
                setStatus((await response.text()) || 'Send failed.', true);
            }
        } catch {
            setStatus('Network error - test email not sent.', true);
        } finally {
            button.disabled = false;
        }
    });
});
