using System.Text.RegularExpressions;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace AgentX.App.Helpers;

/// <summary>
/// Lightweight keyword-based syntax highlighter for code blocks.
/// Produces colorized <see cref="Run"/> inlines for a <see cref="RichTextBlock"/>.
/// Supports C#, Python, JavaScript/TypeScript, SQL, JSON, HTML/XML, Rust, Go, Java, Bash, YAML, and CSS.
/// </summary>
public static class SyntaxHighlighter
{
    // ── Token colors (One Dark Pro inspired, tuned for Agent-X dark theme) ──

    private static readonly Color KeywordColor = Color.FromArgb(255, 198, 120, 221);    // purple
    private static readonly Color TypeColor = Color.FromArgb(255, 229, 192, 123);        // gold
    private static readonly Color StringColor = Color.FromArgb(255, 152, 195, 121);      // green
    private static readonly Color CommentColor = Color.FromArgb(255, 92, 99, 112);       // grey
    private static readonly Color NumberColor = Color.FromArgb(255, 209, 154, 102);      // orange
    private static readonly Color FunctionColor = Color.FromArgb(255, 97, 175, 239);     // blue
    private static readonly Color OperatorColor = Color.FromArgb(255, 86, 182, 194);     // cyan
    private static readonly Color DefaultColor = Color.FromArgb(230, 220, 220, 230);     // light grey
    private static readonly Color TagColor = Color.FromArgb(255, 224, 108, 117);         // red (HTML tags)
    private static readonly Color AttrColor = Color.FromArgb(255, 209, 154, 102);        // orange (attributes)

    // ── Language definitions ──

    private static readonly Dictionary<string, LanguageDefinition> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["csharp"] = CSharp(),
        ["cs"] = CSharp(),
        ["c#"] = CSharp(),
        ["python"] = Python(),
        ["py"] = Python(),
        ["javascript"] = JavaScript(),
        ["js"] = JavaScript(),
        ["typescript"] = TypeScript(),
        ["ts"] = TypeScript(),
        ["tsx"] = TypeScript(),
        ["jsx"] = JavaScript(),
        ["sql"] = Sql(),
        ["json"] = Json(),
        ["html"] = Html(),
        ["xml"] = Html(),
        ["xaml"] = Html(),
        ["svg"] = Html(),
        ["rust"] = Rust(),
        ["rs"] = Rust(),
        ["go"] = Go(),
        ["golang"] = Go(),
        ["java"] = Java(),
        ["kotlin"] = Java(),
        ["bash"] = Bash(),
        ["sh"] = Bash(),
        ["shell"] = Bash(),
        ["zsh"] = Bash(),
        ["powershell"] = Bash(),
        ["yaml"] = Yaml(),
        ["yml"] = Yaml(),
        ["css"] = Css(),
        ["scss"] = Css(),
        ["cpp"] = Cpp(),
        ["c"] = Cpp(),
        ["c++"] = Cpp(),
    };

    /// <summary>
    /// Returns true if syntax highlighting is available for the given language identifier.
    /// </summary>
    public static bool IsSupported(string? language) =>
        !string.IsNullOrEmpty(language) && Languages.ContainsKey(language);

    /// <summary>
    /// Creates a collection of colored <see cref="Run"/> inlines from a code string.
    /// Falls back to a single default-colored Run if the language is unsupported.
    /// </summary>
    public static List<Run> Highlight(string code, string? language)
    {
        if (string.IsNullOrEmpty(language) || !Languages.TryGetValue(language, out var langDef))
        {
            return [new Run { Text = code, Foreground = new SolidColorBrush(DefaultColor) }];
        }

        return Tokenize(code, langDef);
    }

    // ═══════════════════════════════════════════════════════════════════
    // TOKENIZER
    // ═══════════════════════════════════════════════════════════════════

    private static List<Run> Tokenize(string code, LanguageDefinition lang)
    {
        var runs = new List<Run>();
        var matches = new List<(int Start, int Length, Color Color)>();

        // Apply each pattern and collect all matches
        foreach (var rule in lang.Rules)
        {
            foreach (Match m in rule.Pattern.Matches(code))
            {
                var group = rule.CaptureGroup > 0 && rule.CaptureGroup < m.Groups.Count
                    ? m.Groups[rule.CaptureGroup]
                    : m;
                matches.Add((group.Index, group.Length, rule.Color));
            }
        }

        // Sort by position, longer matches first for ties
        matches.Sort((a, b) =>
        {
            var cmp = a.Start.CompareTo(b.Start);
            return cmp != 0 ? cmp : b.Length.CompareTo(a.Length);
        });

        // Remove overlapping matches (first match wins)
        var filtered = new List<(int Start, int Length, Color Color)>();
        int lastEnd = 0;
        foreach (var m in matches)
        {
            if (m.Start >= lastEnd)
            {
                filtered.Add(m);
                lastEnd = m.Start + m.Length;
            }
        }

        // Build runs
        int pos = 0;
        foreach (var m in filtered)
        {
            // Plain text before this match
            if (m.Start > pos)
            {
                runs.Add(new Run
                {
                    Text = code[pos..m.Start],
                    Foreground = new SolidColorBrush(DefaultColor)
                });
            }

            // Highlighted token
            runs.Add(new Run
            {
                Text = code.Substring(m.Start, m.Length),
                Foreground = new SolidColorBrush(m.Color)
            });

            pos = m.Start + m.Length;
        }

        // Remaining text
        if (pos < code.Length)
        {
            runs.Add(new Run
            {
                Text = code[pos..],
                Foreground = new SolidColorBrush(DefaultColor)
            });
        }

        return runs;
    }

    // ═══════════════════════════════════════════════════════════════════
    // LANGUAGE DEFINITIONS
    // ═══════════════════════════════════════════════════════════════════

    private static LanguageDefinition CSharp() => new(
    [
        Rule(@"//.*$", CommentColor, RegexOptions.Multiline),
        Rule(@"/\*[\s\S]*?\*/", CommentColor),
        Rule(@"@""(?:[^""]|"""")*""", StringColor),
        Rule(@"\$""(?:[^""\\]|\\.)*""", StringColor),
        Rule(@"""(?:[^""\\]|\\.)*""", StringColor),
        Rule(@"'(?:[^'\\]|\\.)'", StringColor),
        Rule(@"\b(abstract|as|async|await|base|bool|break|byte|case|catch|char|checked|class|const|continue|decimal|default|delegate|do|double|else|enum|event|explicit|extern|false|finally|fixed|float|for|foreach|goto|if|implicit|in|int|interface|internal|is|lock|long|namespace|new|null|object|operator|out|override|params|private|protected|public|readonly|record|ref|return|sbyte|sealed|short|sizeof|stackalloc|static|string|struct|switch|this|throw|true|try|typeof|uint|ulong|unchecked|unsafe|ushort|using|var|virtual|void|volatile|while|yield|when|and|or|not|with|init|required|global)\b", KeywordColor),
        Rule(@"\b(Task|List|Dictionary|IEnumerable|IReadOnlyList|Action|Func|Nullable|Span|Memory|CancellationToken|ILogger|StringBuilder|Exception|Guid|DateTime|TimeSpan|Uri)\b", TypeColor),
        Rule(@"\b[A-Z][a-zA-Z0-9]*(?=\s*[<(.])", TypeColor),
        Rule(@"\b([a-zA-Z_]\w*)\s*(?=\()", FunctionColor, captureGroup: 1),
        Rule(@"\b\d+\.?\d*[fFdDmM]?\b", NumberColor),
        Rule(@"[+\-*/%=!<>&|^~?:]+", OperatorColor),
    ]);

    private static LanguageDefinition Python() => new(
    [
        Rule(@"#.*$", CommentColor, RegexOptions.Multiline),
        Rule(@"(""""""[\s\S]*?""""""|'''[\s\S]*?''')", StringColor),
        Rule(@"f""(?:[^""\\]|\\.)*""", StringColor),
        Rule(@"f'(?:[^'\\]|\\.)*'", StringColor),
        Rule(@"""(?:[^""\\]|\\.)*""", StringColor),
        Rule(@"'(?:[^'\\]|\\.)*'", StringColor),
        Rule(@"\b(and|as|assert|async|await|break|class|continue|def|del|elif|else|except|False|finally|for|from|global|if|import|in|is|lambda|None|nonlocal|not|or|pass|raise|return|True|try|while|with|yield)\b", KeywordColor),
        Rule(@"\b(int|float|str|bool|list|dict|tuple|set|bytes|type|object|range|enumerate|map|filter|zip|len|print|super|self|cls)\b", TypeColor),
        Rule(@"\b([a-zA-Z_]\w*)\s*(?=\()", FunctionColor, captureGroup: 1),
        Rule(@"\b\d+\.?\d*[jJ]?\b", NumberColor),
        Rule(@"[+\-*/%=!<>&|^~@:]+", OperatorColor),
    ]);

    private static LanguageDefinition JavaScript() => new(
    [
        Rule(@"//.*$", CommentColor, RegexOptions.Multiline),
        Rule(@"/\*[\s\S]*?\*/", CommentColor),
        Rule(@"`(?:[^`\\]|\\.)*`", StringColor),
        Rule(@"""(?:[^""\\]|\\.)*""", StringColor),
        Rule(@"'(?:[^'\\]|\\.)*'", StringColor),
        Rule(@"\b(async|await|break|case|catch|class|const|continue|debugger|default|delete|do|else|export|extends|false|finally|for|from|function|get|if|import|in|instanceof|let|new|null|of|return|set|static|super|switch|this|throw|true|try|typeof|undefined|var|void|while|with|yield)\b", KeywordColor),
        Rule(@"\b(Array|Boolean|Date|Error|Function|JSON|Map|Math|Number|Object|Promise|Proxy|RegExp|Set|String|Symbol|WeakMap|WeakSet|console|document|window)\b", TypeColor),
        Rule(@"\b([a-zA-Z_$]\w*)\s*(?=\()", FunctionColor, captureGroup: 1),
        Rule(@"\b\d+\.?\d*[eE]?[+-]?\d*n?\b", NumberColor),
        Rule(@"[+\-*/%=!<>&|^~?:]+|=>", OperatorColor),
    ]);

    private static LanguageDefinition TypeScript() => new(
    [
        Rule(@"//.*$", CommentColor, RegexOptions.Multiline),
        Rule(@"/\*[\s\S]*?\*/", CommentColor),
        Rule(@"`(?:[^`\\]|\\.)*`", StringColor),
        Rule(@"""(?:[^""\\]|\\.)*""", StringColor),
        Rule(@"'(?:[^'\\]|\\.)*'", StringColor),
        Rule(@"\b(abstract|as|async|await|break|case|catch|class|const|continue|debugger|declare|default|delete|do|else|enum|export|extends|false|finally|for|from|function|get|if|implements|import|in|instanceof|interface|is|keyof|let|namespace|new|null|of|override|private|protected|public|readonly|return|satisfies|set|static|super|switch|this|throw|true|try|type|typeof|undefined|var|void|while|with|yield)\b", KeywordColor),
        Rule(@"\b(any|boolean|bigint|never|number|object|string|symbol|unknown|void|Array|Promise|Record|Partial|Required|Readonly|Pick|Omit|Exclude|Extract|ReturnType|Parameters)\b", TypeColor),
        Rule(@"\b([a-zA-Z_$]\w*)\s*(?=\()", FunctionColor, captureGroup: 1),
        Rule(@"\b\d+\.?\d*[eE]?[+-]?\d*n?\b", NumberColor),
        Rule(@"[+\-*/%=!<>&|^~?:]+|=>", OperatorColor),
    ]);

    private static LanguageDefinition Sql() => new(
    [
        Rule(@"--.*$", CommentColor, RegexOptions.Multiline),
        Rule(@"/\*[\s\S]*?\*/", CommentColor),
        Rule(@"'(?:[^'\\]|\\.)*'", StringColor),
        Rule(@"\b(?i)(SELECT|FROM|WHERE|INSERT|INTO|UPDATE|SET|DELETE|CREATE|ALTER|DROP|TABLE|INDEX|VIEW|JOIN|INNER|LEFT|RIGHT|OUTER|CROSS|ON|AND|OR|NOT|IN|IS|NULL|AS|ORDER|BY|GROUP|HAVING|LIMIT|OFFSET|UNION|ALL|DISTINCT|COUNT|SUM|AVG|MIN|MAX|BETWEEN|LIKE|EXISTS|CASE|WHEN|THEN|ELSE|END|BEGIN|COMMIT|ROLLBACK|TRANSACTION|PRIMARY|KEY|FOREIGN|REFERENCES|CONSTRAINT|DEFAULT|VALUES|ADD|COLUMN)\b", KeywordColor),
        Rule(@"\b(?i)(INTEGER|TEXT|REAL|BLOB|VARCHAR|CHAR|INT|BIGINT|FLOAT|DOUBLE|DECIMAL|BOOLEAN|DATE|TIMESTAMP|DATETIME)\b", TypeColor),
        Rule(@"\b\d+\.?\d*\b", NumberColor),
        Rule(@"[+\-*/%=!<>&|]+", OperatorColor),
    ]);

    private static LanguageDefinition Json() => new(
    [
        Rule(@"""(?:[^""\\]|\\.)*""\s*(?=:)", FunctionColor),  // keys
        Rule(@"""(?:[^""\\]|\\.)*""", StringColor),
        Rule(@"\b(true|false|null)\b", KeywordColor),
        Rule(@"-?\b\d+\.?\d*([eE][+-]?\d+)?\b", NumberColor),
        Rule(@"[{}\[\]:,]", OperatorColor),
    ]);

    private static LanguageDefinition Html() => new(
    [
        Rule(@"<!--[\s\S]*?-->", CommentColor),
        Rule(@"""(?:[^""\\]|\\.)*""", StringColor),
        Rule(@"'(?:[^'\\]|\\.)*'", StringColor),
        Rule(@"</?[a-zA-Z][\w:.-]*", TagColor),
        Rule(@"/?>", TagColor),
        Rule(@"\b[a-zA-Z:_][\w:.-]*(?==)", AttrColor),
        Rule(@"&\w+;", NumberColor),
    ]);

    private static LanguageDefinition Rust() => new(
    [
        Rule(@"//.*$", CommentColor, RegexOptions.Multiline),
        Rule(@"/\*[\s\S]*?\*/", CommentColor),
        Rule(@"""(?:[^""\\]|\\.)*""", StringColor),
        Rule(@"'(?:[^'\\]|\\.)'", StringColor),
        Rule(@"\b(as|async|await|break|const|continue|crate|dyn|else|enum|extern|false|fn|for|if|impl|in|let|loop|match|mod|move|mut|pub|ref|return|self|Self|static|struct|super|trait|true|type|unsafe|use|where|while|yield)\b", KeywordColor),
        Rule(@"\b(i8|i16|i32|i64|i128|isize|u8|u16|u32|u64|u128|usize|f32|f64|bool|char|str|String|Vec|Box|Rc|Arc|Option|Result|HashMap|HashSet|BTreeMap)\b", TypeColor),
        Rule(@"\b([a-zA-Z_]\w*)\s*(?=\()", FunctionColor, captureGroup: 1),
        Rule(@"\b\d+\.?\d*[fFuUiI]?\d*\b", NumberColor),
        Rule(@"[+\-*/%=!<>&|^~?:]+|->|=>", OperatorColor),
    ]);

    private static LanguageDefinition Go() => new(
    [
        Rule(@"//.*$", CommentColor, RegexOptions.Multiline),
        Rule(@"/\*[\s\S]*?\*/", CommentColor),
        Rule(@"`[^`]*`", StringColor),
        Rule(@"""(?:[^""\\]|\\.)*""", StringColor),
        Rule(@"'(?:[^'\\]|\\.)'", StringColor),
        Rule(@"\b(break|case|chan|const|continue|default|defer|else|fallthrough|for|func|go|goto|if|import|interface|map|package|range|return|select|struct|switch|type|var)\b", KeywordColor),
        Rule(@"\b(bool|byte|complex64|complex128|error|float32|float64|int|int8|int16|int32|int64|rune|string|uint|uint8|uint16|uint32|uint64|uintptr|nil|true|false|iota)\b", TypeColor),
        Rule(@"\b([a-zA-Z_]\w*)\s*(?=\()", FunctionColor, captureGroup: 1),
        Rule(@"\b\d+\.?\d*[eE]?[+-]?\d*\b", NumberColor),
        Rule(@"[+\-*/%=!<>&|^~:]+|:=|<-", OperatorColor),
    ]);

    private static LanguageDefinition Java() => new(
    [
        Rule(@"//.*$", CommentColor, RegexOptions.Multiline),
        Rule(@"/\*[\s\S]*?\*/", CommentColor),
        Rule(@"""(?:[^""\\]|\\.)*""", StringColor),
        Rule(@"'(?:[^'\\]|\\.)'", StringColor),
        Rule(@"\b(abstract|assert|boolean|break|byte|case|catch|char|class|const|continue|default|do|double|else|enum|extends|false|final|finally|float|for|goto|if|implements|import|instanceof|int|interface|long|native|new|null|package|private|protected|public|return|short|static|strictfp|super|switch|synchronized|this|throw|throws|transient|true|try|void|volatile|while|var|yield|sealed|permits|record)\b", KeywordColor),
        Rule(@"\b(String|Integer|Boolean|Double|Float|Long|Short|Byte|Character|Object|Class|List|Map|Set|ArrayList|HashMap|HashSet|Optional|Stream|CompletableFuture|Exception)\b", TypeColor),
        Rule(@"\b([a-zA-Z_]\w*)\s*(?=\()", FunctionColor, captureGroup: 1),
        Rule(@"\b\d+\.?\d*[fFdDlL]?\b", NumberColor),
        Rule(@"[+\-*/%=!<>&|^~?:]+|->", OperatorColor),
    ]);

    private static LanguageDefinition Bash() => new(
    [
        Rule(@"#.*$", CommentColor, RegexOptions.Multiline),
        Rule(@"""(?:[^""\\]|\\.)*""", StringColor),
        Rule(@"'[^']*'", StringColor),
        Rule(@"\b(if|then|else|elif|fi|for|while|do|done|case|esac|in|function|return|local|export|source|alias|unalias|set|unset|readonly|declare|typeset|shift|select|until|break|continue|exit|trap|eval|exec)\b", KeywordColor),
        Rule(@"\b(echo|printf|cd|ls|mkdir|rm|cp|mv|cat|grep|sed|awk|find|sort|uniq|wc|head|tail|curl|wget|git|docker|npm|yarn|pip|sudo|chmod|chown|kill|ps|ssh|scp)\b", FunctionColor),
        Rule(@"\$\{?[a-zA-Z_]\w*\}?", TypeColor),
        Rule(@"\b\d+\.?\d*\b", NumberColor),
        Rule(@"[|&;><]+|&&|\|\||>>|<<", OperatorColor),
    ]);

    private static LanguageDefinition Yaml() => new(
    [
        Rule(@"#.*$", CommentColor, RegexOptions.Multiline),
        Rule(@"""(?:[^""\\]|\\.)*""", StringColor),
        Rule(@"'[^']*'", StringColor),
        Rule(@"^[\w.-]+(?=\s*:)", FunctionColor, RegexOptions.Multiline),
        Rule(@"\b(true|false|yes|no|null|~)\b", KeywordColor),
        Rule(@"\b\d+\.?\d*\b", NumberColor),
        Rule(@"[:\-|>]", OperatorColor),
    ]);

    private static LanguageDefinition Css() => new(
    [
        Rule(@"/\*[\s\S]*?\*/", CommentColor),
        Rule(@"""(?:[^""\\]|\\.)*""", StringColor),
        Rule(@"'(?:[^'\\]|\\.)*'", StringColor),
        Rule(@"[.#][\w-]+", TagColor),
        Rule(@"@[\w-]+", KeywordColor),
        Rule(@"\b(inherit|initial|unset|none|auto|block|inline|flex|grid|absolute|relative|fixed|sticky|solid|dashed|dotted|hidden|visible|scroll|center|left|right|top|bottom|normal|bold|italic|underline|transparent|important)\b", KeywordColor),
        Rule(@"[\w-]+(?=\s*:)", FunctionColor),
        Rule(@"#[0-9a-fA-F]{3,8}\b", NumberColor),
        Rule(@"\b\d+\.?\d*(px|em|rem|%|vh|vw|deg|s|ms)?\b", NumberColor),
        Rule(@"[{};:,]", OperatorColor),
    ]);

    private static LanguageDefinition Cpp() => new(
    [
        Rule(@"//.*$", CommentColor, RegexOptions.Multiline),
        Rule(@"/\*[\s\S]*?\*/", CommentColor),
        Rule(@"#\s*\w+.*$", KeywordColor, RegexOptions.Multiline),
        Rule(@"""(?:[^""\\]|\\.)*""", StringColor),
        Rule(@"'(?:[^'\\]|\\.)'", StringColor),
        Rule(@"\b(alignas|alignof|auto|bool|break|case|catch|char|char8_t|char16_t|char32_t|class|concept|const|consteval|constexpr|constinit|continue|co_await|co_return|co_yield|decltype|default|delete|do|double|else|enum|explicit|export|extern|false|float|for|friend|goto|if|inline|int|long|mutable|namespace|new|noexcept|nullptr|operator|private|protected|public|register|requires|return|short|signed|sizeof|static|static_assert|static_cast|struct|switch|template|this|thread_local|throw|true|try|typedef|typeid|typename|union|unsigned|using|virtual|void|volatile|wchar_t|while)\b", KeywordColor),
        Rule(@"\b(size_t|int8_t|int16_t|int32_t|int64_t|uint8_t|uint16_t|uint32_t|uint64_t|string|vector|map|set|array|unique_ptr|shared_ptr|optional|pair|tuple|function)\b", TypeColor),
        Rule(@"\b(std|cout|cin|endl|printf|scanf|malloc|free|memcpy|strlen|strcmp)\b", TypeColor),
        Rule(@"\b([a-zA-Z_]\w*)\s*(?=\()", FunctionColor, captureGroup: 1),
        Rule(@"\b\d+\.?\d*[fFlLuU]*\b", NumberColor),
        Rule(@"0x[0-9a-fA-F]+", NumberColor),
        Rule(@"[+\-*/%=!<>&|^~?:]+|->|::", OperatorColor),
    ]);

    // ── Helpers ──

    private static TokenRule Rule(string pattern, Color color, RegexOptions options = RegexOptions.None, int captureGroup = 0) =>
        new(new Regex(pattern, RegexOptions.Compiled | options), color, captureGroup);

    private record TokenRule(Regex Pattern, Color Color, int CaptureGroup = 0);

    private record LanguageDefinition(TokenRule[] Rules);
}
