namespace Spar.WindowAutomation.Automation;

/// <summary>The page-side functions, run through <c>Runtime.callFunctionOn</c> with <c>this</c> bound to an element or the document.</summary>
internal static class Scripts
{
    private const string Normalize = "const norm = s => (s || '').replace(/\\s+/g, ' ').trim();";

    private const string Match = "const match = s => exact ? norm(s) === text : norm(s).toLowerCase().includes(text.toLowerCase());";

    /// <summary>Form controls labelled by a label's own text, <c>aria-label</c> or <c>aria-labelledby</c>.</summary>
    public const string ByLabel = "function(text, exact) {" + Normalize + Match + """
          const found = new Set();
          for (const label of this.querySelectorAll('label')) {
            let own = '';
            for (const n of label.childNodes) {
              if (n.nodeType === 3 || !(n.matches && n.matches('input,select,textarea,button,[role]'))) own += n.textContent;
            }
            if (label.control && match(own)) found.add(label.control);
          }
          for (const e of this.querySelectorAll('[aria-label]')) if (match(e.getAttribute('aria-label'))) found.add(e);
          for (const e of this.querySelectorAll('[aria-labelledby]')) {
            const t = e.getAttribute('aria-labelledby').split(/\s+/).map(id => document.getElementById(id)?.textContent || '').join(' ');
            if (match(t)) found.add(e);
          }
          return [...found];
        }
        """;

    /// <summary>The smallest elements whose text contains (or equals) <c>text</c>.</summary>
    public const string ByText = "function(text, exact) {" + Normalize + Match + """
          const skip = new Set(['SCRIPT', 'STYLE', 'TEMPLATE', 'HEAD', 'NOSCRIPT']);
          const found = [];
          const contains = s => norm(s).toLowerCase().includes(text.toLowerCase());
          // Descends while the text is contained; keeps the deepest elements that match (equal, when exact).
          const walk = e => {
            if (skip.has(e.tagName) || !contains(e.textContent)) return false;
            let child = false;
            for (const c of e.children) child = walk(c) || child;
            if (child) return true;
            if (match(e.textContent)) { found.push(e); return true; }
            return false;
          };
          walk(this.nodeType === 9 ? this.documentElement : this);
          return found;
        }
        """;

    public const string ByTestId = "function(id) { return [...this.querySelectorAll('[data-testid=\"' + CSS.escape(id) + '\"]')]; }";

    public const string ByCss = "function(selector) { return [...this.querySelectorAll(selector)]; }";

    /// <summary>
    /// Playwright-style actionability in one call: attached, visible, enabled (and editable), scrolled into view,
    /// the same box at the next animation frame, and the click point hits the element (shadow roots included).
    /// </summary>
    public const string Actionable = """
        function(needEnabled, needEditable, pointer) {
          const e = this;
          if (!e.isConnected) return { state: 'detached' };
          const style = getComputedStyle(e);
          let r = e.getBoundingClientRect();
          if (r.width === 0 || r.height === 0 || style.visibility === 'hidden') return { state: 'not visible' };
          if (needEnabled && (e.matches(':disabled') || e.closest('[aria-disabled="true"]'))) return { state: 'not enabled' };
          if (needEditable && (e.readOnly || e.getAttribute('aria-readonly') === 'true')) return { state: 'not editable' };
          if (r.top < 0 || r.left < 0 || r.bottom > innerHeight || r.right > innerWidth) e.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' });
          if (!pointer) return { state: 'ok' };
          // Stable: the box now and at the next animation frame are the same.
          const a = e.getBoundingClientRect();
          return new Promise(resolve => {
            requestAnimationFrame(() => {
              const b = e.getBoundingClientRect();
              if (a.x !== b.x || a.y !== b.y || a.width !== b.width || a.height !== b.height) return resolve({ state: 'not stable' });
              const x = b.x + b.width / 2, y = b.y + b.height / 2;
              let hit = document.elementFromPoint(x, y);
              while (hit && hit.shadowRoot) { const inner = hit.shadowRoot.elementFromPoint(x, y); if (!inner || inner === hit) break; hit = inner; }
              for (let n = hit; n; n = n.parentNode instanceof ShadowRoot ? n.parentNode.host : n.parentNode) {
                if (n === e) return resolve({ state: 'ok', x, y });
              }
              const d = hit ? '<' + hit.tagName.toLowerCase() + (hit.id ? '#' + hit.id : '') + (hit.className && typeof hit.className === 'string' ? ' class="' + hit.className.slice(0, 60) + '"' : '') + '>' : 'nothing';
              resolve({ state: 'click point hits ' + d });
            });
          });
        }
        """;

    /// <summary>What an assertion reads from one element.</summary>
    public const string State = """
        function() {
          const e = this;
          const style = getComputedStyle(e);
          const r = e.getBoundingClientRect();
          return {
            visible: e.isConnected && r.width > 0 && r.height > 0 && style.visibility !== 'hidden',
            enabled: !(e.matches(':disabled') || e.closest('[aria-disabled="true"]')),
            checked: (e.type === 'checkbox' || e.type === 'radio') ? e.checked : e.getAttribute('aria-checked') === 'true',
            text: (e.textContent || '').replace(/\s+/g, ' ').trim(),
            value: 'value' in e ? String(e.value) : null,
          };
        }
        """;

    public const string Describe = """
        function() {
          const e = this;
          const cls = typeof e.className === 'string' && e.className ? ' class="' + e.className.slice(0, 40) + (e.className.length > 40 ? '…' : '') + '"' : '';
          const text = (e.textContent || '').replace(/\s+/g, ' ').trim();
          return '<' + e.tagName.toLowerCase() + (e.id ? ' id="' + e.id + '"' : '') + cls + '>' + (text ? ' "' + text.slice(0, 40) + '"' : '');
        }
        """;

    public const string FocusAndSelectAll = """
        function() {
          this.focus();
          if (typeof this.select === 'function' && this.tagName !== 'SELECT') this.select();
          else if (this.isContentEditable) { const range = document.createRange(); range.selectNodeContents(this); const s = getSelection(); s.removeAllRanges(); s.addRange(range); }
          return document.activeElement === this;
        }
        """;

    public const string Focus = "function() { this.focus(); return document.activeElement === this; }";

    /// <summary>Selects options by value or label and fires <c>input</c> and <c>change</c>, as the library does (not trusted input).</summary>
    public const string SelectOption = """
        function(wanted) {
          if (this.tagName !== 'SELECT') return { error: 'not a <select>' };
          const options = [...this.options];
          const picked = wanted.map(w => options.find(o => o.value === w || o.label === w));
          if (picked.some(o => !o)) return { error: 'options are ' + options.map(o => '"' + o.label + '"').join(', ') };
          for (const o of options) o.selected = picked.includes(o);
          this.dispatchEvent(new Event('input', { bubbles: true }));
          this.dispatchEvent(new Event('change', { bubbles: true }));
          return { selected: picked.map(o => o.value) };
        }
        """;

    public const string BoundingBox = "function() { const r = this.getBoundingClientRect(); return [r.x + scrollX, r.y + scrollY, r.width, r.height]; }";

    public const string TwoFrames = "new Promise(r => requestAnimationFrame(() => requestAnimationFrame(() => r(true))))";
}
