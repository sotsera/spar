// One-off: copies spikes/window-tests' WebView2 declarations into this spike, without the profile and permission
// interfaces it does not use (their vtable slots become placeholders). Run from spikes/: node window-automation/scripts/adapt-interop.mjs
import fs from "node:fs";

let src = fs.readFileSync("window-tests/WindowTests.Win/Interop/WebView2.cs", "utf8").replace(/\r\n/g, "\n");
src = src.replace("namespace Spar.WindowTests.Interop;", "namespace Spar.WindowAutomation.Interop;");
src = src.replace(/\/\/ Hand-written from WebView2\.h[\s\S]*?offsets of the used methods\./,
  "// Adapted from spikes/window-tests (hand-written from WebView2.h, Microsoft.Web.WebView2 1.0.4314.35, C vtable\n" +
  "// structs; its flat ICoreWebView2_22 was printed by spikes/window-tests/tools/gen-webview.mjs). Slots this spike never\n" +
  "// calls are [PreserveSig] int placeholders that keep the offsets of the used methods.");
const unused = [
  "ICoreWebView2ClearBrowsingDataCompletedHandler", "ICoreWebView2ProfileDeletedEventHandler",
  "ICoreWebView2GetNonDefaultPermissionSettingsCompletedHandler", "ICoreWebView2PermissionSettingCollectionView",
  "ICoreWebView2PermissionSetting", "ICoreWebView2PermissionRequestedEventHandler",
  "ICoreWebView2PermissionRequestedEventArgs3", "ICoreWebView2Profile8",
];
for (const name of unused) {
  const re = new RegExp("\\n(// ICoreWebView2Profile8, flat[^\\n]*\\n)?\\[GeneratedComInterface[^\\n]*\\]\\n\\[Guid\\(\"[^\"]+\"\\)\\]\\ninternal partial interface " + name + "\\b[^\\n]*\\n\\{[\\s\\S]*?\\n\\}\\n");
  if (!re.test(src)) throw new Error(name);
  src = src.replace(re, "\n");
}
src = src.replace("    // 23 ICoreWebView2::add_PermissionRequested\n    EventRegistrationToken AddPermissionRequested(ICoreWebView2PermissionRequestedEventHandler eventHandler);", "    [PreserveSig] int S023AddPermissionRequested();");
src = src.replace("    // 105 ICoreWebView2_13::get_Profile\n    ICoreWebView2Profile8 GetProfile();", "    [PreserveSig] int S105GetProfile();");
src = src.replace(/\n{3,}/g, "\n\n");
fs.writeFileSync("window-automation/Shared/Interop/WebView2.cs", src);
