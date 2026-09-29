using System.Text;

namespace AgentX.Core.Services.Annotations;

/// <summary>
/// A range of a passage chosen for a highlight. Offsets are zero-based, <see cref="End"/> is
/// exclusive, and <see cref="Text"/> is exactly the passage text between them.
/// </summary>
public readonly record struct AnnotationRange(int Start, int End, string Text);

/// <summary>
/// Places text the operator selected in a viewer onto the passage it was selected from, so a
/// highlight is saved with offsets that point into the passage text itself.
/// </summary>
/// <remarks>
/// A viewer reports the selected string and at best an approximate position: WinUI text
/// pointers count element boundaries as well as characters, and a selection can come back
/// with other line breaks than the passage holds. The selection is therefore looked up in the
/// passage, exactly first and then with every run of white space treated alike, and when it
/// occurs more than once the occurrence nearest the reported position wins. Surrounding white
/// space is not part of a highlight.
/// </remarks>
public static class AnnotationSelection
{
    /// <summary>
    /// Finds <paramref name="selectedText"/> in <paramref name="passage"/>.
    /// </summary>
    /// <param name="passage">The passage text the viewer shows.</param>
    /// <param name="selectedText">The text the viewer reports as selected.</param>
    /// <param name="positionHint">
    /// Where the viewer says the selection starts, in characters from the start of the passage;
    /// negative when unknown, in which case the first occurrence is taken.
    /// </param>
    /// <returns>The range, or null when the selection is empty or not part of the passage.</returns>
    public static AnnotationRange? Locate(string? passage, string? selectedText, int positionHint)
    {
        if (string.IsNullOrEmpty(passage) || string.IsNullOrWhiteSpace(selectedText))
        {
            return null;
        }

        var wanted = selectedText.Trim();

        var exact = NearestOccurrence(passage, wanted, positionHint, index => index);
        if (exact >= 0)
        {
            return new AnnotationRange(exact, exact + wanted.Length, passage.Substring(exact, wanted.Length));
        }

        // Line breaks and spacing may differ between what the viewer returns and the stored
        // text, so compare with each run of white space collapsed to one space. The map gives,
        // for every character of the collapsed passage, where it sits in the original.
        var (flatPassage, map) = CollapseWhiteSpace(passage);
        var (flatWanted, _) = CollapseWhiteSpace(wanted);

        var flatStart = NearestOccurrence(flatPassage, flatWanted, positionHint, index => map[index]);
        if (flatStart < 0)
        {
            return null;
        }

        // The wanted text is trimmed, so the match starts and ends on characters that are not
        // white space, and both ends map to real characters of the passage.
        var start = map[flatStart];
        var end = map[flatStart + flatWanted.Length - 1] + 1;
        return new AnnotationRange(start, end, passage[start..end]);
    }

    /// <summary>
    /// The occurrence of <paramref name="value"/> in <paramref name="text"/> whose original
    /// position is nearest <paramref name="positionHint"/>, or -1 when there is none.
    /// </summary>
    private static int NearestOccurrence(string text, string value, int positionHint, Func<int, int> originalPosition)
    {
        var best = -1;
        var bestDistance = long.MaxValue;

        for (var index = text.IndexOf(value, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(value, index + 1, StringComparison.Ordinal))
        {
            if (positionHint < 0)
            {
                return index;
            }

            var distance = Math.Abs((long)originalPosition(index) - positionHint);
            if (distance < bestDistance)
            {
                best = index;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static (string Text, int[] Map) CollapseWhiteSpace(string text)
    {
        var builder = new StringBuilder(text.Length);
        var map = new List<int>(text.Length);
        var inWhiteSpace = false;

        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                if (!inWhiteSpace)
                {
                    builder.Append(' ');
                    map.Add(i);
                    inWhiteSpace = true;
                }

                continue;
            }

            builder.Append(text[i]);
            map.Add(i);
            inWhiteSpace = false;
        }

        return (builder.ToString(), map.ToArray());
    }
}
