/* 文件页：拖拽、上传进度、复制链接 */
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

  // ---- 带进度的上传（不整页跳转）----
  if (form && input) {
    form.addEventListener('submit', (event) => {
      const files = [];
      if (input.files) files.push.apply(files, input.files);
      if (folderInput && folderInput.files) files.push.apply(files, folderInput.files);

      if (files.length === 0) {
        event.preventDefault();
        window.alert('先选文件，或者把文件拖进来。');
        return;
      }

      event.preventDefault();

      const data = new FormData(form);
      const xhr = new XMLHttpRequest();
      xhr.open('POST', form.action, true);
      xhr.setRequestHeader('Accept', 'application/json');

      if (progress) progress.hidden = false;
      if (button) {
        button.disabled = true;
        button.textContent = '上传中…';
      }

      xhr.upload.addEventListener('progress', (e) => {
        if (!e.lengthComputable || !bar || !status) return;
        const percent = Math.round((e.loaded / e.total) * 100);
        bar.style.width = percent + '%';
        status.textContent =
          '已上传 ' + (e.loaded / 1024 / 1024).toFixed(1) + ' MB / ' +
          (e.total / 1024 / 1024).toFixed(1) + ' MB（' + percent + '%）';
      });

      xhr.addEventListener('load', () => {
        let result = null;
        try { result = JSON.parse(xhr.responseText); } catch (e) { /* 忽略 */ }

        if (xhr.status === 200 && result && result.ok) {
          if (status) status.textContent = result.message || '上传完成，正在刷新…';
          setTimeout(() => window.location.reload(), 500);
          return;
        }

        if (button) {
          button.disabled = false;
          button.textContent = '上传';
        }
        if (status) {
          status.textContent = (result && (result.message || (result.failed && result.failed.join('；'))))
            || ('上传失败：HTTP ' + xhr.status);
        }
      });

      xhr.addEventListener('error', () => {
        if (button) {
          button.disabled = false;
          button.textContent = '上传';
        }
        if (status) status.textContent = '网络错误，上传中断。';
      });

      xhr.send(data);
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
