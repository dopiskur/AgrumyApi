// Four synchronized ways to set WeatherLocationLat/Lon: click the map, drag the marker, "My
// Location" (browser Geolocation), or type coordinates directly.
document.addEventListener('DOMContentLoaded', function () {
    const mapEl = document.getElementById('weatherLocationMap');
    const latInput = document.getElementById('WeatherLocationLat');
    const lonInput = document.getElementById('WeatherLocationLon');
    if (!mapEl || !latInput || !lonInput || typeof L === 'undefined') {
        return;
    }

    const locateButton = document.getElementById('weatherUseMyLocation');
    const status = document.getElementById('weatherLocationStatus');

    // Zagreb - same as the inputs' own placeholder values, so an unconfigured install centers
    // somewhere plausible instead of the middle of the Atlantic (Leaflet's implicit 0,0 default).
    const DEFAULT_LAT = 45.815;
    const DEFAULT_LON = 15.982;

    function parsedOrDefault(input, fallback) {
        const value = parseFloat(input.value);
        return Number.isFinite(value) ? value : fallback;
    }

    const startLat = parsedOrDefault(latInput, DEFAULT_LAT);
    const startLon = parsedOrDefault(lonInput, DEFAULT_LON);
    const hadStartingValue = latInput.value !== '' && lonInput.value !== '';

    const map = L.map(mapEl).setView([startLat, startLon], hadStartingValue ? 11 : 5);
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
    }).addTo(map);

    const marker = L.marker([startLat, startLon], { draggable: true }).addTo(map);

    function setStatus(text) {
        if (status) { status.textContent = text; }
    }

    // Shared by every input path (map click, drag, geolocation) so the marker/map/text-inputs
    // never fall out of sync regardless of which one triggered the change.
    function applyLocation(lat, lon, { panMap = true, moveMarker = true, updateInputs = true } = {}) {
        const rounded = { lat: Math.round(lat * 1e6) / 1e6, lon: Math.round(lon * 1e6) / 1e6 };
        if (updateInputs) {
            latInput.value = rounded.lat;
            lonInput.value = rounded.lon;
        }
        if (moveMarker) {
            marker.setLatLng([rounded.lat, rounded.lon]);
        }
        if (panMap) {
            map.panTo([rounded.lat, rounded.lon]);
        }
    }

    map.on('click', (e) => {
        applyLocation(e.latlng.lat, e.latlng.lng, { panMap: false });
    });

    marker.on('dragend', () => {
        const pos = marker.getLatLng();
        applyLocation(pos.lat, pos.lng, { panMap: false, moveMarker: false });
    });

    function onInputEdited() {
        const lat = parseFloat(latInput.value);
        const lon = parseFloat(lonInput.value);
        if (Number.isFinite(lat) && Number.isFinite(lon)) {
            applyLocation(lat, lon, { updateInputs: false });
        }
    }
    latInput.addEventListener('change', onInputEdited);
    lonInput.addEventListener('change', onInputEdited);

    if (locateButton) {
        locateButton.addEventListener('click', () => {
            if (!navigator.geolocation) {
                setStatus('Geolocation is not supported by this browser.');
                return;
            }
            setStatus('Locating…');
            navigator.geolocation.getCurrentPosition(
                (position) => {
                    applyLocation(position.coords.latitude, position.coords.longitude);
                    map.setView(marker.getLatLng(), 13);
                    setStatus('Location set from browser.');
                },
                (error) => {
                    setStatus(error.code === error.PERMISSION_DENIED
                        ? 'Location permission denied.'
                        : 'Could not determine location: ' + error.message);
                },
                { enableHighAccuracy: true, timeout: 10000 }
            );
        });
    }

    // The tab starts hidden (Bootstrap tab-pane), so Leaflet measured a zero-size container at
    // init - fix the tile grid up once the Weather tab is actually shown.
    document.querySelector('[data-bs-target="#tab-weather"]')?.addEventListener('shown.bs.tab', () => {
        map.invalidateSize();
    });
});
