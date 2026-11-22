/* 图墙：瀑布流 + 无限滚动 + 灯箱 */
(function () {
  'use strict';

  const page = document.getElementById('gallery');
  if (!page) return;

  const flow = document.getElementById('gallery-flow');
  const status = document.getElementById('gallery-status');
  const sentinel = document.getElementById('gallery-sentinel');
  const lightbox = document.getElementById('gallery-lightbox');
  const large = document.getElementById('gallery-large');
  const caption = document.getElementById('gallery-caption');

  const tags = page.dataset.tags || '';
  let currentPage = 1;
  let loading = false;
  let exhausted = false;

  const proxy = (url) => '/api/gallery/image?url=' + encodeURIComponent(url);

  function card(item) {
    const box = document.createElement('figure');
    box.className = 'gallery-card';

    const image = document.createElement('img');
    image.loading = 'lazy';
    image.decoding = 'async';
    image.alt = '';
    image.src = proxy(item.previewUrl);
    image.addEventListener('click', () => open(item));
    image.addEventListener('error', () => box.remove());

    box.appendChild(image);
    return box;
  }

  function open(item) {
    large.src = proxy(item.originalUrl || item.previewUrl);
    caption.textContent = item.tags ? item.tags.slice(0, 160) : '';
    lightbox.hidden = false;
    document.body.classList.add('gallery-locked');
  }

  function close() {
    lightbox.hidden = true;
    large.src = '';
    document.body.classList.remove('gallery-locked');
  }

  async function loadMore() {
    if (loading || exhausted) return;
    loading = true;
    if (status) status.textContent = '加载中…（第 ' + currentPage + ' 页）';

    try {
      const response = await fetch('/api/gallery?page=' + currentPage + '&tags=' + encodeURIComponent(tags), {
        headers: { Accept: 'application/json' },
      });

      if (!response.ok) throw new Error('HTTP ' + response.status);

      const items = await response.json();
      if (!Array.isArray(items) || items.length === 0) {
        exhausted = true;
        if (status) status.textContent = '到底了，换个标签试试。';
        return;
      }

      const fragment = document.createDocumentFragment();
      items.forEach((item) => fragment.appendChild(card(item)));
      flow.appendChild(fragment);

      currentPage += 1;
      if (status) status.textContent = '已加载 ' + flow.children.length + ' 张，继续往下滚。';
    } catch (error) {
      if (status) status.textContent = '加载失败：' + error.message + '（点一下这里重试）';
      if (status) status.addEventListener('click', loadMore, { once: true });
    } finally {
      loading = false;
    }
  }

  if ('IntersectionObserver' in window && sentinel) {
    const observer = new IntersectionObserver((entries) => {
      if (entries.some((entry) => entry.isIntersecting)) loadMore();
    }, { rootMargin: '600px 0px' });
    observer.observe(sentinel);
  } else {
    window.addEventListener('scroll', () => {
      if (window.innerHeight + window.scrollY >= document.body.offsetHeight - 600) loadMore();
    });
  }

  document.getElementById('gallery-close').addEventListener('click', close);
  lightbox.addEventListener('click', (event) => {
    if (event.target === lightbox) close();
  });
  document.addEventListener('keydown', (event) => {
    if (event.key === 'Escape' && !lightbox.hidden) close();
  });

  loadMore();
})();
