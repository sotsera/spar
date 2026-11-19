// Profile isolation probe. The host calls probe.read() and probe.write(token) through Runtime.evaluate.
// Nothing here touches devices, notifications, the clipboard or the network outside the served origins.

function openDb() {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open('spike', 1);
    request.onupgradeneeded = () => request.result.createObjectStore('kv');
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

function requestValue(request) {
  return new Promise((resolve, reject) => {
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

window.probe = {
  async read() {
    const state = {};
    state.ls = localStorage.getItem('token');
    state.cookie = document.cookie || null;
    try {
      const db = await openDb();
      state.idb = (await requestValue(db.transaction('kv').objectStore('kv').get('token'))) ?? null;
      db.close();
    } catch (error) {
      state.idb = 'error:' + error;
    }
    try {
      state.sw = (await navigator.serviceWorker.getRegistrations()).length;
    } catch (error) {
      state.sw = 'error:' + error;
    }
    try {
      state.wm = (await navigator.permissions.query({ name: 'window-management' })).state;
    } catch (error) {
      state.wm = 'error:' + error;
    }
    return state;
  },

  async write(token) {
    localStorage.setItem('token', token);
    document.cookie = `token=${token}; Max-Age=86400; Path=/; Secure; SameSite=Strict`;
    const db = await openDb();
    const tx = db.transaction('kv', 'readwrite');
    tx.objectStore('kv').put(token, 'token');
    await new Promise((resolve, reject) => { tx.oncomplete = resolve; tx.onerror = () => reject(tx.error); });
    db.close();
    await navigator.serviceWorker.register('/sw.js', { scope: '/' });
    await navigator.serviceWorker.ready;
    return 'ok';
  },

  // Asks for the window-management permission (no device, no OS prompt: the host answers PermissionRequested).
  async screens() {
    try {
      const details = await window.getScreenDetails();
      return 'granted:' + details.screens.length;
    } catch (error) {
      return 'error:' + error.name;
    }
  },
};
