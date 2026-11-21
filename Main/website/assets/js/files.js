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

  // ---- 逐文件上传（每个文件一个请求，失败不影响其它文件）----
  if (form && input) {
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
      const action = form.action;

      const totalBytes = files.reduce((sum, file) => sum + file.size, 0);
      let doneBytes = 0;
      let failed = 0;

      if (progress) progress.hidden = false;
      if (button) {
        button.disabled = true;
        button.textContent = '上传中…';
      }

      const updateProgress = (loadedCurrent, index) => {
        const overall = totalBytes === 0 ? 0 : Math.round(((doneBytes + loadedCurrent) / totalBytes) * 100);
        if (bar) bar.style.width = overall + '%';
        if (status) {
          status.textContent = '第 ' + (index + 1) + ' / ' + files.length + ' 个（' + overall + '%）　' +
            files[index].name;
        }
      };

      const uploadOne = (file, index) => new Promise((resolve) => {
        const data = new FormData();
        data.append('Uploads', file, file.webkitRelativePath || file.name);
        if (token) data.append('__RequestVerificationToken', token);

        const xhr = new XMLHttpRequest();
        xhr.open('POST', action, true);
        xhr.setRequestHeader('Accept', 'application/json');

        xhr.upload.addEventListener('progress', (e) => {
          if (e.lengthComputable) updateProgress(e.loaded, index);
        });

        xhr.addEventListener('load', () => {
          doneBytes += file.size;
          if (xhr.status !== 200) {
            failed++;
            if (status) {
              status.textContent = '「' + file.name + '」失败：HTTP ' + xhr.status +
                (xhr.status === 520 || xhr.status === 524 ? '（隧道超时/源站不可达，稍后重试）' : '') +
                '，继续上传其余文件…';
            }
          }
          updateProgress(0, index);
          resolve();
        });

        xhr.addEventListener('error', () => {
          doneBytes += file.size;
          failed++;
          if (status) status.textContent = '「' + file.name + '」网络错误，继续上传其余文件…';
          resolve();
        });

        xhr.send(data);
      });

      (async () => {
        for (let i = 0; i < files.length; i++) {
          // Cloudflare 单请求上限约 100MB，超了必然失败，直接跳过并提示
          if (files[i].size > 95 * 1024 * 1024) {
            failed++;
            doneBytes += files[i].size;
            if (status) {
              status.textContent = '「' + files[i].name + '」超过 95MB，已跳过（请改用 Syncthing 或内网传输）';
            }
            continue;
          }

          await uploadOne(files[i], i);
        }

        if (status) {
          status.textContent = failed === 0
            ? '全部上传完成（' + files.length + ' 个），正在刷新…'
            : '完成，其中 ' + failed + ' 个失败，正在刷新…';
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
