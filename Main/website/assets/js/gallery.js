/* 图墙：JS 分配到最短列（避免 CSS columns 重排导致已加载的图跳位置） */
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

  lightbox.hidden = true;

  const tags = page.dataset.tags || '';
  const source = page.dataset.source || '';

  let currentPage = 1;
  let loading = false;
  let exhausted = false;
  let columns = [];
  let columnCount = 0;
  const allItems = [];

  const proxy = (url) => (url && url.startsWith('/')) ? url : '/api/gallery/image?url=' + encodeURIComponent(url);

  function desiredColumns() {
    const width = window.innerWidth;
    if (width < 760) return 2;
    if (width < 1100) return 3;
    return 4;
  }

  function rebuildColumns() {
    columnCount = desiredColumns();
    flow.textContent = '';
    columns = [];

    for (let i = 0; i < columnCount; i++) {
      const column = document.createElement('div');
      column.className = 'gallery-column';
      column.dataset.height = '0';
      flow.appendChild(column);
      columns.push(column);
    }
  }

  function shortestColumn() {
    let target = columns[0];
    let min = Number.MAX_SAFE_INTEGER;
    columns.forEach((column) => {
      const height = Number(column.dataset.height || 0);
      if (height < min) {
        min = height;
        target = column;
      }
    });
    return target;
  }

  function addToFlow(item) {
    const column = shortestColumn();
    const width = column.clientWidth || 300;
    const ratio = item.width && item.height ? item.height / item.width : 1.2;
    const estimated = width * ratio;

    const box = document.createElement('figure');
    box.className = 'gallery-card';
    box.style.aspectRatio = item.width && item.height ? (item.width + ' / ' + item.height) : '';

    const image = document.createElement('img');
    image.loading = 'lazy';
    image.decoding = 'async';
    image.alt = '';
    if (item.width && item.height) {
      image.style.aspectRatio = item.width + ' / ' + item.height;
    }
    image.src = proxy(item.previewUrl);
    image.addEventListener('click', () => open(item));
    image.addEventListener('error', () => box.remove());

    box.appendChild(image);
    column.appendChild(box);
    column.dataset.height = String(Number(column.dataset.height || 0) + estimated + 12);
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
      const url = '/api/gallery?page=' + currentPage +
        '&tags=' + encodeURIComponent(tags) +
        '&source=' + encodeURIComponent(source);

      const response = await fetch(url, { headers: { Accept: 'application/json' } });
      if (!response.ok) throw new Error('HTTP ' + response.status);

      const items = await response.json();
      if (!Array.isArray(items) || items.length === 0) {
        exhausted = true;
        if (status) status.textContent = '到底了，换个标签或换源试试。';
        return;
      }

      items.forEach((item) => {
        allItems.push(item);
        addToFlow(item);
      });

      currentPage += 1;
      if (status) status.textContent = '已加载 ' + allItems.length + ' 张，继续往下滚。';
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

  let resizeTimer = null;
  window.addEventListener('resize', () => {
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(() => {
      if (!allItems.length) return;
      rebuildColumns();
      allItems.forEach(addToFlow);
    }, 220);
  });

  document.getElementById('gallery-close').addEventListener('click', close);
  lightbox.addEventListener('click', (event) => {
    if (event.target === lightbox) close();
  });
  document.addEventListener('keydown', (event) => {
    if (event.key === 'Escape' && !lightbox.hidden) close();
  });

  rebuildColumns();
  loadMore();
})();
