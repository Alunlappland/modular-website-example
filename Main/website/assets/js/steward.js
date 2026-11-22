/* 代理站长：单栏对话 + 工具轨迹 */
(function () {
  'use strict';

  const shell = document.getElementById('steward');
  if (!shell) return;

  const form = document.getElementById('steward-form');
  const input = document.getElementById('steward-text');
  const send = document.getElementById('steward-send');
  const messages = document.getElementById('steward-messages');
  const provider = document.getElementById('steward-provider');
  const clear = document.getElementById('steward-clear');

  const el = (tag, cls, text) => {
    const node = document.createElement(tag);
    if (cls) node.className = cls;
    if (text !== undefined) node.textContent = text;
    return node;
  };

  function welcome() {
    const box = el('div', 'ai-welcome');
    box.appendChild(el('p', 'ai-welcome-title', '今天要我做什么？'));
    box.appendChild(el('p', 'muted small', '查体检、读写笔记、读博客、存草稿、看文件、塞剪贴板；不能删除东西。'));
    messages.appendChild(box);
  }

  function bubble(role, text) {
    const box = el('article', 'ai-bubble ai-' + role);
    const body = el('div', 'ai-text', text);
    box.appendChild(body);
    messages.appendChild(box);
    messages.scrollTop = messages.scrollHeight;
    return body;
  }

  function tracePanel(items) {
    if (!items || items.length === 0) return;
    const box = el('div', 'steward-trace');
    items.forEach((item) => box.appendChild(el('span', 'steward-tool', item)));
    messages.appendChild(box);
    messages.scrollTop = messages.scrollHeight;
  }

  async function sendMessage() {
    const text = input.value.trim();
    if (!text) return;

    const welcomeBox = messages.querySelector('.ai-welcome');
    if (welcomeBox) welcomeBox.remove();

    bubble('user', text);
    input.value = '';

    send.disabled = true;
    send.textContent = '处理中…';

    const pending = bubble('assistant', '……');

    try {
      const response = await fetch('/api/steward/chat', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify({
          providerId: provider ? provider.value : null,
          message: text,
        }),
      });

      const data = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error((data && (data.error || data.detail)) || ('HTTP ' + response.status));
      }

      pending.textContent = data.reply || '（没有回复）';
      tracePanel(data.trace);
    } catch (error) {
      pending.textContent = '⚠️ ' + error.message;
      pending.parentElement.classList.add('error');
    } finally {
      send.disabled = false;
      send.textContent = '发送';
      input.focus();
    }
  }

  form.addEventListener('submit', (event) => {
    event.preventDefault();
    sendMessage();
  });

  input.addEventListener('keydown', (event) => {
    if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
      event.preventDefault();
      sendMessage();
    }
  });

  if (clear) {
    clear.addEventListener('click', async () => {
      if (!window.confirm('清空代理站长的对话记录？')) return;
      await fetch('/api/steward/chat', { method: 'DELETE' });
      messages.textContent = '';
      welcome();
    });
  }
})();
