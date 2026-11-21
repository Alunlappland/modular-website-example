/* 文件页：拖拽、分块上传、复制链接 */
(function () {
  'use strict';

  const dropzone = document.getElementById('dropzone');
  const form = document.getElementById('upload-form');
  const input = document.getElementById('upload-input');
  const folderInput = document.getElementById('upload-folder');
  const button = document.getElementById('upload-btn');
  const progress = document.getElementById('upload-progress');
  const bar = document.getElementById('upload-bar');
  const status = document.getElementById('upload-status');

  // ---- 拖拽 ----
  if (dropzone && input) {
    ['dragenter', 'dragover'].forEach((name) => {
      dropzone.addEventListener(name, (event) => {
        event.preventDefault();
        dropzone.classList.add('over');
      });
    });

    ['dragleave', 'drop'].forEach((name) => {
      dropzone.addEventListener(name, (event) => {
        event.preventDefault();
        dropzone.classList.remove('over');
      });
    });

    dropzone.addEventListener('drop', (event) => {
      const files = event.dataTransfer && event.dataTransfer.files;
      if (files && files.length > 0) {
        input.files = files;
      }
    });
  }

  // ---- 上传：小文件整传，大文件分块（2MB/块，避开 Cloudflare 的 100 秒与 100MB 限制）----
  if (form && input) {
    const CHUNK_SIZE = 2 * 1024 * 1024;
    const DIRECT_LIMIT = 4 * 1024 * 1024;

    const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

    const randomId = () => {
      if (window.crypto && window.crypto.randomUUID) {
        return window.crypto.randomUUID().replace(/-/g, '');
      }
      return String(Date.now()) + Math.random().toString(16).slice(2);
    };

    const handlerUrl = (name) => {
      const url = new URL(form.action, window.location.href);
      url.searchParams.set('handler', name);
      return url.toString();
    };

    const postForm = (url, data, onProgress) => new Promise((resolve) => {
      const xhr = new XMLHttpRequest();
      xhr.open('POST', url, true);
      xhr.setRequestHeader('Accept', 'application/json');

      if (onProgress) {
        xhr.upload.addEventListener('progress', (event) => {
          if (event.lengthComputable) onProgress(event.loaded);
        });
      }

      xhr.addEventListener('load', () => {
        let json = null;
        try { json = JSON.parse(xhr.responseText); } catch (error) { /* 非 JSON 响应 */ }
        resolve({ status: xhr.status, json });
      });

      xhr.addEventListener('error', () => resolve({ status: 0, json: null }));
      xhr.send(data);
    });

    form.addEventListener('submit', (event) => {
      event.preventDefault();

      const files = [];
      if (input.files) files.push.apply(files, input.files);
      if (folderInput && folderInput.files) files.push.apply(files, folderInput.files);

      if (files.length === 0) {
        window.alert('先选文件，或者把文件拖进来。');
        return;
      }

      const tokenInput = form.querySelector('input[name="__RequestVerificationToken"]');
      const token = tokenInput ? tokenInput.value : '';
      const totalBytes = files.reduce((sum, file) => sum + file.size, 0);
      let doneBytes = 0;
      let failed = 0;

      if (progress) progress.hidden = false;
      if (button) {
        button.disabled = true;
        button.textContent = '上传中…';
      }

      const paint = (fileIndex, currentLoaded) => {
        const overall = totalBytes === 0 ? 0 : Math.round(((doneBytes + currentLoaded) / totalBytes) * 100);
        if (bar) bar.style.width = overall + '%';
        if (status) {
          status.textContent = '第 ' + (fileIndex + 1) + ' / ' + files.length + ' 个（' + overall + '%）　' +
            files[fileIndex].name;
        }
      };

      const uploadDirect = async (file, index) => {
        const data = new FormData();
        data.append('Uploads', file, file.webkitRelativePath || file.name);
        if (token) data.append('__RequestVerificationToken', token);

        const result = await postForm(handlerUrl('Upload'), data, (loaded) => paint(index, loaded));
        if (result.status !== 200 || !result.json || !result.json.ok) {
          throw new Error('HTTP ' + result.status);
        }
      };

      const uploadChunked = async (file, index) => {
        const id = randomId();
        const relative = file.webkitRelativePath || file.name;
        let offset = 0;

        while (offset < file.size) {
          const end = Math.min(offset + CHUNK_SIZE, file.size);
          const blob = file.slice(offset, end);
          let advanced = false;

          for (let attempt = 0; attempt < 3 && !advanced; attempt++) {
            const data = new FormData();
            data.append('Chunk', blob, 'chunk');
            data.append('uploadId', id);
            data.append('offset', String(offset));
            if (token) data.append('__RequestVerificationToken', token);

            const result = await postForm(handlerUrl('Chunk'), data, (loaded) => paint(index, offset + loaded));

            if (result.status === 200 && result.json && result.json.ok) {
              offset = end;
              advanced = true;
            } else if (result.status === 409 && result.json && typeof result.json.expected === 'number') {
              // 上次中断过：服务端已有字节数不同，对齐后重发
              offset = result.json.expected;
              advanced = true;
            } else {
              await sleep(700 * (attempt + 1));
            }
          }

          if (!advanced) throw new Error('分块上传中断');
          paint(index, offset);
        }

        const done = new FormData();
        done.append('uploadId', id);
        done.append('fileName', relative);
        done.append('totalBytes', String(file.size));
        if (token) done.append('__RequestVerificationToken', token);

        const result = await postForm(handlerUrl('Complete'), done);
        if (result.status !== 200 || !result.json || !result.json.ok) {
          throw new Error('合并失败（HTTP ' + result.status + '）');
        }
      };

      (async () => {
        for (let i = 0; i < files.length; i++) {
          const file = files[i];
          try {
            if (file.size >= DIRECT_LIMIT) {
              await uploadChunked(file, i);
            } else {
              await uploadDirect(file, i);
            }
          } catch (error) {
            failed++;
            if (status) status.textContent = '「' + file.name + '」失败：' + error.message + '，继续…';
          }
          doneBytes += file.size;
          paint(i, 0);
        }

        if (status) {
          status.textContent = failed === 0
            ? '全部完成（' + files.length + ' 个），正在刷新…'
            : '完成，' + failed + ' 个失败，正在刷新…';
        }

        setTimeout(() => window.location.reload(), 800);
      })();
    });
  }

  // ---- 复制分享链接 ----
  const copy = document.getElementById('copy-link');
  const link = document.getElementById('new-link');
  if (copy && link) {
    copy.addEventListener('click', async () => {
      const url = location.origin + link.textContent.trim();
      try {
        await navigator.clipboard.writeText(url);
        copy.textContent = '已复制';
        setTimeout(() => { copy.textContent = '复制'; }, 1600);
      } catch (error) {
        window.prompt('复制这个链接：', url);
      }
    });
  }
})();
