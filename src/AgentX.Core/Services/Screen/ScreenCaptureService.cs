using System.Runtime.InteropServices;
using System.Text;
using AgentX.Core.Documents.Processors;
using AgentX.Core.Services.Settings;
using Serilog;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace AgentX.Core.Services.Screen;

/// <summary>
/// P/Invoke-based screen capture service for unpackaged WinUI 3 apps.
/// <para>
/// Captures screen or window content using GDI32/User32 native APIs
/// (<see cref="PrintWindow"/>, <see cref="BitBlt"/>), converts the result to a
/// <see cref="SoftwareBitmap"/>, and runs OCR through
/// <see cref="ImageProcessor.ExtractTextFromSoftwareBitmapAsync"/>.
/// </para>
/// <para>
/// If <c>EnableScreenAwareness</c> is <c>false</c> in
/// <see cref="ISettingsService"/>, both capture methods return an empty
/// <see cref="ScreenContextResult"/> immediately without invoking native code.
/// All P/Invoke calls are wrapped in try/catch — capture failures are logged and
/// result in an empty <see cref="ScreenContextResult"/> rather than a thrown exception.
/// </para>
/// </summary>
public sealed class ScreenCaptureService : IScreenCaptureService
{
    // ── P/Invoke Declarations ────────────────────────────────────────────────

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(
        IntPtr hdc, int x, int y, int w, int h,
        IntPtr hdcSrc, int x1, int y1, int rop);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, int nFlags);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    // ── Constants ─────────────────────────────────────────────────────────────

    private const int SRCCOPY = 0x00CC0020;
    private const int PW_RENDERFULLCONTENT = 2;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int MaxWindowTitleLength = 512;

    // ── RECT struct ───────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly ISettingsService _settingsService;
    private readonly ILogger _log;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises a new <see cref="ScreenCaptureService"/> instance.
    /// </summary>
    /// <param name="settingsService">Settings service for checking screen awareness toggle.</param>
    /// <param name="logger">Serilog logger instance.</param>
    public ScreenCaptureService(ISettingsService settingsService, ILogger logger)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _log = logger.ForContext<ScreenCaptureService>();
    }

    // ── IScreenCaptureService ─────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ScreenContextResult> CaptureAndOcrAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!await IsScreenAwarenessEnabledAsync())
        {
            _log.Debug("Screen awareness is disabled — skipping full-screen capture");
            return CreateEmptyResult();
        }

        _log.Debug("Starting full-screen capture and OCR");

        var activeTitle = GetActiveWindowTitle();
        var ideContext = IdeWindowDetector.Detect(activeTitle);
        var screenWidth = GetSystemMetrics(SM_CXSCREEN);
        var screenHeight = GetSystemMetrics(SM_CYSCREEN);

        if (screenWidth <= 0 || screenHeight <= 0)
        {
            _log.Warning("Invalid screen dimensions {Width}x{Height} — aborting capture", screenWidth, screenHeight);
            return CreateEmptyResult(activeTitle);
        }

        IntPtr hdcScreen = IntPtr.Zero;
        IntPtr hdcMem = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr hOld = IntPtr.Zero;

        try
        {
            hdcScreen = GetDC(IntPtr.Zero);
            if (hdcScreen == IntPtr.Zero)
            {
                _log.Warning("GetDC for screen returned null — aborting capture");
                return CreateEmptyResult(activeTitle);
            }

            hdcMem = CreateCompatibleDC(hdcScreen);
            hBitmap = CreateCompatibleBitmap(hdcScreen, screenWidth, screenHeight);
            hOld = SelectObject(hdcMem, hBitmap);

            var success = BitBlt(
                hdcMem, 0, 0, screenWidth, screenHeight,
                hdcScreen, 0, 0, SRCCOPY);

            if (!success)
            {
                _log.Warning("BitBlt failed for full-screen capture — aborting");
                return CreateEmptyResult(activeTitle);
            }

            // Flush GDI operations before reading the bitmap
            SelectObject(hdcMem, hOld);

            ct.ThrowIfCancellationRequested();

            var ocrText = await HBitmapToOcrTextAsync(hBitmap, screenWidth, screenHeight, ct);

            _log.Information(
                "Full-screen capture completed: {ScreenWidth}x{ScreenHeight}, OCR extracted {CharCount} characters",
                screenWidth, screenHeight, ocrText.Length);

            return new ScreenContextResult
            {
                OcrText = ocrText,
                ActiveWindowTitle = activeTitle,
                CapturedAtUtc = DateTime.UtcNow,
                IdeContext = ideContext,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Full-screen capture failed unexpectedly");
            return CreateEmptyResult(activeTitle);
        }
        finally
        {
            if (hOld != IntPtr.Zero && hdcMem != IntPtr.Zero)
                SelectObject(hdcMem, hOld);
            if (hBitmap != IntPtr.Zero)
                DeleteObject(hBitmap);
            if (hdcMem != IntPtr.Zero)
                DeleteDC(hdcMem);
            if (hdcScreen != IntPtr.Zero)
                ReleaseDC(IntPtr.Zero, hdcScreen);
        }
    }

    /// <inheritdoc />
    public async Task<ScreenContextResult> CaptureActiveWindowAndOcrAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!await IsScreenAwarenessEnabledAsync())
        {
            _log.Debug("Screen awareness is disabled — skipping active-window capture");
            return CreateEmptyResult();
        }

        _log.Debug("Starting active-window capture and OCR");

        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            _log.Warning("GetForegroundWindow returned null — no active window");
            return CreateEmptyResult();
        }

        var activeTitle = GetActiveWindowTitle();
        var ideContext = IdeWindowDetector.Detect(activeTitle);

        if (!GetWindowRect(hwnd, out var rect))
        {
            _log.Warning("GetWindowRect failed for HWND {Hwnd} — aborting capture", hwnd);
            return CreateEmptyResult(activeTitle);
        }

        var windowWidth = rect.Width;
        var windowHeight = rect.Height;

        if (windowWidth <= 0 || windowHeight <= 0)
        {
            _log.Warning("Invalid window dimensions {Width}x{Height} — aborting capture", windowWidth, windowHeight);
            return CreateEmptyResult(activeTitle);
        }

        IntPtr hdcScreen = IntPtr.Zero;
        IntPtr hdcMem = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr hOld = IntPtr.Zero;

        try
        {
            hdcScreen = GetDC(IntPtr.Zero);
            if (hdcScreen == IntPtr.Zero)
            {
                _log.Warning("GetDC for screen returned null — aborting window capture");
                return CreateEmptyResult(activeTitle);
            }

            hdcMem = CreateCompatibleDC(hdcScreen);
            hBitmap = CreateCompatibleBitmap(hdcScreen, windowWidth, windowHeight);
            hOld = SelectObject(hdcMem, hBitmap);

            // PW_RENDERFULLCONTENT (2) captures content from modern UWP/WinUI windows
            var success = PrintWindow(hwnd, hdcMem, PW_RENDERFULLCONTENT);

            if (!success)
            {
                _log.Warning("PrintWindow failed for HWND {Hwnd} — aborting capture", hwnd);
                return CreateEmptyResult(activeTitle);
            }

            // Flush GDI operations
            SelectObject(hdcMem, hOld);

            ct.ThrowIfCancellationRequested();

            var ocrText = await HBitmapToOcrTextAsync(hBitmap, windowWidth, windowHeight, ct);

            _log.Information(
                "Active-window capture completed: {Title} ({Width}x{Height}), OCR extracted {CharCount} characters",
                activeTitle, windowWidth, windowHeight, ocrText.Length);

            return new ScreenContextResult
            {
                OcrText = ocrText,
                ActiveWindowTitle = activeTitle,
                CapturedAtUtc = DateTime.UtcNow,
                IdeContext = ideContext,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Active-window capture failed unexpectedly");
            return CreateEmptyResult(activeTitle);
        }
        finally
        {
            if (hOld != IntPtr.Zero && hdcMem != IntPtr.Zero)
                SelectObject(hdcMem, hOld);
            if (hBitmap != IntPtr.Zero)
                DeleteObject(hBitmap);
            if (hdcMem != IntPtr.Zero)
                DeleteDC(hdcMem);
            if (hdcScreen != IntPtr.Zero)
                ReleaseDC(IntPtr.Zero, hdcScreen);
        }
    }

    // ── Private Helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Checks whether screen awareness is enabled in application settings.
    /// </summary>
    private async Task<bool> IsScreenAwarenessEnabledAsync()
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            return settings.EnableScreenAwareness;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to read screen awareness setting — defaulting to disabled");
            return false;
        }
    }

    /// <summary>
    /// Gets the title of the currently active (foreground) window.
    /// </summary>
    private static string GetActiveWindowTitle()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return string.Empty;

        var sb = new StringBuilder(MaxWindowTitleLength);
        var length = GetWindowText(hwnd, sb, MaxWindowTitleLength);
        return length > 0 ? sb.ToString() : string.Empty;
    }

    /// <summary>
    /// Converts an HBITMAP to a <see cref="SoftwareBitmap"/>, runs OCR via
    /// <see cref="ImageProcessor.ExtractTextFromSoftwareBitmapAsync"/>, and returns the text.
    /// </summary>
    private async Task<string> HBitmapToOcrTextAsync(IntPtr hBitmap, int width, int height, CancellationToken ct)
    {
        SoftwareBitmap? softwareBitmap = null;

        try
        {
            softwareBitmap = await HBitmapToSoftwareBitmapAsync(hBitmap, width, height, ct);
            ct.ThrowIfCancellationRequested();

            var ocrText = await ImageProcessor.ExtractTextFromSoftwareBitmapAsync(softwareBitmap, ct);

            return string.IsNullOrWhiteSpace(ocrText) ? string.Empty : ocrText;
        }
        finally
        {
            softwareBitmap?.Dispose();
        }
    }

    /// <summary>
    /// Converts a GDI HBITMAP to a WinRT <see cref="SoftwareBitmap"/>.
    /// <para>
    /// Uses <see cref="System.Drawing.Bitmap.FromHbitmap"/> to create a GDI+ bitmap
    /// from the native handle, saves it as PNG to a memory stream, then decodes it
    /// via <see cref="BitmapDecoder"/> into a <see cref="SoftwareBitmap"/> suitable
    /// for OCR.
    /// </para>
    /// <para>
    /// If either dimension exceeds 4096 pixels, the bitmap is scaled down
    /// to stay within the <c>OcrEngine</c> limit.
    /// </para>
    /// </summary>
    private static async Task<SoftwareBitmap> HBitmapToSoftwareBitmapAsync(
        IntPtr hBitmap, int width, int height, CancellationToken ct)
    {
        // Convert HBITMAP → System.Drawing.Bitmap → PNG byte stream → SoftwareBitmap
        // This is the most reliable path for unpackaged WinUI 3 apps.
        using var sysDrawingBmp = System.Drawing.Bitmap.FromHbitmap(hBitmap);

        // Save as PNG to a memory stream for BitmapDecoder compatibility
        using var ms = new MemoryStream();
        sysDrawingBmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        ms.Position = 0;

        // Copy the PNG bytes into a WinRT InMemoryRandomAccessStream
        // (DataWriter is the most reliable way to populate an InMemoryRandomAccessStream
        // without relying on Stream↔IRandomAccessStream interop extensions.)
        using var winrtStream = new InMemoryRandomAccessStream();
        using var dataWriter = new DataWriter(winrtStream);
        dataWriter.WriteBytes(ms.ToArray());
        await dataWriter.StoreAsync();
        await dataWriter.FlushAsync();
        winrtStream.Seek(0);

        ct.ThrowIfCancellationRequested();

        var decoder = await BitmapDecoder.CreateAsync(winrtStream);

        // OcrEngine requires Bgra8 / Premultiplied alpha and dimensions <= 4096
        const uint maxDim = 4096;

        if (decoder.PixelWidth > maxDim || decoder.PixelHeight > maxDim)
        {
            var scale = Math.Min((double)maxDim / decoder.PixelWidth, (double)maxDim / decoder.PixelHeight);
            var scaledWidth = (uint)(decoder.PixelWidth * scale);
            var scaledHeight = (uint)(decoder.PixelHeight * scale);

            return await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                new BitmapTransform
                {
                    ScaledWidth = scaledWidth,
                    ScaledHeight = scaledHeight,
                    InterpolationMode = BitmapInterpolationMode.Fant,
                },
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);
        }

        return await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied);
    }

    /// <summary>
    /// Creates an empty <see cref="ScreenContextResult"/> with optional window title.
    /// </summary>
    private static ScreenContextResult CreateEmptyResult(string activeWindowTitle = "") =>
        new()
        {
            OcrText = string.Empty,
            ActiveWindowTitle = activeWindowTitle,
            CapturedAtUtc = DateTime.UtcNow,
        };
}