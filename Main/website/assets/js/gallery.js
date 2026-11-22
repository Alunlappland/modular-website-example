/* 图墙：JS 分列 + 收藏 + 屏蔽标签 + 搜索历史 */
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
  const lightboxFav = document.getElementById('gallery-lightbox-fav');
  const lightboxOpen = document.getElementById('gallery-lightbox-open');

  lightbox.hidden = true;

  const tags = page.dataset.tags || '';
  const source = page.dataset.source || '';
  const view = page.dataset.view || 'all';

  const state = {
    blocked: new Set(),
    favorites: new Set(),
    items: [],
    current: null,
    page: 1,
    loading: false,
    exhausted: false,
    columns: [],
    columnCount: 0,
  };

  const proxy = (url) => (url && url.startsWith('/')) ? url : '/api/gallery/image?url=' + encodeURIComponent(url);
  const keyOf = (item) => item.source + ':' + item.id;

  function desiredColumns() {
    const width = window.innerWidth;
    if (width < 760) return 2;
    if (width < 1100) return 3;
    return 4;
  }

  function rebuildColumns() {
    state.columnCount = desiredColumns();
    flow.textContent = '';
    state.columns = [];

    for (let i = 0; i < state.columnCount; i++) {
      const column = document.createElement('div');
      column.className = 'gallery-column';
      column.dataset.height = '0';
      flow.appendChild(column);
      state.columns.push(column);
    }
  }

  function shortestColumn() {
    let target = state.columns[0];
    let min = Number.MAX_SAFE_INTEGER;
    state.columns.forEach((column) => {
      const height = Number(column.dataset.height || 0);
      if (height < min) { min = height; target = column; }
    });
    return target;
  }

  function isBlocked(item) {
    if (!item.tags) return false;
    const words = String(item.tags).toLowerCase().split(/\s+/);
    for (const tag of state.blocked) {
      if (words.includes(tag.toLowerCase())) return true;
    }
    return false;
  }

  function addToFlow(item) {
    const column = shortestColumn();
    const width = column.clientWidth || 300;
    const ratio = item.width && item.height ? item.height / item.width : 1.2;
    const estimated = width * ratio;

    const box = document.createElement('figure');
    box.className = 'gallery-card';
    box.dataset.key = keyOf(item);

    const image = document.createElement('img');
    image.loading = 'lazy';
    image.decoding = 'async';
    image.alt = '';
    if (item.width && item.height) image.style.aspectRatio = item.width + ' / ' + item.height;
    image.src = proxy(item.previewUrl);
    image.addEventListener('click', () => open(item));
    image.addEventListener('error', () => box.remove());

    const star = document.createElement('button');
    star.type = 'button';
    star.className = 'gallery-star' + (state.favorites.has(keyOf(item)) ? ' on' : '');
    star.textContent = state.favorites.has(keyOf(item)) ? '♥' : '♡';
    star.title = '收藏';
    star.addEventListener('click', (event) => {
      event.stopPropagation();
      toggleFavorite(item, star);
    });

    box.appendChild(image);
    box.appendChild(star);
    column.appendChild(box);
    column.dataset.height = String(Number(column.dataset.height || 0) + estimated + 12);
  }

  async function toggleFavorite(item, button) {
    const key = keyOf(item);
    const isFav = state.favorites.has(key);

    try {
      if (isFav) {
        await fetch('/api/gallery/favorite?key=' + encodeURIComponent(key), { method: 'DELETE' });
        state.favorites.delete(key);
      } else {
        await fetch('/api/gallery/favorite', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            id: item.id,
            imageKey: key,
            previewUrl: item.previewUrl,
            originalUrl: item.originalUrl,
            source: item.source,
            tags: item.tags,
          }),
        });
        state.favorites.add(key);
      }

      if (button) {
        const on = state.favorites.has(key);
        button.classList.toggle('on', on);
        button.textContent = on ? '♥' : '♡';
      }

      if (state.current && keyOf(state.current) === key) {
        updateLightboxFavorite();
      }
    } catch (error) {
      window.alert('操作失败：' + error.message);
    }
  }

  function updateLightboxFavorite() {
    if (!state.current) return;
    const on = state.favorites.has(keyOf(state.current));
    lightboxFav.textContent = on ? '♥ 已收藏' : '♥ 收藏';
    lightboxFav.classList.toggle('on', on);
  }

  function renderTags(item) {
    caption.textContent = '';
    const words = (item.tags || '').split(/\s+/).filter(Boolean).slice(0, 24);

    words.forEach((word) => {
      const chip = document.createElement('button');
      chip.type = 'button';
      chip.className = 'gallery-tag';
      chip.textContent = word;
      chip.title = '屏蔽这个标签';
      chip.addEventListener('click', async () => {
        if (!window.confirm('屏蔽标签「' + word + '」？之后不再显示带这个标签的图。')) return;
        await fetch('/api/gallery/block', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ tag: word }),
        });
        state.blocked.add(word);
        renderBlocked();
        removeBlockedCards();
      });
      caption.appendChild(chip);
    });
  }

  function open(item) {
    state.current = item;
    large.src = proxy(item.originalUrl || item.previewUrl);
    renderTags(item);
    updateLightboxFavorite();
    lightboxOpen.href = proxy(item.originalUrl || item.previewUrl);
    lightbox.hidden = false;
    document.body.classList.add('gallery-locked');
  }

  function close() {
    lightbox.hidden = true;
    large.src = '';
    document.body.classList.remove('gallery-locked');
  }

  function removeBlockedCards() {
    flow.querySelectorAll('.gallery-card').forEach((card) => {
      const model = state.items.find((item) => keyOf(item) === card.dataset.key);
      if (model && isBlocked(model)) card.remove();
    });
    if (state.current && isBlocked(state.current)) close();
  }

  function renderBlocked() {
    const box = document.getElementById('gallery-blocked');
    const list = document.getElementById('gallery-blocked-list');
    if (!box || !list) return;

    list.textContent = '';
    box.hidden = state.blocked.size === 0;

    state.blocked.forEach((tag) => {
      const chip = document.createElement('button');
      chip.type = 'button';
      chip.className = 'chip on gallery-unblock';
      chip.textContent = tag + ' ×';
      chip.title = '取消屏蔽';
      chip.addEventListener('click', async () => {
        await fetch('/api/gallery/block?tag=' + encodeURIComponent(tag), { method: 'DELETE' });
        state.blocked.delete(tag);
        renderBlocked();
      });
      list.appendChild(chip);
    });
  }

  function renderHistory(items) {
    const box = document.getElementById('gallery-history');
    const list = document.getElementById('gallery-history-list');
    if (!box || !list) return;

    list.textContent = '';
    box.hidden = !items || items.length === 0;

    (items || []).forEach((entry) => {
      const link = document.createElement('a');
      link.className = 'chip';
      link.href = '/gallery?tags=' + encodeURIComponent(entry.query) +
        '&source=' + encodeURIComponent(entry.source || source);
      link.textContent = entry.query;
      list.appendChild(link);
    });
  }

  async function loadPrefs() {
    try {
      const response = await fetch('/api/gallery/prefs', { headers: { Accept: 'application/json' } });
      if (!response.ok) return;

      const data = await response.json();
      state.blocked = new Set(data.blockedTags || []);
      state.favorites = new Set(data.favorites || []);
      renderBlocked();
      renderHistory(data.history);
    } catch (error) { /* 偏好加载失败不影响看图 */ }
  }

  async function loadMore() {
    if (state.loading || state.exhausted) return;
    state.loading = true;
    if (status) status.textContent = '加载中…（第 ' + state.page + ' 页）';

    try {
      const url = view === 'favorites'
        ? '/api/gallery/favorites'
        : '/api/gallery?page=' + state.page +
          '&tags=' + encodeURIComponent(tags) +
          '&source=' + encodeURIComponent(source);

      const response = await fetch(url, { headers: { Accept: 'application/json' } });
      if (!response.ok) throw new Error('HTTP ' + response.status);

      const items = await response.json();
      if (!Array.isArray(items) || items.length === 0) {
        state.exhausted = true;
        if (status) {
          status.textContent = view === 'favorites' ? '还没有收藏。看到喜欢的点 ♡。' : '到底了，换个标签或换源试试。';
        }
        return;
      }

      let shown = 0;
      items.forEach((item) => {
        state.items.push(item);
        if (!isBlocked(item)) {
          addToFlow(item);
          shown++;
        }
      });

      state.page += 1;
      if (status) {
        status.textContent = '已加载 ' + flow.children.length * 1 + ' 张' +
          (state.blocked.size ? '（已过滤屏蔽标签）' : '') + '，继续往下滚。';
      }

      if (view === 'favorites') state.exhausted = true;
    } catch (error) {
      if (status) status.textContent = '加载失败：' + error.message + '（点一下这里重试）';
      if (status) status.addEventListener('click', loadMore, { once: true });
    } finally {
      state.loading = false;
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
      if (!state.items.length) return;
      rebuildColumns();
      state.items.filter((item) => !isBlocked(item)).forEach(addToFlow);
    }, 220);
  });

  document.getElementById('gallery-close').addEventListener('click', close);
  lightbox.addEventListener('click', (event) => {
    if (event.target === lightbox) close();
  });
  document.addEventListener('keydown', (event) => {
    if (event.key === 'Escape' && !lightbox.hidden) close();
  });

  lightboxFav.addEventListener('click', () => {
    if (state.current) toggleFavorite(state.current, null);
  });

  (async () => {
    await loadPrefs();
    rebuildColumns();
    await loadMore();
  })();
})();
