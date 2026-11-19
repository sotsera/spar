// Usage: node tools/gen-webview.mjs <WebView2.h> — prints the flat ICoreWebView2_22 declaration used by the spike:
// every slot from 3 to 123, named methods where the spike calls them, [PreserveSig] placeholders elsewhere.
const named = {
  3: 'ICoreWebView2Settings8 GetSettings();',
  5: 'void Navigate(string uri);',
  15: 'EventRegistrationToken AddNavigationCompleted(ICoreWebView2NavigationCompletedEventHandler eventHandler);',
  16: 'void RemoveNavigationCompleted(EventRegistrationToken token);',
  23: 'EventRegistrationToken AddPermissionRequested(ICoreWebView2PermissionRequestedEventHandler eventHandler);',
  31: 'void Reload();',
  36: 'void CallDevToolsProtocolMethod(string methodName, string parametersAsJson, ICoreWebView2CallDevToolsProtocolMethodCompletedHandler handler);',
  37: 'uint GetBrowserProcessId();',
  42: 'ICoreWebView2DevToolsProtocolEventReceiver GetDevToolsProtocolEventReceiver(string eventName);',
  44: 'EventRegistrationToken AddNewWindowRequested(ICoreWebView2NewWindowRequestedEventHandler eventHandler);',
  55: 'EventRegistrationToken AddWebResourceRequested(ICoreWebView2WebResourceRequestedEventHandler eventHandler);',
  56: 'void RemoveWebResourceRequested(EventRegistrationToken token);',
  99: 'void CallDevToolsProtocolMethodForSession(string sessionId, string methodName, string parametersAsJson, ICoreWebView2CallDevToolsProtocolMethodCompletedHandler handler);',
  105: 'ICoreWebView2Profile8 GetProfile();',
  123: 'void AddWebResourceRequestedFilterWithRequestSourceKinds(string uri, int resourceContext, int requestSourceKinds);',
};
import fs from 'node:fs';
const h = fs.readFileSync(process.argv[2], 'utf8');
const m = h.match(/typedef struct ICoreWebView2_22Vtbl\s*\{([\s\S]*?)\}\s*ICoreWebView2_22Vtbl;/);
const ms = [...m[1].matchAll(/DECLSPEC_XFGVIRT\((\w+),\s*(\w+)\)/g)];
const lines = [];
ms.forEach((x, i) => {
  if (i < 3) return;
  lines.push(named[i] ? `    // ${i} ${x[1]}::${x[2]}\n    ${named[i]}` : `    [PreserveSig] int S${String(i).padStart(3, '0')}${x[2]}();`);
});
console.log(lines.join('\n'));
