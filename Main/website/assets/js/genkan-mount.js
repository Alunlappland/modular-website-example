/* 玄关挂载 SDK：给挂在 Extend 下的程序用
 * - 静态挂载（/route/...）：同源，直接 fetch('/api/me')
 * - iframe 挂载：通过 postMessage 拿登录信息（需要 extend.json 里配 allowedOrigin + exposeUser）
 */
(function () {
  'use strict';

  var listeners = [];
  var currentUser = null;

  function emit(user) {
    currentUser = user;
    listeners.forEach(function (fn) {
      try { fn(user); } catch (error) { /* 调用方异常不影响别的 */ }
    });
  }

  window.addEventListener('message', function (event) {
    if (event.data && event.data.type === 'genkan:user' && event.data.user) {
      emit(event.data.user);
    }
  });

  function inFrame() {
    return window.parent && window.parent !== window;
  }

  var GenkanMount = {
    /** 当前用户（可能为 null，表示还没拿到） */
    get current() {
      return currentUser;
    },

    /** 拿到用户后回调；已经有则立即执行 */
    onUser(fn) {
      if (typeof fn !== 'function') return;
      if (currentUser) fn(currentUser);
      listeners.push(fn);
    },

    /** 主动获取：静态挂载走 /api/me，iframe 挂载向父页面打招呼 */
    async ready() {
      if (currentUser) return currentUser;

      try {
        var response = await fetch('/api/me', { headers: { Accept: 'application/json' } });
        if (response.ok) {
          emit(await response.json());
          return currentUser;
        }
      } catch (error) { /* iframe 跨域时 fetch 会失败，走 postMessage */ }

      if (inFrame()) {
        window.parent.postMessage({ type: 'genkan:hello' }, '*');
      }

      return currentUser;
    },

    /** 让宿主页面跳转（iframe 内不能用 top.location 跨域） */
    open(path) {
      if (typeof path !== 'string' || path.charAt(0) !== '/') return;
      if (inFrame()) {
        window.parent.postMessage({ type: 'genkan:open', path: path }, '*');
      } else {
        window.location.href = path;
      }
    },
  };

  window.GenkanMount = GenkanMount;
})();
