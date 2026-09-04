// The Divider Designer is a one-time UX aid to fill R1/R2 (always stored in ohms) - never a
// separate storage mechanism. MAX17048 ignores R1/R2 entirely, hence the show/hide below.

document.addEventListener('DOMContentLoaded', function () {
    var script = document.querySelector('script[data-sensor-battery-select-id]');
    if (!script) {
        return;
    }
    var voltageDividerId = script.getAttribute('data-voltage-divider-id');
    var sensorBatterySelect = document.getElementById(script.getAttribute('data-sensor-battery-select-id'));
    var fieldsBlock = document.getElementById('battery-divider-fields');
    if (!sensorBatterySelect || !fieldsBlock) {
        return;
    }

    function syncVisibility() {
        fieldsBlock.hidden = sensorBatterySelect.value !== voltageDividerId;
    }
    sensorBatterySelect.addEventListener('change', syncVisibility);
    syncVisibility();

    // ---- Divider Designer ------------------------------------------------------------------

    var nominalSelect = document.getElementById('divider-nominal-voltage');
    var customWrap = document.getElementById('divider-custom-voltage-wrap');
    var customInput = document.getElementById('divider-custom-voltage');
    var recommendBtn = document.getElementById('divider-recommend-btn');
    var recommendationText = document.getElementById('divider-recommendation');
    var useValuesBtn = document.getElementById('divider-use-values-btn');
    var preset1to1Btn = document.getElementById('divider-preset-1to1-btn');
    var r1Input = document.getElementById('battery-divider-r1');
    var r2Input = document.getElementById('battery-divider-r2');
    if (!nominalSelect || !recommendBtn) {
        return;
    }

    // E12 series (kOhm) from 100k up - kept at 100k+ to stay well clear of the ESP32 ADC pin's
    // input impedance and keep divider current negligible (microamps, not a meaningful battery drain).
    var standardKOhm = [100, 120, 150, 180, 220, 270, 330, 390, 470, 560, 680, 820,
        1000, 1200, 1500, 1800, 2200, 2700, 3300, 3900, 4700, 5600, 6800, 8200, 10000];
    var recommendedR1Ohm = null;
    var recommendedR2Ohm = null;

    nominalSelect.addEventListener('change', function () {
        customWrap.hidden = nominalSelect.value !== 'custom';
    });

    recommendBtn.addEventListener('click', function () {
        var nominalVoltage = nominalSelect.value === 'custom'
            ? parseFloat(customInput.value)
            : parseFloat(nominalSelect.value);
        if (!nominalVoltage || nominalVoltage <= 0) {
            recommendationText.textContent = 'Enter a valid nominal voltage.';
            useValuesBtn.disabled = true;
            return;
        }

        // LiPo packs charge to ~1.135x their nominal per-cell voltage (4.2V / 3.7V) - scale the
        // same way regardless of cell count so a 2S/3S pack's peak is estimated consistently.
        var estimatedMaxVoltage = nominalVoltage * (4.2 / 3.7);
        // Target divided-down reading at that peak comfortably under the ESP32 ADC's 3.3V max -
        // 3.0V leaves headroom for measurement error/spikes without needing attenuation math here.
        var requiredRatio = estimatedMaxVoltage / 3.0;

        var r2KOhm = 100; // fixed reference leg - only R1 varies to hit the ratio
        var r1KOhm = standardKOhm[standardKOhm.length - 1];
        for (var i = 0; i < standardKOhm.length; i++) {
            if ((standardKOhm[i] + r2KOhm) / r2KOhm >= requiredRatio) {
                r1KOhm = standardKOhm[i];
                break;
            }
        }

        recommendedR1Ohm = r1KOhm * 1000;
        recommendedR2Ohm = r2KOhm * 1000;
        recommendationText.textContent = 'Recommended: R1=' + r1KOhm + 'kΩ, R2=' + r2KOhm + 'kΩ '
            + '(divided reading at ' + estimatedMaxVoltage.toFixed(2) + 'V full charge: '
            + (estimatedMaxVoltage * r2KOhm / (r1KOhm + r2KOhm)).toFixed(2) + 'V).';
        useValuesBtn.disabled = false;
    });

    useValuesBtn.addEventListener('click', function () {
        if (recommendedR1Ohm === null) {
            return;
        }
        r1Input.value = recommendedR1Ohm;
        r2Input.value = recommendedR2Ohm;
    });

    preset1to1Btn.addEventListener('click', function () {
        r1Input.value = 100000;
        r2Input.value = 100000;
    });
});
