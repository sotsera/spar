// Spar's window-test engine in page script: role and accessible-name queries, label/text/test-id queries, chaining,
// strict mode, actionability, element state, and a watcher that confirms input reached its target. Written for the
// spike from the WAI-ARIA 1.2, HTML-AAM and Accessible Name 1.2 specifications. It runs in an isolated script world
// (WebKitGTK) or the main world (the Windows copy) and keeps no state between calls except the armed input watcher.
(() => {
  if (globalThis.__sparAutomation) return;

  const ROLES = new Set(('alert alertdialog application article banner blockquote button caption cell checkbox code columnheader ' +
    'combobox complementary contentinfo definition deletion dialog directory document emphasis feed figure form generic grid ' +
    'gridcell group heading img insertion link list listbox listitem log main mark marquee math menu menubar menuitem ' +
    'menuitemcheckbox menuitemradio meter navigation none note option paragraph presentation progressbar radio radiogroup ' +
    'region row rowgroup rowheader scrollbar search searchbox separator slider spinbutton status strong subscript ' +
    'superscript switch tab table tablist tabpanel term textbox time timer toolbar tooltip tree treegrid treeitem').split(' '));
  const NAME_FROM_CONTENT = new Set(('button cell checkbox columnheader gridcell heading link menuitem menuitemcheckbox ' +
    'menuitemradio option radio row rowheader switch tab tooltip treeitem caption code deletion emphasis insertion mark ' +
    'strong subscript superscript term time').split(' '));
  const LABELABLE = 'button, input:not([type=hidden]), meter, output, progress, select, textarea';
  const SKIP_TEXT = new Set(['SCRIPT', 'STYLE', 'TEMPLATE', 'HEAD', 'NOSCRIPT']);
  const norm = s => (s || '').replace(/[\s​]+/g, ' ').trim();

  // ---- roles -------------------------------------------------------------------------------------------------
  const sectioning = e => e.parentElement && e.parentElement.closest('article, aside, main, nav, section');
  function implicitRole(e) {
    const tag = e.localName;
    switch (tag) {
      case 'a': case 'area': return e.hasAttribute('href') ? 'link' : null;
      case 'article': return 'article';
      case 'aside': return 'complementary';
      case 'blockquote': return 'blockquote';
      case 'button': return 'button';
      case 'caption': return 'caption';
      case 'code': return 'code';
      case 'datalist': return 'listbox';
      case 'dd': return 'definition';
      case 'del': return 'deletion';
      case 'details': return 'group';
      case 'dfn': case 'dt': return 'term';
      case 'dialog': return 'dialog';
      case 'em': return 'emphasis';
      case 'fieldset': return 'group';
      case 'figure': return 'figure';
      case 'footer': return sectioning(e) ? null : 'contentinfo';
      case 'form': return 'form';
      case 'h1': case 'h2': case 'h3': case 'h4': case 'h5': case 'h6': return 'heading';
      case 'header': return sectioning(e) ? null : 'banner';
      case 'hr': return 'separator';
      case 'html': return 'document';
      case 'img': return e.getAttribute('alt') === '' && !e.title && !e.hasAttribute('aria-label') && !e.hasAttribute('aria-labelledby') ? 'presentation' : 'img';
      case 'input': {
        const type = (e.getAttribute('type') || 'text').toLowerCase();
        const list = e.hasAttribute('list');
        switch (type) {
          case 'button': case 'image': case 'reset': case 'submit': return 'button';
          case 'checkbox': return 'checkbox';
          case 'radio': return 'radio';
          case 'range': return 'slider';
          case 'number': return 'spinbutton';
          case 'search': return list ? 'combobox' : 'searchbox';
          case 'email': case 'tel': case 'text': case 'url': case 'password': return list ? 'combobox' : 'textbox';
          case 'file': return 'button';
          case 'hidden': case 'color': return null;
          default: return 'textbox';
        }
      }
      case 'ins': return 'insertion';
      case 'li': return 'listitem';
      case 'main': return 'main';
      case 'mark': return 'mark';
      case 'math': return 'math';
      case 'menu': case 'ol': case 'ul': return 'list';
      case 'meter': return 'meter';
      case 'nav': return 'navigation';
      case 'optgroup': return 'group';
      case 'option': return 'option';
      case 'output': return 'status';
      case 'p': return 'paragraph';
      case 'progress': return 'progressbar';
      case 'search': return 'search';
      case 'section': return e.hasAttribute('aria-label') || e.hasAttribute('aria-labelledby') || e.title ? 'region' : null;
      case 'select': return e.multiple || e.size > 1 ? 'listbox' : 'combobox';
      case 'strong': return 'strong';
      case 'sub': return 'subscript';
      case 'sup': return 'superscript';
      case 'table': return 'table';
      case 'tbody': case 'thead': case 'tfoot': return 'rowgroup';
      case 'td': return e.closest('table')?.getAttribute('role') === 'grid' ? 'gridcell' : 'cell';
      case 'th': return e.scope === 'row' || (!e.closest('thead') && e.parentElement?.firstElementChild === e && e.parentElement.children.length > 1 && e.closest('tbody')) ? 'rowheader' : 'columnheader';
      case 'textarea': return 'textbox';
      case 'time': return 'time';
      case 'tr': return 'row';
      default: return null;
    }
  }
  const focusable = e => e.tabIndex >= 0 || e.hasAttribute('tabindex');
  function role(e) {
    const attr = e.getAttribute('role');
    if (attr) {
      for (const token of attr.trim().toLowerCase().split(/\s+/)) {
        if (!ROLES.has(token)) continue;
        // Presentational roles are ignored on focusable elements (conflict resolution).
        if ((token === 'none' || token === 'presentation') && focusable(e)) return implicitRole(e);
        return token;
      }
    }
    return implicitRole(e);
  }

  // ---- hidden from the accessibility tree ----------------------------------------------------------------------
  // aria-hidden, hidden, inert or display:none on the element or an ancestor (cached per call).
  function excluded(e, cache) {
    const known = cache.get(e);
    if (known !== undefined) return known;
    const hidden = e.getAttribute('aria-hidden') === 'true' || e.hidden || e.inert || getComputedStyle(e).display === 'none' ||
      (e.parentElement ? excluded(e.parentElement, cache) : false);
    cache.set(e, hidden);
    return hidden;
  }
  // visibility is inherited, so only the element's own computed value matters (a visible child of a hidden parent shows).
  function hiddenForA11y(e, cache) {
    if (excluded(e, cache)) return true;
    const v = getComputedStyle(e).visibility;
    return v === 'hidden' || v === 'collapse';
  }

  // ---- accessible name (Accessible Name 1.2) -----------------------------------------------------------------
  const isBlock = e => { const d = getComputedStyle(e).display; return d !== 'inline' && d !== 'contents'; };
  function embeddedValue(e, r) {
    if (r === 'textbox' || r === 'searchbox') return 'value' in e ? e.value : e.textContent;
    if (r === 'combobox' || r === 'listbox') {
      if (e.localName === 'select') return [...e.selectedOptions].map(o => o.label).join(' ');
      if (e.localName === 'input') return e.value;
      const selected = e.querySelector('[aria-selected=true]');
      return selected ? selected.textContent : e.textContent;
    }
    if (r === 'slider' || r === 'spinbutton' || r === 'scrollbar' || r === 'progressbar' || r === 'meter')
      return e.getAttribute('aria-valuetext') ?? e.getAttribute('aria-valuenow') ?? e.value ?? '';
    return null;
  }
  function textAlternative(e, ctx) {
    if (ctx.visited.has(e)) return '';
    ctx.visited.add(e);
    const r = role(e);
    // 2A: hidden, unless this traversal started at a hidden aria-labelledby target.
    if (!ctx.hiddenOk && hiddenForA11y(e, ctx.cache)) return '';
    // 2B: aria-labelledby (not when already following one).
    if (!ctx.inLabelledBy) {
      const ids = (e.getAttribute('aria-labelledby') || '').split(/\s+/).filter(Boolean);
      const refs = ids.map(id => e.getRootNode().getElementById?.(id) ?? document.getElementById(id)).filter(Boolean);
      if (refs.length) {
        return refs.map(ref => textAlternative(ref, { ...ctx, inLabelledBy: true, recursion: true, hiddenOk: hiddenForA11y(ref, ctx.cache), visited: new Set() })).join(' ');
      }
    }
    const embedded = ctx.recursion && embeddedValue(e, r);
    // 2C: aria-label (an embedded control met while recursing uses its value instead).
    const label = norm(e.getAttribute('aria-label'));
    if (label && !(ctx.recursion && embedded !== null && embedded !== false)) return label;
    // 2D: native labelling, unless presentational.
    if (r !== 'none' && r !== 'presentation') {
      const native = nativeName(e, r, ctx);
      if (native) return native;
    }
    // 2E: embedded control while recursing.
    if (ctx.recursion && embedded !== null && embedded !== false) return embedded;
    // 2F: name from content.
    if (NAME_FROM_CONTENT.has(r) || ctx.recursion || e.localName === 'label' || e.localName === 'legend') {
      const content = fromContent(e, ctx);
      if (norm(content)) return content;
    }
    // 2I: tooltip.
    return e.getAttribute('title') || '';
  }
  function nativeName(e, r, ctx) {
    const tag = e.localName;
    if (tag === 'input') {
      const type = (e.getAttribute('type') || 'text').toLowerCase();
      if (type === 'button' || type === 'submit' || type === 'reset') return e.value || (type === 'submit' ? 'Submit' : type === 'reset' ? 'Reset' : '');
      if (type === 'image') return e.alt || e.value || 'Submit';
    }
    if (e.matches(LABELABLE) && e.labels && e.labels.length) {
      const text = [...e.labels].map(l => textAlternative(l, { ...ctx, recursion: true, visited: new Set([e]) })).join(' ');
      if (norm(text)) return text;
    }
    if (tag === 'img' || tag === 'area') return e.getAttribute('alt') || '';
    if (tag === 'fieldset') { const legend = e.querySelector(':scope > legend'); if (legend) return textAlternative(legend, { ...ctx, recursion: true }); }
    if (tag === 'figure') { const caption = e.querySelector(':scope > figcaption'); if (caption) return textAlternative(caption, { ...ctx, recursion: true }); }
    if (tag === 'table') { const caption = e.querySelector(':scope > caption'); if (caption) return textAlternative(caption, { ...ctx, recursion: true }); }
    if ((tag === 'input' || tag === 'textarea') && !e.title && e.placeholder) return e.placeholder;
    return '';
  }
  function pseudo(e, which) {
    const content = getComputedStyle(e, which).content;
    if (!content || content === 'none' || content === 'normal') return '';
    const m = content.match(/^"(.*)"$/s);
    return m ? m[1] : '';
  }
  function fromContent(e, ctx) {
    let text = pseudo(e, '::before');
    const children = e.localName === 'slot' ? e.assignedNodes() : [...(e.shadowRoot ? e.shadowRoot.childNodes : e.childNodes)];
    for (const child of children) {
      if (child.nodeType === 3) text += child.textContent;
      else if (child.nodeType === 1) {
        if (SKIP_TEXT.has(child.tagName)) continue;
        const part = textAlternative(child, { ...ctx, recursion: true });
        text += isBlock(child) ? ' ' + part + ' ' : part;
      }
    }
    return text + pseudo(e, '::after');
  }
  const accessibleName = (e, cache) => norm(textAlternative(e, { cache, visited: new Set(), recursion: false, inLabelledBy: false, hiddenOk: false }));

  // ---- states ---------------------------------------------------------------------------------------------------
  const disabled = e => e.matches(':disabled') || !!e.closest('[aria-disabled=true]');
  function checked(e) {
    if (e.localName === 'input' && (e.type === 'checkbox' || e.type === 'radio')) return e.indeterminate ? 'mixed' : e.checked;
    const a = e.getAttribute('aria-checked');
    return a === 'mixed' ? 'mixed' : a === 'true';
  }
  const level = e => /^h[1-6]$/.test(e.localName) ? +e.localName[1] : +(e.getAttribute('aria-level') || 0);

  // ---- matching -------------------------------------------------------------------------------------------------
  function matcher(spec, exact) {
    if (spec && typeof spec === 'object') { const re = new RegExp(spec.re, spec.flags); return s => re.test(norm(s)); }
    const want = norm(spec);
    return exact ? s => norm(s) === want : s => norm(s).toLowerCase().includes(want.toLowerCase());
  }
  function byRole(root, step, cache) {
    const nameMatches = step.name !== undefined && step.name !== null ? matcher(step.name, step.exact) : null;
    const out = [];
    for (const e of root.querySelectorAll('*')) {
      if (role(e) !== step.role) continue;
      if (!step.includeHidden && hiddenForA11y(e, cache)) continue;
      if (step.checked !== undefined && checked(e) !== step.checked) continue;
      if (step.pressed !== undefined && (e.getAttribute('aria-pressed') === 'true') !== step.pressed) continue;
      if (step.expanded !== undefined && (e.getAttribute('aria-expanded') === 'true') !== step.expanded) continue;
      if (step.selected !== undefined && ((e.selected ?? e.getAttribute('aria-selected') === 'true') === true) !== step.selected) continue;
      if (step.level !== undefined && level(e) !== step.level) continue;
      if (step.disabled !== undefined && disabled(e) !== step.disabled) continue;
      if (nameMatches && !nameMatches(accessibleName(e, cache))) continue;
      out.push(e);
    }
    return out;
  }
  // Form controls by their label: a <label>'s own text (controls inside it excluded), aria-labelledby or aria-label.
  function byLabel(root, step, cache) {
    const match = matcher(step.text, step.exact);
    const found = new Set();
    for (const label of root.querySelectorAll('label')) {
      let own = '';
      for (const n of label.childNodes) if (n.nodeType === 3 || !(n.matches && n.matches(LABELABLE + ', [role]'))) own += n.textContent;
      if (label.control && match(own)) found.add(label.control);
    }
    for (const e of root.querySelectorAll('[aria-labelledby]')) {
      const text = e.getAttribute('aria-labelledby').split(/\s+/).map(id => document.getElementById(id)?.textContent || '').join(' ');
      if (match(text)) found.add(e);
    }
    for (const e of root.querySelectorAll('[aria-label]')) if (match(e.getAttribute('aria-label'))) found.add(e);
    return [...found].sort((a, b) => a.compareDocumentPosition(b) & 2 ? 1 : -1);
  }
  // The deepest elements whose text contains (or, exact, equals) the text.
  function byText(root, step) {
    const match = matcher(step.text, step.exact);
    const contains = typeof step.text === 'string' ? s => norm(s).toLowerCase().includes(norm(step.text).toLowerCase()) : match;
    const found = [];
    const walk = e => {
      if (SKIP_TEXT.has(e.tagName) || !contains(e.textContent)) return false;
      let child = false;
      for (const c of e.children) child = walk(c) || child;
      if (child) return true;
      if (match(e.textContent)) { found.push(e); return true; }
      return false;
    };
    walk(root.nodeType === 9 ? root.documentElement : root);
    return found;
  }
  function query(root, step, cache) {
    switch (step.k) {
      case 'role': return byRole(root, step, cache);
      case 'label': return byLabel(root, step, cache);
      case 'text': return byText(root, step);
      case 'testid': return [...root.querySelectorAll('[data-testid="' + CSS.escape(step.id) + '"]')];
      case 'css': return [...root.querySelectorAll(step.sel)];
      default: throw new Error('unknown step ' + step.k);
    }
  }
  function resolve(chain, cache) {
    let current = [document];
    for (const step of chain) {
      if (step.k === 'nth') { const i = step.i < 0 ? current.length + step.i : step.i; current = i >= 0 && i < current.length ? [current[i]] : []; continue; }
      const next = new Set();
      for (const root of current) for (const e of query(root, step, cache)) next.add(e);
      current = [...next];
    }
    return current;
  }

  // ---- actionability, state, description ------------------------------------------------------------------------
  function describe(e) {
    const cls = typeof e.className === 'string' && e.className ? ' class="' + e.className.slice(0, 40) + (e.className.length > 40 ? '…' : '') + '"' : '';
    const text = norm(e.textContent);
    return '<' + e.localName + (e.id ? ' id="' + e.id + '"' : '') + cls + '>' + (text ? ' "' + text.slice(0, 40) + '"' : '');
  }
  const visible = e => { const r = e.getBoundingClientRect(); return e.isConnected && r.width > 0 && r.height > 0 && getComputedStyle(e).visibility === 'visible'; };
  const frame = () => new Promise(r => requestAnimationFrame(() => r()));
  function hitTarget(x, y) {
    let hit = document.elementFromPoint(x, y);
    while (hit && hit.shadowRoot) { const inner = hit.shadowRoot.elementFromPoint(x, y); if (!inner || inner === hit) break; hit = inner; }
    return hit;
  }
  const within = (node, e) => { for (let n = node; n; n = n.parentNode instanceof ShadowRoot ? n.parentNode.host : n.parentNode) if (n === e) return true; return false; };
  function single(chain, cache) {
    const found = resolve(chain, cache);
    if (found.length === 1) return { element: found[0] };
    if (found.length === 0) return { why: 'no element matches' };
    return { strict: found.length, described: found.slice(0, 5).map(describe) };
  }
  async function actionable(req) {
    const cache = new Map();
    const s = single(req.chain, cache);
    if (!s.element) return s;
    const e = s.element;
    if (!e.isConnected) return { why: describe(e) + ': detached' };
    if (!visible(e)) return { why: describe(e) + ': not visible' };
    if (req.enabled && disabled(e)) return { why: describe(e) + ': not enabled' };
    if (req.editable && (e.readOnly || e.getAttribute('aria-readonly') === 'true')) return { why: describe(e) + ': not editable' };
    let r = e.getBoundingClientRect();
    if (r.top < 0 || r.left < 0 || r.bottom > innerHeight || r.right > innerWidth) e.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' });
    if (!req.pointer) return { ok: true };
    const a = e.getBoundingClientRect();
    await frame();
    const b = e.getBoundingClientRect();
    if (a.x !== b.x || a.y !== b.y || a.width !== b.width || a.height !== b.height) return { why: describe(e) + ': not stable', unstable: true };
    const x = b.x + b.width / 2, y = b.y + b.height / 2;
    const hit = hitTarget(x, y);
    if (!within(hit, e)) return { why: describe(e) + ': click point hits ' + (hit ? describe(hit).replace(/ ".*$/, '') : 'nothing') };
    if (req.arm) arm(e, req.arm);
    return { ok: true, x, y, dpr: devicePixelRatio };
  }
  function state(req) {
    const cache = new Map();
    const found = resolve(req.chain, cache);
    if (found.length !== 1) return { count: found.length };
    const e = found[0];
    return {
      count: 1,
      state: {
        visible: visible(e),
        enabled: !disabled(e),
        checked: checked(e) === true,
        text: norm(e.textContent),
        value: 'value' in e ? String(e.value) : null,
        focused: document.activeElement === e,
      },
    };
  }

  // ---- input watcher: did the trusted event reach the target? -------------------------------------------------------
  let armed = null;
  function arm(target, kind) {
    disarm();
    // Pointer events, not mouse events: a pointerdown handler that calls preventDefault (Radix menus and selects do)
    // suppresses the compatibility mousedown/mouseup.
    const types = kind === 'pointer' ? ['pointerdown', 'pointerup'] : ['keydown', 'keyup'];
    const w = { target, kind, events: [], waiters: [], need: kind === 'pointer' ? 1 : 0 };
    w.listener = ev => {
      w.events.push({ type: ev.type, trusted: ev.isTrusted, onTarget: target ? within(ev.composedPath()[0], target) : true, activation: navigator.userActivation ? navigator.userActivation.isActive : null });
      for (const waiter of [...w.waiters]) waiter();
    };
    for (const t of types) addEventListener(t, w.listener, true);
    w.types = types;
    armed = w;
  }
  function disarm() {
    if (!armed) return;
    for (const t of armed.types) removeEventListener(t, armed.listener, true);
    armed = null;
  }
  // Waits until `count` events of the final type (pointerup or keyup) arrived, then one task more so the click or the
  // key's default action, dispatched in the same task, has run.
  function landed(req) {
    const w = armed;
    if (!w) return { error: 'nothing armed' };
    const last = w.kind === 'pointer' ? 'pointerup' : 'keyup';
    const done = () => w.events.filter(e => e.type === last).length >= (req.count || 1);
    const report = () => {
      const first = w.events[0];
      const result = { seen: w.events.length, trusted: w.events.every(e => e.trusted), onTarget: first ? first.onTarget : false, activation: w.events.some(e => e.activation) };
      disarm();
      return result;
    };
    if (done()) return new Promise(r => setTimeout(() => r(report()), 0));
    return new Promise(resolveLanded => {
      const timer = setTimeout(() => resolveLanded({ ...report(), timedOut: true }), req.timeout || 1000);
      w.waiters.push(() => { if (done()) { clearTimeout(timer); setTimeout(() => resolveLanded(report()), 0); } });
    });
  }

  // ---- other actions ------------------------------------------------------------------------------------------------
  function focus(req) {
    const s = single(req.chain, new Map());
    if (!s.element) return s;
    const e = s.element;
    e.focus();
    if (req.selectAll) {
      if (typeof e.select === 'function' && e.localName !== 'select') e.select();
      else if (e.isContentEditable) { const range = document.createRange(); range.selectNodeContents(e); const sel = getSelection(); sel.removeAllRanges(); sel.addRange(range); }
    }
    if (req.arm) arm(e, req.arm);
    return { ok: document.activeElement === e || e.contains(document.activeElement) };
  }
  // Fill without the keyboard: execCommand('insertText') fires beforeinput/input from the editor (isTrusted true).
  function insertText(req) {
    const s = single(req.chain, new Map());
    if (!s.element) return s;
    const e = s.element;
    e.focus();
    if (typeof e.select === 'function') e.select();
    let trusted = null;
    const watch = ev => { trusted = ev.isTrusted; };
    addEventListener('input', watch, true);
    const ok = req.text.length ? document.execCommand('insertText', false, req.text) : document.execCommand('delete');
    removeEventListener('input', watch, true);
    return { ok, trusted, value: e.value };
  }
  function selectOption(req) {
    const s = single(req.chain, new Map());
    if (!s.element) return s;
    const e = s.element;
    if (e.localName !== 'select') return { error: 'not a <select>' };
    const options = [...e.options];
    const picked = req.values.map(w => options.find(o => o.value === w || o.label === w));
    if (picked.some(o => !o)) return { error: 'options are ' + options.map(o => '"' + o.label + '"').join(', ') };
    for (const o of options) o.selected = picked.includes(o);
    e.dispatchEvent(new Event('input', { bubbles: true }));
    e.dispatchEvent(new Event('change', { bubbles: true }));
    return { ok: true };
  }
  function box(req) {
    const s = single(req.chain, new Map());
    if (!s.element) return s;
    const r = s.element.getBoundingClientRect();
    return { ok: true, x: r.x, y: r.y, width: r.width, height: r.height };
  }
  // Every element exposed to assistive technology, with role and name (for comparisons with the platform's tree).
  function tree() {
    const cache = new Map();
    const all = [...document.querySelectorAll('*')];
    const out = [];
    all.forEach((e, index) => {
      const r = role(e);
      if (!r || r === 'none' || r === 'presentation' || r === 'generic' || hiddenForA11y(e, cache)) return;
      out.push({ index, role: r, name: accessibleName(e, cache), tag: e.localName });
    });
    return out;
  }

  // ---- untrusted input: script-dispatched events, for hosts without trusted input ----------------------------------
  function synthClick(req) {
    const target = hitTarget(req.x, req.y);
    if (!target) return { error: 'nothing at the point' };
    const init = { bubbles: true, cancelable: true, composed: true, clientX: req.x, clientY: req.y, button: 0, view: window };
    const pointer = { ...init, pointerId: 1, pointerType: 'mouse', isPrimary: true };
    // A real mouse moves to the point first; Radix Select tells a click from a press-drag-release by the pointermove
    // delta, and without moves it cancels the option's pointerup (the first script-input run failed 60 of 60 there).
    if (req.moves !== false) {
      target.dispatchEvent(new PointerEvent('pointerover', { ...pointer, buttons: 0 }));
      target.dispatchEvent(new PointerEvent('pointermove', { ...pointer, buttons: 0 }));
      target.dispatchEvent(new MouseEvent('mousemove', { ...init, buttons: 0 }));
    }
    target.dispatchEvent(new PointerEvent('pointerdown', { ...pointer, buttons: 1 }));
    target.dispatchEvent(new MouseEvent('mousedown', { ...init, buttons: 1 }));
    const focusTarget = target.closest('button, a[href], input, select, textarea, [tabindex]');
    if (focusTarget) focusTarget.focus();
    target.dispatchEvent(new PointerEvent('pointerup', { ...pointer, buttons: 0 }));
    target.dispatchEvent(new MouseEvent('mouseup', { ...init, buttons: 0 }));
    target.dispatchEvent(new MouseEvent('click', { ...init, buttons: 0, detail: 1 }));
    return { ok: true };
  }
  function synthKey(req) {
    const target = document.activeElement || document.body;
    const init = { key: req.key, code: req.key.length === 1 ? 'Key' + req.key.toUpperCase() : req.key, bubbles: true, cancelable: true, composed: true };
    target.dispatchEvent(new KeyboardEvent('keydown', init));
    target.dispatchEvent(new KeyboardEvent('keyup', init));
    return { ok: true };
  }

  const ops = {
    synthClick, synthKey,
    count: req => ({ count: resolve(req.chain, new Map()).length }),
    describe: req => resolve(req.chain, new Map()).slice(0, 5).map(describe),
    state, actionable, landed, focus, insertText, selectOption, box, tree,
    arm: req => { arm(null, req.kind); return { ok: true }; },
    frames: () => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(() => r(true)))),
    url: () => location.href,
    name: req => { const s = single(req.chain, new Map()); return s.element ? { name: accessibleName(s.element, new Map()), role: role(s.element) } : s; },
  };
  Object.defineProperty(globalThis, '__sparAutomation', {
    value: {
      run: req => ops[req.op](req),
      // For hosts that keep element handles themselves (the Windows copy): one step from a root, and an element's index.
      queryFrom: (root, step) => query(root, step, new Map()),
      indexOf: e => [...document.querySelectorAll('*')].indexOf(e),
    },
  });
})();
