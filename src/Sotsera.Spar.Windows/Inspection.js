// Fixed development-only operations. Requests cannot supply executable JavaScript.
request => {
  if (location.href !== request.source) return { code: 'document_retired' };
  const visible = element => {
    const box = element.getBoundingClientRect();
    const style = getComputedStyle(element);
    return box.width > 0 && box.height > 0 && style.visibility !== 'hidden' && style.display !== 'none';
  };
  const selector = element => {
    if (element.id) return '#' + CSS.escape(element.id);
    const parts = [];
    for (let node = element; node && node !== document.documentElement; node = node.parentElement) {
      const siblings = [...node.parentElement.children].filter(sibling => sibling.tagName === node.tagName);
      parts.unshift(node.localName + ':nth-of-type(' + (siblings.indexOf(node) + 1) + ')');
    }
    return 'html>' + parts.join('>');
  };
  if (request.operation === 'snapshot') {
    const elements = [];
    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_TEXT);
    let text = '';
    let visited = 0;
    let node;
    while (visited < 1000 && (node = walker.nextNode())) {
      visited++;
      const element = node instanceof Element ? node : node.parentElement;
      if (!element || element.closest('script,style,noscript') || !visible(element)) continue;
      if (node.nodeType === Node.TEXT_NODE && text.length < 2500) text += node.nodeValue.slice(0, 2500 - text.length);
      if (node instanceof Element && elements.length < 32 && node.matches('h1,h2,h3,button,input,textarea,select,a,[role],label')) elements.push(node);
    }
    return {
      code: 'ok', title: document.title.slice(0, 200), text,
      truncated: visited >= 1000 || elements.length >= 32 || text.length >= 2500,
      elements: elements.map(element => ({
        selector: selector(element).slice(0, 512), tag: element.localName,
        role: element.getAttribute('role'), text: (element.innerText || element.getAttribute('aria-label') || '').slice(0, 100),
        value: element.type === 'password' ? '[redacted]' : String(element.value ?? '').slice(0, 100),
        disabled: !!element.disabled,
      })),
    };
  }
  try {
    const matches = document.querySelectorAll(request.selector);
    if (matches.length !== 1) return { code: matches.length ? 'ambiguous_target' : 'target_missing' };
    const element = matches[0];
    if (!visible(element) || element.disabled || element.closest('[inert]')) return { code: 'target_unavailable' };
    if (request.operation === 'click') {
      if (!(element instanceof HTMLElement)) return { code: 'unsupported_target' };
      element.click();
    } else if (request.operation === 'type') {
      if (!(element instanceof HTMLTextAreaElement) &&
          !(element instanceof HTMLInputElement && ['text','search','email','url','tel'].includes(element.type))) {
        return { code: 'unsupported_target' };
      }
      if (element.readOnly) return { code: 'target_unavailable' };
      element.focus();
      const prototype = element instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
      Object.getOwnPropertyDescriptor(prototype, 'value').set.call(element, request.text);
      element.dispatchEvent(new Event('input', { bubbles: true }));
      element.dispatchEvent(new Event('change', { bubbles: true }));
    } else return { code: 'unknown_operation' };
    return { code: 'ok', dispatched: true };
  } catch {
    return { code: 'invalid_selector_or_action' };
  }
}
