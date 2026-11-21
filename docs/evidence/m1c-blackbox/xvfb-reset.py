# Opens and closes a display connection N times in a row, each the server's only client; counts the opens that fail.
import ctypes, sys, time
x = ctypes.CDLL("libX11.so.6")
x.XOpenDisplay.restype = ctypes.c_void_p
x.XOpenDisplay.argtypes = [ctypes.c_char_p]
x.XCloseDisplay.argtypes = [ctypes.c_void_p]
x.XSync.argtypes = [ctypes.c_void_p, ctypes.c_int]
display, n = sys.argv[1].encode(), int(sys.argv[2])
failed = 0; first = None
for i in range(n):
    d = x.XOpenDisplay(display)
    if not d:
        failed += 1; first = first if first is not None else i
        continue
    x.XSync(d, 0)
    x.XCloseDisplay(d)
print(f"{sys.argv[3]}: {n} opens, {failed} failed (first at {first})")
