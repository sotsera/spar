---
name: spar-native-interop
description: Implement or audit Spar's platform imports on Windows and Linux, Win32 and COM ABI declarations, GLib, GTK, WebKitGTK and libc imports, native struct layouts, callback and signal-handler signatures, and native handle and GObject reference ownership.
---

# Check native interop

Use `dotnet-advanced:dotnet-pinvoke` for general `LibraryImport` and marshalling technique. That skill excludes COM, so COM and WebView2 rules live here and in `spar-webview2-hosting`; GObject and main-loop rules live here and in `spar-webkitgtk-hosting`.

- **Check against the real header.** Compare each declaration with the target SDK or library header for the actual OS and architecture. Check scalar widths, signedness, alignment, calling convention, pointer depth, string encoding and buffer units. Win32 `BOOL` and `LONG` and GLib `gboolean` are 32-bit; `gsize` is pointer-sized.
- **Reuse the chosen bindings** (plan 3, "Bindings"). On Windows, use CsWin32 wherever it covers the API, and source-generated imports (`LibraryImport`, `GeneratedComInterface`) for the gaps; change generated declarations through their inputs (`NativeMethods.txt`, `NativeMethods.json`), never by hand. On Linux, use `LibraryImport` and `[UnmanagedCallersOnly]` against glib, gio, gtk and webkit. The NativeAOT exe links only glibc and loads those libraries at first use, so the startup check finds a missing one before any call does.
- **Follow each API's error contract.** Read the last error immediately when the API sets it. Handle HRESULTs and sentinel return values exactly as documented. Check every `GError**` out-parameter and free it with `g_error_free`.
- **Make ownership visible.** Mark handles, pointers and object references as owned or borrowed wherever the difference is easy to miss. Release each one with the exact matching operation, on the correct thread.
- **Keep callbacks safe.** Keep callbacks and buffers alive until the native work has actually completed. Catch managed exceptions at every unmanaged entry point.
- **Hand-declared COM.**
  - Declare every slot of every base interface, in header order, even slots you never call; use `[PreserveSig] int SlotNN()` placeholders. Count from the start of the C vtable, IUnknown included. A slot-order test checks each one against the header: `tests/Spar.Tests/ComSlots.cs`, for WebView2.h (Spar.Windows) and for the Windows SDK's UIAutomationClient.h (the black-box fixture's UI Automation client in Spar.Testing, an explicit skip where no SDK is installed). A placeholder of a property getter is named `SlotNNN_GetX`.
  - Cast to the interface whose methods you actually call. A derived interface with no reachable method fails its cast only under AOT, at run time.
  - Pass IIDs explicitly, never through `Type.GUID`.
  - Use `[PreserveSig]` where a failure HRESULT is an expected result (cancel, `E_FAIL` from `ClearClientData`).
  - Ownership: a method that hands out an interface returns a raw `nint` with one owned reference. Wrap it with `CreateObjectFlags.UniqueInstance` and release it with `ComObject.FinalRelease()` on its STA. Pointers passed into event callbacks are borrowed: no wrapper, no release.
- **GLib and GObject.**
  - Read each parameter's and return value's transfer annotation in the library's reference documentation. Transfer full is released with its matching call (`g_object_unref`, `g_free`, `g_bytes_unref`); transfer none is borrowed.
  - Signal handlers and sources are `[UnmanagedCallersOnly]` functions whose signature matches the signal's C signature in the 6.0 API exactly (the script message signal passes a `JSCValue*`; plan 3 lists what changed from 4.1). Pass a `GCHandle` as user data with a destroy notify that frees it, and disconnect handlers during teardown.
  - Strings are UTF-8. Free returned owned strings with `g_free`.
- **Never import a variadic C function.** Use its non-variadic form (`g_object_new_with_properties`, libsecret's `…v` functions). On Linux, bind `dlerror` before the first `dlopen`.
- **Process environment on Linux.** Set variables that native code reads (`JSC_useWasm`, `GSK_RENDERER`) with libc `setenv`, before other threads start. When each one is set is in `spar-webkitgtk-hosting` ("Startup"). `Environment.SetEnvironmentVariable` reaches neither `getenv` nor child processes.
- **Assert layouts at run time** where a size is easy to get wrong (`CREDENTIALW` is 80 bytes on x64), and wipe secret buffers before the matching free (`CredFree`, `LocalFree`; libsecret's `secret_password_free`).
- **Check under NativeAOT.** Verify interop with warnings treated as errors, and run the published executable for any new import, callback or COM interface. Linux NativeAOT needs `clang` and `zlib1g-dev`. To see where image size goes, publish with `-p:IlcGenerateMapFile=true`.
- **Test the real call.** Check layouts against native evidence, and exercise the affected native call, its callback and its failure path. Use `spar-webview2-hosting` for COM and window lifetime, and `spar-webkitgtk-hosting` for the main loop and GObject lifetime. The evidence for these rules is in `docs/pitfalls.md` ("Windows host", "Linux host").
