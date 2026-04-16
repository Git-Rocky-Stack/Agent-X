using AgentX.Core.Documents.Models;
using AgentX.Core.Helpers;
using Serilog;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace AgentX.Core.Documents.Processors;

/// <summary>
/// Extracts text from image files using Windows 10/11 built-in OCR capabilities
/// via <see cref="OcrEngine"/>.
/// <para>
/// Requires the Windows 10 SDK (10.0.19041.0 or later). Uses the user's installed
/// language recognizers to perform OCR. If no recognizer is available or OCR produces
/// no results, the extracted text falls back to a placeholder message.
/// </para>
/// <para>
/// Supported formats: PNG, JPG, JPEG, BMP, TIFF. The image is loaded as a
/// <see cref="SoftwareBitmap"/> via <see cref="BitmapDecoder"/> for maximum codec
/// compatibility.
/// </para>
/// </summary>
public class ImageProcessor : IDocumentProcessor
{
    private static readonly ILogger Log = Serilog.Log.ForContext<ImageProcessor>();

    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".tiff"
    };

    /// <summary>
    /// Maximum image dimension (width or height) supported by <see cref="OcrEngine"/>.
    /// Images larger than this in either dimension will be scaled down before OCR.
    /// The Windows OCR engine limit is 4096 x 4096 pixels.
    /// </summary>
    private const uint MaxOcrDimension = 4096;

    /// <inheritdoc />
    public IReadOnlySet<string> SupportedExtensions => Extensions;

    /// <inheritdoc />
    public bool CanProcess(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return !string.IsNullOrEmpty(ext) && Extensions.Contains(ext);
    }

    /// <inheritdoc />
    public async Task<ProcessedDocument> ProcessAsync(string filePath, CancellationToken ct = default)
    {
        Log.Debug("Processing image file: {FilePath}", filePath);

        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("Image file not found.", filePath);

        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        var document = new ProcessedDocument
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            FileType = ext.TrimStart('.'),
            FileSizeBytes = fileInfo.Length,
            PageCount = 1,
        };

        try
        {
            var hashTask = HashHelper.ComputeFileHashAsync(filePath, ct);

            var (text, width, height) = await ExtractTextFromImageAsync(filePath, ct);

            document.ContentHash = await hashTask;
            document.ExtractedText = text;
            document.WordCount = CountWords(text);
            document.Metadata.Custom["width"] = width.ToString();
            document.Metadata.Custom["height"] = height.ToString();
            document.Metadata.Custom["dimensions"] = $"{width}x{height}";

            // File timestamps
            document.Metadata.CreatedDate = fileInfo.CreationTimeUtc;
            document.Metadata.ModifiedDate = fileInfo.LastWriteTimeUtc;

            Log.Information(
                "Successfully processed image: {FileName} ({Width}x{Height}, {WordCount} words extracted)",
                document.FileName, width, height, document.WordCount);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to process image file: {FilePath}", filePath);
            document.ExtractedText = "[Image - no text extracted]";
            document.Metadata.Custom["error"] = ex.Message;
        }

        return document;
    }

    /// <summary>
    /// Loads the image file, performs OCR using the Windows OCR engine, and returns
    /// the extracted text along with image dimensions.
    /// </summary>
    private static async Task<(string Text, uint Width, uint Height)> ExtractTextFromImageAsync(
        string filePath, CancellationToken ct)
    {
        // Obtain an OcrEngine from the user's installed language profile
        var ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (ocrEngine is null)
        {
            Log.Warning("No OCR engine available from user profile languages");
            return ("[Image - no text extracted]", 0, 0);
        }

        // Load the image file using Windows Storage APIs and BitmapDecoder
        var storageFile = await StorageFile.GetFileFromPathAsync(filePath);
        using var stream = await storageFile.OpenAsync(FileAccessMode.Read);

        ct.ThrowIfCancellationRequested();

        var decoder = await BitmapDecoder.CreateAsync(stream);
        var originalWidth = decoder.PixelWidth;
        var originalHeight = decoder.PixelHeight;

        // OcrEngine requires SoftwareBitmap in Bgra8 with premultiplied alpha,
        // and dimensions must not exceed MaxOcrDimension
        SoftwareBitmap softwareBitmap;

        if (originalWidth > MaxOcrDimension || originalHeight > MaxOcrDimension)
        {
            // Scale down while preserving aspect ratio
            var scale = Math.Min(
                (double)MaxOcrDimension / originalWidth,
                (double)MaxOcrDimension / originalHeight);

            var scaledWidth = (uint)(originalWidth * scale);
            var scaledHeight = (uint)(originalHeight * scale);

            Log.Debug(
                "Scaling image from {OrigW}x{OrigH} to {ScaledW}x{ScaledH} for OCR",
                originalWidth, originalHeight, scaledWidth, scaledHeight);

            var transform = new BitmapTransform
            {
                ScaledWidth = scaledWidth,
                ScaledHeight = scaledHeight,
                InterpolationMode = BitmapInterpolationMode.Fant,
            };

            var pixelData = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);

            softwareBitmap = pixelData;
        }
        else
        {
            // Load at original size
            softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied);
        }

        ct.ThrowIfCancellationRequested();

        // Perform OCR
        OcrResult ocrResult;
        try
        {
            ocrResult = await ocrEngine.RecognizeAsync(softwareBitmap);
        }
        finally
        {
            softwareBitmap.Dispose();
        }

        // Extract text from OCR result lines
        if (ocrResult.Lines.Count == 0)
        {
            Log.Debug("OCR returned no text lines for image: {FilePath}", filePath);
            return ("[Image - no text extracted]", originalWidth, originalHeight);
        }

        var extractedText = string.Join(
            Environment.NewLine,
            ocrResult.Lines.Select(line => line.Text));

        if (string.IsNullOrWhiteSpace(extractedText))
        {
            return ("[Image - no text extracted]", originalWidth, originalHeight);
        }

        return (extractedText, originalWidth, originalHeight);
    }

    /// <summary>
    /// Performs OCR on an existing <see cref="SoftwareBitmap"/> without requiring a file path.
    /// <para>
    /// This overload is designed for screen-capture and in-memory pipeline scenarios
    /// where a <see cref="SoftwareBitmap"/> is already available (e.g. from P/Invoke
    /// screen capture). The bitmap is scaled down if either dimension exceeds
    /// <see cref="MaxOcrDimension"/> and then passed to <see cref="OcrEngine"/>
    /// for text recognition.
    /// </para>
    /// </summary>
    /// <param name="bitmap">
    /// A <see cref="SoftwareBitmap"/> in any pixel format. The method converts to
    /// <see cref="BitmapPixelFormat.Bgra8"/> with <see cref="BitmapAlphaMode.Premultiplied"/>
    /// if necessary, as required by <see cref="OcrEngine"/>.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The extracted OCR text, or an empty string if no text is found.</returns>
    public static async Task<string> ExtractTextFromSoftwareBitmapAsync(
        SoftwareBitmap bitmap, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        var ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (ocrEngine is null)
        {
            Log.Warning("No OCR engine available from user profile languages");
            return string.Empty;
        }

        ct.ThrowIfCancellationRequested();

        // Ensure the bitmap is in Bgra8 / Premultiplied — OcrEngine requirement
        SoftwareBitmap? ocrBitmap = null;
        SoftwareBitmap bitmapToOcr;

        if (bitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
            bitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
        {
            ocrBitmap = SoftwareBitmap.Convert(
                bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            bitmapToOcr = ocrBitmap;
        }
        else
        {
            bitmapToOcr = bitmap;
        }

        // Scale down if dimensions exceed MaxOcrDimension
        if (bitmapToOcr.PixelWidth > MaxOcrDimension || bitmapToOcr.PixelHeight > MaxOcrDimension)
        {
            var scale = Math.Min(
                (double)MaxOcrDimension / bitmapToOcr.PixelWidth,
                (double)MaxOcrDimension / bitmapToOcr.PixelHeight);

            var scaledWidth = (uint)(bitmapToOcr.PixelWidth * scale);
            var scaledHeight = (uint)(bitmapToOcr.PixelHeight * scale);

            Log.Debug(
                "Scaling SoftwareBitmap from {OrigW}x{OrigH} to {ScaledW}x{ScaledH} for OCR",
                bitmapToOcr.PixelWidth, bitmapToOcr.PixelHeight, scaledWidth, scaledHeight);

            // Use BitmapDecoder round-trip for scaling — create an in-memory stream,
            // encode the bitmap to it, then decode with a transform
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetSoftwareBitmap(bitmapToOcr);
            await encoder.FlushAsync();

            // Reset stream position for decoding
            stream.Seek(0);

            var transform = new BitmapTransform
            {
                ScaledWidth = scaledWidth,
                ScaledHeight = scaledHeight,
                InterpolationMode = BitmapInterpolationMode.Fant,
            };

            var decoder = await BitmapDecoder.CreateAsync(stream);
            var scaledBitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);

            // Dispose intermediate bitmaps
            ocrBitmap?.Dispose();
            ocrBitmap = scaledBitmap;
            bitmapToOcr = scaledBitmap;
        }

        ct.ThrowIfCancellationRequested();

        // Perform OCR
        OcrResult ocrResult;
        try
        {
            ocrResult = await ocrEngine.RecognizeAsync(bitmapToOcr);
        }
        finally
        {
            // Only dispose the converted/scaled bitmap, not the caller's original
            if (ocrBitmap is not null && !ReferenceEquals(ocrBitmap, bitmap))
                ocrBitmap.Dispose();
        }

        if (ocrResult.Lines.Count == 0)
        {
            Log.Debug("OCR returned no text lines from SoftwareBitmap");
            return string.Empty;
        }

        var extractedText = string.Join(
            Environment.NewLine,
            ocrResult.Lines.Select(line => line.Text));

        return string.IsNullOrWhiteSpace(extractedText) ? string.Empty : extractedText;
    }

    /// <summary>
    /// Counts words by splitting on whitespace, filtering out empty entries.
    /// Ignores the fallback placeholder text when counting.
    /// </summary>
    private static long CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text == "[Image - no text extracted]")
            return 0;

        return text.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries).LongLength;
    }
}
