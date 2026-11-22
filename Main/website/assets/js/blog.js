/* åšå®¢äº¤äº’ï¼šååº”ã€è¡Œçº§æ³¨é‡Šã€å†™ä½œé¢„è§ˆ */
(function () {
  'use strict';

  const csrfMeta = document.querySelector('meta[name="csrf"]');
  const csrf = csrfMeta ? csrfMeta.content : '';
  const headers = { 'Content-Type': 'application/json', 'RequestVerificationToken': csrf };

  const article = document.querySelector('.post[data-slug]');
  const slug = article ? article.dataset.slug : null;

  // ---- ç‚¹èµ / ç‚¹è¸© ----
  if (slug) {
    document.querySelectorAll('.react').forEach((button) => {
      button.addEventListener('click', async () => {
        try {
          const response = await fetch('/api/blog/' + slug + '/react', {
            method: 'POST',
            headers,
            body: JSON.stringify({ kind: button.dataset.kind }),
          });
          if (!response.ok) return;
          const counts = await response.json();
          const like = document.getElementById('like-count');
          const dislike = document.getElementById('dislike-count');
          if (like) like.textContent = counts.likes;
          if (dislike) dislike.textContent = counts.dislikes;
          button.classList.add('pressed');
        } catch (error) {
          /* å¿½ç•¥ç½‘ç»œé”™è¯¯ */
        }
      });
    });
  }

  // ---- è¡Œçº§æ³¨é‡Š ----
  const pop = document.getElementById('anno-pop');
  const form = document.getElementById('anno-form');
  const status = document.getElementById('anno-status');

  if (pop && form) {
    const blockIndex = form.elements['blockIndex'];
    const blockHash = form.elements['blockHash'];
    const author = form.elements['author'];
    const body = form.elements['body'];

    document.querySelectorAll('.anno-btn').forEach((button) => {
      button.addEventListener('click', () => {
        pop.hidden = false;
        blockIndex.value = button.dataset.block;
        blockHash.value = button.dataset.hash;
        if (status) status.textContent = '';
        author.focus();
      });
    });

    const cancel = document.getElementById('anno-cancel');
    if (cancel) {
      cancel.addEventListener('click', () => { pop.hidden = true; });
    }

    form.addEventListener('submit', async (event) => {
      event.preventDefault();
      if (!slug) return;

      try {
        const response = await fetch('/api/blog/' + slug + '/annotations', {
          method: 'POST',
          headers,
          body: JSON.stringify({
            blockIndex: Number(blockIndex.value),
            blockHash: blockHash.value,
            author: author.value,
            body: body.value,
          }),
        });

        if (response.ok) {
          location.reload();
          return;
        }

        const text = await response.text();
        if (status) status.textContent = text || 'æäº¤å¤±è´¥ï¼Œç¨åå†è¯•ã€‚';
      } catch (error) {
        if (status) status.textContent = 'ç½‘ç»œé”™è¯¯ï¼Œç¨åå†è¯•ã€‚';
      }
    });
  }

  // ---- ç®¡ç†å‘˜å®¡æ ¸/åˆ é™¤ï¼ˆAJAXï¼Œä¸äº§ç”Ÿé¡µé¢å†å²ï¼‰----
  document.addEventListener('click', async (event) => {
    const button = event.target.closest('[data-action]');
    if (!button) return;

    const container = button.closest('.mod');
    if (!container || !slug) return;

    event.preventDefault();

    const kind = container.dataset.kind;
    const id = container.dataset.id;
    const action = button.dataset.action;
    const base = kind === 'comment' ? 'comments' : 'annotations';

    try {
      const response = await fetch('/api/blog/' + slug + '/' + base + '/' + id + '/moderate', {
        method: 'POST',
        headers,
        body: JSON.stringify({ action }),
      });
      if (!response.ok) return;

      const item = container.closest('li');
      const block = container.closest('.md-block');

      if (action === 'delete') {
        if (item) item.remove();
      } else {
        if (item) item.classList.remove('pending');
        const pending = item ? item.querySelector('em') : null;
        if (pending) pending.remove();
        container.querySelectorAll('[data-action="approve"], [data-action="reject"]').forEach((el) => el.remove());
      }

      if (block) {
        const list = block.querySelector('.anno-list');
        if (list && list.querySelectorAll('li').length === 0) list.remove();

        const annoButton = block.querySelector('.anno-btn');
        if (annoButton) {
          const count = block.querySelectorAll('.anno-list li').length;
          annoButton.textContent = 'ï¼‹' + (count > 0 ? count : '');
        }
      }
    } catch (error) {
      /* ç½‘ç»œé”™è¯¯æ—¶ä¿æŒåŸçŠ¶ */
    }
  });

  // ---- å†™ä½œé¡µå®æ—¶é¢„è§ˆ ----
  const textarea = document.querySelector('textarea[name="Body"]');
  const preview = document.getElementById('preview');

  if (textarea && preview) {
    let timer = null;

    const render = async () => {
      try {
        const response = await fetch('/api/blog/preview', {
          method: 'POST',
          headers,
          body: JSON.stringify({ markdown: textarea.value }),
        });
        if (!response.ok) return;
        const blocks = await response.json();
        preview.innerHTML = blocks
          .map((block) => '<section class="md-block"><div class="md-content">' + block.html + '</div></section>')
          .join('');
      } catch (error) {
        /* å¿½ç•¥ */
      }
    };

    textarea.addEventListener('input', () => {
      clearTimeout(timer);
      timer = setTimeout(render, 350);
    });

    render();
  }
})();

/* ÎÄÕÂÄÚËÑË÷£ºCtrl+F ´ò¿ªÒ³ÃæËÑË÷Ìõ£¬¸ßÁÁËùÓĞÃüÖĞ²¢¿ÉÌø×ª */
(function () {
  'use strict';

  const body = document.querySelector('.post-body');
  if (!body) return;

  let originalHtml = null;
  let marks = [];
  let current = -1;

  const bar = document.createElement('div');
  bar.className = 'post-search';
  bar.hidden = true;
  bar.innerHTML =
    '<input type="search" id="post-search-input" placeholder="ÔÚ±¾ÎÄÖĞ²éÕÒ¡­" autocomplete="off" />' +
    '<span class="post-search-count" id="post-search-count"></span>' +
    '<button type="button" class="link" id="post-search-prev">ÉÏÒ»¸ö</button>' +
    '<button type="button" class="link" id="post-search-next">ÏÂÒ»¸ö</button>' +
    '<button type="button" class="link" id="post-search-close">¹Ø±Õ</button>';
  document.body.appendChild(bar);

  const input = bar.querySelector('#post-search-input');
  const count = bar.querySelector('#post-search-count');

  function clearHighlights() {
    if (originalHtml !== null) {
      body.innerHTML = originalHtml;
    }
    marks = [];
    current = -1;
    if (count) count.textContent = '';
  }

  function close() {
    clearHighlights();
    bar.hidden = true;
    originalHtml = null;
  }

  function highlight(query) {
    if (originalHtml === null) {
      originalHtml = body.innerHTML;
    } else {
      body.innerHTML = originalHtml;
    }

    marks = [];
    current = -1;

    const trimmed = query.trim();
    if (!trimmed) {
      if (count) count.textContent = '';
      return;
    }

    const lower = trimmed.toLowerCase();
    const walker = document.createTreeWalker(body, NodeFilter.SHOW_TEXT, {
      acceptNode(node) {
        if (!node.nodeValue || !node.nodeValue.trim()) return NodeFilter.FILTER_REJECT;
        if (node.parentElement && node.parentElement.closest('script,style')) return NodeFilter.FILTER_REJECT;
        return node.nodeValue.toLowerCase().includes(lower) ? NodeFilter.FILTER_ACCEPT : NodeFilter.FILTER_REJECT;
      },
    });

    const nodes = [];
    while (walker.nextNode()) nodes.push(walker.currentNode);

    nodes.forEach((node) => {
      const text = node.nodeValue;
      const source = text.toLowerCase();
      const fragment = document.createDocumentFragment();
      let index = 0;
      let found = source.indexOf(lower, index);

      while (found >= 0) {
        if (found > index) fragment.appendChild(document.createTextNode(text.slice(index, found)));
        const mark = document.createElement('mark');
        mark.className = 'post-search-hit';
        mark.textContent = text.slice(found, found + trimmed.length);
        fragment.appendChild(mark);
        marks.push(mark);
        index = found + trimmed.length;
        found = source.indexOf(lower, index);
      }

      if (index < text.length) fragment.appendChild(document.createTextNode(text.slice(index)));

      const parent = node.parentNode;
      if (parent) parent.replaceChild(fragment, node);
    });

    if (count) count.textContent = marks.length ? '1 / ' + marks.length : 'Ã»ÓĞÕÒµ½';

    if (marks.length) {
      current = 0;
      marks[0].classList.add('current');
      marks[0].scrollIntoView({ block: 'center', behavior: 'smooth' });
    }
  }

  function step(delta) {
    if (!marks.length) return;
    marks[current].classList.remove('current');
    current = (current + delta + marks.length) % marks.length;
    marks[current].classList.add('current');
    marks[current].scrollIntoView({ block: 'center', behavior: 'smooth' });
    if (count) count.textContent = (current + 1) + ' / ' + marks.length;
  }

  function open() {
    bar.hidden = false;
    input.focus();
    input.select();
    if (input.value) highlight(input.value);
  }

  document.addEventListener('keydown', function (event) {
    const isFind = (event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'f';
    if (isFind) {
      event.preventDefault();
      open();
      return;
    }

    if (bar.hidden) return;

    if (event.key === 'Escape') {
      event.preventDefault();
      close();
    } else if (event.key === 'Enter' && document.activeElement === input) {
      event.preventDefault();
      step(event.shiftKey ? -1 : 1);
    }
  });

  let timer = null;
  input.addEventListener('input', function () {
    clearTimeout(timer);
    timer = setTimeout(() => highlight(input.value), 120);
  });

  bar.querySelector('#post-search-next').addEventListener('click', () => step(1));
  bar.querySelector('#post-search-prev').addEventListener('click', () => step(-1));
  bar.querySelector('#post-search-close').addEventListener('click', close);
})();
