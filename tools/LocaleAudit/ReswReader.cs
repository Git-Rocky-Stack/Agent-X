using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace LocaleAudit;

public static class ReswReader
{
    public static IReadOnlyDictionary<string, string> ReadFile(string path)
    {
        var doc = XDocument.Load(path);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var data in doc.Root!.Elements("data"))
        {
            var name = data.Attribute("name")?.Value;
            var value = data.Element("value")?.Value ?? string.Empty;
            if (!string.IsNullOrEmpty(name))
                result[name] = value;
        }
        return result;
    }

    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ReadAllLocales(string stringsRoot)
    {
        if (!Directory.Exists(stringsRoot))
            throw new DirectoryNotFoundException($"Strings root not found: {stringsRoot}");

        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var localeDir in Directory.EnumerateDirectories(stringsRoot))
        {
            var locale = Path.GetFileName(localeDir);
            var reswPath = Path.Combine(localeDir, "Resources.resw");
            result[locale] = File.Exists(reswPath) ? ReadFile(reswPath) : new Dictionary<string, string>();
        }
        return result;
    }
}
