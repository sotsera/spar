using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Theme.Desktop.Interop;

namespace Theme.Desktop;

/// <summary>
/// A second, independent capture: Windows.Graphics.Capture on the window (works offscreen). DWM hands over a frame each
/// time it composes a change of the window, stamped with SystemRelativeTime (QPC in 100 ns units), so the timeline shows
/// what the compositor actually produced. Raw vtable calls only (slot numbers from the Windows SDK headers:
/// windows.graphics.capture.h, windows.graphics.capture.interop.h, windows.graphics.directx.direct3d11.interop.h,
/// d3d11.h), so no WinRT projection is needed under NativeAOT.
/// </summary>
internal sealed unsafe partial class WgcCapture
{
    private static readonly Guid IidGraphicsCaptureItemInterop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid IidGraphicsCaptureItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid IidFramePoolStatics2 = new("589B103F-6BBC-5DF5-A991-02E28B3B66D5");
    private static readonly Guid IidDxgiDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly Guid IidDxgiInterfaceAccess = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
    private static readonly Guid IidTexture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    private readonly ManualResetEventSlim _attach = new();
    private nint _window;
    private readonly Stopwatch _clock;
    private readonly string _directory;
    private readonly TimeSpan _duration;
    private readonly List<string> _frames = [];
    private readonly Thread _thread;
    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _stop;
    private int _pngs;

    public WgcCapture(Stopwatch clock, string directory, TimeSpan duration)
    {
        _clock = clock;
        _directory = directory;
        _duration = duration;
        _thread = new Thread(Run) { Name = "WGC capture", IsBackground = true, Priority = ThreadPriority.AboveNormal };
    }

    public int Count { get; private set; }

    /// <summary>Completes with "ok" or the failure once StartCapture has run.</summary>
    public Task<string> Started => _started.Task;

    /// <summary>Prepares D3D and the WinRT factories (slow on first use) before the window exists.</summary>
    public void Start() => _thread.Start();

    /// <summary>
    /// Starts capturing <paramref name="window"/>, which must be shown (CreateForWindow rejects a hidden window with
    /// E_INVALIDARG); a cloaked window is accepted. Blocks until StartCapture has returned.
    /// </summary>
    public string Attach(nint window)
    {
        _window = window;
        _attach.Set();
        return _started.Task.GetAwaiter().GetResult();
    }

    public Task StopAsync()
    {
        _stop = true;
        return _finished.Task;
    }

    public string FramesJson()
    {
        lock (_frames)
        {
            return "[\n" + string.Join(",\n", _frames) + "\n]";
        }
    }

    private static void* Slot(nint self, int index) => (*(void***)self)[index];

    private static void Check(int hr, string what)
    {
        if (hr < 0) throw new COMException(what + " failed", hr);
    }

    private void Run()
    {
        nint device = 0, context = 0, dxgi = 0, winrtDevice = 0, interop = 0, item = 0, statics = 0, pool = 0, session = 0, staging = 0;
        try
        {
            Check(Native.RoInitialize(1), "RoInitialize");
            Check(Native.D3D11CreateDevice(0, 1 /* HARDWARE */, 0, 0x20 /* BGRA_SUPPORT */, null, 0, 7, &device, null, &context), "D3D11CreateDevice");
            dxgi = WebView2Raw.Query(device, IidDxgiDevice);
            Check(Native.CreateDirect3D11DeviceFromDXGIDevice(dxgi, &winrtDevice), "CreateDirect3D11DeviceFromDXGIDevice");

            interop = Factory("Windows.Graphics.Capture.GraphicsCaptureItem", IidGraphicsCaptureItemInterop);
            statics = Factory("Windows.Graphics.Capture.Direct3D11CaptureFramePool", IidFramePoolStatics2);
            _attach.Wait();
            var iid = IidGraphicsCaptureItem;
            Check(((delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)Slot(interop, 3))(interop, _window, &iid, &item), "CreateForWindow");
            SizeInt32 size;
            Check(((delegate* unmanaged[Stdcall]<nint, SizeInt32*, int>)Slot(item, 7))(item, &size), "GraphicsCaptureItem.Size");

            // CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized = 87, buffers, size)
            Check(((delegate* unmanaged[Stdcall]<nint, nint, int, int, SizeInt32, nint*, int>)Slot(statics, 6))(statics, winrtDevice, 87, 3, size, &pool), "CreateFreeThreaded");
            Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Slot(pool, 10))(pool, item, &session), "CreateCaptureSession");
            Check(((delegate* unmanaged[Stdcall]<nint, int>)Slot(session, 6))(session), "StartCapture");
            _started.TrySetResult($"ok {size.Width}x{size.Height}");

            var bins = new int[32768];
            string? lastSignature = null;
            var startedAt = _clock.Elapsed;
            var ticksPer100ns = Stopwatch.Frequency / 1e7;
            var clockStart = Stopwatch.GetTimestamp() - _clock.ElapsedTicks; // Stopwatch ticks are QPC ticks.
            while (!_stop && _clock.Elapsed - startedAt < _duration)
            {
                nint frame = 0;
                Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(pool, 7))(pool, &frame), "TryGetNextFrame");
                if (frame == 0)
                {
                    Thread.Sleep(1);
                    continue;
                }

                var receivedAt = _clock.Elapsed.TotalMilliseconds;
                long relative;
                ((delegate* unmanaged[Stdcall]<nint, long*, int>)Slot(frame, 7))(frame, &relative);
                // SystemRelativeTime is QPC-based; the host's clock is a Stopwatch (QPC) started at process start.
                var at = ((relative * ticksPer100ns) - clockStart) * 1000.0 / Stopwatch.Frequency;
                SizeInt32 content;
                ((delegate* unmanaged[Stdcall]<nint, SizeInt32*, int>)Slot(frame, 8))(frame, &content);
                nint surface = 0, access = 0, texture = 0;
                ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(frame, 6))(frame, &surface);
                access = WebView2Raw.Query(surface, IidDxgiInterfaceAccess);
                var textureIid = IidTexture2D;
                Check(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(access, 3))(access, &textureIid, &texture), "GetInterface(ID3D11Texture2D)");

                Texture2DDesc desc;
                ((delegate* unmanaged[Stdcall]<nint, Texture2DDesc*, void>)Slot(texture, 10))(texture, &desc);
                if (staging == 0)
                {
                    var stagingDesc = desc;
                    stagingDesc.Usage = 3; // STAGING
                    stagingDesc.BindFlags = 0;
                    stagingDesc.CpuAccessFlags = 0x20000; // READ
                    stagingDesc.MiscFlags = 0;
                    Check(((delegate* unmanaged[Stdcall]<nint, Texture2DDesc*, nint, nint*, int>)Slot(device, 5))(device, &stagingDesc, 0, &staging), "CreateTexture2D");
                }

                ((delegate* unmanaged[Stdcall]<nint, nint, nint, void>)Slot(context, 47))(context, staging, texture); // CopyResource
                MappedSubresource mapped;
                Check(((delegate* unmanaged[Stdcall]<nint, nint, uint, int, uint, MappedSubresource*, int>)Slot(context, 14))(context, staging, 0, 1 /* READ */, 0, &mapped), "Map");
                var width = Math.Min(content.Width, (int)desc.Width);
                var height = Math.Min(content.Height, (int)desc.Height);
                var stride = (int)(mapped.RowPitch / 4);
                var pixels = (uint*)mapped.Data;
                Analyse(pixels, stride, width, height, at, receivedAt, bins, ref lastSignature);
                ((delegate* unmanaged[Stdcall]<nint, nint, uint, void>)Slot(context, 15))(context, staging, 0); // Unmap

                Marshal.Release(texture);
                Marshal.Release(access);
                Marshal.Release(surface);
                Marshal.Release(frame);
                Count++;
            }
        }
        catch (Exception exception)
        {
            _started.TrySetResult("failed: " + exception.Message + $" (0x{exception.HResult:X8})");
            Console.Error.WriteLine("WGC capture failure: " + exception);
        }
        finally
        {
            if (session != 0)
            {
                // IClosable.Close is on a separate interface; releasing the last reference stops the capture.
                Marshal.Release(session);
            }

            foreach (var pointer in new[] { pool, statics, item, interop, winrtDevice, dxgi, staging, context, device })
            {
                if (pointer != 0) Marshal.Release(pointer);
            }

            _finished.TrySetResult();
        }
    }

    private void Analyse(uint* pixels, int stride, int width, int height, double at, double receivedAt, int[] bins, ref string? lastSignature)
    {
        // The captured image starts at the window's visible frame (DWMWA_EXTENDED_FRAME_BOUNDS), without resize borders.
        Rect frameBounds;
        Dwm.DwmGetWindowAttribute(_window, 9, &frameBounds, (uint)sizeof(Rect));
        var origin = new Point();
        User32.ClientToScreen(_window, ref origin);
        User32.GetClientRect(_window, out var client);
        var clientLeft = origin.X - frameBounds.Left;
        var clientTop = origin.Y - frameBounds.Top;
        var clientRect = new Rect { Left = Math.Max(0, clientLeft), Top = Math.Max(0, clientTop), Right = Math.Min(width, clientLeft + client.Right), Bottom = Math.Min(height, clientTop + client.Bottom) };
        var titleRect = new Rect { Left = clientRect.Left + 8, Top = Math.Max(0, clientTop - 24), Right = clientRect.Right - 160, Bottom = Math.Max(1, clientTop - 4) };
        var clientStats = FrameCapture.AnalyseRegion(pixels, stride, clientRect, bins);
        var titleStats = FrameCapture.AnalyseRegion(pixels, stride, titleRect, bins);
        var signature = FrameCapture.Signature(clientStats, titleStats);
        string? png = null;
        if (signature != lastSignature && _pngs < 120)
        {
            png = $"wgc-{Count:D4}-{at:F0}ms.png";
            Png.Write(Path.Combine(_directory, png), pixels, stride, height, 2, width);
            _pngs++;
        }

        lastSignature = signature;
        var record = new StringBuilder(256);
        record.Append(CultureInfo.InvariantCulture, $"{{\"i\":{Count},\"at\":{at:F1},\"received\":{receivedAt:F1},\"ok\":1,\"visible\":1,\"cloaked\":0,");
        record.Append("\"client\":").Append(FrameCapture.Json(clientStats)).Append(",\"title\":").Append(FrameCapture.Json(titleStats));
        if (png is not null) record.Append(",\"png\":\"").Append(png).Append('"');
        record.Append('}');
        lock (_frames)
        {
            _frames.Add(record.ToString());
        }
    }

    private static nint Factory(string className, Guid iid)
    {
        nint hstring;
        fixed (char* name = className)
        {
            Check(Native.WindowsCreateString(name, (uint)className.Length, &hstring), "WindowsCreateString");
        }

        try
        {
            nint factory;
            Check(Native.RoGetActivationFactory(hstring, &iid, &factory), "RoGetActivationFactory " + className);
            return factory;
        }
        finally
        {
            Native.WindowsDeleteString(hstring);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SizeInt32
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Texture2DDesc
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public uint Format;
        public uint SampleCount;
        public uint SampleQuality;
        public uint Usage;
        public uint BindFlags;
        public uint CpuAccessFlags;
        public uint MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MappedSubresource
    {
        public nint Data;
        public uint RowPitch;
        public uint DepthPitch;
    }

    private static partial class Native
    {
        [LibraryImport("combase")]
        public static partial int RoInitialize(int type);

        [LibraryImport("combase")]
        public static partial int RoGetActivationFactory(nint classId, Guid* iid, nint* factory);

        [LibraryImport("combase")]
        public static partial int WindowsCreateString(char* source, uint length, nint* hstring);

        [LibraryImport("combase")]
        public static partial int WindowsDeleteString(nint hstring);

        [LibraryImport("d3d11")]
        public static partial int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags, int* featureLevels, uint levels, uint sdkVersion, nint* device, int* featureLevel, nint* context);

        [LibraryImport("d3d11")]
        public static partial int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, nint* graphicsDevice);
    }
}
