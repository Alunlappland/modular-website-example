/* 文件页：拖拽、分块上传、失败重试、复制链接 */
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
  const retry = document.getElementById('upload-retry');

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

  // ---- 上传 ----
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
        try { json = JSON.parse(xhr.responseText); } catch (error) { /* 非 JSON */ }
        resolve({ status: xhr.status, json });
      });

      xhr.addEventListener('error', () => resolve({ status: 0, json: null }));
      xhr.send(data);
    });

    const tokenInput = () => form.querySelector('input[name="__RequestVerificationToken"]');

    const uploadDirect = async (file, token, onProgress) => {
      const data = new FormData();
      data.append('Uploads', file, file.webkitRelativePath || file.name);
      if (token) data.append('__RequestVerificationToken', token);

      const result = await postForm(handlerUrl('Upload'), data, onProgress);
      if (result.status !== 200 || !result.json || !result.json.ok) {
        throw new Error('HTTP ' + result.status);
      }
    };

    const uploadChunked = async (file, token, onProgress) => {
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

          const result = await postForm(handlerUrl('Chunk'), data, (loaded) => onProgress(offset + loaded));

          if (result.status === 200 && result.json && result.json.ok) {
            offset = end;
            advanced = true;
          } else if (result.status === 409 && result.json && typeof result.json.expected === 'number') {
            offset = result.json.expected;
            advanced = true;
          } else {
            await sleep(700 * (attempt + 1));
          }
        }

        if (!advanced) throw new Error('分块中断');
        onProgress(offset);
      }

      const done = new FormData();
      done.append('uploadId', id);
      done.append('fileName', relative);
      done.append('totalBytes', String(file.size));
      if (token) done.append('__RequestVerificationToken', token);

      const result = await postForm(handlerUrl('Complete'), done);
      if (result.status !== 200 || !result.json || !result.json.ok) {
        throw new Error('合并失败 HTTP ' + result.status);
      }
    };

    const setBusy = (busy, text) => {
      if (button) {
        button.disabled = busy;
        button.textContent = busy ? '上传中…' : '上传';
      }
      if (retry) retry.hidden = true;
      if (busy && progress) progress.hidden = false;
      if (text && status) status.textContent = text;
    };

    const runBatch = async (files, label) => {
      if (!files.length) return;

      const token = tokenInput() ? tokenInput().value : '';
      const totalBytes = files.reduce((sum, file) => sum + file.size, 0);
      let doneBytes = 0;
      const failed = [];

      setBusy(true, '准备上传…');

      const paint = (index, currentLoaded) => {
        const overall = totalBytes === 0 ? 0 : Math.round(((doneBytes + currentLoaded) / totalBytes) * 100);
        if (bar) bar.style.width = overall + '%';
        if (status) {
          status.textContent = label + '　第 ' + (index + 1) + ' / ' + files.length + ' 个（' + overall + '%）　' +
            files[index].name;
        }
      };

      for (let i = 0; i < files.length; i++) {
        const file = files[i];
        try {
          if (file.size >= DIRECT_LIMIT) {
            await uploadChunked(file, token, (loaded) => paint(i, loaded));
          } else {
            await uploadDirect(file, token, (loaded) => paint(i, loaded));
          }
        } catch (error) {
          failed.push(file);
        }
        doneBytes += file.size;
        paint(i, 0);
      }

      const okCount = files.length - failed.length;

      if (failed.length === 0) {
        if (status) status.textContent = label + '：成功 ' + okCount + ' 个，正在刷新…';
        setTimeout(() => window.location.reload(), 700);
        return;
      }

      if (status) {
        status.textContent = label + '：成功 ' + okCount + ' 个，失败 ' + failed.length + ' 个';
      }

      setBusy(false);
      if (retry) {
        retry.hidden = false;
        retry.textContent = '重试失败文件（' + failed.length + '）';
        retry.onclick = () => runBatch(failed.slice(), '重试');
      }
    };

    form.addEventListener('submit', (event) => {
      event.preventDefault();

      const files = [];
      if (input.files) files.push.apply(files, input.files);
      if (folderInput && folderInput.files) files.push.apply(files, folderInput.files);

      if (files.length === 0) {
        window.alert('先选文件，或者把文件拖进来。');
        return;
      }

      let label = files.length + ' 个文件';
      const first = files[0].webkitRelativePath || '';
      if (first.indexOf('/') > 0) {
        label = '「' + first.split('/')[0] + '」文件夹（' + files.length + ' 个文件）';
      }

      runBatch(files, label);
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
