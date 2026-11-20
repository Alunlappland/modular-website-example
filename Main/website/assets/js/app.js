/* 全站小脚本：装修中的入口、提示气泡 */
(function () {
  'use strict';

  const hint = document.getElementById('hint');
  const hintDefault = hint ? hint.textContent.trim() : '';
  let timer = null;

  document.querySelectorAll('[data-soon="1"]').forEach((el) => {
    el.setAttribute('aria-disabled', 'true');
    el.addEventListener('click', (event) => {
      event.preventDefault();
      if (!hint) return;
      const title = el.querySelector('.entry-title')?.textContent?.replace('装修中', '').trim() || '这个房间';
      hint.textContent = '「' + title + '」还在装修，先别推门。';
      hint.classList.add('warn');
      clearTimeout(timer);
      timer = setTimeout(() => {
        hint.textContent = hintDefault;
        hint.classList.remove('warn');
      }, 2600);
    });
  });
})();
