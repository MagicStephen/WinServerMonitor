// Hover crosshair + tooltip for LineChart components.
// Uses event delegation, so charts re-rendered by Blazor need no JS interop.
(function () {
    function hide(chart) {
        chart.querySelector('.chart-crosshair').hidden = true;
        chart.querySelector('.chart-tooltip').hidden = true;
    }

    function show(chart, clientX) {
        const labels = JSON.parse(chart.dataset.labels || '[]');
        if (labels.length === 0) { hide(chart); return; }
        const series = JSON.parse(chart.dataset.series || '[]');
        const [padLeft, plotWidth, width, padTop, plotHeight, height] = chart.dataset.geom.split(',').map(Number);

        const svg = chart.querySelector('svg');
        const rect = svg.getBoundingClientRect();
        const scale = rect.width / width;
        const vx = (clientX - rect.left) / scale;
        const ratio = Math.min(1, Math.max(0, (vx - padLeft) / plotWidth));
        const index = Math.round(ratio * (labels.length - 1));
        const x = padLeft + (labels.length > 1 ? plotWidth * index / (labels.length - 1) : 0);

        const crosshair = chart.querySelector('.chart-crosshair');
        crosshair.style.left = (x * scale) + 'px';
        crosshair.style.top = (padTop * scale) + 'px';
        crosshair.style.height = (plotHeight * scale) + 'px';
        crosshair.hidden = false;

        const tooltip = chart.querySelector('.chart-tooltip');
        tooltip.replaceChildren();
        const title = document.createElement('div');
        title.className = 'tt-title';
        title.textContent = labels[index];
        tooltip.appendChild(title);
        for (const s of series) {
            const row = document.createElement('div');
            row.className = 'tt-row';
            const sw = document.createElement('span');
            sw.className = 'swatch';
            sw.style.background = `var(--series-${s.slot})`;
            const name = document.createElement('span');
            name.textContent = s.name;
            const value = document.createElement('strong');
            value.textContent = s.values[index] ?? '–';
            row.append(sw, name, value);
            tooltip.appendChild(row);
        }
        tooltip.hidden = false;
        const flip = x * scale > rect.width * 0.6;
        tooltip.style.left = flip ? '' : (x * scale + 12) + 'px';
        tooltip.style.right = flip ? (rect.width - x * scale + 12) + 'px' : '';
        tooltip.style.top = (padTop * scale) + 'px';
    }

    document.addEventListener('pointermove', e => {
        const chart = e.target.closest && e.target.closest('.wsm-chart');
        document.querySelectorAll('.wsm-chart').forEach(c => { if (c !== chart) hide(c); });
        if (chart) show(chart, e.clientX);
    });
    document.addEventListener('pointerleave', () => document.querySelectorAll('.wsm-chart').forEach(hide));
})();

window.wsm = window.wsm || {};
window.wsm.scrollToBottom = function (element) {
    if (element) element.scrollTop = element.scrollHeight;
};
