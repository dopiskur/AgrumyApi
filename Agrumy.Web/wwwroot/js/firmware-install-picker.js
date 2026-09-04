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
