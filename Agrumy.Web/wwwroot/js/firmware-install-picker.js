// Each <option>'s value IS its manifest URL, so no board-name-to-URL mapping lives in JS. Setting
// the .manifest property (not just the attribute) is what makes esp-web-tools re-fetch it.

document.addEventListener('DOMContentLoaded', function () {
    var select = document.getElementById('flashBoardSelect');
    var installButton = document.getElementById('flashInstallButton');
    if (!select || !installButton) {
        return;
    }
    select.addEventListener('change', function () {
        installButton.manifest = select.value;
    });
});

// esp-web-tools' own install dialog (<ewt-install-dialog>) is appended straight to document.body,
// not inside our button, and is removed the moment the user closes it - without this, the page
// looks identical to before flashing the instant that popup goes away, with nothing to remind the
// admin anything happened. The dialog's "closed" event bubbles AND is composed (crosses its shadow
// root) - the library's own connect.js relies on that same event to close the serial port - so a
// document-level listener catches it without patching the vendored library. There is no public
// success/failure signal on this event (only an internal, unexposed flash-progress state), so the
// banner is worded as "session ended", not "succeeded" - it would be misleading to claim more than
// this can actually tell.
document.addEventListener('closed', function (ev) {
    if (!ev.target || ev.target.tagName !== 'EWT-INSTALL-DIALOG') {
        return;
    }
    var banner = document.getElementById('flashResultBanner');
    var text = document.getElementById('flashResultText');
    if (!banner || !text) {
        return;
    }
    text.textContent = 'Flash session ended at ' + new Date().toLocaleTimeString() +
        ' - reconnect the device and check its reported firmware version to confirm the install succeeded.';
    banner.hidden = false;
});
