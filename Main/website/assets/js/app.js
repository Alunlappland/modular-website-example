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

/* �ֻ������ࡹ���� */
(function () {
  'use strict';

  const toggle = document.getElementById('more-toggle');
  const drawer = document.getElementById('mdrawer');
  const backdrop = document.getElementById('mdrawer-backdrop');
  const close = document.getElementById('more-close');
  if (!toggle || !drawer) return;

  function open() {
    drawer.hidden = false;
    toggle.setAttribute('aria-expanded', 'true');
    document.body.style.overflow = 'hidden';
  }

  function hide() {
    drawer.hidden = true;
    toggle.setAttribute('aria-expanded', 'false');
    document.body.style.overflow = '';
  }

  toggle.addEventListener('click', () => (drawer.hidden ? open() : hide()));
  if (backdrop) backdrop.addEventListener('click', hide);
  if (close) close.addEventListener('click', hide);

  document.addEventListener('keydown', (event) => {
    if (event.key === 'Escape' && !drawer.hidden) hide();
  });
})();
