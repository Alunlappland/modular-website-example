/* AI 对话：统一视觉、流式输出、会话文件存储 */
(function () {
  'use strict';

  const shell = document.getElementById('ai');
  if (!shell) return;

  const providerSelect = document.getElementById('ai-provider');
  const messages = document.getElementById('ai-messages');
  const form = document.getElementById('ai-form');
  const textarea = document.getElementById('ai-text');
  const send = document.getElementById('ai-send');
  const chatsList = document.getElementById('ai-chats');
  const balanceBox = document.getElementById('ai-balance');
  const settings = document.getElementById('ai-settings');
  const modelInput = document.getElementById('ai-model');

  const state = {
    chatId: null,
    provider: shell.dataset.default || 'deepseek',
    model: '',
    controller: null,
    messages: [],
    busy: false,
  };

  const el = (tag, className, text) => {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
  };

  const escape = (value) => value;

  async function getJson(url) {
    const response = await fetch(url, { headers: { Accept: 'application/json' } });
    if (!response.ok) throw new Error('HTTP ' + response.status);
    return response.json();
  }

  async function postJson(url, body) {
    const response = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify(body),
    });
    const data = await response.json().catch(() => null);
    if (!response.ok) throw new Error((data && data.error) || 'HTTP ' + response.status);
    return data;
  }

  // ---------- 会话列表 ----------

  async function loadChats() {
    try {
      const items = await getJson('/api/ai/chats');
      chatsList.textContent = '';

      if (!items.length) {
        chatsList.appendChild(el('li', 'ai-empty', '还没有对话'));
        return;
      }

      items.forEach((item) => {
        const li = el('li', 'ai-chat');
        if (item.id === state.chatId) li.classList.add('on');

        const button = el('button', 'ai-chat-open', item.title || '新对话');
        button.type = 'button';
        button.addEventListener('click', () => openChat(item.id));

        const remove = el('button', 'ai-chat-del', '×');
        remove.type = 'button';
        remove.title = '删除这个对话';
        remove.addEventListener('click', async (event) => {
          event.stopPropagation();
          if (!window.confirm('删除「' + (item.title || '新对话') + '」？')) return;
          await fetch('/api/ai/chats/' + item.id, { method: 'DELETE' });
          if (state.chatId === item.id) newChat();
          loadChats();
        });

        li.appendChild(button);
        li.appendChild(remove);
        chatsList.appendChild(li);
      });
    } catch (error) {
      chatsList.textContent = '读取对话失败：' + error.message;
    }
  }

  // ---------- 渲染 ----------

  function addBubble(role, text) {
    const bubble = el('article', 'ai-bubble ai-' + role);
    const content = el('div', 'ai-text');
    content.textContent = text;
    bubble.appendChild(content);
    messages.appendChild(bubble);
    messages.scrollTop = messages.scrollHeight;
    return content;
  }

  function renderDocument(document) {
    messages.textContent = '';
    (document.messages || []).forEach((message) => addBubble(message.role, message.content));
  }

  function newChat() {
    state.chatId = null;
    state.messages = [];
    messages.textContent = '';
    const welcome = el('div', 'ai-welcome');
    welcome.appendChild(el('p', 'ai-welcome-title', '今天想聊什么？'));
    welcome.appendChild(el('p', 'muted small', '消息经本站转发，对话以文件形式存在服务器上，只有你能看到。'));
    messages.appendChild(welcome);
    loadChats();
  }

  async function openChat(id) {
    try {
      const document = await getJson('/api/ai/chats/' + id);
      state.chatId = document.id;
      state.messages = document.messages || [];
      if (document.provider) {
        providerSelect.value = document.provider;
        state.provider = document.provider;
      }
      renderDocument(document);
      loadChats();
    } catch (error) {
      window.alert('打开对话失败：' + error.message);
    }
  }

  // ---------- 发送 ----------

  function setBusy(busy) {
    state.busy = busy;
    send.textContent = busy ? '停止' : '发送';
  }

  function modelFor(provider) {
    try { return localStorage.getItem('genkan.ai.model.' + provider) || ''; } catch (error) { return ''; }
  }

  function rememberModel(provider, value) {
    try {
      if (value) localStorage.setItem('genkan.ai.model.' + provider, value);
      else localStorage.removeItem('genkan.ai.model.' + provider);
    } catch (error) { /* 隐私模式下忽略 */ }
  }

  async function sendMessage() {
    if (state.busy) return;

    const text = textarea.value.trim();
    if (!text) return;

    const welcome = messages.querySelector('.ai-welcome');
    if (welcome) welcome.remove();

    const userMessage = { role: 'user', content: text };
    state.messages.push(userMessage);
    addBubble('user', text);
    textarea.value = '';

    const assistantNode = addBubble('assistant', '');
    assistantNode.classList.add('streaming');

    setBusy(true);

    try {
      state.controller = new AbortController();

      const response = await fetch('/api/ai/chat', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'text/event-stream' },
        signal: state.controller.signal,
        body: JSON.stringify({
          providerId: state.provider,
          chatId: state.chatId,
          model: state.model || undefined,
          messages: state.messages.slice(-40),
        }),
      });

      if (!response.ok || !response.body) {
        let message = 'HTTP ' + response.status;
        try {
          const data = await response.json();
          message = data.error || message;
        } catch (error) { /* 忽略 */ }
        throw new Error(message);
      }

      const reader = response.body.getReader();
      const decoder = new TextDecoder();
      let buffer = '';

      while (true) {
        const chunk = await reader.read();
        if (chunk.done) break;

        buffer += decoder.decode(chunk.value, { stream: true });

        let index;
        while ((index = buffer.indexOf('\n\n')) >= 0) {
          const frame = buffer.slice(0, index).trim();
          buffer = buffer.slice(index + 2);
          if (!frame.startsWith('data:')) continue;

          let payload;
          try { payload = JSON.parse(frame.slice(5).trim()); } catch (error) { continue; }

          if (payload.error) {
            assistantNode.textContent += (assistantNode.textContent ? '\n' : '') + '⚠️ ' + payload.error;
            assistantNode.classList.add('error');
          } else if (payload.text) {
            assistantNode.textContent += payload.text;
            messages.scrollTop = messages.scrollHeight;
          } else if (payload.done) {
            state.chatId = payload.chatId;
          }
        }
      }

      state.messages.push({ role: 'assistant', content: assistantNode.textContent });
      await loadChats();
    } catch (error) {
      if (error.name === 'AbortError') {
        if (!assistantNode.textContent) assistantNode.textContent = '（已停止）';
      } else {
        assistantNode.textContent = '⚠️ ' + error.message;
        assistantNode.classList.add('error');
      }
    } finally {
      state.controller = null;
      assistantNode.classList.remove('streaming');
      setBusy(false);
      textarea.focus();

      if (assistantNode.textContent) {
        const copy = el('button', 'ai-copy', '复制');
        copy.type = 'button';
        copy.addEventListener('click', async () => {
          try {
            await navigator.clipboard.writeText(assistantNode.textContent);
            copy.textContent = '已复制';
            setTimeout(() => { copy.textContent = '复制'; }, 1400);
          } catch (error) {
            window.prompt('复制这段：', assistantNode.textContent);
          }
        });
        assistantNode.parentElement.appendChild(copy);
      }
    }
  }

  form.addEventListener('submit', (event) => {
    event.preventDefault();
    if (state.busy) {
      if (state.controller) state.controller.abort();
      return;
    }
    sendMessage();
  });

  textarea.addEventListener('keydown', (event) => {
    if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
      event.preventDefault();
      sendMessage();
    }
  });

  // ---------- 余额 ----------

  async function loadBalance() {
    balanceBox.textContent = '';

    const title = el('p', 'ai-side-title', '余额');
    balanceBox.appendChild(title);

    const loading = el('p', 'muted small', '查询中…');
    balanceBox.appendChild(loading);

    try {
      const items = await getJson('/api/ai/balance');
      balanceBox.textContent = '';
      balanceBox.appendChild(el('p', 'ai-side-title', '余额'));

      items.forEach((item) => {
        const row = el('div', 'ai-balance-row');
        row.appendChild(el('span', 'ai-balance-name', item.name));
        const amount = el('span', 'ai-balance-amount' + (item.ok ? '' : ' off'), item.amount);
        amount.title = item.detail || '';
        row.appendChild(amount);
        balanceBox.appendChild(row);
      });

      const note = el('p', 'muted small', '查不到就点开看说明；不是所有供应商都提供余额接口。');
      balanceBox.appendChild(note);
    } catch (error) {
      balanceBox.textContent = '';
      balanceBox.appendChild(el('p', 'muted small', '查询失败：' + error.message));
    }
  }

  // ---------- 密钥 ----------

  document.getElementById('ai-keys-btn').addEventListener('click', () => {
    settings.hidden = !settings.hidden;
  });

  document.getElementById('ai-settings-close').addEventListener('click', () => {
    settings.hidden = true;
  });

  settings.addEventListener('click', async (event) => {
    const providerId = event.target && event.target.dataset && event.target.dataset.save;
    if (!providerId) return;

    const input = settings.querySelector('input[data-provider="' + providerId + '"]');
    const key = input.value.trim();
    if (!key) {
      window.alert('先填入 Key。');
      return;
    }

    try {
      await postJson('/api/ai/keys', { providerId, key });
      input.value = '';
      event.target.textContent = '已保存';
      setTimeout(() => { event.target.textContent = '保存'; }, 1500);
    } catch (error) {
      window.alert('保存失败：' + error.message);
    }
  });

  // ---------- 初始化 ----------

  providerSelect.addEventListener('change', () => {
    state.provider = providerSelect.value;
    state.model = modelFor(state.provider);
    if (modelInput) modelInput.value = state.model;
  });

  if (modelInput) {
    modelInput.addEventListener('change', () => {
      state.model = modelInput.value.trim();
      rememberModel(state.provider, state.model);
    });
  }

  document.getElementById('ai-new').addEventListener('click', newChat);
  document.getElementById('ai-export-btn').addEventListener('click', () => {
    if (!state.chatId) { window.alert('先打开一个对话。'); return; }
    window.location.href = '/api/ai/chats/' + state.chatId + '/export';
  });

  document.getElementById('ai-balance-btn').addEventListener('click', loadBalance);

  state.model = modelFor(state.provider);
  if (modelInput) modelInput.value = state.model;

  loadChats();
  loadBalance();
})();
