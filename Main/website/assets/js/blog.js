/* 博客交互：反应、行级注释、写作预览 */
(function () {
  'use strict';

  const csrfMeta = document.querySelector('meta[name="csrf"]');
  const csrf = csrfMeta ? csrfMeta.content : '';
  const headers = { 'Content-Type': 'application/json', 'RequestVerificationToken': csrf };

  const article = document.querySelector('.post[data-slug]');
  const slug = article ? article.dataset.slug : null;

  // ---- 点赞 / 点踩 ----
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
          /* 忽略网络错误 */
        }
      });
    });
  }

  // ---- 行级注释 ----
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
        if (status) status.textContent = text || '提交失败，稍后再试。';
      } catch (error) {
        if (status) status.textContent = '网络错误，稍后再试。';
      }
    });
  }

  // ---- 写作页实时预览 ----
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
        /* 忽略 */
      }
    };

    textarea.addEventListener('input', () => {
      clearTimeout(timer);
      timer = setTimeout(render, 350);
    });

    render();
  }
})();
