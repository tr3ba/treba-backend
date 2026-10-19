import { el, icon, money, when, badge, button, heading, empty, field, input, select, toast, statusName } from './ui.js';

const tg = window.Telegram?.WebApp;
const root = document.querySelector('#app');
const nav = document.querySelector('#navigation');
const previewRole = new URLSearchParams(location.search).get('preview');
let startupPreview = false;
let preview = null, bootstrap, current = { view: 'home' }, generation = 0, dirty = false, mutating = false;
const isSeller = () => bootstrap?.user.role === 'Seller';
const roleName = () => isSeller() ? 'Продавец' : 'Модератор';
const emptyId = '00000000-0000-0000-0000-000000000000';
function append(parent, ...children) { parent.append(...children.flat(Infinity).filter(child => child !== null && child !== undefined && child !== false)); }

async function request(path, method = 'GET', body = undefined) {
    if (preview) {
        if (method !== 'GET') throw new Error('Это предпросмотр интерфейса. Изменения доступны после подключения бота.');
        const [route, query] = path.split('?'); const args = new URLSearchParams(query);
        if (route === 'bootstrap') return structuredClone(preview.bootstrap);
        if (route === 'products') {
            const rows = preview.products.filter(p => (!args.get('status') || p.status === args.get('status')) && (!args.get('search') || (p.name + p.sku).toLowerCase().includes(args.get('search').toLowerCase())));
            return { content: structuredClone(rows), page: 1, totalPages: 1, totalElements: rows.length };
        }
        if (/^products\/[^/]+\/history$/.test(route)) return structuredClone(preview.history);
        if (/^products\/[^/]+$/.test(route)) return structuredClone(preview.products.find(p => p.id === route.split('/')[1]));
        if (route === 'attributes') return [];
        if (/^imports\/[^/]+$/.test(route)) return structuredClone(preview.importPreview);
        if (route === 'reviews') return { content: preview.reviews, page: 1, totalPages: 1, totalElements: preview.reviews.length };
        throw new Error('Этот экран доступен в подключённом боте.');
    }
    const headers = { Authorization: 'Tma ' + tg.initData };
    if (body !== undefined && !(body instanceof FormData)) headers['Content-Type'] = 'application/json';
    const url = path.startsWith('/') ? path : '/api/telegram/mini/' + path;
    const response = await fetch(url, { method, headers, body: body === undefined ? undefined : body instanceof FormData ? body : JSON.stringify(body), cache: 'no-store' });
    if (!response.ok) {
        let error; try { error = await response.json(); } catch { error = {}; }
        if (response.status === 401) throw new Error('Подтвердите привязку в профиле Treba и откройте Mini App заново.');
        if (response.status === 403) throw new Error('Это действие недоступно вашей роли.');
        if (response.status === 429) throw new Error('Слишком много запросов. Подождите немного и повторите.');
        throw new Error(error.title || error.message || 'Не удалось выполнить запрос. Попробуйте ещё раз.');
    }
    return response.status === 204 ? null : response.json();
}

function setDirty(value) { dirty = value; if (value) tg?.enableClosingConfirmation?.(); else tg?.disableClosingConfirmation?.(); }
async function confirmAction(message) {
    if (tg?.initData && tg.isVersionAtLeast?.('6.2')) return new Promise(resolve => tg.showConfirm(message, resolve));
    return window.confirm(message);
}
async function perform(control, operation) {
    if (mutating) return;
    if (preview) return toast('В предпросмотре изменения отключены. Подключите бота для работы с товарами.');
    mutating = true; control.disabled = true;
    const before = [...control.childNodes]; control.textContent = 'Подождите…';
    try { await operation(); }
    catch (error) { toast(error.message || 'Проверьте подключение и попробуйте снова.'); }
    finally { mutating = false; control.disabled = false; control.replaceChildren(...before); }
}
function renderNavigation() {
    const tabs = isSeller() ? [['home', 'home', 'Обзор'], ['products', 'box', 'Товары'], ['import', 'import', 'Импорт'], ['profile', 'person', 'Профиль']]
        : [['home', 'home', 'Обзор'], ['moderation', 'guard', 'Проверка'], ['reviews', 'review', 'Отзывы'], ['profile', 'person', 'Профиль']];
    nav.replaceChildren(...tabs.map(([view, name, label]) => el('button', { class: current.view === view ? 'active' : '', type: 'button', onclick: () => navigate(view), 'aria-current': current.view === view ? 'page' : undefined }, icon(name), label)));
}
async function navigate(view, args = {}, force = false) {
    if (mutating && !force) return toast('Дождитесь завершения текущего действия.');
    if (dirty && !force && !await confirmAction('Есть несохранённые изменения. Выйти без сохранения?')) return;
    setDirty(false); current = { view, ...args }; const ticket = ++generation;
    renderNavigation();
    if (view === 'home') tg?.BackButton?.hide(); else tg?.BackButton?.show();
    root.replaceChildren(el('div', { class: 'loading' }, el('span', { class: 'spinner' }), 'Загрузка…'));
    window.scrollTo({ top: 0 });
    try {
        const page = await ({ home: homePage, products: productsPage, moderation: productsPage, product: productPage,
            edit: editorPage, import: importPage, importPreview: importPreviewPage, reviews: reviewsPage, profile: profilePage, guard: guardPage }[view] || homePage)(current);
        if (ticket === generation) root.replaceChildren(page);
    } catch (error) {
        if (ticket !== generation) return;
        root.replaceChildren(empty('Не удалось открыть экран', error.message), button('Попробовать снова', () => navigate(view, args, true), 'secondary wide'));
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
    el('div', {}, el('h2', {}, 'Вход через Guard'), el('p', {}, 'Сканируйте QR на странице входа')), icon('arrow')); }

async function homePage() {
    bootstrap = await request('bootstrap'); renderNavigation();
    const stats = bootstrap.stats;
    const page = el('div', {}, el('section', { class: 'welcome' }, el('div', {}, el('p', {}, 'ВАША РАБОЧАЯ ПАНЕЛЬ'), el('h1', {}, 'Привет, ' + bootstrap.user.name.split(' ')[0]), el('p', { style: 'margin-top:8px' }, isSeller() ? 'Всё важное для вашего магазина.' : 'Помогаем каталогу оставаться качественным.')),
        el('span', { class: 'avatar' }, bootstrap.user.name.slice(0, 1))), guardCard(),
        el('div', { class: 'stats' }, stat(stats.total, 'Всего товаров'), stat(stats.pending, 'На проверке', true), stat(isSeller() ? stats.drafts : bootstrap.pendingReviews, isSeller() ? 'Черновики' : 'Новых отзывов')));
    if (isSeller()) append(page, el('section', { class: 'card' }, el('div', { class: 'section-title', style: 'margin-top:0' }, el('h2', {}, 'Товар по ссылке'), icon('import')),
        el('p', { class: 'hint', style: 'margin-bottom:15px' }, 'Добавьте ссылку Rozetka. Проверьте данные и отправьте товар на модерацию.'), button('Импортировать товар', () => navigate('import'), 'primary wide')));
    const rows = await request('products?status=' + (isSeller() ? '' : 'PENDING_MODERATION'));
    append(page, el('div', { class: 'section-title' }, el('h2', {}, isSeller() ? 'Последние товары' : 'Ожидают решения'), button('Все', () => navigate(isSeller() ? 'products' : 'moderation'), 'secondary small')),
        rows.content.length ? rows.content.slice(0, 3).map(productCard) : empty('Пока всё спокойно', isSeller() ? 'Добавьте первый товар вручную или по ссылке.' : 'Новых товаров на проверке нет.'));
    if (!isSeller()) append(page, button('Открыть весь каталог', () => navigate('products'), 'secondary wide'));
    return page;
}
function stat(value, label, highlight = false) { return el('div', { class: 'stat' + (highlight ? ' highlight' : '') }, el('strong', {}, value), el('span', {}, label)); }

async function productsPage(args) {
    const moderation = args.view === 'moderation';
    const status = args.status ?? (moderation ? 'PENDING_MODERATION' : '');
    const search = args.search || ''; const pageIndex = args.page || 1;
    const data = await request('products?' + new URLSearchParams({ status, search, page: pageIndex }));
    const page = el('div', {}, heading(moderation ? 'Проверка товаров' : isSeller() ? 'Мои товары' : 'Каталог', moderation ? 'Проверьте карточку перед публикацией.' : 'Цены, остатки и статус публикации.'));
    if (isSeller()) append(page, button('＋ Добавить товар', () => navigate('edit'), 'primary wide'));
    let query = search;
    const filter = newStatus => navigate(args.view, { status: newStatus, search: query });
    const searchInput = input(search, value => query = value, { type: 'search', placeholder: 'Название или артикул', 'aria-label': 'Поиск товаров' });
    searchInput.addEventListener('keydown', event => { if (event.key === 'Enter') filter(status); });
    const searchButton = button(icon('search'), () => filter(status), 'secondary'); searchButton.setAttribute('aria-label', 'Найти товары');
    append(page, el('div', { class: 'search-row', style: 'margin-top:16px' }, field('', searchInput), searchButton),
        el('div', { class: 'filter-row' }, [['', 'Все'], ['DRAFT', 'Черновики'], ['PENDING_MODERATION', 'На проверке'], ['ACTIVE', 'Опубликованы'], ['REJECTED', 'Отклонены']]
            .map(([value, label]) => el('button', { type: 'button', class: 'filter-chip' + (value === status ? ' active' : ''), onclick: () => filter(value) }, label))),
        data.content.length ? data.content.map(productCard) : empty('Товары не найдены', 'Измените поиск или выбранный статус.'));
    if (data.totalPages > 1) append(page, el('div', { class: 'pagination' }, button('Назад', () => navigate(args.view, { status, search, page: pageIndex - 1 }), 'secondary small', pageIndex <= 1),
        `${pageIndex} / ${data.totalPages}`, button('Далее', () => navigate(args.view, { status, search, page: pageIndex + 1 }), 'secondary small', pageIndex >= data.totalPages)));
    return page;
}

function gallery(urls) { return urls.length ? el('div', { class: 'gallery' }, urls.map((url, index) => el('figure', {}, el('img', { src: url, alt: 'Фото товара ' + (index + 1), loading: index ? 'lazy' : 'eager', referrerpolicy: 'no-referrer' }), el('figcaption', {}, `${index + 1} / ${urls.length}`)))) : null; }
function timeline(rows) { return rows.length ? el('div', {}, rows.map(row => el('div', { class: 'history-entry' }, el('strong', {}, { Submitted: 'Отправлен на проверку', Approved: 'Опубликован', Rejected: 'Отклонён' }[row.decision] || row.decision), el('small', {}, when(row.occurredAt), ' · ', row.actorName), row.reason ? el('p', {}, row.reason) : null))) : el('p', { class: 'hint' }, 'Новых решений по этому товару пока нет.'); }
async function productPage(args) {
    const product = await request('products/' + args.id);
    const history = await request('products/' + args.id + '/history');
    const last = history[0];
    const page = el('div', {}, back(() => navigate(isSeller() ? 'products' : 'moderation')), gallery(product.images.map(i => i.imageUrl)),
        el('section', { class: 'product-head' }, badge(product.status), el('h1', {}, product.name), el('strong', {}, money(product.price))));
    if (last?.decision === 'Rejected' && product.status === 'REJECTED') append(page, el('div', { class: 'notice error' }, el('strong', {}, 'Нужно исправить'), el('p', {}, last.reason)));
    append(page, el('section', { class: 'card detail-grid' }, detail('Артикул', product.sku), detail('Остаток', product.stockQuantity + ' шт.'), detail('Категория', product.categoryName), detail('Бренд', product.brandName)),
        el('section', { class: 'card' }, el('h2', { style: 'margin-bottom:13px' }, 'Описание'), el('p', { class: 'description' }, product.description || 'Описание не заполнено.')));
    if (product.variants.length > 1) append(page, el('section', { class: 'card' }, el('h2', { style: 'margin-bottom:12px' }, 'Варианты'), product.variants.map(v => el('div', { class: 'characteristic' }, el('strong', {}, v.name, v.isActive ? '' : ' · Отключён'), el('p', {}, v.sku, ' · ', money(v.price), ' · ', v.stockQuantity, ' шт.')))));
    if (product.attributes.length) {
        const definitions = await request('attributes?categoryId=' + product.categoryId);
        append(page, el('details', { class: 'card' }, el('summary', {}, 'Характеристики'), product.attributes.map(a => {
            const definition = definitions.find(d => d.id === a.attributeId);
            return el('div', { class: 'characteristic' }, el('span', {}, definition?.name || 'Характеристика'), String(a.stringValue ?? a.numberValue ?? (a.boolValue == null ? definition?.options.find(o => o.id === a.optionId)?.name || '' : a.boolValue ? 'Да' : 'Нет')));
        })));
    }
    append(page, el('details', { class: 'card' }, el('summary', {}, 'История модерации'), timeline(history)));
    if (isSeller() && !['PENDING_MODERATION', 'ARCHIVED'].includes(product.status)) {
        append(page, button('Редактировать товар', () => navigate('edit', { id: product.id }), 'primary wide'));
        if (['DRAFT', 'REJECTED'].includes(product.status)) {
            const submit = button('На модерацию', () => perform(submit, async () => { await request(`products/${product.id}/submit`, 'POST', {}); setDirty(false); toast('Товар отправлен на модерацию', true); await navigate('product', { id: product.id }, true); }), 'secondary wide', !!preview);
            append(page, el('div', { style: 'height:10px' }), submit);
        }
    }
    if (!isSeller() && product.status === 'PENDING_MODERATION') {
        const approve = button('Опубликовать', async () => {
            if (!await confirmAction('Опубликовать этот товар в каталоге Treba?')) return;
            await perform(approve, async () => { await request(`products/${product.id}/approve`, 'POST', {}); toast('Товар опубликован', true); await navigate('moderation', {}, true); });
        }, 'primary', !!preview);
        const reject = button('Отклонить', () => rejectionDialog(product.id), 'danger', !!preview);
        append(page, el('div', { class: 'actions' }, reject, approve));
    }
    return page;
}
function detail(label, value) { return el('div', {}, el('span', {}, label), value || '—'); }
function back(click) { return el('button', { type: 'button', class: 'back-button', onclick: click }, icon('back'), 'Назад'); }
function rejectionDialog(id) {
    let reason = '';
    const textarea = el('textarea', { maxlength: 2000, placeholder: 'Что продавцу нужно исправить?', 'aria-label': 'Причина отклонения', oninput: event => reason = event.target.value });
    const submit = button('Отклонить товар', () => perform(submit, async () => {
        if (!reason.trim()) throw new Error('Укажите причину отклонения.');
        await request(`products/${id}/reject`, 'POST', { reason }); overlay.remove(); toast('Решение сохранено', true); await navigate('moderation', {}, true);
    }), 'danger');
    const overlay = el('div', { class: 'modal-backdrop' }, el('section', { class: 'modal', role: 'dialog', 'aria-modal': 'true', 'aria-labelledby': 'reject-title' },
        el('h2', { id: 'reject-title' }, 'Причина отклонения'), el('p', {}, 'Продавец увидит её в карточке товара и сможет внести исправления.'), textarea,
        el('div', { class: 'actions' }, button('Отмена', () => overlay.remove(), 'secondary'), submit)));
    document.body.append(overlay); textarea.focus();
}

async function editorPage(args) {
    if (!isSeller()) throw new Error('Редактор доступен продавцу.');
    const existing = args.id ? await request('products/' + args.id) : null;
    if (existing && ['PENDING_MODERATION', 'ARCHIVED'].includes(existing.status)) throw new Error('Этот товар сейчас нельзя редактировать.');
    const allowed = bootstrap.categories.filter(c => bootstrap.allowedCategoryIds.includes(c.id));
    const model = existing ? { ...existing, expectedUpdatedAt: existing.updatedAt, variants: structuredClone(existing.variants), attributes: structuredClone(existing.attributes) }
        : { name: '', slug: '', categoryId: allowed[0]?.id || emptyId, brandId: bootstrap.brands[0]?.id || emptyId, storeId: bootstrap.stores[0]?.id || emptyId, description: '', shortDescription: '',
            warrantyMonths: 0, countryOfOrigin: '', variants: [{ id: crypto.randomUUID(), name: 'Основной', sku: '', price: 0, oldPrice: null, stockQuantity: 0, isActive: true }], attributes: [] };
    let id = args.id; const pendingImages = []; const savedImages = existing?.images || [];
    let definitions = model.categoryId !== emptyId ? await request('attributes?categoryId=' + model.categoryId) : [];
    const page = el('div', {}, back(() => navigate(id ? 'product' : 'products', id ? { id } : {})), heading(id ? 'Редактирование' : 'Новый товар', 'Сначала сохраните черновик, затем отправьте на проверку.'));
    if (!allowed.length) append(page, el('div', { class: 'notice error' }, 'Нет разрешённых категорий. Обратитесь к администратору Treba.'));
    const update = key => value => { model[key] = value; setDirty(true); };
    const attributesPanel = el('div');
    const variantPanel = el('div', { class: 'stack' });
    const categoriesSelect = select(bootstrap.categories.filter(c => allowed.some(a => a.id === c.id) || c.id === model.categoryId), model.categoryId, async value => {
        model.categoryId = value; model.attributes = []; setDirty(true);
        try { definitions = await request('attributes?categoryId=' + value); renderAttributes(attributesPanel, model, definitions); } catch (error) { toast(error.message); }
    }, { required: true });
    const form = el('form', { class: 'card stack', onsubmit: event => event.preventDefault() },
        field('Название *', input(model.name, update('name'), { maxlength: 250, required: true })),
        field('Категория *', categoriesSelect),
        field('Бренд *', select(bootstrap.brands, model.brandId, update('brandId'), { required: true })),
        !id ? field('Магазин *', select(bootstrap.stores, model.storeId, update('storeId'), { required: true })) : null,
        field('Описание', el('textarea', { maxlength: 10000, rows: 6, oninput: event => update('description')(event.target.value) }, model.description)),
        field('Краткое описание', input(model.shortDescription, update('shortDescription'), { maxlength: 500 })),
        el('div', { class: 'field-grid' }, field('Гарантия, месяцев', input(model.warrantyMonths, update('warrantyMonths'), { type: 'number', min: 0, max: 120, step: 1 })), field('Страна производства', input(model.countryOfOrigin, update('countryOfOrigin'), { maxlength: 100 }))),
        el('section', { class: 'form-section' }, el('h2', {}, 'Варианты'), variantPanel,
            button('＋ Добавить вариант', () => { model.variants.push({ id: crypto.randomUUID(), name: 'Новый вариант', sku: '', price: 0, oldPrice: null, stockQuantity: 0, isActive: true }); setDirty(true); renderVariants(); }, 'secondary small')),
        attributesPanel);
    function renderVariants() {
        variantPanel.replaceChildren(...model.variants.map(variant => el('section', { class: 'variant' },
            el('div', { class: 'variant-header' }, el('label', { class: 'check-label' }, el('input', { type: 'checkbox', checked: variant.isActive, onchange: event => { variant.isActive = event.target.checked; setDirty(true); } }), 'Активный вариант'),
                !existing?.variants.some(v => v.id === variant.id) && model.variants.length > 1 ? button('Убрать', () => { model.variants.splice(model.variants.indexOf(variant), 1); model.attributes = model.attributes.filter(a => a.variantId !== variant.id); setDirty(true); renderVariants(); }, 'secondary small') : null),
            field('Название варианта *', input(variant.name, value => { variant.name = value; setDirty(true); }, { maxlength: 250, required: true })),
            field('Артикул *', input(variant.sku, value => { variant.sku = value; setDirty(true); }, { maxlength: 100, required: true })),
            el('div', { class: 'field-grid' }, field('Цена, ₴ *', input(variant.price || '', value => { variant.price = value; setDirty(true); }, { type: 'number', min: .01, max: 999999999.99, step: .01, required: true })),
                field('Старая цена, ₴', input(variant.oldPrice, value => { variant.oldPrice = Number.isNaN(value) ? null : value; setDirty(true); }, { type: 'number', min: .01, step: .01 }))),
            field('Остаток *', input(variant.stockQuantity, value => { variant.stockQuantity = value; setDirty(true); }, { type: 'number', min: 0, max: 1000000, step: 1, required: true })))));
        renderAttributes(attributesPanel, model, definitions);
    }
    renderVariants();
    const fileInput = el('input', { type: 'file', accept: 'image/jpeg,image/png,image/webp,image/gif', multiple: true, onchange: event => {
        const files = Array.from(event.target.files || []);
        if (files.length + pendingImages.length + savedImages.length > 8 || files.some(f => f.size > 5 * 1024 * 1024)) return toast('Допустимо до 8 фотографий размером не более 5 МБ каждая.');
        pendingImages.push(...files); setDirty(true); imageHint.textContent = `Выбрано новых фотографий: ${pendingImages.length}`;
    } });
    const imageHint = el('small', { class: 'hint' }, 'Фото загрузятся при сохранении. JPG, PNG, WEBP или GIF; до 5 МБ.');
    form.append(el('section', { class: 'form-section stack' }, el('h2', {}, 'Фотографии'), gallery(existing?.images.map(i => i.imageUrl) || []), field('Добавить фотографии', fileInput), imageHint));
    async function save(control, submit) {
        if (!form.reportValidity()) return;
        await perform(control, async () => {
            const payload = { ...model, slug: model.slug || 'treba-' + crypto.randomUUID().replaceAll('-', ''), sku: model.variants[0]?.sku || '', price: model.variants[0]?.price || 0, stockQuantity: model.variants[0]?.stockQuantity || 0 };
            const saved = await request(id ? 'products/' + id : 'products', id ? 'PUT' : 'POST', payload);
            id = saved.id; model.expectedUpdatedAt = saved.updatedAt; model.slug = saved.slug;
            while (pendingImages.length) {
                const file = pendingImages[0]; const body = new FormData(); body.append('File', file); body.append('IsMain', String(savedImages.length === 0)); body.append('SortOrder', String(savedImages.length));
                const uploaded = await request(`/api/v1/products/${id}/images/upload`, 'POST', body);
                savedImages.push(uploaded); pendingImages.shift();
            }
            if (submit) await request(`products/${id}/submit`, 'POST', {});
            setDirty(false); toast(submit ? 'Товар отправлен на модерацию' : 'Черновик сохранён', true); await navigate('product', { id }, true);
        });
    }
    const draft = button('Черновик', () => save(draft, false), 'secondary', !!preview || !allowed.length);
    const submit = button('На модерацию', () => save(submit, true), 'primary', !!preview || !allowed.length);
    form.append(el('div', { class: 'actions' }, draft, submit)); append(page, form); return page;
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
        if (definition.type === 'BOOLEAN') control = select([{ id: '', name: 'Выберите' }, { id: 'true', name: 'Да' }, { id: 'false', name: 'Нет' }], attribute.boolValue == null ? '' : String(attribute.boolValue), change);
        else if (['SELECT', 'MULTI_SELECT'].includes(definition.type)) control = select([{ id: '', name: 'Выберите' }, ...definition.options], attribute.optionId || '', change);
        else control = input(definition.type === 'NUMBER' ? attribute.numberValue : attribute.stringValue, change,
            { type: definition.type === 'NUMBER' ? 'number' : definition.type === 'DATE' ? 'date' : 'text', step: definition.type === 'NUMBER' ? '.0001' : undefined, maxlength: 2000 });
        content.append(el('div', { class: 'characteristic-row' }, el('header', {}, el('strong', {}, definition.name + (definition.isRequired ? ' *' : '')), button('Убрать', () => { model.attributes.splice(model.attributes.indexOf(attribute), 1); setDirty(true); renderAttributes(panel, model, definitions); }, 'secondary small', definition.isRequired && model.attributes.filter(a => a.attributeId === definition.id).length === 1)),
            select([{ id: '', name: 'Весь товар' }, ...model.variants.map(v => ({ id: v.id, name: v.name }))], attribute.variantId || '', value => { attribute.variantId = value || null; setDirty(true); }), control));
    }
    let selected = definitions[0]?.id || '';
    if (definitions.length) content.append(select(definitions, selected, value => selected = value), button('＋ Добавить характеристику', () => { model.attributes.push({ attributeId: selected }); setDirty(true); renderAttributes(panel, model, definitions); }, 'secondary small'));
    else content.append(el('p', { class: 'hint' }, 'В этой категории характеристики ещё не настроены.'));
    panel.replaceChildren(el('details', { class: 'form-section', open: definitions.some(d => d.isRequired) }, el('summary', {}, 'Характеристики'), content));
}

async function importPage() {
    if (!isSeller()) throw new Error('Импорт доступен продавцу.');
    let url = '';
    const submit = button('Получить данные товара', () => {
        if (!form.reportValidity()) return;
        return perform(submit, async () => { const data = await request('imports', 'POST', { url }); await navigate('importPreview', { id: data.id }, true); });
    }, 'primary wide', !!preview);
    const form = el('form', { class: 'card stack', onsubmit: event => event.preventDefault() },
        field('Ссылка на товар Rozetka', input('', value => url = value, { type: 'url', placeholder: 'https://rozetka.com.ua/…/p123456/', required: true, maxlength: 1500 })), submit);
    return el('div', {}, heading('Импорт товара', 'Новая карточка — из одной ссылки.'),
        el('section', { class: 'import-hero' }, icon('import'), el('h2', {}, 'Ссылка. Проверка. Готово.'), el('p', {}, 'Название, описание и фотографии появятся в предпросмотре. Вы решаете, когда отправить товар на проверку.')),
        form, el('ol', { class: 'steps' }, el('li', {}, 'Вставьте ссылку на конкретный товар.'), el('li', {}, 'Treba проверит разрешение на его категорию.'), el('li', {}, 'Проверьте данные, укажите свою цену и остаток.'), el('li', {}, 'Отправьте карточку модератору.')),
        el('p', { class: 'hint' }, 'Если источник ограничил загрузку, карточку можно заполнить вручную.'), button('Добавить вручную', () => navigate('edit'), 'secondary wide'),
        preview ? button('Показать пример предпросмотра', () => navigate('importPreview', { id: preview.importPreview.id }), 'secondary wide') : null);
}
async function importPreviewPage(args) {
    const data = await request('imports/' + args.id);
    if (data.submittedAt) return el('div', {}, heading('Товар уже отправлен'), button('Открыть карточку', () => navigate('product', { id: data.productId }), 'primary wide'));
    const model = { name: data.name, description: data.description, brandId: bootstrap.brands.find(b => b.name.toLowerCase() === data.brandName.toLowerCase())?.id || '', storeId: bootstrap.stores[0]?.id || '',
        sku: 'TRB-' + data.id.slice(0, 8).toUpperCase(), price: data.sourcePrice || 0, stockQuantity: 0, variants: [], attributes: [] };
    const definitions = await request('attributes?categoryId=' + data.categoryId); const attributePanel = el('div'); renderAttributes(attributePanel, model, definitions);
    const update = key => value => { model[key] = value; setDirty(true); };
    const page = el('div', {}, back(() => navigate('import')), heading('Предпросмотр импорта', 'Карточка ещё не опубликована.'),
        el('div', { class: 'notice success' }, icon('check'), ' Категория разрешена: ', data.categoryName), gallery(data.images));
    if (!data.images.length) append(page, el('div', { class: 'notice' }, 'Источник не передал фотографии. Они не будут добавлены автоматически.'));
    const form = el('form', { class: 'card stack', onsubmit: event => event.preventDefault() },
        field('Название *', input(model.name, update('name'), { required: true, maxlength: 250 })),
        field('Описание', el('textarea', { rows: 7, maxlength: 10000, oninput: event => update('description')(event.target.value) }, model.description)),
        field('Бренд Treba *', select([{ id: '', name: 'Выберите бренд' }, ...bootstrap.brands], model.brandId, update('brandId'), { required: true }), 'На источнике: ' + (data.brandName || 'Не указан')),
        field('Магазин *', select([{ id: '', name: 'Выберите магазин' }, ...bootstrap.stores], model.storeId, update('storeId'), { required: true })),
        field('Ваш артикул *', input(model.sku, update('sku'), { required: true, maxlength: 100 })),
        el('div', { class: 'field-grid' }, field('Ваша цена, ₴ *', input(model.price || '', update('price'), { type: 'number', min: .01, max: 999999999.99, step: .01, required: true })),
            field('Ваш остаток *', input(0, update('stockQuantity'), { type: 'number', min: 0, max: 1000000, step: 1, required: true }))), attributePanel,
        el('a', { href: data.sourceUrl, class: 'source-link', target: '_blank', rel: 'noopener noreferrer' }, 'Посмотреть источник ↗'));
    if (data.characteristics.length) form.append(el('details', {}, el('summary', {}, 'Характеристики источника'), data.characteristics.map(c => el('div', { class: 'characteristic' }, el('span', {}, c.name), c.value))));
    const submit = button('Всё верно — на модерацию', () => {
        if (!form.reportValidity()) return;
        return perform(submit, async () => { const result = await request(`imports/${data.id}/submit`, 'POST', model); setDirty(false); toast('Товар отправлен на модерацию', true); await navigate('product', { id: result.productId }, true); });
    }, 'primary wide', !!preview);
    form.append(el('p', { class: 'hint' }, 'Проверьте данные карточки. После отправки товар появится в очереди модератора.'), submit);
    append(page, form); setDirty(!preview); return page;
}

async function reviewsPage(args) {
    if (isSeller()) throw new Error('Модерация отзывов недоступна продавцу.');
    const data = await request('reviews?' + new URLSearchParams({ page: args.page || 1, status: args.status || 'PENDING' }));
    const page = el('div', {}, heading('Отзывы на проверке', 'Решения сохраняются в журнале действий.'));
    if (!data.content.length) append(page, empty('Новых отзывов нет', 'Все текущие отзывы уже рассмотрены.'));
    for (const review of data.content) {
        const card = el('section', { class: 'card review-card' }, el('small', {}, review.productName), el('div', { class: 'stars', 'aria-label': review.rating + ' из 5' }, '★'.repeat(review.rating) + '☆'.repeat(5 - review.rating)),
            el('h2', {}, review.title), el('p', {}, review.comment), el('small', {}, review.authorName, ' · ', when(review.createdAt)));
        const decision = status => async event => {
            const control = event.currentTarget;
            if (!await confirmAction(status === 'PUBLISHED' ? 'Опубликовать отзыв?' : 'Отклонить отзыв?')) return;
            await perform(control, async () => { await request('reviews/' + review.id, 'PUT', { status }); toast('Решение сохранено', true); await navigate('reviews', args, true); });
        };
        card.append(el('div', { class: 'actions' }, button('Отклонить', decision('REJECTED'), 'danger', !!preview), button('Опубликовать', decision('PUBLISHED'), 'primary', !!preview))); append(page, card);
    }
    if (data.totalPages > 1) append(page, el('div', { class: 'pagination' }, button('Назад', () => navigate('reviews', { page: (args.page || 1) - 1 }), 'secondary small', (args.page || 1) <= 1), `${args.page || 1} / ${data.totalPages}`,
        button('Далее', () => navigate('reviews', { page: (args.page || 1) + 1 }), 'secondary small', (args.page || 1) >= data.totalPages)));
    return page;
}

async function profilePage() {
    const user = bootstrap.user;
    return el('div', {}, heading('Мой профиль'), el('section', { class: 'card profile-identity' }, el('span', { class: 'avatar' }, user.name.slice(0, 1)), el('h2', {}, user.name), el('p', {}, roleName()),
        el('span', { class: 'badge status-published', style: 'margin-top:12px' }, 'Telegram привязан')),
        el('section', { class: 'card' }, el('dl', { class: 'profile-info' }, el('div', {}, el('dt', {}, 'ID Treba'), el('dd', {}, user.id)), el('div', {}, el('dt', {}, 'ID Telegram'), el('dd', {}, user.telegramId)),
            isSeller() ? el('div', {}, el('dt', {}, 'Магазины'), el('dd', {}, bootstrap.stores.map(s => s.name).join(', ') || 'Нет активного магазина')) : null)), guardCard(),
        isSeller() ? el('details', { class: 'card' }, el('summary', {}, 'Разрешённые категории'), bootstrap.categories.filter(c => bootstrap.allowedCategoryIds.includes(c.id)).map(c => el('p', { class: 'hint', style: 'padding:7px 0' }, c.name))) : null,
        el('p', { class: 'hint' }, 'Изменить личные данные или отключить Telegram можно в профиле на сайте Treba.'));
}

async function guardPage() {
    const scan = button(el('span', { class: 'actions' }, icon('qr'), 'Открыть сканер QR'), () => {
        if (!tg?.isVersionAtLeast?.('6.4') || !tg?.showScanQrPopup) return toast('Откройте Mini App в актуальном Telegram на телефоне.');
        tg.showScanQrPopup({ text: 'Сканируйте QR на странице входа Treba' }, value => {
            if (!/^treba-guard:v1:[a-f0-9]{32}:[A-Za-z0-9_-]{43}$/.test(value)) { toast('Это не QR-код входа Treba.'); return false; }
            confirmGuard(value); return true;
        });
    }, 'primary wide', !!preview);
    return el('div', { class: 'guard-view' }, el('div', { class: 'shield' }, icon('guard')), heading('Treba Guard'),
        el('p', {}, 'На странице входа в админку выберите Guard. Отсканируйте QR и подтвердите вход на своё устройство.'), scan,
        el('section', { class: 'card', style: 'margin-top:23px;text-align:left' }, el('h3', {}, 'Подтверждайте только свой вход'), el('p', { class: 'hint' }, 'Сверьте название устройства и шестизначный код. QR действует две минуты и используется один раз.')));
}
async function confirmGuard(payload) {
    try {
        const data = await request('guard/preview', 'POST', { qrPayload: payload });
        const approve = button('Подтвердить вход', () => perform(approve, async () => {
            await request('guard/approve', 'POST', { qrPayload: payload }); overlay.remove(); tg?.HapticFeedback?.notificationOccurred('success'); toast('Вход подтверждён. Вернитесь к админке на своём устройстве.', true);
        }), 'primary');
        const overlay = el('div', { class: 'modal-backdrop' }, el('section', { class: 'modal guard-confirm', role: 'dialog', 'aria-modal': 'true', 'aria-labelledby': 'guard-title' },
            el('h2', { id: 'guard-title' }, 'Подтвердить вход?'), el('p', {}, data.deviceName), el('div', { class: 'guard-code' }, data.displayCode), el('p', {}, 'Код должен совпадать с кодом на странице входа. Срок действия — до ' + when(data.expiresAt) + '.'),
            el('div', { class: 'actions' }, button('Отмена', () => overlay.remove(), 'secondary'), approve)));
        document.body.append(overlay); approve.focus();
    } catch (error) { toast(error.message); }
}

async function start() {
    tg?.ready(); tg?.expand();
    const updateTheme = () => document.body.classList.toggle('telegram-dark', tg?.colorScheme === 'dark'); updateTheme(); tg?.onEvent?.('themeChanged', updateTheme);
    if (previewRole) {
        const response = await fetch('/api/telegram/preview?' + new URLSearchParams({ role: previewRole }), { cache: 'no-store' });
        if (!response.ok) throw new Error('Предпросмотр доступен только в локальной среде разработки.');
        preview = await response.json();
        if (new URLSearchParams(location.search).get('screen') === 'loading') { startupPreview = true; return; }
        document.querySelector('.app-shell').prepend(el('div', { class: 'preview-banner' }, 'Предпросмотр · изменения отключены', select([{ id: 'Seller', name: 'Продавец' }, { id: 'Moderator', name: 'Модератор' }], preview.bootstrap.user.role,
            value => { location.search = new URLSearchParams({ preview: value }).toString(); }, { 'aria-label': 'Роль для предпросмотра' })));
    } else if (!tg?.initData) {
        root.replaceChildren(empty('Откройте Treba в Telegram', 'Привяжите Telegram в своём профиле на сайте и откройте Mini App кнопкой в боте.')); return;
    }
    bootstrap = await request('bootstrap');
    document.querySelector('#header').append(el('span', { class: 'role-pill' }, roleName()));
    tg?.BackButton?.onClick(() => navigate('home'));
    const initial = new URLSearchParams(location.search).get('view');
    await navigate(initial === 'guard' ? 'guard' : 'home');
}
window.addEventListener('beforeunload', event => { if (dirty) { event.preventDefault(); event.returnValue = ''; } });
function finishStartup() {
    if (startupPreview) return;
    const shell = document.querySelector('.app-shell'); const startup = document.querySelector('#startup');
    shell.hidden = false; shell.inert = false; root.setAttribute('aria-busy', 'false');
    startup.classList.add('is-leaving');
    startup.addEventListener('transitionend', () => startup.remove(), { once: true });
    setTimeout(() => startup.remove(), 350);
}
start().catch(error => root.replaceChildren(empty('Не удалось открыть Treba', error.message))).finally(finishStartup);
