using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Export.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace AgentX.Core.Services.Export.Formatters;

/// <summary>
/// Formats conversations as PowerPoint presentations (.pptx) using the OpenXML SDK.
/// Produces a base64-encoded string of the binary PPTX output per the
/// <see cref="IExportFormatter"/> binary convention.
/// <para>
/// Slide 1 is a title slide with conversation metadata. Subsequent slides present
/// each user/assistant exchange with the user question as a heading and the
/// assistant response as body text.
/// </para>
/// </summary>
public sealed class PptxFormatter : IExportFormatter
{
    public ExportFormat Format => ExportFormat.Pptx;
    public string FileExtension => ".pptx";
    public string MimeType => "application/vnd.openxmlformats-officedocument.presentationml.presentation";

    /// <inheritdoc />
    public async Task<string> ExportConversationAsync(
        ConversationEntity conversation,
        ExportOptions options,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var title = options.Title ?? conversation.Title ?? "Conversation Export";

        var bytes = await Task.Run(() =>
        {
            using var ms = new MemoryStream();
            BuildPptx(conversation, options, title, ms, ct);
            return ms.ToArray();
        }, ct);

        return Convert.ToBase64String(bytes);
    }

    /// <inheritdoc />
    public async Task<string> ExportConversationsAsync(
        IReadOnlyList<ConversationEntity> conversations,
        ExportOptions options,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // For batch PPTX, use the first conversation to produce a combined presentation.
        // Each conversation gets its own title slide followed by content slides.
        var title = options.Title ?? "Conversation Export";

        var bytes = await Task.Run(() =>
        {
            using var ms = new MemoryStream();
            BuildBatchPptx(conversations, options, title, ms, ct);
            return ms.ToArray();
        }, ct);

        return Convert.ToBase64String(bytes);
    }

    // ════════════════════════════════════════════════════════════════
    //  PPTX generation
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds a PPTX presentation for a single conversation into the provided stream.
    /// </summary>
    private static void BuildPptx(
        ConversationEntity conversation,
        ExportOptions options,
        string title,
        Stream outputStream,
        CancellationToken ct)
    {
        using var presentationDoc = PresentationDocument.Create(outputStream, PresentationDocumentType.Presentation);
        var (presentationPart, presentation, slideMasterPart, slideLayoutPart, _) =
            InitializePresentation(presentationDoc);

        uint slideIdCounter = 256U;
        var messages = conversation.Messages
            .OrderBy(m => m.SortOrder)
            .Where(m => m.Role != "system")
            .ToList();

        // -- Title slide
        var titleSlidePart = presentationPart.AddNewPart<SlidePart>();
        titleSlidePart.Slide = CreatePptxTitleSlide(title, conversation, options);
        titleSlidePart.AddPart(slideLayoutPart);

        var titleSlideRelId = presentationPart.GetIdOfPart(titleSlidePart);
        presentation.SlideIdList!.AppendChild(
            new P.SlideId { Id = slideIdCounter++, RelationshipId = titleSlideRelId });

        // -- Content slides -- one per user/assistant pair
        for (var i = 0; i < messages.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var message = messages[i];

            if (message.Role == "user")
            {
                var heading = TruncateText(message.Content, 200);
                var body = string.Empty;

                // Pair with the following assistant response if available
                if (i + 1 < messages.Count && messages[i + 1].Role == "assistant")
                {
                    body = TruncateText(messages[i + 1].Content, 800);
                    i++; // Skip the paired assistant message
                }

                var slidePart = presentationPart.AddNewPart<SlidePart>();
                slidePart.Slide = CreatePptxContentSlide(heading, body);
                slidePart.AddPart(slideLayoutPart);

                var slideRelId = presentationPart.GetIdOfPart(slidePart);
                presentation.SlideIdList!.AppendChild(
                    new P.SlideId { Id = slideIdCounter++, RelationshipId = slideRelId });
            }
            else if (message.Role == "assistant")
            {
                // Orphaned assistant message (no preceding user question)
                var slidePart = presentationPart.AddNewPart<SlidePart>();
                slidePart.Slide = CreatePptxContentSlide("Response", TruncateText(message.Content, 800));
                slidePart.AddPart(slideLayoutPart);

                var slideRelId = presentationPart.GetIdOfPart(slidePart);
                presentation.SlideIdList!.AppendChild(
                    new P.SlideId { Id = slideIdCounter++, RelationshipId = slideRelId });
            }
        }

        presentation.Save();
    }

    /// <summary>
    /// Builds a PPTX presentation for multiple conversations into the provided stream.
    /// Each conversation gets its own title slide followed by content slides.
    /// </summary>
    private static void BuildBatchPptx(
        IReadOnlyList<ConversationEntity> conversations,
        ExportOptions options,
        string title,
        Stream outputStream,
        CancellationToken ct)
    {
        using var presentationDoc = PresentationDocument.Create(outputStream, PresentationDocumentType.Presentation);
        var (presentationPart, presentation, slideMasterPart, slideLayoutPart, _) =
            InitializePresentation(presentationDoc);

        uint slideIdCounter = 256U;

        // -- Overall title slide
        var batchTitleSlidePart = presentationPart.AddNewPart<SlidePart>();
        batchTitleSlidePart.Slide = CreatePptxBatchTitleSlide(title, conversations.Count);
        batchTitleSlidePart.AddPart(slideLayoutPart);

        var batchTitleRelId = presentationPart.GetIdOfPart(batchTitleSlidePart);
        presentation.SlideIdList!.AppendChild(
            new P.SlideId { Id = slideIdCounter++, RelationshipId = batchTitleRelId });

        // -- Each conversation's slides
        foreach (var conversation in conversations)
        {
            ct.ThrowIfCancellationRequested();

            var messages = conversation.Messages
                .OrderBy(m => m.SortOrder)
                .Where(m => m.Role != "system")
                .ToList();

            // Conversation title slide
            var convTitleSlidePart = presentationPart.AddNewPart<SlidePart>();
            convTitleSlidePart.Slide = CreatePptxTitleSlide(
                conversation.Title ?? "Conversation", conversation, options);
            convTitleSlidePart.AddPart(slideLayoutPart);

            var convTitleRelId = presentationPart.GetIdOfPart(convTitleSlidePart);
            presentation.SlideIdList!.AppendChild(
                new P.SlideId { Id = slideIdCounter++, RelationshipId = convTitleRelId });

            // Content slides
            for (var i = 0; i < messages.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var message = messages[i];

                if (message.Role == "user")
                {
                    var heading = TruncateText(message.Content, 200);
                    var body = string.Empty;

                    if (i + 1 < messages.Count && messages[i + 1].Role == "assistant")
                    {
                        body = TruncateText(messages[i + 1].Content, 800);
                        i++;
                    }

                    var slidePart = presentationPart.AddNewPart<SlidePart>();
                    slidePart.Slide = CreatePptxContentSlide(heading, body);
                    slidePart.AddPart(slideLayoutPart);

                    var slideRelId = presentationPart.GetIdOfPart(slidePart);
                    presentation.SlideIdList!.AppendChild(
                        new P.SlideId { Id = slideIdCounter++, RelationshipId = slideRelId });
                }
                else if (message.Role == "assistant")
                {
                    var slidePart = presentationPart.AddNewPart<SlidePart>();
                    slidePart.Slide = CreatePptxContentSlide("Response", TruncateText(message.Content, 800));
                    slidePart.AddPart(slideLayoutPart);

                    var slideRelId = presentationPart.GetIdOfPart(slidePart);
                    presentation.SlideIdList!.AppendChild(
                        new P.SlideId { Id = slideIdCounter++, RelationshipId = slideRelId });
                }
            }
        }

        presentation.Save();
    }

    // ════════════════════════════════════════════════════════════════
    //  Presentation infrastructure
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Initializes the presentation with slide master, theme, and slide layout.
    /// Returns the key parts needed to add slides.
    /// </summary>
    private static (
        PresentationPart presentationPart,
        P.Presentation presentation,
        SlideMasterPart slideMasterPart,
        SlideLayoutPart slideLayoutPart,
        ThemePart themePart)
        InitializePresentation(PresentationDocument presentationDoc)
    {
        var presentationPart = presentationDoc.AddPresentationPart();
        var presentation = new P.Presentation(
            new P.SlideMasterIdList(),
            new P.SlideIdList(),
            new P.SlideSize { Cx = 12192000, Cy = 6858000, Type = P.SlideSizeValues.Screen16x9 },
            new P.NotesSize { Cx = 6858000, Cy = 9144000 });
        presentationPart.Presentation = presentation;

        // -- Slide master
        var slideMasterPart = presentationPart.AddNewPart<SlideMasterPart>();
        slideMasterPart.SlideMaster = CreateMinimalSlideMaster();

        // -- Theme
        var themePart = slideMasterPart.AddNewPart<ThemePart>();
        themePart.Theme = CreateMinimalTheme();

        // -- Slide layout
        var slideLayoutPart = slideMasterPart.AddNewPart<SlideLayoutPart>();
        slideLayoutPart.SlideLayout = CreateMinimalSlideLayout();

        // Wire slide master ID in the presentation
        var slideMasterRelId = presentationPart.GetIdOfPart(slideMasterPart);
        presentation.SlideMasterIdList!.AppendChild(
            new P.SlideMasterId { Id = 2147483648U, RelationshipId = slideMasterRelId });

        // Wire slide layout ID in the slide master
        var slideLayoutRelId = slideMasterPart.GetIdOfPart(slideLayoutPart);
        slideMasterPart.SlideMaster.SlideLayoutIdList!.AppendChild(
            new P.SlideLayoutId { Id = 2147483649U, RelationshipId = slideLayoutRelId });

        return (presentationPart, presentation, slideMasterPart, slideLayoutPart, themePart);
    }

    // ════════════════════════════════════════════════════════════════
    //  Slide creation
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a PPTX title slide with the conversation title and metadata.
    /// </summary>
    private static P.Slide CreatePptxTitleSlide(
        string title,
        ConversationEntity conversation,
        ExportOptions options)
    {
        // Subtitle content
        var subtitleParts = new List<string>();
        if (options.IncludeMetadata)
        {
            subtitleParts.Add($"Exported: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            subtitleParts.Add($"Messages: {conversation.MessageCount}");
            if (!string.IsNullOrWhiteSpace(conversation.ModelId))
            {
                subtitleParts.Add($"Model: {conversation.ModelId}");
            }
        }

        var subtitleText = string.Join("  |  ", subtitleParts);
        if (string.IsNullOrWhiteSpace(subtitleText))
        {
            subtitleText = "Agent-X Export";
        }

        var titleShape = CreatePptxTextShape(
            id: 2U,
            name: "Title",
            x: 457200,
            y: 1600000,
            cx: 11277600,
            cy: 1600000,
            text: title,
            fontSize: 4400,
            bold: true,
            color: "1A1A2E");

        var subtitleShape = CreatePptxTextShape(
            id: 3U,
            name: "Subtitle",
            x: 457200,
            y: 3400000,
            cx: 11277600,
            cy: 800000,
            text: subtitleText,
            fontSize: 1800,
            bold: false,
            color: "6C757D");

        return CreatePptxSlide(titleShape, subtitleShape);
    }

    /// <summary>
    /// Creates a batch title slide with the export title and conversation count.
    /// </summary>
    private static P.Slide CreatePptxBatchTitleSlide(string title, int conversationCount)
    {
        var titleShape = CreatePptxTextShape(
            id: 2U,
            name: "Title",
            x: 457200,
            y: 1600000,
            cx: 11277600,
            cy: 1600000,
            text: title,
            fontSize: 4400,
            bold: true,
            color: "1A1A2E");

        var subtitleShape = CreatePptxTextShape(
            id: 3U,
            name: "Subtitle",
            x: 457200,
            y: 3400000,
            cx: 11277600,
            cy: 800000,
            text: $"Exported {conversationCount} conversations on {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            fontSize: 1800,
            bold: false,
            color: "6C757D");

        return CreatePptxSlide(titleShape, subtitleShape);
    }

    /// <summary>
    /// Creates a PPTX content slide with a heading and body text.
    /// Used for each user/assistant exchange.
    /// </summary>
    private static P.Slide CreatePptxContentSlide(string heading, string body)
    {
        var shapes = new List<P.Shape>
        {
            CreatePptxTextShape(
                id: 2U,
                name: "Heading",
                x: 457200,
                y: 274638,
                cx: 11277600,
                cy: 1200000,
                text: heading,
                fontSize: 2800,
                bold: true,
                color: "0D6EFD"),
        };

        if (!string.IsNullOrWhiteSpace(body))
        {
            shapes.Add(CreatePptxTextShape(
                id: 3U,
                name: "Body",
                x: 457200,
                y: 1700000,
                cx: 11277600,
                cy: 4800000,
                text: body,
                fontSize: 1600,
                bold: false,
                color: "212529"));
        }

        return CreatePptxSlide(shapes.ToArray());
    }

    /// <summary>
    /// Builds a PPTX Slide containing the provided shapes inside a ShapeTree.
    /// </summary>
    private static P.Slide CreatePptxSlide(params P.Shape[] shapes)
    {
        var shapeTree = new P.ShapeTree();

        // Group shape properties (required root element)
        var nvGrpSpPr = new P.NonVisualGroupShapeProperties();
        nvGrpSpPr.Append(new P.NonVisualDrawingProperties { Id = 1U, Name = "" });
        nvGrpSpPr.Append(new P.NonVisualGroupShapeDrawingProperties());
        nvGrpSpPr.Append(new P.ApplicationNonVisualDrawingProperties());
        shapeTree.Append(nvGrpSpPr);

        var grpSpPr = new P.GroupShapeProperties();
        grpSpPr.Append(new A.TransformGroup());
        shapeTree.Append(grpSpPr);

        // Individual shapes
        foreach (var shape in shapes)
        {
            shapeTree.Append(shape);
        }

        return new P.Slide(
            new P.CommonSlideData(shapeTree),
            new P.ColorMapOverride(new A.MasterColorMapping()));
    }

    /// <summary>
    /// Creates a PPTX Shape element with text content at the specified position.
    /// Font sizes are in hundredths of a point (e.g. 4400 = 44pt).
    /// Positions and extents are in EMUs (1 inch = 914400 EMUs).
    /// </summary>
    private static P.Shape CreatePptxTextShape(
        uint id,
        string name,
        long x,
        long y,
        long cx,
        long cy,
        string text,
        int fontSize,
        bool bold,
        string color)
    {
        var shape = new P.Shape();

        // -- Non-visual shape properties
        var nvSpPr = new P.NonVisualShapeProperties();
        nvSpPr.Append(new P.NonVisualDrawingProperties { Id = id, Name = name });
        nvSpPr.Append(new P.NonVisualShapeDrawingProperties());
        nvSpPr.Append(new P.ApplicationNonVisualDrawingProperties());
        shape.Append(nvSpPr);

        // -- Shape properties (position + geometry)
        var spPr = new P.ShapeProperties();
        var xfrm = new A.Transform2D();
        xfrm.Append(new A.Offset { X = x, Y = y });
        xfrm.Append(new A.Extents { Cx = cx, Cy = cy });
        spPr.Append(xfrm);
        spPr.Append(new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle });
        shape.Append(spPr);

        // -- Text body
        var txBody = new P.TextBody();
        txBody.Append(new A.BodyProperties());
        txBody.Append(new A.ListStyle());

        var para = new A.Paragraph();
        var run = new A.Run();

        var rPr = new A.RunProperties { FontSize = fontSize, Bold = bold, Language = "en-US" };
        var solidFill = new A.SolidFill(new A.RgbColorModelHex { Val = color });
        rPr.Append(solidFill);
        run.Append(rPr);
        run.Append(new A.Text(text));

        para.Append(run);
        txBody.Append(para);
        shape.Append(txBody);

        return shape;
    }

    // ════════════════════════════════════════════════════════════════
    //  Minimal presentation infrastructure
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a minimal but valid SlideMaster for a PPTX presentation.
    /// </summary>
    private static P.SlideMaster CreateMinimalSlideMaster()
    {
        var slideMaster = new P.SlideMaster();

        var commonSlideData = new P.CommonSlideData();
        var shapeTree = new P.ShapeTree();
        var nvGrpSpPr = new P.NonVisualGroupShapeProperties();
        nvGrpSpPr.Append(new P.NonVisualDrawingProperties { Id = 1U, Name = "" });
        nvGrpSpPr.Append(new P.NonVisualGroupShapeDrawingProperties());
        nvGrpSpPr.Append(new P.ApplicationNonVisualDrawingProperties());
        shapeTree.Append(nvGrpSpPr);

        var grpSpPr = new P.GroupShapeProperties();
        grpSpPr.Append(new A.TransformGroup());
        shapeTree.Append(grpSpPr);

        commonSlideData.Append(shapeTree);
        slideMaster.Append(commonSlideData);

        slideMaster.Append(new P.ColorMap
        {
            Background1 = A.ColorSchemeIndexValues.Light1,
            Text1 = A.ColorSchemeIndexValues.Dark1,
            Background2 = A.ColorSchemeIndexValues.Light2,
            Text2 = A.ColorSchemeIndexValues.Dark2,
            Accent1 = A.ColorSchemeIndexValues.Accent1,
            Accent2 = A.ColorSchemeIndexValues.Accent2,
            Accent3 = A.ColorSchemeIndexValues.Accent3,
            Accent4 = A.ColorSchemeIndexValues.Accent4,
            Accent5 = A.ColorSchemeIndexValues.Accent5,
            Accent6 = A.ColorSchemeIndexValues.Accent6,
            Hyperlink = A.ColorSchemeIndexValues.Hyperlink,
            FollowedHyperlink = A.ColorSchemeIndexValues.FollowedHyperlink
        });

        slideMaster.Append(new P.SlideLayoutIdList());

        return slideMaster;
    }

    /// <summary>
    /// Creates a minimal but valid SlideLayout for a PPTX presentation.
    /// </summary>
    private static P.SlideLayout CreateMinimalSlideLayout()
    {
        var slideLayout = new P.SlideLayout();

        var commonSlideData = new P.CommonSlideData();
        var shapeTree = new P.ShapeTree();
        var nvGrpSpPr = new P.NonVisualGroupShapeProperties();
        nvGrpSpPr.Append(new P.NonVisualDrawingProperties { Id = 1U, Name = "" });
        nvGrpSpPr.Append(new P.NonVisualGroupShapeDrawingProperties());
        nvGrpSpPr.Append(new P.ApplicationNonVisualDrawingProperties());
        shapeTree.Append(nvGrpSpPr);

        var grpSpPr = new P.GroupShapeProperties();
        grpSpPr.Append(new A.TransformGroup());
        shapeTree.Append(grpSpPr);

        commonSlideData.Append(shapeTree);
        slideLayout.Append(commonSlideData);

        slideLayout.Append(new P.ColorMap
        {
            Background1 = A.ColorSchemeIndexValues.Light1,
            Text1 = A.ColorSchemeIndexValues.Dark1,
            Background2 = A.ColorSchemeIndexValues.Light2,
            Text2 = A.ColorSchemeIndexValues.Dark2,
            Accent1 = A.ColorSchemeIndexValues.Accent1,
            Accent2 = A.ColorSchemeIndexValues.Accent2,
            Accent3 = A.ColorSchemeIndexValues.Accent3,
            Accent4 = A.ColorSchemeIndexValues.Accent4,
            Accent5 = A.ColorSchemeIndexValues.Accent5,
            Accent6 = A.ColorSchemeIndexValues.Accent6,
            Hyperlink = A.ColorSchemeIndexValues.Hyperlink,
            FollowedHyperlink = A.ColorSchemeIndexValues.FollowedHyperlink
        });

        return slideLayout;
    }

    /// <summary>
    /// Creates a minimal but valid Theme for a PPTX presentation.
    /// Uses Agent-X brand colors (blue accent #0D6EFD) with Calibri font.
    /// </summary>
    private static A.Theme CreateMinimalTheme()
    {
        var theme = new A.Theme { Name = "Agent-X" };
        var themeElements = new A.ThemeElements();

        // -- Color scheme
        var colorScheme = new A.ColorScheme { Name = "Agent-X" };
        var dk1 = new A.Dark1Color();
        dk1.Append(new A.RgbColorModelHex { Val = "000000" });
        colorScheme.Append(dk1);

        var lt1 = new A.Light1Color();
        lt1.Append(new A.RgbColorModelHex { Val = "FFFFFF" });
        colorScheme.Append(lt1);

        var dk2 = new A.Dark2Color();
        dk2.Append(new A.RgbColorModelHex { Val = "44546A" });
        colorScheme.Append(dk2);

        var lt2 = new A.Light2Color();
        lt2.Append(new A.RgbColorModelHex { Val = "E7E6E6" });
        colorScheme.Append(lt2);

        var accent1 = new A.Accent1Color();
        accent1.Append(new A.RgbColorModelHex { Val = "0D6EFD" });
        colorScheme.Append(accent1);

        var accent2 = new A.Accent2Color();
        accent2.Append(new A.RgbColorModelHex { Val = "E04545" });
        colorScheme.Append(accent2);

        var accent3 = new A.Accent3Color();
        accent3.Append(new A.RgbColorModelHex { Val = "E29B0E" });
        colorScheme.Append(accent3);

        var accent4 = new A.Accent4Color();
        accent4.Append(new A.RgbColorModelHex { Val = "36A621" });
        colorScheme.Append(accent4);

        var accent5 = new A.Accent5Color();
        accent5.Append(new A.RgbColorModelHex { Val = "0D6EFD" });
        colorScheme.Append(accent5);

        var accent6 = new A.Accent6Color();
        accent6.Append(new A.RgbColorModelHex { Val = "6C757D" });
        colorScheme.Append(accent6);

        var hlink = new A.Hyperlink();
        hlink.Append(new A.RgbColorModelHex { Val = "0D6EFD" });
        colorScheme.Append(hlink);

        var folHlink = new A.FollowedHyperlinkColor();
        folHlink.Append(new A.RgbColorModelHex { Val = "800080" });
        colorScheme.Append(folHlink);

        themeElements.Append(colorScheme);

        // -- Font scheme
        var fontScheme = new A.FontScheme { Name = "Agent-X" };
        var majorFont = new A.MajorFont();
        majorFont.Append(new A.LatinFont { Typeface = "Calibri" });
        fontScheme.Append(majorFont);

        var minorFont = new A.MinorFont();
        minorFont.Append(new A.LatinFont { Typeface = "Calibri" });
        fontScheme.Append(minorFont);

        themeElements.Append(fontScheme);

        // -- Format scheme
        var formatScheme = new A.FormatScheme { Name = "Agent-X" };

        // Fill styles (3 required)
        var fillStyleList = new A.FillStyleList();
        fillStyleList.Append(new A.SolidFill(new A.RgbColorModelHex { Val = "FFFFFF" }));
        fillStyleList.Append(new A.SolidFill(new A.RgbColorModelHex { Val = "E7E6E6" }));
        fillStyleList.Append(new A.SolidFill(new A.RgbColorModelHex { Val = "44546A" }));
        formatScheme.Append(fillStyleList);

        // Line styles (3 required)
        var lineStyleList = new A.LineStyleList();
        var line1 = new A.Outline { Width = 9525 };
        line1.Append(new A.SolidFill(new A.RgbColorModelHex { Val = "000000" }));
        lineStyleList.Append(line1);

        var line2 = new A.Outline { Width = 25400 };
        line2.Append(new A.SolidFill(new A.RgbColorModelHex { Val = "44546A" }));
        lineStyleList.Append(line2);

        var line3 = new A.Outline { Width = 38100 };
        line3.Append(new A.SolidFill(new A.RgbColorModelHex { Val = "44546A" }));
        lineStyleList.Append(line3);
        formatScheme.Append(lineStyleList);

        // Effect styles (3 required)
        var effectStyleList = new A.EffectStyleList();
        effectStyleList.Append(new A.EffectStyle(new A.EffectList()));
        effectStyleList.Append(new A.EffectStyle(new A.EffectList()));
        effectStyleList.Append(new A.EffectStyle(new A.EffectList()));
        formatScheme.Append(effectStyleList);

        // Background fill styles (3 required)
        var bgFillStyleList = new A.BackgroundFillStyleList();
        bgFillStyleList.Append(new A.SolidFill(new A.RgbColorModelHex { Val = "FFFFFF" }));
        bgFillStyleList.Append(new A.SolidFill(new A.RgbColorModelHex { Val = "E7E6E6" }));
        bgFillStyleList.Append(new A.SolidFill(new A.RgbColorModelHex { Val = "44546A" }));
        formatScheme.Append(bgFillStyleList);

        themeElements.Append(formatScheme);

        theme.Append(themeElements);
        return theme;
    }

    // ════════════════════════════════════════════════════════════════
    //  Utility
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Truncates text to the specified maximum length, appending an ellipsis
    /// when the text exceeds the limit.
    /// </summary>
    private static string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        if (text.Length <= maxLength)
            return text;

        return text[..maxLength] + "...";
    }
}
