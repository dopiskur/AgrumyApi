// Renders one ECharts line chart for a single sensor metric.
// sensorPayload is the parsed { sensorData: [...] } object shared by SensorData/Index and
// SensorData/Report; each record's dateCreated is a full "YYYY-MM-DD HH:MM:SS" string.
function renderSensorChart(containerId, sensorPayload, fieldName, label) {
    var container = document.getElementById(containerId);
    if (!container) {
        return;
    }

    var sensorData = (sensorPayload && sensorPayload.sensorData) || [];
    var points = [];
    for (var i = 0; i < sensorData.length; i++) {
        var record = sensorData[i];
        var value = record[fieldName];
        if (value === undefined || value === null) {
            continue;
        }
        // MySQL's "YYYY-MM-DD HH:MM:SS" isn't reliably parsed by `new Date(...)` across
        // browsers - swapping in a "T" makes it a proper ISO 8601 local datetime.
        var dateValue = new Date(String(record.dateCreated).replace(' ', 'T'));
        points.push([dateValue, value]);
    }

    if (points.length === 0) {
        container.innerHTML = '<div class="text-center text-secondary py-5">Nema podataka za ovu metriku</div>';
        return;
    }

    var chart = echarts.init(container);
    chart.setOption({
        title: { text: label },
        tooltip: { trigger: 'axis' },
        xAxis: { type: 'time' },
        yAxis: { type: 'value' },
        dataZoom: [
            { type: 'inside' },
            { type: 'slider' }
        ],
        series: [{
            name: label,
            type: 'line',
            showSymbol: false,
            data: points
        }]
    });

    window.addEventListener('resize', function () {
        chart.resize();
    });
}
