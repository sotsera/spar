// Spar's window-test engine (plan section 5, level 5; decision "Step 8's shape: window tests driven by Spar's own
// API"): finding elements by role and accessible name, label, text, placeholder, alt text and title; chained and
// filtered locators with strict mode; element state for assertions; and actionability before trusted input (attached,
// visible, stable over a frame, enabled, editable, scrolled into view, and hit by the pointer at its click point).
// Written from WAI-ARIA 1.2, HTML-AAM and Accessible Name and Description Computation 1.2.
//
// It is host-neutral: the Windows host runs it in an isolated world through the DevTools Protocol, and the Linux host
// will run it in a script world of its own, so the page's scripts never see it and cannot change what it uses. Each
// call resolves its locator again from the document (no element handles between calls), and keeps no state between
// calls except the armed pointer watcher. It defines one property, `__sparAutomation`, on its own world's global
// object. No eval, string timers or DOM sinks: it works under any page policy.
(() => {
  "use strict";

  if (globalThis.__sparAutomation !== undefined) {
    return;
  }

  // ---- Roles (WAI-ARIA 1.2, HTML-AAM) ----------------------------------------------------------------------------

  const knownRoles = new Set([
    "alert", "alertdialog", "application", "article", "banner", "blockquote", "button", "caption", "cell", "checkbox",
    "code", "columnheader", "combobox", "complementary", "contentinfo", "definition", "deletion", "dialog", "directory",
    "document", "emphasis", "feed", "figure", "form", "generic", "grid", "gridcell", "group", "heading", "img",
    "insertion", "link", "list", "listbox", "listitem", "log", "main", "mark", "marquee", "math", "menu", "menubar",
    "menuitem", "menuitemcheckbox", "menuitemradio", "meter", "navigation", "none", "note", "option", "paragraph",
    "presentation", "progressbar", "radio", "radiogroup", "region", "row", "rowgroup", "rowheader", "scrollbar",
    "search", "searchbox", "separator", "slider", "spinbutton", "status", "strong", "subscript", "superscript",
    "switch", "tab", "table", "tablist", "tabpanel", "term", "textbox", "time", "timer", "toolbar", "tooltip", "tree",
    "treegrid", "treeitem",
  ]);

  // Roles whose name may come from their content (Accessible Name 1.2, "name from: contents").
  const nameFromContent = new Set([
    "button", "cell", "checkbox", "columnheader", "gridcell", "heading", "link", "menuitem", "menuitemcheckbox",
    "menuitemradio", "option", "radio", "row", "rowheader", "switch", "tab", "tooltip", "treeitem",
  ]);

  // Roles whose children are presentational (WAI-ARIA 1.2): their content belongs to them, so it is no text of theirs.
  const childrenPresentational = new Set([
    "button", "checkbox", "img", "math", "menuitemcheckbox", "menuitemradio", "meter", "option", "progressbar", "radio",
    "scrollbar", "separator", "slider", "switch", "tab",
  ]);

  // Text-level roles: shown inline, they are part of a sentence, so their text stays in the text of the element
  // around them.
  const textLevel = new Set([
    "code", "deletion", "emphasis", "insertion", "link", "mark", "strong", "subscript", "superscript", "term", "time",
  ]);

  // Content that is never text a person reads (the SVG `title` and `desc` are a name and a description).
  const notText = new Set(["script", "style", "template", "noscript", "head", "title", "desc"]);

  const labelable = "button, input:not([type=hidden]), meter, output, progress, select, textarea";
  const skippedContent = new Set(["SCRIPT", "STYLE", "TEMPLATE", "NOSCRIPT", "HEAD"]);
  const sectioning = "article, aside, main, nav, section";

  const normalize = text => (text ?? "").replace(/[\s​]+/g, " ").trim();

  function inputRole(element) {
    const type = (element.getAttribute("type") ?? "text").toLowerCase();
    const withList = element.hasAttribute("list");
    switch (type) {
      case "button":
      case "image":
      case "reset":
      case "submit":
      case "file":
        return "button";
      case "checkbox":
        return "checkbox";
      case "radio":
        return "radio";
      case "range":
        return "slider";
      case "number":
        return "spinbutton";
      case "search":
        return withList ? "combobox" : "searchbox";
      case "hidden":
      case "color":
      case "date":
      case "datetime-local":
      case "month":
      case "time":
      case "week":
        return type === "hidden" ? null : "textbox";
      default:
        return withList ? "combobox" : "textbox";
    }
  }

  function implicitRole(element) {
    switch (element.localName) {
      case "a":
      case "area":
        return element.hasAttribute("href") ? "link" : null;
      case "article":
        return "article";
      case "aside":
        return "complementary";
      case "blockquote":
        return "blockquote";
      case "button":
        return "button";
      case "caption":
        return "caption";
      case "code":
        return "code";
      case "datalist":
        return "listbox";
      case "dd":
        return "definition";
      case "del":
        return "deletion";
      case "details":
      case "fieldset":
      case "optgroup":
        return "group";
      case "dfn":
      case "dt":
        return "term";
      case "dialog":
        return "dialog";
      case "em":
        return "emphasis";
      case "figure":
        return "figure";
      case "footer":
        return element.parentElement?.closest(sectioning) ? null : "contentinfo";
      case "form":
        return "form";
      case "h1":
      case "h2":
      case "h3":
      case "h4":
      case "h5":
      case "h6":
        return "heading";
      case "header":
        return element.parentElement?.closest(sectioning) ? null : "banner";
      case "hr":
        return "separator";
      case "html":
        return "document";
      case "img":
        return element.getAttribute("alt") === "" && !hasAuthorName(element) ? "presentation" : "img";
      case "input":
        return inputRole(element);
      case "ins":
        return "insertion";
      case "li":
        return "listitem";
      case "main":
        return "main";
      case "mark":
        return "mark";
      case "math":
        return "math";
      case "menu":
      case "ol":
      case "ul":
        return "list";
      case "meter":
        return "meter";
      case "nav":
        return "navigation";
      case "option":
        return "option";
      case "output":
        return "status";
      case "p":
        return "paragraph";
      case "progress":
        return "progressbar";
      case "search":
        return "search";
      case "section":
        return hasAuthorName(element) ? "region" : null;
      case "select":
        return element.multiple || element.size > 1 ? "listbox" : "combobox";
      case "strong":
        return "strong";
      case "sub":
        return "subscript";
      case "sup":
        return "superscript";
      case "summary":
        return "button";
      case "table":
        return "table";
      case "tbody":
      case "tfoot":
      case "thead":
        return "rowgroup";
      case "td":
        return /^(grid|treegrid)$/.test(element.closest("table")?.getAttribute("role") ?? "") ? "gridcell" : "cell";
      case "textarea":
        return "textbox";
      case "th":
        return headerRole(element);
      case "time":
        return "time";
      case "tr":
        return "row";
      default:
        return null;
    }
  }

  function headerRole(element) {
    const scope = (element.getAttribute("scope") ?? "").toLowerCase();
    if (scope === "row" || scope === "rowgroup") {
      return "rowheader";
    }

    if (scope === "col" || scope === "colgroup" || element.closest("thead")) {
      return "columnheader";
    }

    // A header cell first in a body row, with data cells after it, heads its row.
    const row = element.parentElement;
    const firstOfRow = row?.firstElementChild === element;
    const hasData = row ? [...row.children].some(cell => cell.localName === "td") : false;
    return firstOfRow && hasData ? "rowheader" : "columnheader";
  }

  const hasAuthorName = element =>
    element.hasAttribute("aria-label") || element.hasAttribute("aria-labelledby") || element.hasAttribute("title");

  const isFocusable = element => element.tabIndex >= 0 || element.hasAttribute("tabindex");

  /** The element's role: the first known token of `role`, presentational roles ignored on focusable elements. */
  function roleOf(element) {
    const explicit = element.getAttribute("role");
    if (explicit !== null) {
      for (const token of explicit.trim().toLowerCase().split(/\s+/)) {
        if (!knownRoles.has(token)) {
          continue;
        }

        if ((token === "none" || token === "presentation") && (isFocusable(element) || hasGlobalAria(element))) {
          return implicitRole(element);
        }

        return token;
      }
    }

    return implicitRole(element);
  }

  const hasGlobalAria = element =>
    element.hasAttribute("aria-label") || element.hasAttribute("aria-labelledby") || element.hasAttribute("aria-describedby");

  // ---- Hidden from the accessibility tree --------------------------------------------------------------------------

  /** One call's caches: computed styles and hidden state, so a query reads each element's style once. */
  function newCache() {
    return { styles: new Map(), excluded: new Map(), before: new Map(), after: new Map() };
  }

  function styleOf(element, cache) {
    let style = cache.styles.get(element);
    if (style === undefined) {
      style = getComputedStyle(element);
      cache.styles.set(element, style);
    }

    return style;
  }

  function parentOf(node) {
    if (node.parentElement) {
      return node.parentElement;
    }

    const root = node.parentNode;
    return root instanceof ShadowRoot ? root.host : null;
  }

  /** `aria-hidden`, `hidden`, `inert` or `display: none` on the element or an ancestor. */
  function excluded(element, cache) {
    const known = cache.excluded.get(element);
    if (known !== undefined) {
      return known;
    }

    const own = element.getAttribute("aria-hidden") === "true" || element.hidden || element.inert
      || styleOf(element, cache).display === "none";
    const parent = parentOf(element);
    const result = own || (parent !== null && excluded(parent, cache));
    cache.excluded.set(element, result);
    return result;
  }

  /** Hidden for assistive technology: excluded, or its own `visibility` (inherited, so a visible child of a hidden parent shows). */
  function hiddenForAria(element, cache) {
    if (excluded(element, cache)) {
      return true;
    }

    const visibility = styleOf(element, cache).visibility;
    return visibility === "hidden" || visibility === "collapse";
  }

  /** An element the devtools' tree lists: one with a role that is not presentational or generic, and not hidden. */
  function isTreeNode(element, cache) {
    const role = roleOf(element);
    return Boolean(role) && role !== "none" && role !== "presentation" && role !== "generic"
      && !hiddenForAria(element, cache);
  }

  // ---- Accessible name (Accessible Name 1.2, section 4.3.2) ---------------------------------------------------------

  // A hidden element's own name is computed as if it were shown (its hidden content too), as Chromium names the nodes
  // it leaves out of the tree, so a query that includes hidden elements can still match them by name. With `borrowed`
  // (a set), it collects the text nodes the name takes from other elements (labels, legends, captions, aria-labelledby),
  // which the snapshot then leaves out of those elements' own text.
  function accessibleName(element, cache, borrowed) {
    const hiddenAllowed = hiddenForAria(element, cache);
    const context = {
      cache, root: element, visited: new Set(), recursing: false, inLabelledBy: false, hiddenAllowed,
      borrowed: borrowed ?? null, borrowing: false,
    };
    return normalize(textAlternative(element, context));
  }

  function idReferences(element, attribute) {
    const ids = (element.getAttribute(attribute) ?? "").split(/\s+/).filter(id => id.length > 0);
    const root = element.getRootNode();
    return ids.map(id => (root.getElementById ? root.getElementById(id) : null) ?? document.getElementById(id)).filter(e => e);
  }

  function textAlternative(element, context) {
    if (context.visited.has(element)) {
      return "";
    }

    context.visited.add(element);
    const role = roleOf(element);

    // 2A: hidden, unless reached from aria-labelledby, which may name hidden content.
    if (!context.hiddenAllowed && hiddenForAria(element, context.cache)) {
      return "";
    }

    // 2B: aria-labelledby, not when already following one.
    if (!context.inLabelledBy) {
      const references = idReferences(element, "aria-labelledby");
      if (references.length > 0) {
        return references.map(reference => textAlternative(reference, {
          ...context,
          visited: new Set(),
          recursing: true,
          inLabelledBy: true,
          borrowing: !isTreeNode(reference, context.cache),
          hiddenAllowed: hiddenForAria(reference, context.cache),
        })).join(" ");
      }
    }

    // 2C/2E: a control embedded in another element's label contributes its value.
    const embedded = context.recursing && element !== context.root ? embeddedValue(element, role, context) : null;
    const label = normalize(element.getAttribute("aria-label"));
    if (label.length > 0 && embedded === null) {
      return label;
    }

    if (embedded !== null) {
      return embedded;
    }

    // 2D: the host language's own label, unless presentational.
    if (role !== "none" && role !== "presentation") {
      const native = nativeName(element, context);
      if (normalize(native).length > 0) {
        return native;
      }
    }

    // 2F: name from content, for roles that allow it and inside a traversal.
    if (nameFromContent.has(role) || context.recursing || element.localName === "label" || element.localName === "legend") {
      const content = fromContent(element, context);
      if (normalize(content).length > 0) {
        return content;
      }
    }

    // 2I: the tooltip.
    return element.getAttribute("title") ?? "";
  }

  // In the devtools' snapshot (`borrowed` given), a password field named into another element's name is redacted, as
  // its own value is.
  function embeddedValue(element, role, context) {
    switch (role) {
      case "textbox":
      case "searchbox":
        if (context.borrowed !== null && element instanceof HTMLInputElement && element.type === "password") {
          return element.value.length > 0 ? "(redacted)" : "";
        }

        return "value" in element ? String(element.value) : element.textContent ?? "";
      case "combobox":
      case "listbox":
        if (element.localName === "select") {
          return [...element.selectedOptions].map(option => option.label).join(" ");
        }

        if (element.localName === "input") {
          return element.value;
        }

        return element.querySelector("[aria-selected=true]")?.textContent ?? "";
      case "slider":
      case "spinbutton":
      case "progressbar":
      case "meter":
      case "scrollbar":
        return element.getAttribute("aria-valuetext") ?? element.getAttribute("aria-valuenow") ?? String(element.value ?? "");
      default:
        return null;
    }
  }

  function nativeName(element, context) {
    const name = element.localName;
    if (name === "input") {
      const type = (element.getAttribute("type") ?? "text").toLowerCase();
      if (type === "button" || type === "submit" || type === "reset") {
        return element.value || (type === "submit" ? "Submit" : type === "reset" ? "Reset" : "");
      }

      if (type === "image") {
        return element.getAttribute("alt") || element.value || "Submit";
      }
    }

    if (element.matches(labelable) && element.labels && element.labels.length > 0) {
      const text = [...element.labels]
        .map(label => textAlternative(label, {
          ...context,
          visited: new Set([element]),
          recursing: true,
          borrowing: !isTreeNode(label, context.cache),
        }))
        .join(" ");
      if (normalize(text).length > 0) {
        return text;
      }
    }

    switch (name) {
      case "img":
      case "area":
        return element.getAttribute("alt") ?? "";
      case "fieldset":
        return childName(element, ":scope > legend", context);
      case "figure":
        return childName(element, ":scope > figcaption", context);
      case "table":
        return childName(element, ":scope > caption", context);
      case "svg":
        return childName(element, ":scope > title", context);
      case "input":
      case "textarea":
        return element.hasAttribute("title") ? "" : element.getAttribute("placeholder") ?? "";
      default:
        return "";
    }
  }

  function childName(element, selector, context) {
    const child = element.querySelector(selector);
    const borrowing = child !== null && !isTreeNode(child, context.cache);
    return child ? textAlternative(child, { ...context, recursing: true, borrowing }) : "";
  }

  function pseudoContent(element, which, context) {
    const map = which === "::before" ? context.cache.before : context.cache.after;
    let text = map.get(element);
    if (text === undefined) {
      const content = getComputedStyle(element, which).content;
      const quoted = /^"(.*)"$/s.exec(content ?? "");
      text = quoted ? quoted[1] : "";
      map.set(element, text);
    }

    return text;
  }

  function isInline(element, context) {
    const display = styleOf(element, context.cache).display;
    return display === "inline" || display === "contents";
  }

  function fromContent(element, context) {
    let text = pseudoContent(element, "::before", context);
    const children = element.localName === "slot"
      ? element.assignedNodes({ flatten: true })
      : [...(element.shadowRoot ?? element).childNodes];
    for (const child of children) {
      if (child.nodeType === Node.TEXT_NODE) {
        text += child.textContent;
        if (context.borrowing && context.borrowed !== null) {
          context.borrowed.add(child);
        }

        continue;
      }

      if (child.nodeType !== Node.ELEMENT_NODE || skippedContent.has(child.tagName)) {
        continue;
      }

      const part = textAlternative(child, { ...context, recursing: true });
      text += isInline(child, context) ? part : " " + part + " ";
    }

    return text + pseudoContent(element, "::after", context);
  }

  // ---- State -----------------------------------------------------------------------------------------------------

  const isDisabled = element =>
    element.matches(":disabled") || element.closest("[aria-disabled=true]") !== null;

  function checkedState(element) {
    if (element.localName === "input" && (element.type === "checkbox" || element.type === "radio")) {
      return element.indeterminate ? "mixed" : element.checked;
    }

    const value = element.getAttribute("aria-checked");
    return value === "mixed" ? "mixed" : value === "true";
  }

  const pressedState = element => element.getAttribute("aria-pressed") === "true";

  // Undefined where the element says nothing, so `expanded: false` matches only elements that can expand.
  function expandedState(element) {
    if (element.localName === "details") {
      return element.open;
    }

    const value = element.getAttribute("aria-expanded");
    return value === null ? undefined : value === "true";
  }

  const selectedState = element =>
    element.localName === "option" ? element.selected : element.getAttribute("aria-selected") === "true";

  function levelOf(element) {
    const heading = /^h([1-6])$/.exec(element.localName);
    const aria = Number.parseInt(element.getAttribute("aria-level") ?? "", 10);
    return Number.isFinite(aria) ? aria : heading ? Number(heading[1]) : 0;
  }

  function isEditable(element) {
    if (isDisabled(element)) {
      return false;
    }

    if (element.localName === "input" || element.localName === "textarea") {
      return !element.readOnly && element.getAttribute("aria-readonly") !== "true";
    }

    if (element.localName === "select") {
      return true;
    }

    return element.isContentEditable && element.getAttribute("aria-readonly") !== "true";
  }

  /** Visible as the browser automation library defines it: a non-empty box, and `visibility: visible`. */
  function isVisible(element, cache) {
    if (!element.isConnected) {
      return false;
    }

    if (element.localName === "option") {
      const select = element.closest("select");
      return select !== null && isVisible(select, cache);
    }

    const box = element.getBoundingClientRect();
    return box.width > 0 && box.height > 0 && styleOf(element, cache).visibility === "visible";
  }

  /** The text a person reads: the rendered text where the element renders, its content otherwise. */
  function textOf(element) {
    if (element.localName === "input" && /^(button|submit|reset)$/.test(element.type)) {
      return normalize(element.value);
    }

    const rendered = typeof element.innerText === "string" && element.isConnected ? element.innerText : element.textContent;
    return normalize(rendered);
  }

  // ---- Matching and queries ------------------------------------------------------------------------------------

  /** A matcher for a string (substring and case-insensitive, or exact) or `{ source, flags }` for a regular expression. */
  function matcher(pattern, exact) {
    if (pattern !== null && typeof pattern === "object") {
      const expression = new RegExp(pattern.source, pattern.flags);
      return text => expression.test(normalize(text));
    }

    const wanted = normalize(pattern);
    if (exact) {
      return text => normalize(text) === wanted;
    }

    const lower = wanted.toLowerCase();
    return text => normalize(text).toLowerCase().includes(lower);
  }

  /** Every element under `root`, shadow roots included, in document order. */
  function* allElements(root) {
    const start = root.nodeType === Node.DOCUMENT_NODE ? root.documentElement : root;
    if (!start) {
      return;
    }

    const stack = [start];
    while (stack.length > 0) {
      const element = stack.pop();
      if (element !== root || root.nodeType === Node.DOCUMENT_NODE) {
        yield element;
      }

      const children = [...element.children];
      if (element.shadowRoot) {
        children.unshift(...element.shadowRoot.children);
      }

      for (let index = children.length - 1; index >= 0; index--) {
        stack.push(children[index]);
      }
    }
  }

  function byRole(root, step, cache) {
    const nameMatches = step.name === undefined || step.name === null ? null : matcher(step.name, step.exact === true);
    const found = [];
    for (const element of allElements(root)) {
      if (roleOf(element) !== step.role) {
        continue;
      }

      if (!step.includeHidden && hiddenForAria(element, cache)) {
        continue;
      }

      if (!statesMatch(element, step)) {
        continue;
      }

      if (nameMatches !== null && !nameMatches(accessibleName(element, cache))) {
        continue;
      }

      found.push(element);
    }

    return found;
  }

  function statesMatch(element, step) {
    if (step.checked !== undefined && checkedState(element) !== step.checked) {
      return false;
    }

    if (step.pressed !== undefined && pressedState(element) !== step.pressed) {
      return false;
    }

    if (step.expanded !== undefined && expandedState(element) !== step.expanded) {
      return false;
    }

    if (step.selected !== undefined && selectedState(element) !== step.selected) {
      return false;
    }

    if (step.level !== undefined && levelOf(element) !== step.level) {
      return false;
    }

    return step.disabled === undefined || isDisabled(element) === step.disabled;
  }

  /** A label's own text: its content without the controls inside it, which it labels rather than names. */
  function ownLabelText(label) {
    let text = "";
    for (const node of label.childNodes) {
      if (node.nodeType === Node.TEXT_NODE) {
        text += node.textContent;
      } else if (node.nodeType === Node.ELEMENT_NODE && !node.matches(labelable) && !skippedContent.has(node.tagName)) {
        text += " " + ownLabelText(node) + " ";
      }
    }

    return text;
  }

  /** Form controls by their label: a `<label>` (for or around them), `aria-labelledby` or `aria-label`. */
  function byLabel(root, step, cache) {
    const matches = matcher(step.text, step.exact === true);
    const found = new Set();
    for (const element of allElements(root)) {
      if (element.localName === "label" && element.control && matches(ownLabelText(element))) {
        found.add(element.control);
        continue;
      }

      if (element.hasAttribute("aria-labelledby")) {
        const text = idReferences(element, "aria-labelledby").map(reference => reference.textContent).join(" ");
        if (matches(text)) {
          found.add(element);
        }
      }

      if (element.hasAttribute("aria-label") && matches(element.getAttribute("aria-label"))) {
        found.add(element);
      }
    }

    return inDocumentOrder([...found].filter(element => step.includeHidden || !hiddenForAria(element, cache)));
  }

  /**
   * Elements by their text: the smallest elements whose text matches, so a match is the element a person sees the
   * text in, not its every ancestor.
   */
  function byText(root, step) {
    const matches = matcher(step.text, step.exact === true);
    const found = [];
    const visit = element => {
      if (skippedContent.has(element.tagName)) {
        return false;
      }

      // Only a subtree whose content holds the text can hold a match (a regular expression may match anywhere).
      const content = element.textContent ?? "";
      if (typeof step.text === "string" ? !containsText(content, step.text) : normalize(content).length === 0) {
        return false;
      }

      let child = false;
      for (const next of element.children) {
        child = visit(next) || child;
      }

      if (child) {
        return true;
      }

      if (matches(textOf(element))) {
        found.push(element);
        return true;
      }

      return false;
    };

    const start = root.nodeType === Node.DOCUMENT_NODE ? root.body ?? root.documentElement : root;
    if (start) {
      visit(start);
    }

    return found;
  }

  const containsText = (text, wanted) => normalize(text).toLowerCase().includes(normalize(wanted).toLowerCase());

  function byAttribute(root, step, attribute) {
    const matches = matcher(step.text, step.exact === true);
    return [...allElements(root)].filter(element => element.hasAttribute(attribute) && matches(element.getAttribute(attribute)));
  }

  function inDocumentOrder(elements) {
    return elements.sort((a, b) => (a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING ? -1 : 1));
  }

  function query(root, step, cache) {
    switch (step.kind) {
      case "role":
        return byRole(root, step, cache);
      case "label":
        return byLabel(root, step, cache);
      case "text":
        return byText(root, step);
      case "placeholder":
        return byAttribute(root, step, "placeholder");
      case "alt":
        return byAttribute(root, step, "alt");
      case "title":
        return byAttribute(root, step, "title");
      case "css":
        return [...root.querySelectorAll(step.selector)];
      case "ref":
        return byReference(root, step);
      default:
        throw new Error("Unknown locator step: " + step.kind);
    }
  }

  function applyFilter(elements, step, cache) {
    return elements.filter(element => {
      if (step.hasText !== undefined && !matcher(step.hasText, false)(textOf(element))) {
        return false;
      }

      if (step.hasNotText !== undefined && matcher(step.hasNotText, false)(textOf(element))) {
        return false;
      }

      if (step.has !== undefined && resolveFrom([element], step.has, cache).length === 0) {
        return false;
      }

      return step.hasNot === undefined || resolveFrom([element], step.hasNot, cache).length === 0;
    });
  }

  function resolveFrom(roots, chain, cache) {
    let current = roots;
    for (const step of chain) {
      if (step.kind === "nth") {
        const index = step.index < 0 ? current.length + step.index : step.index;
        current = index >= 0 && index < current.length ? [current[index]] : [];
        continue;
      }

      if (step.kind === "filter") {
        current = applyFilter(current, step, cache);
        continue;
      }

      const next = new Set();
      for (const root of current) {
        for (const element of query(root, step, cache)) {
          next.add(element);
        }
      }

      current = inDocumentOrder([...next]);
    }

    return current;
  }

  const resolve = (chain, cache) => resolveFrom([document], chain, cache);

  // ---- Describing elements for failure messages ------------------------------------------------------------------

  function describe(element, cache) {
    const role = roleOf(element);
    const name = role ? accessibleName(element, cache) : "";
    const text = textOf(element);
    const shown = name || text;
    const tag = element.id ? `<${element.localName} id="${element.id}">` : `<${element.localName}>`;
    const what = role ? `${role} ${tag}` : tag;
    return shown ? `${what} "${shorten(shown)}"` : what;
  }

  const shorten = text => (text.length > 60 ? text.slice(0, 57) + "…" : text);

  /**
   * Why a chain finds nothing, and what is near: the step where the matches ran out, and up to five elements that
   * a person might have meant (the same role with other names, or the same name with other roles).
   */
  function explain(chain, cache) {
    let current = [document];
    for (let index = 0; index < chain.length; index++) {
      const step = chain[index];
      const next = resolveFrom(current, [step], cache);
      if (next.length === 0) {
        return { step: index, near: nearCandidates(current, step, cache) };
      }

      current = next;
    }

    return { step: -1, near: [] };
  }

  function nearCandidates(roots, step, cache) {
    const near = [];
    const add = element => {
      if (near.length < 5 && !hiddenForAria(element, cache)) {
        near.push(describe(element, cache));
      }
    };

    for (const root of roots) {
      for (const element of allElements(root)) {
        if (near.length >= 5) {
          return near;
        }

        if (step.kind === "role" && roleOf(element) === step.role) {
          add(element);
        } else if (step.kind === "role" && typeof step.name === "string" && roleOf(element)
          && containsText(accessibleName(element, cache), step.name)) {
          add(element);
        } else if (step.kind === "label" && element.matches(labelable)) {
          add(element);
        } else if (step.kind === "text" && typeof step.text === "string" && element.children.length === 0
          && containsText(element.textContent, step.text.slice(0, Math.max(3, Math.floor(step.text.length / 2))))) {
          add(element);
        }
      }
    }

    return near;
  }

  /** The single element of a chain (strict mode), or why there is not exactly one. */
  function single(chain, cache) {
    const found = resolve(chain, cache);
    if (found.length === 1) {
      return { element: found[0] };
    }

    if (found.length === 0) {
      // A snapshot reference names one element; once it left the document, waiting does not bring it back.
      const reference = chain.find(step => step.kind === "ref");
      if (reference !== undefined) {
        return {
          stale: references.has(reference.ref)
            ? reference.ref + " is no longer in the page; take tree again"
            : reference.ref + " is not a reference of the last tree (e1 to e" + references.size + "); take tree again",
        };
      }

      return { retry: "no element matches", ...explain(chain, cache) };
    }

    return { strict: found.length, matches: found.slice(0, 5).map(element => describe(element, cache)) };
  }

  // ---- Actionability and the pointer watcher ---------------------------------------------------------------------

  const nextFrame = () => new Promise(resolveFrame => requestAnimationFrame(() => resolveFrame()));

  function hitTarget(x, y) {
    let hit = document.elementFromPoint(x, y);
    while (hit?.shadowRoot) {
      const inner = hit.shadowRoot.elementFromPoint(x, y);
      if (!inner || inner === hit) {
        break;
      }

      hit = inner;
    }

    return hit;
  }

  function contains(element, node) {
    for (let current = node; current; current = current.parentNode instanceof ShadowRoot ? current.parentNode.host : current.parentNode) {
      if (current === element) {
        return true;
      }
    }

    return false;
  }

  function inViewport(box) {
    return box.top >= 0 && box.left >= 0 && box.bottom <= innerHeight && box.right <= innerWidth;
  }

  // Whether the element is in the viewport and not cut off by a scrolling ancestor (a box inside the window can still
  // be scrolled out of its container's view, where the container, not the element, would get the click).
  function inView(element) {
    const box = element.getBoundingClientRect();
    if (!inViewport(box)) {
      return false;
    }

    for (let parent = element.parentElement; parent !== null && parent !== document.body && parent !== document.documentElement; parent = parent.parentElement) {
      const style = getComputedStyle(parent);
      if (style.overflowX === "visible" && style.overflowY === "visible") {
        continue;
      }

      const clip = parent.getBoundingClientRect();
      if (box.top < clip.top || box.left < clip.left || box.bottom > clip.bottom || box.right > clip.right) {
        return false;
      }
    }

    return true;
  }

  /** The point a click aims at: the middle of the part of the box inside the viewport. */
  function clickPoint(box) {
    const left = Math.max(box.left, 0);
    const top = Math.max(box.top, 0);
    const right = Math.min(box.right, innerWidth);
    const bottom = Math.min(box.bottom, innerHeight);
    return { x: (left + right) / 2, y: (top + bottom) / 2 };
  }

  /**
   * Whether a running animation or transition moves the element or one of its ancestors: one frame of the same box can
   * fall between two frames of an animation that has started, or just before one starts. A finished or paused one,
   * and one the element does not sit in, does not count.
   */
  function isAnimating(element) {
    if (typeof document.getAnimations !== "function") {
      return false;
    }

    for (const animation of document.getAnimations()) {
      const target = animation.effect && animation.effect.target;
      if (animation.playState === "running" && target instanceof Element && contains(target, element)) {
        return true;
      }
    }

    return false;
  }

  const sameBox = (a, b) => a.x === b.x && a.y === b.y && a.width === b.width && a.height === b.height;

  /**
   * The checks before an action, in the browser automation library's order: one element (strict), attached,
   * visible, enabled, editable, scrolled into view, stable over a frame, and hit at its click point. A check that
   * may pass later answers `retry`; strict mode answers `strict`, which never retries.
   */
  async function actionable(request) {
    const cache = newCache();
    const found = single(request.chain, cache);
    if (!found.element) {
      return found;
    }

    const element = found.element;
    const name = describe(element, cache);
    if (!isVisible(element, cache)) {
      return { retry: name + ": not visible" };
    }

    if (request.enabled && isDisabled(element)) {
      return { retry: name + ": not enabled" };
    }

    if (request.editable && !isEditable(element)) {
      return { retry: name + ": not editable" };
    }

    if (!inView(element)) {
      element.scrollIntoView({ block: "center", inline: "center", behavior: "instant" });
    }

    if (!request.pointer) {
      return { ok: true, element: name };
    }

    const before = element.getBoundingClientRect();
    await nextFrame();
    const after = element.getBoundingClientRect();
    if (!element.isConnected || !sameBox(before, after) || isAnimating(element)) {
      return { retry: name + ": not stable", unstable: true };
    }

    const point = clickPoint(after);
    const hit = hitTarget(point.x, point.y);
    if (!hit || !contains(element, hit)) {
      return { retry: name + ": " + (hit ? describe(hit, cache) + " would receive the click" : "nothing is at its click point") };
    }

    armPointer(element);
    return { ok: true, element: name, x: point.x, y: point.y };
  }

  // Trusted input as the page receives it, counted, so a host whose input travels outside the page (XTEST through the
  // X server on Linux) can wait until a key or pointer event it sent has arrived, as a DevTools input call does on
  // Windows. Untrusted events (the page's own dispatches) are not counted. Each type is counted too, so a key waits for
  // its own keydown or keyup: a pointer event the engine did not send (WebKit's mousemove after a layout under a still
  // pointer) counts as input and once answered for a key that had not arrived.
  let inputs = 0;
  const inputsByType = new Map();
  const inputWaiters = new Set();
  const countInput = event => {
    if (!event.isTrusted) {
      return;
    }

    inputs++;
    inputsByType.set(event.type, (inputsByType.get(event.type) ?? 0) + 1);
    for (const waiter of inputWaiters) {
      waiter();
    }
  };

  for (const type of ["keydown", "keyup", "pointerdown", "pointerup", "pointermove", "mousemove", "wheel"]) {
    addEventListener(type, countInput, true);
  }

  /**
   * Resolves once the document has the focus, or after `timeout` milliseconds; answers whether it has. A key sent to a
   * window that has just been given the X focus, before GTK and WebKit have handled the focus, never reached the page
   * (measured in the CI container: Control of Control+End, so End alone moved the caret).
   */
  function focused(request) {
    if (document.hasFocus()) {
      return { focused: true };
    }

    return new Promise(resolve => {
      const started = performance.now();
      const check = () => {
        if (document.hasFocus() || performance.now() - started >= (request.timeout ?? 1000)) {
          resolve({ focused: document.hasFocus() });
          return;
        }

        setTimeout(check, 5);
      };
      check();
    });
  }

  /**
   * Resolves once more than `after` trusted inputs (of `type`, when given) arrived, or after `timeout` milliseconds;
   * answers whether they did, and whether the document had the focus.
   */
  function inputAfter(request) {
    const after = request.after ?? 0;
    const counted = () => (request.type ? inputsByType.get(request.type) ?? 0 : inputs);
    if (counted() > after) {
      return { seen: true, count: counted() };
    }

    return new Promise(resolve => {
      let answered = false;
      const done = seen => {
        if (answered) {
          return;
        }

        answered = true;
        inputWaiters.delete(check);
        clearTimeout(timer);
        resolve({ seen, count: counted(), focused: document.hasFocus() });
      };
      // Answered once the task that delivered the event has ended, its default action (typing, focus) included.
      const check = () => {
        if (counted() > after) {
          inputWaiters.delete(check);
          setTimeout(() => done(true), 0);
        }
      };
      const timer = setTimeout(() => done(false), request.timeout ?? 1000);
      inputWaiters.add(check);
    });
  }

  /** Text inserted where the focus is, as an input method does: trusted `beforeinput` and `input`, no key events. */
  function insertText(request) {
    return { ok: document.execCommand("insertText", false, request.text ?? "") };
  }

  // The armed watcher: the next press must land on the element. If it does not (the page moved something over it
  // between the check and the input), the press and the click it would cause are stopped before the page sees them,
  // and the action is tried again, as the browser automation library does. A press that landed but whose click does
  // not reach the element, which is still in the page, means the element moved between the press and the release (an
  // animation that started after the stability check): that click, on whatever was under the release, is stopped too,
  // and the action is tried again. An element the page removed on the press or the release is no miss, and only the
  // first click counts: a label's click on its control comes after it.
  // Its listeners are registered once, when the engine is installed at the document's creation, before any page
  // script: listeners on one target run in the order they were added, so stopping an event here keeps it from every
  // listener of the page, its own capturing ones on the window included. They do nothing while no action is armed.
  let armed = null;

  const stopIfMissed = event => {
    if (armed !== null && armed.seen && !armed.onTarget) {
      event.preventDefault();
      event.stopImmediatePropagation();
    }
  };

  const onDown = event => {
    const watch = armed;
    if (watch === null) {
      return;
    }

    if (watch.seen) {
      stopIfMissed(event);
      return;
    }

    watch.seen = true;
    const target = event.composedPath()[0];
    watch.onTarget = contains(watch.element, target);
    if (!watch.onTarget) {
      watch.hit = target instanceof Element ? describe(target, newCache()) : String(target);
      stopIfMissed(event);
    }
  };

  const onClick = event => {
    const watch = armed;
    if (watch === null) {
      return;
    }

    stopIfMissed(event);
    if (!watch.seen || !watch.onTarget || watch.clicks++ > 0) {
      return;
    }

    if (contains(watch.element, event.composedPath()[0])) {
      watch.clicked = true;
    } else if (watch.element.isConnected) {
      watch.moved = true;
      event.preventDefault();
      event.stopImmediatePropagation();
    }
  };

  for (const [type, listener] of [["pointerdown", onDown], ["mousedown", stopIfMissed], ["pointerup", stopIfMissed],
    ["mouseup", stopIfMissed], ["click", onClick]]) {
    addEventListener(type, listener, true);
  }

  function armPointer(element) {
    armed = { element, seen: false, onTarget: false, hit: null, clicked: false, moved: false, clicks: 0 };
  }

  function disarm() {
    armed = null;
  }

  /** After the input: whether the press landed on the element; disarms the watcher. */
  function landed() {
    const watch = armed;
    disarm();
    if (watch === null) {
      return { armed: false, seen: false, onTarget: false };
    }

    return { armed: true, seen: watch.seen, onTarget: watch.onTarget, hit: watch.hit, clicked: watch.clicked, moved: watch.moved };
  }

  // ---- The other operations ------------------------------------------------------------------------------------

  function state(request) {
    const cache = newCache();
    const found = resolve(request.chain, cache);
    const result = { count: found.length };
    if (found.length > 1) {
      result.matches = found.slice(0, 5).map(element => describe(element, cache));
    }

    if (found.length !== 1) {
      result.texts = request.all ? found.map(textOf) : undefined;
      return result;
    }

    const element = found[0];
    const checked = checkedState(element);
    result.element = describe(element, cache);
    result.visible = isVisible(element, cache);
    result.enabled = !isDisabled(element);
    result.editable = isEditable(element);
    result.checked = checked === true;
    result.mixed = checked === "mixed";
    result.focused = element === deepActiveElement();
    result.text = textOf(element);
    result.value = /^(input|textarea|select)$/.test(element.localName) ? String(element.value) : null;
    result.role = roleOf(element);
    result.name = result.role ? accessibleName(element, cache) : "";
    if (request.attribute) {
      result.attribute = element.getAttribute(request.attribute);
    }

    return result;
  }

  function deepActiveElement() {
    let active = document.activeElement;
    while (active?.shadowRoot?.activeElement) {
      active = active.shadowRoot.activeElement;
    }

    return active;
  }

  /** Focuses the element; with `selectAll`, selects its text, so typed text replaces it. */
  function focus(request) {
    const cache = newCache();
    const found = single(request.chain, cache);
    if (!found.element) {
      return found;
    }

    const element = found.element;
    element.focus({ preventScroll: false });
    if (request.selectAll) {
      selectContents(element);
    }

    const active = deepActiveElement();
    return active === element || contains(element, active) ? { ok: true } : { retry: describe(element, cache) + ": cannot take the focus" };
  }

  function selectContents(element) {
    if (typeof element.select === "function" && element.localName !== "select") {
      element.select();
      return;
    }

    if (element.isContentEditable) {
      const range = document.createRange();
      range.selectNodeContents(element);
      const selection = getSelection();
      selection.removeAllRanges();
      selection.addRange(range);
    }
  }

  // Input types whose value cannot be typed as text: the library sets their value and fires input and change.
  const valueTypes = new Set(["color", "date", "datetime-local", "month", "range", "time", "week"]);

  /** Before a fill: the element is checked as for an action, focused and its text selected; some types are set here. */
  async function prepareFill(request) {
    const check = await actionable({ chain: request.chain, enabled: true, editable: true, pointer: false });
    if (!check.ok) {
      return check;
    }

    const cache = newCache();
    const element = single(request.chain, cache).element;
    if (element.localName === "input" && valueTypes.has(element.type)) {
      element.focus();
      element.value = request.value;
      if (element.value !== request.value) {
        return { error: `${describe(element, cache)} does not accept the value "${request.value}"` };
      }

      element.dispatchEvent(new Event("input", { bubbles: true, composed: true }));
      element.dispatchEvent(new Event("change", { bubbles: true }));
      return { ok: true, done: true, element: check.element };
    }

    if (element.localName !== "input" && element.localName !== "textarea" && !element.isContentEditable) {
      return { error: `${describe(element, cache)} is not a text field, a text area or editable content` };
    }

    element.focus();
    selectContents(element);
    return { ok: true, done: false, element: check.element };
  }

  /** Selects options of a `<select>` by value or label and fires input and change, as the library does. */
  async function selectOption(request) {
    const check = await actionable({ chain: request.chain, enabled: true, pointer: false });
    if (!check.ok) {
      return check;
    }

    const cache = newCache();
    const element = single(request.chain, cache).element;
    if (element.localName !== "select") {
      return { error: `${describe(element, cache)} is not a <select>` };
    }

    const options = [...element.options];
    const picked = request.values.map(wanted => options.find(option => option.value === wanted || option.label === wanted));
    if (picked.some(option => option === undefined)) {
      return { retry: `${describe(element, cache)} has no option ${request.values.map(v => `"${v}"`).join(", ")}; its options are ${options.map(o => `"${o.label}"`).join(", ")}` };
    }

    if (!element.multiple && picked.length > 1) {
      return { error: `${describe(element, cache)} takes one option` };
    }

    for (const option of options) {
      option.selected = picked.includes(option);
    }

    element.dispatchEvent(new Event("input", { bubbles: true, composed: true }));
    element.dispatchEvent(new Event("change", { bubbles: true }));
    return { ok: true, selected: picked.map(option => option.value) };
  }

  /** The element's box in CSS pixels of the viewport, after scrolling it into view (element screenshots). */
  function box(request) {
    const cache = newCache();
    const found = single(request.chain, cache);
    if (!found.element) {
      return found;
    }

    const element = found.element;
    if (!isVisible(element, cache)) {
      return { retry: describe(element, cache) + ": not visible" };
    }

    if (!inView(element)) {
      element.scrollIntoView({ block: "center", inline: "center", behavior: "instant" });
    }

    const rect = element.getBoundingClientRect();
    return { ok: true, x: rect.x, y: rect.y, width: rect.width, height: rect.height };
  }

  // ---- References (the devtools' snapshot) ---------------------------------------------------------------------

  // The last snapshot's elements by reference ("e12"), held weakly in this world, which is the document's own: a new
  // document starts with none, so a reference never reaches an element of another document.
  let references = new Map();

  /** The referenced element when it is still in the document and under `root`; otherwise none. */
  function byReference(root, step) {
    const element = references.get(step.ref)?.deref();
    return element !== undefined && element.isConnected && (root === document || root.contains(element)) ? [element] : [];
  }

  /** A form field's value for the snapshot: a password's is never given. */
  function fieldValue(element) {
    if (element instanceof HTMLInputElement) {
      if (element.type === "password") {
        return element.value.length > 0 ? "(redacted)" : "";
      }

      return ["checkbox", "radio", "button", "submit", "reset", "file", "image", "hidden"].includes(element.type) ? undefined : element.value;
    }

    if (element instanceof HTMLTextAreaElement || element instanceof HTMLSelectElement) {
      return element.value;
    }

    return element.isContentEditable ? textOf(element) : undefined;
  }

  /** A value or a text as the snapshot gives it: at most 200 characters, then " …". */
  const cut = text => text.length > 200 ? text.slice(0, 200) + " …" : text;

  /**
   * The devtools' tree: every element exposed to assistive technology, in document order, with its depth among them,
   * a reference for later actions, its text where its name does not show it, and the states a reader of the page
   * needs. At most `limit` elements.
   */
  function snapshot(request) {
    const cache = newCache();
    const limit = request.limit ?? 2000;
    const listed = [];
    const depths = new Map();
    const borrowed = new Set();
    references = new Map();
    let truncated = false;
    for (const element of allElements(document)) {
      if (!isTreeNode(element, cache)) {
        continue;
      }

      if (listed.length === limit) {
        truncated = true;
        break;
      }

      let parent = element.parentElement;
      while (parent !== null && !depths.has(parent)) {
        parent = parent.parentElement;
      }

      const depth = parent === null ? 0 : depths.get(parent) + 1;
      depths.set(element, depth);
      const ref = "e" + (listed.length + 1);
      references.set(ref, new WeakRef(element));
      listed.push({ element, ref, role: roleOf(element), name: accessibleName(element, cache, borrowed), depth });
    }

    // Every name first, so the text a later element's label borrows is known before the text around it is read.
    const nodes = [];
    const inline = new Set();
    for (const { element, ref, role, name, depth } of listed) {
      const node = { ref, role, name };
      const value = fieldValue(element);
      if (!inline.has(element)) {
        // Read even where it is not shown, so a text-level element inside a button or a field shows no text either.
        const text = ownText(element, cache, borrowed, inline);
        if (text !== "" && text !== name && value === undefined && !childrenPresentational.has(role)) {
          node.text = cut(text);
        }
      }

      node.depth = depth;
      if (value !== undefined && value !== "") {
        node.value = cut(value);
      }

      for (const [key, state] of Object.entries(states(element))) {
        if (state !== undefined) {
          node[key] = state;
        }
      }

      nodes.push(node);
    }

    return { nodes, truncated, title: document.title, url: location.href };
  }

  /**
   * The text an element shows that no other line of the tree does: its rendered text content, in order, without the
   * content of the elements listed under it (the text-level ones shown inline excepted: a link or an emphasis is part
   * of its sentence, so its text stays here and `inline` collects it) and without the text that names another element
   * (`borrowed`: a label's, a legend's). Hidden content is left out; a field's value never is text. Read only until
   * there is more than the snapshot gives.
   */
  function ownText(element, cache, borrowed, inline) {
    let text = "";
    const visit = node => {
      const children = node.localName === "slot"
        ? node.assignedNodes({ flatten: true })
        : [...(node.shadowRoot ?? node).childNodes];
      for (const child of children) {
        if (text.length > 1000) {
          return;
        }

        if (child.nodeType === Node.TEXT_NODE) {
          if (!borrowed.has(child) && !hiddenForAria(child.parentElement ?? node, cache)) {
            text += child.data;
          }

          continue;
        }

        if (child.nodeType !== Node.ELEMENT_NODE || notText.has(child.localName) || excluded(child, cache)) {
          continue;
        }

        if (child.localName === "br") {
          text += " ";
          continue;
        }

        const shownInline = isInline(child, { cache });
        if (isTreeNode(child, cache)) {
          if (!shownInline || !textLevel.has(roleOf(child))) {
            continue;
          }

          inline.add(child);
        }

        text += shownInline ? "" : " ";
        visit(child);
        text += shownInline ? "" : " ";
      }
    };

    visit(element);
    return normalize(text);
  }

  /** The states a snapshot shows, each only where the element has it. */
  function states(element) {
    const aria = name => element.getAttribute("aria-" + name);
    const checked = element instanceof HTMLInputElement && (element.type === "checkbox" || element.type === "radio")
      ? element.checked
      : aria("checked") === null ? undefined : aria("checked") === "true";
    return {
      checked,
      disabled: element.matches(":disabled") || aria("disabled") === "true" ? true : undefined,
      expanded: aria("expanded") === null ? undefined : aria("expanded") === "true",
      selected: aria("selected") === "true" ? true : undefined,
      pressed: aria("pressed") === null ? undefined : aria("pressed") === "true",
      level: /^H[1-6]$/.test(element.tagName) ? Number(element.tagName[1]) : aria("level") === null ? undefined : Number(aria("level")),
      focused: document.activeElement === element ? true : undefined,
    };
  }

  /**
   * A click made of the page's own events, where the host has no trusted input on the user's desktop (Linux): the
   * element made actionable first, then the pointer and mouse events and the click at its click point. The page sees
   * them as untrusted (`isTrusted` false), which the devtools' answer says.
   */
  async function syntheticClick(request) {
    const ready = await actionable({ chain: request.chain, enabled: true, editable: false, pointer: true });
    if (!ready.ok) {
      return ready;
    }

    const element = single(request.chain, newCache()).element;
    const point = { bubbles: true, cancelable: true, composed: true, clientX: ready.x, clientY: ready.y, button: 0, view: window };
    element.dispatchEvent(new PointerEvent("pointerdown", { ...point, buttons: 1, pointerType: "mouse", isPrimary: true }));
    element.dispatchEvent(new MouseEvent("mousedown", { ...point, buttons: 1 }));
    if (typeof element.focus === "function") {
      element.focus();
    }

    element.dispatchEvent(new PointerEvent("pointerup", { ...point, buttons: 0, pointerType: "mouse", isPrimary: true }));
    element.dispatchEvent(new MouseEvent("mouseup", { ...point, buttons: 0 }));
    element.dispatchEvent(new MouseEvent("click", { ...point, buttons: 0, detail: 1 }));

    // The watcher actionable() armed waits for trusted input, which these events are not.
    disarm();
    return { ok: true, element: ready.element, x: ready.x, y: ready.y, synthetic: true };
  }

  /** Every element exposed to assistive technology with its role and name (checks against the platform's tree). */
  function tree() {
    const cache = newCache();
    const nodes = [];
    for (const element of allElements(document)) {
      if (isTreeNode(element, cache)) {
        nodes.push({ role: roleOf(element), name: accessibleName(element, cache), tag: element.localName });
      }
    }

    return { nodes };
  }

  const operations = {
    count: request => ({ count: resolve(request.chain, newCache()).length }),
    state,
    explain: request => explain(request.chain, newCache()),
    actionable,
    landed,
    inputCount: request => ({ count: request.type ? inputsByType.get(request.type) ?? 0 : inputs, focused: document.hasFocus() }),
    focused,
    inputAfter,
    insertText,
    focus,
    prepareFill,
    selectOption,
    box,
    tree,
    snapshot,
    syntheticClick,
    frames: () => nextFrame().then(nextFrame).then(() => ({ ok: true })),
  };

  /** One request, as JSON text; the answer is a plain object. An exception answers `error` with its message. */
  async function run(requestText) {
    try {
      const request = JSON.parse(requestText);
      const operation = operations[request.op];
      if (operation === undefined) {
        return { error: "Unknown operation: " + request.op };
      }

      return await operation(request);
    } catch (exception) {
      return { error: String(exception?.message ?? exception) };
    }
  }

  Object.defineProperty(globalThis, "__sparAutomation", {
    value: Object.freeze({ run, roleOf, accessibleName: element => accessibleName(element, newCache()) }),
  });
})();
