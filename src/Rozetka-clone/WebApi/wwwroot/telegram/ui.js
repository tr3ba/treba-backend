export function el(tag, props = {}, ...children) {
    const node = document.createElement(tag);
    for (const [key, value] of Object.entries(props)) {
        if (value === null || value === undefined || value === false) continue;
        if (key.startsWith('on') && typeof value === 'function') node.addEventListener(key.slice(2), value);
        else if (key === 'class') node.className = value;
        else if (['value', 'checked', 'disabled', 'selected', 'multiple', 'required', 'hidden', 'open'].includes(key)) node[key] = value;
        else node.setAttribute(key, value === true ? '' : value);
    }
    for (const child of children.flat(Infinity)) {
        if (child === null || child === undefined || child === false) continue;
        node.append(child instanceof Node ? child : document.createTextNode(String(child)));
    }
    return node;
}

const paths = {
    home: 'M3 10 12 3l9 7v10a1 1 0 0 1-1 1h-5v-7H9v7H4a1 1 0 0 1-1-1Z',
    box: 'M3 7 12 2l9 5v10l-9 5-9-5Zm0 0 9 5 9-5M12 12v10M7.5 4.5l9 5',
    guard: 'M12 2 3 6v6c0 5 9 10 9 10s9-5 9-10V6ZM8 12l3 3 5-6',
    import: 'M12 3v12m-5-5 5 5 5-5M4 15v5h16v-5',
    person: 'M16 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0ZM4 21v-3a6 6 0 0 1 6-6h4a6 6 0 0 1 6 6v3',
    review: 'M4 3h16a1 1 0 0 1 1 1v12a1 1 0 0 1-1 1H8l-5 4V4a1 1 0 0 1 1-1Zm3 5h10M7 12h7',
    arrow: 'M5 12h14m-5-5 5 5-5 5',
    back: 'M19 12H5m5-5-5 5 5 5',
    plus: 'M12 4v16M4 12h16',
    clock: 'M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0ZM12 6v6l4 2',
    check: 'M4 12l5 5L20 6',
    qr: 'M3 3h6v6H3Zm12 0h6v6h-6ZM3 15h6v6H3Zm12 0v3h3v3h3v-6Zm6-3h-3m-6 0v3m0 6v-3',
    search: 'M17 10a7 7 0 1 1-14 0 7 7 0 0 1 14 0Zm-2 5 6 6'
};
export function icon(name) {
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    for (const [key, value] of Object.entries({ viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor', 'stroke-width': '1.7', 'stroke-linecap': 'round', 'stroke-linejoin': 'round', 'aria-hidden': 'true' })) svg.setAttribute(key, value);
    const path = document.createElementNS(svg.namespaceURI, 'path'); path.setAttribute('d', paths[name] || paths.box); svg.append(path); return svg;
}
export const money = value => new Intl.NumberFormat('uk-UA', { style: 'currency', currency: 'UAH', maximumFractionDigits: 2 }).format(value || 0);
export const when = value => value ? new Intl.DateTimeFormat('ru', { dateStyle: 'short', timeStyle: 'short', timeZone: 'Europe/Vilnius' }).format(new Date(value)) : '';
export const statusName = status => ({ DRAFT: 'Чернетка', PENDING_MODERATION: 'На перевірці', ACTIVE: 'Опубліковано', PUBLISHED: 'Опубліковано', REJECTED: 'Відхилено', ARCHIVED: 'В архіві' }[status] || status);
export const badge = status => el('span', { class: 'badge status-' + String(status).toLowerCase() }, statusName(status));
export const button = (text, click, kind = 'primary', disabled = false) => el('button', { type: 'button', class: 'button ' + kind, onclick: click, disabled }, text);
export const heading = (title, description) => el('div', { class: 'page-heading' }, el('h1', {}, title), description ? el('p', {}, description) : null);
export const empty = (title, description = '') => el('section', { class: 'card empty-state' }, icon('box'), el('h2', {}, title), el('p', {}, description));
export function field(label, control, hint) { return el('label', { class: 'field' }, el('span', {}, label), control, hint ? el('small', {}, hint) : null); }
export function input(value, change, props = {}) { return el('input', { ...props, value: value ?? '', oninput: event => change(event.target.type === 'number' ? event.target.valueAsNumber : event.target.value) }); }
export function select(options, value, change, props = {}) {
    const node = el('select', { ...props, onchange: event => change(event.target.value) }, options.map(option => el('option', { value: option.id, selected: option.id === value }, option.name)));
    node.value = value ?? ''; return node;
}
let toastTimer;
export function toast(message, success = false) {
    const node = document.querySelector('#toast'); node.textContent = message; node.className = 'toast' + (success ? ' success' : ''); node.hidden = false;
    clearTimeout(toastTimer); toastTimer = setTimeout(() => node.hidden = true, success ? 3500 : 9000);
}
