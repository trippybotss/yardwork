using SharpGen.Runtime;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using D2DAlphaMode = Vortice.DCommon.AlphaMode;
using D2DFactoryType = Vortice.Direct2D1.FactoryType;
using DWriteFactoryType = Vortice.DirectWrite.FactoryType;
using DxgiAlphaMode = Vortice.DXGI.AlphaMode;
using FeatureLevel = Vortice.Direct3D.FeatureLevel;

namespace Yardwork.Overlay;

/// <summary>
/// Owns the Direct3D 11 device and the swap chain bound to the overlay window, plus a
/// Direct2D/DirectWrite context that draws into the same back buffer for 2D shapes and text.
/// The back buffer is cleared to fully transparent every frame; DWM uses its alpha channel
/// to composite the overlay over whatever is underneath.
/// </summary>
internal sealed class Renderer : IDisposable
{
    private static readonly FeatureLevel[] FeatureLevels =
    {
        FeatureLevel.Level_11_1,
        FeatureLevel.Level_11_0,
        FeatureLevel.Level_10_1,
        FeatureLevel.Level_10_0,
    };

    private readonly ID2D1Factory1 _d2dFactory;
    private readonly ID2D1Device _d2dDevice;
    private ID3D11RenderTargetView? _renderTargetView;
    private ID2D1Bitmap1? _targetBitmap;

    public Renderer(IntPtr hwnd, int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);

        // BGRA support is required for Direct2D interop.
        const DeviceCreationFlags flags = DeviceCreationFlags.BgraSupport;
        Result result = D3D11.D3D11CreateDevice(null, DriverType.Hardware, flags, FeatureLevels,
            out ID3D11Device? device, out ID3D11DeviceContext? context);
        if (result.Failure)
        {
            // No usable GPU (remote desktop, broken driver): fall back to the software rasterizer.
            result = D3D11.D3D11CreateDevice(null, DriverType.Warp, flags, FeatureLevels, out device, out context);
            IsSoftware = true;
        }
        result.CheckError();

        Device = device!;
        Context = context!;

        using var dxgiDevice = Device.QueryInterface<IDXGIDevice>();
        using IDXGIAdapter adapter = dxgiDevice.GetAdapter();
        using var factory = adapter.GetParent<IDXGIFactory2>();

        var description = new SwapChainDescription1
        {
            Width = (uint)Width,
            Height = (uint)Height,
            Format = Format.B8G8R8A8_UNorm,
            Stereo = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 1,
            Scaling = Scaling.Stretch,
            // Blt model: the frame is copied into the window's redirection surface, which keeps
            // per-pixel alpha working together with WS_EX_LAYERED + DwmExtendFrameIntoClientArea.
            SwapEffect = SwapEffect.Discard,
            AlphaMode = DxgiAlphaMode.Unspecified,
            Flags = SwapChainFlags.None,
        };
        SwapChain = factory.CreateSwapChainForHwnd(Device, hwnd, description);
        factory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAll);

        _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(D2DFactoryType.SingleThreaded);
        _d2dDevice = _d2dFactory.CreateDevice(dxgiDevice);
        D2D = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);
        // ClearType needs an opaque background; grayscale antialiasing works on transparent pixels.
        D2D.TextAntialiasMode = Vortice.Direct2D1.TextAntialiasMode.Grayscale;
        D2D.AntialiasMode = AntialiasMode.PerPrimitive;

        DirectWrite = DWrite.DWriteCreateFactory<IDWriteFactory>(DWriteFactoryType.Shared);

        CreateTargets();
    }

    public ID3D11Device Device { get; }
    public ID3D11DeviceContext Context { get; }
    public IDXGISwapChain1 SwapChain { get; }
    public ID2D1DeviceContext D2D { get; }
    public IDWriteFactory DirectWrite { get; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool IsSoftware { get; }

    public void Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == Width && height == Height) return;

        ReleaseTargets();
        SwapChain.ResizeBuffers(0, (uint)width, (uint)height, Format.Unknown, SwapChainFlags.None).CheckError();
        Width = width;
        Height = height;
        CreateTargets();
    }

    /// <summary>Clears the back buffer to transparent and opens a Direct2D drawing session.</summary>
    public void BeginFrame()
    {
        Context.OMSetRenderTargets(_renderTargetView!);
        Context.RSSetViewport(new Viewport(Width, Height));
        Context.ClearRenderTargetView(_renderTargetView!, new Color4(0f, 0f, 0f, 0f));
        D2D.BeginDraw();
    }

    /// <summary>Finishes drawing and presents on the next vertical blank. False means the device was lost.</summary>
    public bool EndFrame()
    {
        bool drawn;
        try
        {
            drawn = D2D.EndDraw().Success;
        }
        catch (SharpGenException)
        {
            drawn = false;
        }

        Result presented = SwapChain.Present(1, PresentFlags.None);
        return drawn && presented.Success;
    }

    private void CreateTargets()
    {
        using (var backBuffer = SwapChain.GetBuffer<ID3D11Texture2D>(0))
            _renderTargetView = Device.CreateRenderTargetView(backBuffer);

        using var surface = SwapChain.GetBuffer<IDXGISurface>(0);
        var properties = new BitmapProperties1(
            new PixelFormat(Format.B8G8R8A8_UNorm, D2DAlphaMode.Premultiplied),
            96f, 96f,
            BitmapOptions.Target | BitmapOptions.CannotDraw);
        _targetBitmap = D2D.CreateBitmapFromDxgiSurface(surface, properties);
        D2D.Target = _targetBitmap;
    }

    private void ReleaseTargets()
    {
        // Every reference to the back buffer must be gone before ResizeBuffers.
        D2D.Target = null;
        _targetBitmap?.Dispose();
        _targetBitmap = null;
        _renderTargetView?.Dispose();
        _renderTargetView = null;
        Context.ClearState();
        Context.Flush();
    }

    public void Dispose()
    {
        ReleaseTargets();
        DirectWrite.Dispose();
        D2D.Dispose();
        _d2dDevice.Dispose();
        _d2dFactory.Dispose();
        SwapChain.Dispose();
        Context.Dispose();
        Device.Dispose();
    }
}
