/* 仪表盘轮询：每 2 秒拉一次 /api/sys 并更新 */
(function () {
  'use strict';

  const $ = (selector) => document.querySelector(selector);

  const setText = (selector, value) => {
    const el = $(selector);
    if (el) el.textContent = value;
  };

  const setWidth = (selector, value) => {
    const el = $(selector);
    if (el) el.style.width = Math.max(0, Math.min(100, value)) + '%';
  };

  const gb = (bytes) => (bytes / 1024 / 1024 / 1024).toFixed(1);
  const kb = (value) => (value >= 1024 ? (value / 1024).toFixed(1) + ' MB/s' : value.toFixed(0) + ' KB/s');

  const uptime = (seconds) => {
    const days = Math.floor(seconds / 86400);
    const hours = Math.floor((seconds % 86400) / 3600);
    const minutes = Math.floor((seconds % 3600) / 60);
    if (days > 0) return '已运行 ' + days + ' 天 ' + hours + ' 小时';
    if (hours > 0) return '已运行 ' + hours + ' 小时 ' + minutes + ' 分';
    return '已运行 ' + minutes + ' 分';
  };

  function renderDisks(disks) {
    const box = $('#disks');
    if (!box || !disks) return;
    box.innerHTML = disks.map((disk) => {
      const used = disk.totalBytes - disk.freeBytes;
      const percent = disk.totalBytes ? (used * 100) / disk.totalBytes : 0;
      const totalGb = gb(disk.totalBytes);
      return '<div class="disk">' +
        '<div class="disk-head"><span>' + disk.name + ' <em>' + disk.label + '</em></span>' +
        '<span>' + gb(disk.freeBytes) + ' GB 可用 / ' + totalGb + ' GB</span></div>' +
        '<div class="bar"><i style="width:' + percent.toFixed(1) + '%"></i></div>' +
        '</div>';
    }).join('');
  }

  function renderProcesses(processes) {
    const body = $('#proc-body');
    if (!body || !processes) return;
    body.innerHTML = processes.map((process) =>
      '<tr><td>' + process.name + '</td><td>' + process.pid + '</td><td>' + process.workingSetMb + ' MB</td></tr>'
    ).join('');
  }

  function renderServices(services) {
    const box = $('#svc-list');
    if (!box || !services) return;
    box.innerHTML = services.map((service) =>
      '<li><span>' + service.name + '</span><b class="tag ' + (service.state === 'Running' ? 'on' : 'off') + '">' +
      service.state + '</b></li>'
    ).join('');
  }

  async function tick() {
    try {
      const response = await fetch('/api/sys', { headers: { Accept: 'application/json' } });
      if (!response.ok) return;
      const snapshot = await response.json();

      setText('#cpu-val', snapshot.cpuPercent.toFixed(1) + '%');
      setWidth('#cpu-bar', snapshot.cpuPercent);
      setText('#mem-val', snapshot.memoryUsedPercent.toFixed(1) + '%');
      setWidth('#mem-bar', snapshot.memoryUsedPercent);
      setText('#mem-sub', (snapshot.memoryFreeMb / 1024).toFixed(1) + ' GB 可用 / ' + (snapshot.memoryTotalMb / 1024).toFixed(1) + ' GB');
      setText('#net-val', '↓ ' + kb(snapshot.rxKbPerSec));
      setText('#net-sub', '↑ ' + kb(snapshot.txKbPerSec));
      setText('#proc-count', snapshot.processCount);
      setText('#uptime', uptime(snapshot.uptimeSeconds));

      renderDisks(snapshot.disks);
      renderProcesses(snapshot.processes);
      renderServices(snapshot.services);
    } catch (error) {
      /* 网络抖动时静默，下一轮再试 */
    }
  }

  tick();
  setInterval(tick, 2000);
})();
