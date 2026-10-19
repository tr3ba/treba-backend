const databaseName = 'treba-editor-drafts-v1';
function open() {
    return new Promise((resolve, reject) => {
        const request = indexedDB.open(databaseName, 1);
        request.onupgradeneeded = () => request.result.createObjectStore('drafts', { keyPath: 'id' });
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(new Error('Draft storage is unavailable.'));
    });
}
async function transaction(mode, operation) {
    const database = await open();
    try {
        return await new Promise((resolve, reject) => {
            const tx = database.transaction('drafts', mode); let result;
            tx.oncomplete = () => resolve(result);
            tx.onerror = tx.onabort = () => reject(new Error('Draft storage is unavailable or full.'));
            operation(tx.objectStore('drafts'), value => result = value);
        });
    } finally { database.close(); }
}
export function get(scope, key) {
    return transaction('readonly', (store, done) => { const request = store.get(scope + ':' + key); request.onsuccess = () => done(request.result || null); });
}
export function put(scope, key, data) {
    return transaction('readwrite', store => store.put({ id: scope + ':' + key, updatedAt: new Date().toISOString(), data }));
}
export function remove(scope, key) { return transaction('readwrite', store => store.delete(scope + ':' + key)); }
export async function meta(scope, key) { const value = await get(scope, key); return value ? { updatedAt: value.updatedAt } : null; }
export async function saveEditor(scope, key, modelJson, photos) {
    await put(scope, key, { modelJson, photos });
}
export async function saveEditorModel(scope, key, modelJson) {
    // Read and update within one transaction so a concurrent photo backup cannot be overwritten.
    return transaction('readwrite', store => {
        const request = store.get(scope + ':' + key);
        request.onsuccess = () => store.put({ id: scope + ':' + key, updatedAt: new Date().toISOString(), data: { ...request.result?.data, modelJson } });
    });
}
function base64(bytes) {
    let value = ''; for (let index = 0; index < bytes.length; index += 8192) value += String.fromCharCode(...bytes.subarray(index, index + 8192));
    return btoa(value);
}
export async function editorStream(scope, key) {
    const entry = await get(scope, key); if (!entry) return null;
    const payload = { modelJson: entry.data.modelJson, photos: (entry.data.photos || []).map(photo => ({ id: photo.id, name: photo.name,
        contentType: photo.contentType, bytes: base64(new Uint8Array(photo.bytes)) })) };
    return DotNet.createJSStreamReference(new Blob([JSON.stringify(payload)], { type: 'application/json' }));
}
