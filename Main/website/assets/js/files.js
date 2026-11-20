/* 文件页：拖拽上传高亮、复制链接 */
(function () {
  'use strict';

  const dropzone = document.getElementById('dropzone');
  const input = document.getElementById('upload-input');
  const form = document.getElementById('upload-form');

  if (dropzone && input && form) {
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

    form.addEventListener('submit', () => {
      const button = form.querySelector('button[type="submit"]');
      if (button) {
        button.disabled = true;
        button.textContent = '上传中…';
      }
    });
  }

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
