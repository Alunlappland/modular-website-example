/* 玄关欢迎页的最小脚本：状态灯、表／里切换、装修中提示 */
(function () {
  'use strict';

  const THEME_KEY = 'genkan.theme';
  const root = document.documentElement;
  const toggle = document.getElementById('theme-toggle');
  const hint = document.getElementById('hint');
  const hintDefault = hint ? hint.textContent : '';

  function applyTheme(name) {
    const theme = name === 'inner' ? 'inner' : 'surface';
    root.setAttribute('data-theme', theme);
    if (toggle) toggle.textContent = theme === 'inner' ? '里' : '表';
    try { localStorage.setItem(THEME_KEY, theme); } catch (e) { /* 隐私模式忽略 */ }
  }

  let saved = 'surface';
  try { saved = localStorage.getItem(THEME_KEY) || 'surface'; } catch (e) { /* ignore */ }
  applyTheme(saved);

  if (toggle) {
    toggle.addEventListener('click', () => {
      applyTheme(root.getAttribute('data-theme') === 'inner' ? 'surface' : 'inner');
    });
  }

  // 装修中的入口：先别跳
  document.querySelectorAll('[data-soon="1"]').forEach((el) => {
    el.addEventListener('click', (ev) => {
      ev.preventDefault();
      if (!hint) return;
      hint.textContent = '「' + (el.querySelector('.entry-title')?.textContent || '这个房间') + '」还在装修。';
      hint.classList.add('warn');
      setTimeout(() => {
        hint.textContent = hintDefault;
        hint.classList.remove('warn');
      }, 2600);
    });
  });

  // 状态灯
  const dot = document.querySelector('#status .dot');
  const statusText = document.getElementById('status-text');
  const versionEl = document.getElementById('version');

  fetch('/healthz', { headers: { Accept: 'application/json' } })
    .then((r) => (r.ok ? r.json() : Promise.reject(r.status)))
    .then((data) => {
      if (dot) { dot.classList.remove('dot-wait', 'dot-bad'); dot.classList.add('dot-ok'); }
      if (statusText) statusText.textContent = '在线';
      if (versionEl && data.version) versionEl.textContent = 'Genkan ' + data.version;
    })
    .catch(() => {
      if (dot) { dot.classList.remove('dot-wait', 'dot-ok'); dot.classList.add('dot-bad'); }
      if (statusText) statusText.textContent = '离线';
    });
})();
