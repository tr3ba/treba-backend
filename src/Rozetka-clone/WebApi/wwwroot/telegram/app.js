import { el, icon, money, when, badge, button, heading, empty, field, input, select, toast, statusName } from './ui.js';
import * as draftStorage from '../js/editor-drafts.js';

const tg = window.Telegram?.WebApp;
const root = document.querySelector('#app');
const nav = document.querySelector('#navigation');
const previewRole = new URLSearchParams(location.search).get('preview');
let startupPreview = false;
let preview = null, bootstrap, current = { view: 'home' }, generation = 0, dirty = false, mutating = false;
let workspaceTimer, activeBackup = null, backupSafe = false, backupRevision = 0;
const isSeller = () => bootstrap?.user.role === 'Seller';
const guardOnly = () => ['Administrator', 'Manager'].includes(bootstrap?.user.role);
const roleName = () => ({ Seller: 'Продавець', Moderator: 'Модератор', Administrator: 'Адміністратор', Manager: 'Менеджер' })[bootstrap?.user.role] || 'Співробітник';
const emptyId = '00000000-0000-0000-0000-000000000000';
function append(parent, ...children) { parent.append(...children.flat(Infinity).filter(child => child !== null && child !== undefined && child !== false)); }

async function request(path, method = 'GET', body = undefined) {
    if (preview) {
        if (method !== 'GET' && !(method === 'POST' && path === 'duplicates')) throw new Error('Це попередній перегляд інтерфейсу. Зміни доступні після підключення бота.');
        const [route, query] = path.split('?'); const args = new URLSearchParams(query);
        if (route === 'bootstrap') return structuredClone(preview.bootstrap);
        if (route === 'products') {
            const rows = preview.products.filter(p => (!args.get('status') || p.status === args.get('status')) && (!args.get('search') || (p.name + p.sku).toLowerCase().includes(args.get('search').toLowerCase())));
            return { content: structuredClone(rows), page: 1, totalPages: 1, totalElements: rows.length };
        }
        if (/^products\/[^/]+\/history$/.test(route)) return structuredClone(preview.history);
        if (/^products\/[^/]+$/.test(route)) return structuredClone(preview.products.find(p => p.id === route.split('/')[1]));
        if (route === 'attributes') return [];
        if (route === 'imports/queue') return preview.importJobs || [];
        if (route === 'duplicates') return [];
        if (/^imports\/[^/]+$/.test(route)) return structuredClone(preview.importPreview);
        if (route === 'reviews') return { content: preview.reviews, page: 1, totalPages: 1, totalElements: preview.reviews.length };
        throw new Error('Цей екран доступний у підключеному боті.');
    }
    const headers = { Authorization: 'Tma ' + tg.initData };
    if (body !== undefined && !(body instanceof FormData)) headers['Content-Type'] = 'application/json';
    const url = path.startsWith('/') ? path : '/api/telegram/mini/' + path;
    const response = await fetch(url, { method, headers, body: body === undefined ? undefined : body instanceof FormData ? body : JSON.stringify(body), cache: 'no-store' });
    if (!response.ok) {
        let error; try { error = await response.json(); } catch { error = {}; }
        if (response.status === 401) throw new Error('Підтвердьте підключення в профілі Treba та відкрийте Mini App знову.');
        if (response.status === 403) throw new Error('Ця дія недоступна вашій ролі.');
        if (response.status === 429) throw new Error('Забагато запитів. Зачекайте трохи та повторіть.');
        throw new Error(error.title || error.message || 'Не вдалося виконати запит. Спробуйте ще раз.');
    }
    return response.status === 204 ? null : response.json();
}

function setDirty(value) { dirty = value; backupSafe = !value; backupRevision++; if (value) { tg?.enableClosingConfirmation?.(); activeBackup?.schedule(); } else tg?.disableClosingConfirmation?.(); }
function attachBackup(key, snapshot, indicator) {
    let timer, pending = Promise.resolve(), closed = false;
    const save = () => {
        if (closed) return pending;
        const value = snapshot(), revision = backupRevision;
        pending = pending.catch(() => {}).then(() => draftStorage.put(bootstrap.user.id, key, value)).then(() => { if(revision === backupRevision) { backupSafe = true; indicator.textContent = 'Зміни збережено в цьому браузері'; tg?.disableClosingConfirmation?.(); } return revision === backupRevision; })
            .catch(() => { indicator.textContent = 'Автозбереження недоступне. Збережіть картку кнопкою «Чернетка».'; return false; });
        return pending;
    };
    activeBackup = { schedule() { clearTimeout(timer); timer = setTimeout(save, 700); }, async flush() { clearTimeout(timer); return await save(); },
        async clear() { clearTimeout(timer); closed = true; await pending; try { await draftStorage.remove(bootstrap.user.id, key); } catch { } } };
}
function recoveryPage(entry, restore, discard) {
    return el('div', {}, heading('Продовжити роботу?', 'Знайдено локальну копію полів і фотографій від ' + when(entry.updatedAt) + '.'),
        el('section', { class: 'card stack' }, el('p', { class: 'hint' }, 'Сервер перевірить актуальність картки під час збереження.'),
            button('Відновити', restore, 'primary wide'), button('Видалити локальну копію', discard, 'secondary wide')));
}
async function confirmAction(message) {
    if (tg?.initData && tg.isVersionAtLeast?.('6.2')) return new Promise(resolve => tg.showConfirm(message, resolve));
    return window.confirm(message);
}
async function perform(control, operation) {
    if (mutating) return;
    if (preview) return toast('У попередньому перегляді зміни вимкнено. Підключіть бота для роботи з товарами.');
    mutating = true; control.disabled = true;
    const before = [...control.childNodes]; control.textContent = 'Зачекайте…';
    try { await operation(); }
    catch (error) { toast(error.message || 'Перевірте підключення та спробуйте знову.'); }
    finally { mutating = false; control.disabled = false; control.replaceChildren(...before); }
}
function renderNavigation() {
    const tabs = guardOnly() ? [['home', 'guard', 'Guard'], ['profile', 'person', 'Профіль']] : isSeller() ? [['home', 'home', 'Огляд'], ['products', 'box', 'Товари'], ['import', 'import', 'Імпорт'], ['profile', 'person', 'Профіль']]
        : [['home', 'home', 'Огляд'], ['moderation', 'guard', 'Перевірка'], ['reviews', 'review', 'Відгуки'], ['profile', 'person', 'Профіль']];
    nav.replaceChildren(...tabs.map(([view, name, label]) => el('button', { class: current.view === view ? 'active' : '', type: 'button', onclick: () => navigate(view), 'aria-current': current.view === view ? 'page' : undefined }, icon(name), label)));
}
async function navigate(view, args = {}, force = false) {
    if (mutating && !force) return toast('Дочекайтеся завершення поточної дії.');
    const backedUp = dirty && activeBackup ? await activeBackup.flush() : false;
    if (dirty && !force && !backedUp && !await confirmAction('Є незбережені зміни. Вийти без збереження?')) return;
    activeBackup = null;
    clearTimeout(workspaceTimer);
    setDirty(false); current = { view, ...args }; const ticket = ++generation;
    renderNavigation();
    if (view === 'home') tg?.BackButton?.hide(); else tg?.BackButton?.show();
    root.replaceChildren(el('div', { class: 'loading' }, el('span', { class: 'spinner' }), 'Завантаження…'));
    window.scrollTo({ top: 0 });
    try {
        const page = await ({ home: homePage, products: productsPage, moderation: productsPage, product: productPage,
            edit: editorPage, import: importPage, importQueue: importQueuePage, bulk: bulkPage, importPreview: importPreviewPage, reviews: reviewsPage, profile: profilePage, guard: guardPage }[view] || homePage)(current);
        if (ticket === generation) root.replaceChildren(page);
    } catch (error) {
        if (ticket !== generation) return;
        root.replaceChildren(empty('Не вдалося відкрити екран', error.message), button('Спробувати знову', () => navigate(view, args, true), 'secondary wide'));
    }
}

function productCard(product) {
    const image = product.images?.[0]?.imageUrl;
    return el('button', { class: 'product-card', type: 'button', onclick: () => navigate('product', { id: product.id }) },
        el('span', { class: 'thumb' }, image ? el('img', { src: image, alt: '', loading: 'lazy', referrerpolicy: 'no-referrer' }) : icon('box')),
        el('span', { class: 'product-copy' }, el('h3', {}, product.name), el('div', { class: 'sku' }, product.sku, ' · ', product.stockQuantity, ' шт.'),
            el('span', { class: 'product-meta' }, el('strong', {}, money(product.price)), badge(product.status))));
}
function guardCard() { return el('button', { type: 'button', class: 'card guard-card', onclick: () => navigate('guard') }, el('span', { class: 'icon-block' }, icon('guard')),
    el('div', {}, el('h2', {}, 'Вхід через Guard'), el('p', {}, 'Скануйте QR на сторінці входу')), icon('arrow')); }

async function homePage() {
    bootstrap = await request('bootstrap'); renderNavigation();
    if (guardOnly()) return el('div', {}, heading('Treba Guard', 'Підтверджуйте вхід до свого облікового запису.'), guardCard());
    const stats = bootstrap.stats;
    const page = el('div', {}, el('section', { class: 'welcome' }, el('div', {}, el('p', {}, 'ВАША РОБОЧА ПАНЕЛЬ'), el('h1', {}, 'Привіт, ' + bootstrap.user.name.split(' ')[0]), el('p', { style: 'margin-top:8px' }, isSeller() ? 'Усе важливе для вашого магазину.' : 'Допомагаємо зберігати якість каталогу.')),
        el('span', { class: 'avatar' }, bootstrap.user.name.slice(0, 1))), guardCard(),
        el('div', { class: 'stats' }, stat(stats.total, 'Усього товарів'), stat(stats.pending, 'На перевірці', true), stat(isSeller() ? stats.drafts : bootstrap.pendingReviews, isSeller() ? 'Чернетки' : 'Нових відгуків')));
    if (isSeller()) append(page, el('section', { class: 'card' }, el('div', { class: 'section-title', style: 'margin-top:0' }, el('h2', {}, 'Товар за посиланням'), icon('import')),
        el('p', { class: 'hint', style: 'margin-bottom:15px' }, 'Додайте посилання Rozetka, Prom.ua або Епіцентру. Перевірте дані та надішліть товар на модерацію.'), button('Імпортувати товар', () => navigate('import'), 'primary wide')));
    const rows = await request('products?status=' + (isSeller() ? '' : 'PENDING_MODERATION'));
    append(page, el('div', { class: 'section-title' }, el('h2', {}, isSeller() ? 'Останні товари' : 'Очікують рішення'), button('Усі', () => navigate(isSeller() ? 'products' : 'moderation'), 'secondary small')),
        rows.content.length ? rows.content.slice(0, 3).map(productCard) : empty('Поки все спокійно', isSeller() ? 'Додайте перший товар вручну або за посиланням.' : 'Нових товарів на перевірці немає.'));
    if (!isSeller()) append(page, button('Відкрити весь каталог', () => navigate('products'), 'secondary wide'));
    return page;
}
function stat(value, label, highlight = false) { return el('div', { class: 'stat' + (highlight ? ' highlight' : '') }, el('strong', {}, value), el('span', {}, label)); }

async function productsPage(args) {
    const moderation = args.view === 'moderation';
    const status = args.status ?? (moderation ? 'PENDING_MODERATION' : '');
    const search = args.search || ''; const pageIndex = args.page || 1;
    const data = await request('products?' + new URLSearchParams({ status, search, page: pageIndex }));
    const page = el('div', {}, heading(moderation ? 'Перевірка товарів' : isSeller() ? 'Мої товари' : 'Каталог', moderation ? 'Перевірте картку перед публікацією.' : 'Ціни, залишки та статус публікації.'));
    const selected = new Set();
    const bulkButton = button('Змінити вибрані (0)', () => navigate('bulk', { ids: [...selected] }), 'secondary wide', true);
    if (isSeller()) append(page, button('＋ Додати товар', () => navigate('edit'), 'primary wide'), bulkButton);
    let query = search;
    const filter = newStatus => navigate(args.view, { status: newStatus, search: query });
    const searchInput = input(search, value => query = value, { type: 'search', placeholder: 'Назва або артикул', 'aria-label': 'Пошук товарів' });
    searchInput.addEventListener('keydown', event => { if (event.key === 'Enter') filter(status); });
    const searchButton = button(icon('search'), () => filter(status), 'secondary'); searchButton.setAttribute('aria-label', 'Знайти товари');
    append(page, el('div', { class: 'search-row', style: 'margin-top:16px' }, field('', searchInput), searchButton),
        el('div', { class: 'filter-row' }, [['', 'Усі'], ['DRAFT', 'Чернетки'], ['PENDING_MODERATION', 'На перевірці'], ['ACTIVE', 'Опубліковані'], ['REJECTED', 'Відхилені']]
            .map(([value, label]) => el('button', { type: 'button', class: 'filter-chip' + (value === status ? ' active' : ''), onclick: () => filter(value) }, label))),
        data.content.length ? data.content.map(product => isSeller() ? el('div', { class: 'workspace-select-product' },
            el('input', { type: 'checkbox', 'aria-label': 'Вибрати: ' + product.name, onchange: event => { if (event.target.checked) selected.add(product.id); else selected.delete(product.id); bulkButton.textContent = 'Змінити вибрані (' + selected.size + ')'; bulkButton.disabled = !selected.size; } }), productCard(product)) : productCard(product)) : empty('Товарів не знайдено', 'Змініть пошук або вибраний статус.'));
    if (data.totalPages > 1) append(page, el('div', { class: 'pagination' }, button('Назад', () => navigate(args.view, { status, search, page: pageIndex - 1 }), 'secondary small', pageIndex <= 1),
        `${pageIndex} / ${data.totalPages}`, button('Далі', () => navigate(args.view, { status, search, page: pageIndex + 1 }), 'secondary small', pageIndex >= data.totalPages)));
    return page;
}

function gallery(urls) { return urls.length ? el('div', { class: 'gallery' }, urls.map((url, index) => el('figure', {}, el('img', { src: url, alt: 'Фото товару ' + (index + 1), loading: index ? 'lazy' : 'eager', referrerpolicy: 'no-referrer' }), el('figcaption', {}, `${index + 1} / ${urls.length}`)))) : null; }
function timeline(rows) { return rows.length ? el('div', {}, rows.map(row => el('div', { class: 'history-entry' }, el('strong', {}, { Submitted: 'Надіслано на перевірку', Approved: 'Опубліковано', Rejected: 'Відхилено' }[row.decision] || row.decision), el('small', {}, when(row.occurredAt), ' · ', row.actorName), row.reason ? el('p', {}, row.reason) : null))) : el('p', { class: 'hint' }, 'Нових рішень щодо цього товару поки немає.'); }
async function productPage(args) {
    const product = await request('products/' + args.id);
    const history = await request('products/' + args.id + '/history');
    const last = history[0];
    const page = el('div', {}, back(() => navigate(isSeller() ? 'products' : 'moderation')), gallery(product.images.map(i => i.imageUrl)),
        el('section', { class: 'product-head' }, badge(product.status), el('h1', {}, product.name), el('strong', {}, money(product.price))));
    if (last?.decision === 'Rejected' && product.status === 'REJECTED') append(page, el('div', { class: 'notice error' }, el('strong', {}, 'Потрібно виправити'), el('p', {}, last.reason)));
    append(page, el('section', { class: 'card detail-grid' }, detail('Артикул', product.sku), detail('Залишок', product.stockQuantity + ' шт.'), detail('Категорія', product.categoryName), detail('Бренд', product.brandName)),
        el('section', { class: 'card' }, el('h2', { style: 'margin-bottom:13px' }, 'Опис'), el('p', { class: 'description' }, product.description || 'Опис не заповнено.')));
    if (product.variants.length > 1) append(page, el('section', { class: 'card' }, el('h2', { style: 'margin-bottom:12px' }, 'Варіанти'), product.variants.map(v => el('div', { class: 'characteristic' }, el('strong', {}, v.name, v.isActive ? '' : ' · Вимкнено'), el('p', {}, v.sku, ' · ', money(v.price), ' · ', v.stockQuantity, ' шт.')))));
    if (product.attributes.length) {
        const definitions = await request('attributes?categoryId=' + product.categoryId);
        append(page, el('details', { class: 'card' }, el('summary', {}, 'Характеристики'), product.attributes.map(a => {
            const definition = definitions.find(d => d.id === a.attributeId);
            return el('div', { class: 'characteristic' }, el('span', {}, definition?.name || 'Характеристика'), String(a.stringValue ?? a.numberValue ?? (a.boolValue == null ? definition?.options.find(o => o.id === a.optionId)?.name || '' : a.boolValue ? 'Так' : 'Ні')));
        })));
    }
    append(page, el('details', { class: 'card' }, el('summary', {}, 'Історія модерації'), timeline(history)));
    if (isSeller() && product.status !== 'ARCHIVED') {
        const remove = button('Видалити товар', async () => {
            if (!await confirmAction(`Видалити «${product.name}» з каталогу? Дані минулих замовлень збережуться.`)) return;
            await perform(remove, async () => { await request(`products/${product.id}`, 'DELETE'); toast('Товар видалено з каталогу.', true); await navigate('products', {}, true); });
        }, 'danger wide', !!preview);
        append(page, remove);
    }
    if (isSeller() && !['PENDING_MODERATION', 'ARCHIVED'].includes(product.status)) {
        append(page, button('Редагувати товар', () => navigate('edit', { id: product.id }), 'primary wide'));
        if (['DRAFT', 'REJECTED'].includes(product.status)) {
            const submit = button('На модерацію', () => perform(submit, async () => { await request(`products/${product.id}/submit`, 'POST', {}); setDirty(false); toast('Товар надіслано на модерацію', true); await navigate('product', { id: product.id }, true); }), 'secondary wide', !!preview);
            append(page, el('div', { style: 'height:10px' }), submit);
        }
    }
    if (!isSeller() && product.status === 'PENDING_MODERATION') {
        const approve = button('Опублікувати', async () => {
            if (!await confirmAction('Опублікувати цей товар у каталозі Treba?')) return;
            await perform(approve, async () => { await request(`products/${product.id}/approve`, 'POST', {}); toast('Товар опубліковано', true); await navigate('moderation', {}, true); });
        }, 'primary', !!preview);
        const reject = button('Відхилити', () => rejectionDialog(product.id), 'danger', !!preview);
        append(page, el('div', { class: 'actions' }, reject, approve));
    }
    return page;
}
function detail(label, value) { return el('div', {}, el('span', {}, label), value || '—'); }
function back(click) { return el('button', { type: 'button', class: 'back-button', onclick: click }, icon('back'), 'Назад'); }
function rejectionDialog(id) {
    let reason = '';
    const textarea = el('textarea', { maxlength: 2000, placeholder: 'Що продавцю потрібно виправити?', 'aria-label': 'Причина відхилення', oninput: event => reason = event.target.value });
    const submit = button('Відхилити товар', () => perform(submit, async () => {
        if (!reason.trim()) throw new Error('Укажіть причину відхилення.');
        await request(`products/${id}/reject`, 'POST', { reason }); overlay.remove(); toast('Рішення збережено', true); await navigate('moderation', {}, true);
    }), 'danger');
    const overlay = el('div', { class: 'modal-backdrop' }, el('section', { class: 'modal', role: 'dialog', 'aria-modal': 'true', 'aria-labelledby': 'reject-title' },
        el('h2', { id: 'reject-title' }, 'Причина відхилення'), el('p', {}, 'Продавець побачить її в картці товару та зможе внести виправлення.'), textarea,
        el('div', { class: 'actions' }, button('Скасувати', () => overlay.remove(), 'secondary'), submit)));
    document.body.append(overlay); textarea.focus();
}

async function editorPage(args) {
    if (!isSeller()) throw new Error('Редактор доступний продавцю.');
    const backupKey = 'editor:' + (args.id || 'new');
    let cached = null; try { cached = await draftStorage.get(bootstrap.user.id, backupKey); } catch { }
    if (cached && !args.restore) return recoveryPage(cached, () => navigate('edit', { ...args, restore: true }, true),
        async () => { await draftStorage.remove(bootstrap.user.id, backupKey); await navigate('edit', args, true); });
    const restored = args.restore ? cached?.data : null;
    let id = restored?.id || args.id;
    const existing = id ? await request('products/' + id) : null;
    if (existing && ['PENDING_MODERATION', 'ARCHIVED'].includes(existing.status)) throw new Error('Цей товар зараз не можна редагувати.');
    const allowed = bootstrap.categories.filter(c => bootstrap.allowedCategoryIds.includes(c.id));
    const model = restored?.model || (existing ? { ...existing, expectedUpdatedAt: existing.updatedAt, variants: structuredClone(existing.variants), attributes: structuredClone(existing.attributes) }
        : { name: '', slug: '', categoryId: allowed[0]?.id || emptyId, brandId: bootstrap.brands[0]?.id || emptyId, storeId: bootstrap.stores[0]?.id || emptyId, description: '', shortDescription: '',
            warrantyMonths: 0, countryOfOrigin: '', variants: [{ id: crypto.randomUUID(), name: 'Основний', sku: '', barcode: '', price: 0, oldPrice: null, stockQuantity: 0, isActive: true }], attributes: [] });
    const pendingImages = (restored?.photos || []).map(p => new File([p.blob], p.name, { type: p.type })); const savedImages = existing?.images || [];
    let definitions = model.categoryId !== emptyId ? await request('attributes?categoryId=' + model.categoryId) : [];
    const page = el('div', {}, back(() => navigate(id ? 'product' : 'products', id ? { id } : {})), heading(id ? 'Редагування' : 'Новий товар', 'Спочатку збережіть чернетку, потім надішліть на перевірку.'));
    const backupHint = el('p', { class: 'hint', role: 'status' }, 'Поля та фотографії автоматично зберігаються в цьому браузері.');
    append(page, backupHint);
    attachBackup(backupKey, () => ({ id, model: structuredClone(model), photos: pendingImages.map(file => ({ name: file.name, type: file.type, blob: file })) }), backupHint);
    if (!allowed.length) append(page, el('div', { class: 'notice error' }, 'Немає дозволених категорій. Зверніться до адміністратора Treba.'));
    const update = key => value => { model[key] = value; setDirty(true); };
    const attributesPanel = el('div');
    const variantPanel = el('div', { class: 'stack' });
    const categoriesSelect = select(bootstrap.categories.filter(c => allowed.some(a => a.id === c.id) || c.id === model.categoryId), model.categoryId, async value => {
        model.categoryId = value; model.attributes = []; setDirty(true);
        try { definitions = await request('attributes?categoryId=' + value); renderAttributes(attributesPanel, model, definitions); } catch (error) { toast(error.message); }
    }, { required: true });
    const form = el('form', { class: 'card stack', onsubmit: event => event.preventDefault() },
        field('Назва *', input(model.name, update('name'), { maxlength: 250, required: true })),
        field('Категорія *', categoriesSelect),
        field('Бренд *', select(bootstrap.brands, model.brandId, update('brandId'), { required: true })),
        field('Артикул виробника (MPN)', input(model.manufacturerPartNumber || '', update('manufacturerPartNumber'), { maxlength: 100 })),
        !id ? field('Магазин *', select(bootstrap.stores, model.storeId, update('storeId'), { required: true })) : null,
        field('Опис', el('textarea', { maxlength: 10000, rows: 6, oninput: event => update('description')(event.target.value) }, model.description)),
        field('Короткий опис', input(model.shortDescription, update('shortDescription'), { maxlength: 500 })),
        el('div', { class: 'field-grid' }, field('Гарантія, місяців', input(model.warrantyMonths, update('warrantyMonths'), { type: 'number', min: 0, max: 120, step: 1 })), field('Країна виробництва', input(model.countryOfOrigin, update('countryOfOrigin'), { maxlength: 100 }))),
        el('section', { class: 'form-section' }, el('h2', {}, 'Варіанти'), variantPanel,
            button('＋ Додати варіант', () => { model.variants.push({ id: crypto.randomUUID(), name: 'Новий варіант', sku: '', price: 0, oldPrice: null, stockQuantity: 0, isActive: true }); setDirty(true); renderVariants(); }, 'secondary small')),
        attributesPanel);
    function renderVariants() {
        variantPanel.replaceChildren(...model.variants.map(variant => el('section', { class: 'variant' },
            el('div', { class: 'variant-header' }, el('label', { class: 'check-label' }, el('input', { type: 'checkbox', checked: variant.isActive, onchange: event => { variant.isActive = event.target.checked; setDirty(true); } }), 'Активний варіант'),
                !existing?.variants.some(v => v.id === variant.id) && model.variants.length > 1 ? button('Прибрати', () => { model.variants.splice(model.variants.indexOf(variant), 1); model.attributes = model.attributes.filter(a => a.variantId !== variant.id); setDirty(true); renderVariants(); }, 'secondary small') : null),
            field('Назва варіанта *', input(variant.name, value => { variant.name = value; setDirty(true); }, { maxlength: 250, required: true })),
            field('Артикул *', input(variant.sku, value => { variant.sku = value; setDirty(true); }, { maxlength: 100, required: true })),
            field('Штрихкод / EAN', input(variant.barcode || '', value => { variant.barcode = value; setDirty(true); }, { maxlength: 100 })),
            el('div', { class: 'field-grid' }, field('Ціна, ₴ *', input(variant.price || '', value => { variant.price = value; setDirty(true); }, { type: 'number', min: .01, max: 999999999.99, step: .01, required: true })),
                field('Стара ціна, ₴', input(variant.oldPrice, value => { variant.oldPrice = Number.isNaN(value) ? null : value; setDirty(true); }, { type: 'number', min: .01, step: .01 }))),
            field('Залишок *', input(variant.stockQuantity, value => { variant.stockQuantity = value; setDirty(true); }, { type: 'number', min: 0, max: 1000000, step: 1, required: true })))));
        renderAttributes(attributesPanel, model, definitions);
    }
    renderVariants();
    const duplicatePanel = el('div', { class: 'stack' });
    form.append(button('Перевірити дублікати', async () => {
        try { await renderDuplicates(duplicatePanel, model.brandId, model.manufacturerPartNumber, model.variants.map(v => v.barcode).filter(Boolean), id); } catch (error) { toast(error.message); }
    }, 'secondary wide'), duplicatePanel, el('label', { class: 'check-label' }, el('input', { type: 'checkbox', checked: !!model.duplicateConfirmed,
        onchange: event => update('duplicateConfirmed')(event.target.checked) }), 'Створити окрему картку в разі збігу'));
    const fileInput = el('input', { type: 'file', accept: 'image/jpeg,image/png,image/webp,image/gif', multiple: true, onchange: event => {
        const files = Array.from(event.target.files || []);
        if (files.length + pendingImages.length + savedImages.length > 8 || files.some(f => f.size > 5 * 1024 * 1024)) return toast('Дозволено до 8 фотографій розміром до 5 МБ кожна.');
        pendingImages.push(...files); setDirty(true); imageHint.textContent = `Вибрано нових фотографій: ${pendingImages.length}`;
    } });
    const imageHint = el('small', { class: 'hint' }, 'Нових фотографій: ' + pendingImages.length + '. Фото завантажаться під час збереження; до 5 МБ кожне.');
    form.append(el('section', { class: 'form-section stack' }, el('h2', {}, 'Фотографії'), gallery(existing?.images.map(i => i.imageUrl) || []), field('Додати фотографії', fileInput), imageHint));
    async function save(control, submit) {
        if (!form.reportValidity()) return;
        await perform(control, async () => {
            const payload = { ...model, slug: model.slug || 'treba-' + crypto.randomUUID().replaceAll('-', ''), sku: model.variants[0]?.sku || '', price: model.variants[0]?.price || 0, stockQuantity: model.variants[0]?.stockQuantity || 0 };
            const duplicates = await renderDuplicates(duplicatePanel, model.brandId, model.manufacturerPartNumber, model.variants.map(v => v.barcode).filter(Boolean), id);
            if (duplicates.length && !model.duplicateConfirmed) { toast('Схожий товар уже є. Відкрийте його або підтвердьте створення окремої картки.'); return; }
            const saved = await request(id ? 'products/' + id : 'products', id ? 'PUT' : 'POST', payload);
            id = saved.id; model.expectedUpdatedAt = saved.updatedAt; model.slug = saved.slug;
            await activeBackup?.flush();
            while (pendingImages.length) {
                const file = pendingImages[0]; const body = new FormData(); body.append('File', file); body.append('IsMain', String(savedImages.length === 0)); body.append('SortOrder', String(savedImages.length));
                const uploaded = await request(`/api/v1/products/${id}/images/upload`, 'POST', body);
                savedImages.push(uploaded); pendingImages.shift();
                await activeBackup?.flush();
            }
            if (submit) await request(`products/${id}/submit`, 'POST', {});
            await activeBackup?.clear(); setDirty(false); toast(submit ? 'Товар надіслано на модерацію' : 'Чернетку збережено', true); await navigate('product', { id }, true);
        });
    }
    const draft = button('Чернетка', () => save(draft, false), 'secondary', !!preview || !allowed.length);
    const submit = button('На модерацію', () => save(submit, true), 'primary', !!preview || !allowed.length);
    form.append(el('div', { class: 'actions' }, draft, submit)); append(page, form); if (restored) setDirty(true); return page;
}

function renderAttributes(panel, model, definitions) {
    for (const definition of definitions.filter(d => d.isRequired)) if (!model.attributes.some(a => a.attributeId === definition.id)) model.attributes.push({ attributeId: definition.id });
    const content = el('div', { class: 'stack' });
    for (const attribute of model.attributes) {
        const definition = definitions.find(d => d.id === attribute.attributeId); if (!definition) continue;
        const change = value => { attribute.stringValue = null; attribute.numberValue = null; attribute.boolValue = null; attribute.optionId = null;
            if (definition.type === 'NUMBER') attribute.numberValue = value;
            else if (definition.type === 'BOOLEAN') attribute.boolValue = value === '' ? null : value === 'true';
            else if (['SELECT', 'MULTI_SELECT'].includes(definition.type)) attribute.optionId = value || null;
            else attribute.stringValue = value; setDirty(true); };
        let control;
        if (definition.type === 'BOOLEAN') control = select([{ id: '', name: 'Виберіть' }, { id: 'true', name: 'Так' }, { id: 'false', name: 'Ні' }], attribute.boolValue == null ? '' : String(attribute.boolValue), change);
        else if (['SELECT', 'MULTI_SELECT'].includes(definition.type)) control = select([{ id: '', name: 'Виберіть' }, ...definition.options], attribute.optionId || '', change);
        else control = input(definition.type === 'NUMBER' ? attribute.numberValue : attribute.stringValue, change,
            { type: definition.type === 'NUMBER' ? 'number' : definition.type === 'DATE' ? 'date' : 'text', step: definition.type === 'NUMBER' ? '.0001' : undefined, maxlength: 2000 });
        content.append(el('div', { class: 'characteristic-row' }, el('header', {}, el('strong', {}, definition.name + (definition.isRequired ? ' *' : '')), button('Прибрати', () => { model.attributes.splice(model.attributes.indexOf(attribute), 1); setDirty(true); renderAttributes(panel, model, definitions); }, 'secondary small', definition.isRequired && model.attributes.filter(a => a.attributeId === definition.id).length === 1)),
            select([{ id: '', name: 'Увесь товар' }, ...model.variants.map(v => ({ id: v.id, name: v.name }))], attribute.variantId || '', value => { attribute.variantId = value || null; setDirty(true); }), control));
    }
    let selected = definitions[0]?.id || '';
    if (definitions.length) content.append(select(definitions, selected, value => selected = value), button('＋ Додати характеристику', () => { model.attributes.push({ attributeId: selected }); setDirty(true); renderAttributes(panel, model, definitions); }, 'secondary small'));
    else content.append(el('p', { class: 'hint' }, 'У цій категорії характеристики ще не налаштовано.'));
    panel.replaceChildren(el('details', { class: 'form-section', open: definitions.some(d => d.isRequired) }, el('summary', {}, 'Характеристики'), content));
}

async function importPage() {
    if (!isSeller()) throw new Error('Імпорт доступний продавцю.');
    let url = '', categoryId = '';
    const submit = button('Додати до черги', () => {
        if (!form.reportValidity()) return;
        const urls = url.split(/[\r\n]+/).map(value => value.trim()).filter(Boolean);
        if (!urls.length || urls.length > 20) return toast('Додайте від 1 до 20 посилань, кожне з нового рядка.');
        return perform(submit, async () => { await request('imports/queue', 'POST', { items: urls.map(url => ({ url, categoryId: categoryId || null })) }); await navigate('importQueue', {}, true); });
    }, 'primary wide', !!preview);
    const form = el('form', { class: 'card stack', onsubmit: event => event.preventDefault() },
        field('Посилання на товари', el('textarea', { rows: 4, placeholder: 'Rozetka, Prom.ua або Епіцентр — одне посилання в рядку', required: true, maxlength: 30000, oninput: event => url = event.target.value })),
        bootstrap.canClassifyImports ? field('Категорія Treba', select([{ id: '', name: 'Визначити автоматично' }, ...bootstrap.categories.filter(c => bootstrap.allowedCategoryIds.includes(c.id))], categoryId, value => categoryId = value),
            'Якщо категорію не визначено, виберіть відповідну для цих посилань. Модератор перевірить картки перед публікацією.') : null, submit,
        button('Відкрити чергу імпорту', () => navigate('importQueue'), 'secondary wide'));
    return el('div', {}, heading('Імпорт товару', 'Нова картка — з одного посилання.'),
        el('section', { class: 'import-hero' }, icon('import'), el('h2', {}, 'Посилання. Перевірка. Готово.'), el('p', {}, 'Назва, опис і фотографії з’являться в попередньому перегляді. Ви вирішуєте, коли надіслати товар на перевірку.')),
        form, el('ol', { class: 'steps' }, el('li', {}, 'Вставте посилання на конкретний товар.'), el('li', {}, 'Treba перевірить дозвіл на його категорію.'), el('li', {}, 'Перевірте дані, укажіть свою ціну та залишок.'), el('li', {}, 'Надішліть картку модератору.')),
        el('p', { class: 'hint' }, 'Якщо джерело обмежило завантаження, картку можна заповнити вручну.'), button('Додати вручну', () => navigate('edit'), 'secondary wide'),
        preview ? button('Показати приклад попереднього перегляду', () => navigate('importPreview', { id: preview.importPreview.id }), 'secondary wide') : null);
}
async function importQueuePage() {
    if (!isSeller()) throw new Error('Черга доступна продавцю.');
    const ticket = generation, panel = el('div', { class: 'stack' }), feedback = el('p', { class: 'hint', role: 'status' });
    const retryCategories = new Map();
    const page = el('div', {}, back(() => navigate('import')), heading('Черга імпорту', 'Можна закрити Telegram: завантаження продовжиться на сервері.'), feedback, panel);
    async function refresh() {
        if (generation !== ticket) return;
        try {
            const rows = await request('imports/queue'); if (generation !== ticket) return;
            feedback.textContent = 'Оновлено: ' + new Date().toLocaleTimeString();
            panel.replaceChildren(...(rows.length ? rows.map(row => {
                const canRetry = ['Failed', 'Expired'].includes(row.status);
                let categoryId = retryCategories.has(row.id) ? retryCategories.get(row.id) : row.categoryId || '';
                const category = canRetry && bootstrap.canClassifyImports ? field('Категорія Treba', select([{ id: '', name: 'Визначити автоматично' }, ...bootstrap.categories.filter(c => bootstrap.allowedCategoryIds.includes(c.id))], categoryId, value => { categoryId = value; retryCategories.set(row.id, value); })) : null;
                const retry = button('Повторити завантаження', () => perform(retry, async () => { await request(`imports/queue/${row.id}/retry` + (bootstrap.canClassifyImports && categoryId ? '?categoryId=' + encodeURIComponent(categoryId) : '?resetCategory=true'), 'POST', {}); retryCategories.delete(row.id); await refresh(); }), 'secondary wide', !!preview);
                return el('section', { class: 'card stack' }, el('strong', {}, { Queued: 'У черзі', Processing: 'Завантажується…', Ready: 'Готовий попередній перегляд', Failed: 'Не вдалося завантажити', Expired: 'Термін попереднього перегляду минув', Submitted: 'Надіслано на модерацію' }[row.status] || row.status),
                    el('a', { class: 'source-link', href: row.sourceUrl, target: '_blank', rel: 'noopener noreferrer' }, row.sourceUrl), row.error ? el('p', { class: 'notice error' }, row.error) : null,
                    row.status === 'Ready' ? button('Перевірити картку', () => navigate('importPreview', { id: row.previewId }), 'primary wide')
                        : row.status === 'Submitted' && row.productId ? button('Відкрити товар', () => navigate('product', { id: row.productId }), 'secondary wide')
                        : canRetry ? el('div', { class: 'stack' }, category, retry) : null);
            }) : [empty('Черга порожня', 'Додайте посилання на товари на екрані імпорту.')]));
            if (rows.some(row => ['Queued', 'Processing'].includes(row.status)) && !preview) workspaceTimer = setTimeout(refresh, 3000);
        } catch (error) { feedback.textContent = error.message; if (generation === ticket && !preview) workspaceTimer = setTimeout(refresh, 10000); }
    }
    await refresh(); return page;
}
async function renderDuplicates(panel, brandId, part, barcode, excludeId) {
    const barcodes = (Array.isArray(barcode) ? barcode : [barcode]).filter(Boolean);
    if (!part && !barcodes.length) { panel.replaceChildren(el('p', { class: 'hint' }, 'Укажіть артикул виробника або штрихкод для пошуку.')); return []; }
    const rows = await request('duplicates', 'POST', { brandId: brandId || emptyId, manufacturerPartNumber: part || '', barcodes, excludeId: excludeId || null });
    panel.replaceChildren(...(rows.length ? rows.map(row => {
        const remove = button('Видалити товар', async () => {
            if (!await confirmAction(`Видалити «${row.name}» з каталогу? Дані минулих замовлень збережуться.`)) return;
            await perform(remove, async () => { await request(`products/${row.id}`, 'DELETE'); await renderDuplicates(panel, brandId, part, barcode, excludeId); });
        }, 'danger wide', !!preview);
        return el('section', { class: 'notice stack' }, el('strong', {}, 'Можливий дублікат'), el('p', {}, row.name, ' · ', row.storeName),
            button('Відкрити наявний товар', () => navigate('product', { id: row.id }), 'secondary wide'), remove);
    }) : [el('p', { class: 'hint' }, 'За цими ідентифікаторами збігів не знайдено.')]));
    return rows;
}
async function bulkPage(args) {
    if (!isSeller() || !args.ids?.length) return el('div', {}, heading('Виберіть товари'), button('Відкрити товари', () => navigate('products'), 'primary wide'));
    let priceMode = '', priceValue = null, stock = null, availability = '';
    const results = el('div', { class: 'stack' });
    const previewButton = button('Перегляд змін', () => {
        if (!form.reportValidity()) return;
        return perform(previewButton, async () => {
            const plan = await request('bulk/preview', 'POST', { productIds: args.ids, price: priceMode === 'fixed' ? priceValue : null, pricePercent: priceMode === 'percent' ? priceValue : null, stockQuantity: stock, isActive: availability ? availability === 'true' : null });
            form.hidden = true;
            const apply = button('Підтвердити зміни', () => perform(apply, async () => { await request('bulk/apply', 'POST', { token: plan.token }); setDirty(false); await navigate('products', {}, true); }), 'primary wide');
            results.replaceChildren(el('p', { class: 'notice' }, 'Перевірте зміни. Статус модерації збережеться; дії потраплять до журналу.'),
                ...plan.rows.map(row => el('section', { class: 'card stack' }, el('strong', {}, row.name), el('small', {}, row.sku),
                    el('p', {}, `Ціна: ${money(row.beforePrice)} → ${money(row.afterPrice)}`), el('p', {}, `Залишок: ${row.beforeStock} → ${row.afterStock}`),
                    row.beforeOldPrice !== row.afterOldPrice ? el('p', {}, 'Стара ціна: ' + money(row.beforeOldPrice) + ' → прибрано') : null,
                    el('p', {}, `Доступність: ${row.beforeActive ? 'доступний' : 'недоступний'} → ${row.afterActive ? 'доступний' : 'недоступний'}`))),
                apply, button('Змінити параметри', () => { form.hidden = false; results.replaceChildren(); }, 'secondary wide'));
        });
    }, 'primary wide', !!preview);
    const form = el('form', { class: 'card stack', onsubmit: event => event.preventDefault() },
        field('Зміна ціни', select([{ id: '', name: 'Не змінювати' }, { id: 'percent', name: 'Змінити на %' }, { id: 'fixed', name: 'Установити ціну' }], '', value => { priceMode = value; setDirty(true); })),
        field('Нове значення / відсоток', input('', value => { priceValue = value; setDirty(true); }, { type: 'number', step: '.01' })),
        field('Залишок — порожньо, щоб не змінювати', input('', value => { stock = Number.isNaN(value) ? null : value; setDirty(true); }, { type: 'number', min: 0, max: 1000000, step: 1 })),
        field('Доступність варіантів', select([{ id: '', name: 'Не змінювати' }, { id: 'true', name: 'Доступні' }, { id: 'false', name: 'Недоступні' }], '', value => { availability = value; setDirty(true); })), previewButton);
    return el('div', {}, back(() => navigate('products')), heading('Масова зміна', 'Вибрано товарів: ' + args.ids.length), form, results);
}
async function importPreviewPage(args) {
    bootstrap = await request('bootstrap');
    const data = await request('imports/' + args.id);
    if (data.submittedAt) return el('div', {}, heading('Товар уже надіслано'), button('Відкрити картку', () => navigate('product', { id: data.productId }), 'primary wide'));
    const backupKey = 'import:' + data.id;
    let cached = null; try { cached = await draftStorage.get(bootstrap.user.id, backupKey); } catch { }
    if (cached && !args.restore) return recoveryPage(cached, () => navigate('importPreview', { ...args, restore: true }, true), async () => { await draftStorage.remove(bootstrap.user.id, backupKey); await navigate('importPreview', args, true); });
    const model = (args.restore ? cached?.data?.model : null) || { name: data.name, description: data.description, manufacturerPartNumber: data.manufacturerPartNumber || '', barcode: data.barcode || '', duplicateConfirmed: false, brandId: data.brandId || bootstrap.brands.find(b => b.name.toLowerCase() === data.brandName.toLowerCase())?.id || '', storeId: bootstrap.stores[0]?.id || '',
        sku: 'TRB-' + data.id.slice(0, 8).toUpperCase(), price: data.sourcePrice || 0, stockQuantity: 0, variants: [], attributes: [] };
    let definitions = await request('attributes?categoryId=' + data.categoryId); const attributePanel = el('div'); renderAttributes(attributePanel, model, definitions);
    const update = key => value => { model[key] = value; setDirty(true); };
    const categoryLabel = el('div', { class: 'notice success' }, icon('check'), ' Категорія дозволена: ', data.categoryName);
    const page = el('div', {}, back(() => navigate('import')), heading('Попередній перегляд імпорту', 'Картку ще не опубліковано.'), categoryLabel, gallery(data.images));
    if (data.canClassifyCategory) {
        const allowedCategories = () => bootstrap.categories.filter(c => bootstrap.allowedCategoryIds.includes(c.id));
        const categorySelect = select(allowedCategories(), data.categoryId, async value => {
            if (mutating) { categorySelect.value = data.categoryId; return; }
            mutating = true; categorySelect.disabled = true;
            try { await applyCategory(await request(`imports/${data.id}/category`, 'PUT', { categoryId: value })); }
            catch (error) { categorySelect.value = data.categoryId; toast(error.message); }
            finally { mutating = false; categorySelect.disabled = false; }
        });
        let categoryName = data.sourceCategoryName, parentId = data.categoryId;
        const parentSelect = select(allowedCategories(), parentId, value => parentId = value);
        const create = button('Створити категорію', () => perform(create, async () => {
            const result = await request(`imports/${data.id}/category`, 'POST', { name: categoryName, parentId });
            bootstrap = await request('bootstrap');
            categorySelect.replaceChildren(...allowedCategories().map(c => el('option', { value: c.id }, c.name)));
            await applyCategory(result); categorySelect.value = result.categoryId;
            toast('Категорію вибрано.', true);
        }), 'secondary wide', !!preview);
        async function applyCategory(result) {
            definitions = await request('attributes?categoryId=' + result.categoryId);
            model.attributes = model.attributes.filter(a => definitions.some(d => d.id === a.attributeId));
            Object.assign(data, result); renderAttributes(attributePanel, model, definitions);
            categoryLabel.replaceChildren(icon('check'), ' Категорія дозволена: ', result.categoryName); setDirty(true);
        }
        append(page, el('section', { class: 'card stack' }, field('Категорія', categorySelect),
            el('details', {}, el('summary', {}, 'Створити категорію'), el('div', { class: 'stack form-section' },
                field('Назва категорії', input(categoryName, value => categoryName = value, { maxlength: 200 })), field('Батьківська категорія', parentSelect), create))));
    }
    const backupHint = el('p', { class: 'hint', role: 'status' }, 'Зміни автоматично зберігаються в цьому браузері.'); append(page, backupHint);
    attachBackup(backupKey, () => ({ model: structuredClone(model) }), backupHint);
    const duplicatePanel = el('div', { class: 'stack' });
    if (data.duplicates?.length) await renderDuplicates(duplicatePanel, model.brandId, model.manufacturerPartNumber, model.barcode);
    if (!data.images.length) append(page, el('div', { class: 'notice' }, 'Джерело не передало фотографії. Їх не буде додано автоматично.'));
    const brandPanel = el('div');
    const brandSelect = select([{ id: '', name: 'Оберіть бренд' }, ...bootstrap.brands], model.brandId, value => { update('brandId')(value); renderBrandCreation(); }, { required: true });
    function renderBrandCreation() {
        brandPanel.replaceChildren();
        if (model.brandId) return;
        let name = data.brandName || '';
        const create = button('Створити бренд', () => perform(create, async () => {
            const brand = await request(`imports/${data.id}/brand`, 'POST', { name });
            bootstrap = await request('bootstrap'); model.brandId = brand.id; setDirty(true);
            brandSelect.replaceChildren(el('option', { value: '' }, 'Оберіть бренд'), ...bootstrap.brands.map(b => el('option', { value: b.id }, b.name)));
            brandSelect.value = brand.id; renderBrandCreation();
        }), 'secondary wide', !!preview);
        brandPanel.append(el('section', { class: 'notice stack' }, el('p', {}, 'Виберіть наявний бренд або створіть новий.'), field('Бренд', input(name, value => name = value, { maxlength: 200 })), create));
    }
    renderBrandCreation();
    const form = el('form', { class: 'card stack', onsubmit: event => event.preventDefault() },
        field('Назва *', input(model.name, update('name'), { required: true, maxlength: 250 })),
        field('Опис', el('textarea', { rows: 7, maxlength: 10000, oninput: event => update('description')(event.target.value) }, model.description)),
        field('Бренд Treba *', brandSelect, 'У джерелі: ' + (data.brandName || 'Не вказано')), brandPanel,
        field('Магазин *', select([{ id: '', name: 'Оберіть магазин' }, ...bootstrap.stores], model.storeId, update('storeId'), { required: true })),
        field('Ваш артикул *', input(model.sku, update('sku'), { required: true, maxlength: 100 })),
        field('Артикул виробника (MPN)', input(model.manufacturerPartNumber, update('manufacturerPartNumber'), { maxlength: 100 })),
        field('Штрихкод / EAN', input(model.barcode, update('barcode'), { maxlength: 100 })),
        button('Перевірити дублікати', async () => { try { await renderDuplicates(duplicatePanel, model.brandId, model.manufacturerPartNumber, model.barcode); } catch (error) { toast(error.message); } }, 'secondary wide'), duplicatePanel,
        el('label', { class: 'check-label' }, el('input', { type: 'checkbox', checked: model.duplicateConfirmed, onchange: event => update('duplicateConfirmed')(event.target.checked) }), 'Створити окрему картку, якщо знайдено схожий товар'),
        el('div', { class: 'field-grid' }, field('Ваша ціна, ₴ *', input(model.price || '', update('price'), { type: 'number', min: .01, max: 999999999.99, step: .01, required: true })),
            field('Ваш залишок *', input(model.stockQuantity, update('stockQuantity'), { type: 'number', min: 0, max: 1000000, step: 1, required: true }))), attributePanel,
        el('a', { href: data.sourceUrl, class: 'source-link', target: '_blank', rel: 'noopener noreferrer' }, 'Переглянути джерело ↗'));
    if (data.characteristics.length) form.append(el('details', {}, el('summary', {}, 'Характеристики джерела'), data.characteristics.map(c => el('div', { class: 'characteristic' }, el('span', {}, c.name), c.value))));
    const submit = button('Усе правильно — на модерацію', () => {
        if (!form.reportValidity()) return;
        return perform(submit, async () => { const result = await request(`imports/${data.id}/submit`, 'POST', model); await activeBackup?.clear(); setDirty(false); toast('Товар надіслано на модерацію', true); await navigate('product', { id: result.productId }, true); });
    }, 'primary wide', !!preview);
    form.append(el('p', { class: 'hint' }, 'Перевірте дані картки. Після надсилання товар з’явиться в черзі модератора.'), submit);
    append(page, form); setDirty(!preview); return page;
}

async function reviewsPage(args) {
    if (isSeller()) throw new Error('Модерація відгуків недоступна продавцю.');
    const data = await request('reviews?' + new URLSearchParams({ page: args.page || 1, status: args.status || 'PENDING' }));
    const page = el('div', {}, heading('Відгуки на перевірці', 'Рішення зберігаються в журналі дій.'));
    if (!data.content.length) append(page, empty('Нових відгуків немає', 'Усі поточні відгуки вже розглянуто.'));
    for (const review of data.content) {
        const card = el('section', { class: 'card review-card' }, el('small', {}, review.productName), el('div', { class: 'stars', 'aria-label': review.rating + ' з 5' }, '★'.repeat(review.rating) + '☆'.repeat(5 - review.rating)),
            el('h2', {}, review.title), el('p', {}, review.comment), el('small', {}, review.authorName, ' · ', when(review.createdAt)));
        const decision = status => async event => {
            const control = event.currentTarget;
            if (!await confirmAction(status === 'PUBLISHED' ? 'Опублікувати відгук?' : 'Відхилити відгук?')) return;
            await perform(control, async () => { await request('reviews/' + review.id, 'PUT', { status }); toast('Рішення збережено', true); await navigate('reviews', args, true); });
        };
        card.append(el('div', { class: 'actions' }, button('Відхилити', decision('REJECTED'), 'danger', !!preview), button('Опублікувати', decision('PUBLISHED'), 'primary', !!preview))); append(page, card);
    }
    if (data.totalPages > 1) append(page, el('div', { class: 'pagination' }, button('Назад', () => navigate('reviews', { page: (args.page || 1) - 1 }), 'secondary small', (args.page || 1) <= 1), `${args.page || 1} / ${data.totalPages}`,
        button('Далі', () => navigate('reviews', { page: (args.page || 1) + 1 }), 'secondary small', (args.page || 1) >= data.totalPages)));
    return page;
}

async function profilePage() {
    const user = bootstrap.user;
    return el('div', {}, heading('Мій профіль'), el('section', { class: 'card profile-identity' }, el('span', { class: 'avatar' }, user.name.slice(0, 1)), el('h2', {}, user.name), el('p', {}, roleName()),
        el('span', { class: 'badge status-published', style: 'margin-top:12px' }, 'Telegram підключено')),
        el('section', { class: 'card' }, el('dl', { class: 'profile-info' }, el('div', {}, el('dt', {}, 'ID Treba'), el('dd', {}, user.id)), el('div', {}, el('dt', {}, 'ID Telegram'), el('dd', {}, user.telegramId)),
            isSeller() ? el('div', {}, el('dt', {}, 'Магазини'), el('dd', {}, bootstrap.stores.map(s => s.name).join(', ') || 'Немає активного магазину')) : null)), guardCard(),
        isSeller() ? el('details', { class: 'card' }, el('summary', {}, 'Дозволені категорії'), bootstrap.categories.filter(c => bootstrap.allowedCategoryIds.includes(c.id)).map(c => el('p', { class: 'hint', style: 'padding:7px 0' }, c.name))) : null,
        el('p', { class: 'hint' }, 'Змінити особисті дані або відключити Telegram можна в профілі на сайті Treba.'));
}

async function guardPage() {
    const scan = button(el('span', { class: 'actions' }, icon('qr'), 'Відкрити сканер QR'), () => {
        if (!tg?.isVersionAtLeast?.('6.4') || !tg?.showScanQrPopup) return toast('Відкрийте Mini App в актуальному Telegram на телефоні.');
        tg.showScanQrPopup({ text: 'Скануйте QR на сторінці входу Treba' }, value => {
            if (!/^treba-guard:v1:[a-f0-9]{32}:[A-Za-z0-9_-]{43}$/.test(value)) { toast('Це не QR-код входу Treba.'); return false; }
            confirmGuard(value); return true;
        });
    }, 'primary wide', !!preview);
    return el('div', { class: 'guard-view' }, el('div', { class: 'shield' }, icon('guard')), heading('Treba Guard'),
        el('p', {}, 'На сторінці входу до панелі виберіть Guard. Відскануйте QR і підтвердьте вхід на своєму пристрої.'), scan,
        el('section', { class: 'card', style: 'margin-top:23px;text-align:left' }, el('h3', {}, 'Підтверджуйте лише свій вхід'), el('p', { class: 'hint' }, 'Звірте назву пристрою та шестизначний код. QR діє дві хвилини та використовується один раз.')));
}
async function confirmGuard(payload) {
    try {
        const data = await request('guard/preview', 'POST', { qrPayload: payload });
        const approve = button('Підтвердити вхід', () => perform(approve, async () => {
            await request('guard/approve', 'POST', { qrPayload: payload }); overlay.remove(); tg?.HapticFeedback?.notificationOccurred('success'); toast('Вхід підтверджено. Поверніться до панелі на своєму пристрої.', true);
        }), 'primary');
        const overlay = el('div', { class: 'modal-backdrop' }, el('section', { class: 'modal guard-confirm', role: 'dialog', 'aria-modal': 'true', 'aria-labelledby': 'guard-title' },
            el('h2', { id: 'guard-title' }, 'Підтвердити вхід?'), el('p', {}, data.deviceName), el('div', { class: 'guard-code' }, data.displayCode), el('p', {}, 'Код має збігатися з кодом на сторінці входу. Термін дії — до ' + when(data.expiresAt) + '.'),
            el('div', { class: 'actions' }, button('Скасувати', () => overlay.remove(), 'secondary'), approve)));
        document.body.append(overlay); approve.focus();
    } catch (error) { toast(error.message); }
}

async function start() {
    tg?.ready(); tg?.expand();
    const updateTheme = () => document.body.classList.toggle('telegram-dark', tg?.colorScheme === 'dark'); updateTheme(); tg?.onEvent?.('themeChanged', updateTheme);
    if (previewRole) {
        const response = await fetch('/api/telegram/preview?' + new URLSearchParams({ role: previewRole }), { cache: 'no-store' });
        if (!response.ok) throw new Error('Попередній перегляд доступний лише в локальному середовищі розробки.');
        preview = await response.json();
        if (new URLSearchParams(location.search).get('screen') === 'loading') { startupPreview = true; return; }
        document.querySelector('.app-shell').prepend(el('div', { class: 'preview-banner' }, 'Попередній перегляд · зміни вимкнено', select([{ id: 'Seller', name: 'Продавець' }, { id: 'Moderator', name: 'Модератор' }], preview.bootstrap.user.role,
            value => { location.search = new URLSearchParams({ preview: value }).toString(); }, { 'aria-label': 'Роль для попереднього перегляду' })));
    } else if (!tg?.initData) {
        root.replaceChildren(empty('Відкрийте Treba в Telegram', 'Підключіть Telegram у своєму профілі на сайті та відкрийте Mini App кнопкою в боті.')); return;
    }
    bootstrap = await request('bootstrap');
    document.querySelector('#header').append(el('span', { class: 'role-pill' }, roleName()));
    tg?.BackButton?.onClick(() => navigate('home'));
    const initial = new URLSearchParams(location.search).get('view');
    const productId = new URLSearchParams(location.search).get('id');
    await navigate(initial === 'guard' ? 'guard' : initial === 'product' && /^[0-9a-f-]{36}$/i.test(productId || '') ? 'product' : 'home', initial === 'product' ? { id: productId } : {});
}
window.addEventListener('beforeunload', event => { if (dirty && !backupSafe) { event.preventDefault(); event.returnValue = ''; } });
function finishStartup() {
    if (startupPreview) return;
    const shell = document.querySelector('.app-shell'); const startup = document.querySelector('#startup');
    shell.hidden = false; shell.inert = false; root.setAttribute('aria-busy', 'false');
    startup.classList.add('is-leaving');
    startup.addEventListener('transitionend', () => startup.remove(), { once: true });
    setTimeout(() => startup.remove(), 350);
}
start().catch(error => root.replaceChildren(empty('Не вдалося відкрити Treba', error.message))).finally(finishStartup);
