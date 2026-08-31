// Renders one ECharts line chart for a single sensor metric.
// sensorPayload is the parsed { sensorData: [...] } object shared by SensorData/Index and
// SensorData/Report; each record's dateCreated is a full "YYYY-MM-DD HH:MM:SS" string.

// One resize listener for the whole page instead of one per chart - the pages render up to
// seven charts from the same payload.
const agrumyCharts = [];
window.addEventListener('resize', () => agrumyCharts.forEach(c => c.resize()));

function renderSensorChart(containerId, sensorPayload, fieldName, label) {
    const container = document.getElementById(containerId);
    if (!container) {
        return;
    }

    const sensorData = (sensorPayload && sensorPayload.sensorData) || [];
    const points = [];
    for (const record of sensorData) {
        const value = record[fieldName];
        if (value === undefined || value === null) {
            continue;
        }
        // MySQL's "YYYY-MM-DD HH:MM:SS" isn't reliably parsed by `new Date(...)` across
        // browsers - swapping in a "T" makes it a proper ISO 8601 local datetime.
        const dateValue = new Date(String(record.dateCreated).replace(' ', 'T'));
        points.push([dateValue, value]);
    }

    if (points.length === 0) {
        container.innerHTML = '<div class="text-center text-secondary py-5">No data for this metric</div>';
        return;
    }

    const chart = echarts.init(container);
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
    agrumyCharts.push(chart);
}
