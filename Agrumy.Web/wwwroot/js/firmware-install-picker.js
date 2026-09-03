// Roadmap #155: keeps the single esp-web-install-button's manifest in sync with the board dropdown
// - each <option>'s value IS its manifest URL (InstallManifest(board)), so no board-name-to-URL
// mapping needs to live in JS. Setting the property (not just the attribute) is what makes
// esp-web-tools actually re-fetch the new manifest - see esp-web-tools' README.

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
