using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using SourceCookifier;

class UnicodeRegexTest
{
    static readonly Type CTags = typeof(Settings).Assembly.GetType("SourceCookifier.CTagsExe", true);
    static readonly BindingFlags StaticMethods = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    static object Call(string method, params object[] args)
    {
        try
        {
            return CTags.GetMethod(method, StaticMethods).Invoke(null, args);
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException;
        }
    }

    static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    static Source NewSource()
    {
        Source source = new Source();
        source.Language = "TXT";
        source.ScopeOperator = ".";
        source.TagTypes["a"] = new TagType();
        source.TagTypes["b"] = new TagType();
        return source;
    }

    static void Parse(string path)
    {
        Call("DoTags", "\"" + path + "\"", "TXT", ".txt", "");
    }

    static void Main(string[] args)
    {
        if (args.Length != 2)
            throw new ArgumentException("Expected ctags directory and writable test directory.");

        Settings.PluginSubFolderBin = Path.GetFullPath(args[0]);
        Settings.ConfigDir = Path.GetFullPath(args[1]);
        Settings.Configs = new Settings.Config();
        Settings.Languages = new SerializableDictionary<string, Language>();

        Language language = new Language { BuildIn = false };
        TagType chinese = new TagType { BuildIn = false };
        chinese.RegexPatterns.Add("/\u300a(.*?)\u300b/\\1/a/ei");
        language.TagTypes["a"] = chinese;
        TagType legacy = new TagType { BuildIn = false };
        legacy.RegexPatterns.Add("/^MARK[ ]+([[:alnum:]]+)/\\1/b/e");
        language.TagTypes["b"] = legacy;
        Settings.Languages["TXT"] = language;

        Call("Init");
        Check((bool)Call("HasLazyStarQuantifier", "\u300a(.*?)\u300b"), "Lazy quantifier not detected.");
        Check(!(bool)Call("HasLazyStarQuantifier", @"\*\?"), "Escaped characters treated as lazy.");
        Check(!(bool)Call("HasLazyStarQuantifier", @"[?*]"), "Character class treated as lazy.");
        Check(!(bool)Call("HasLazyStarQuantifier", @"[[:space:]*?]"), "POSIX class treated as lazy.");
        Check(!(bool)Call("HasLazyStarQuantifier", @"[]*?]"), "Leading bracket treated as a class end.");
        Check(!(bool)Call("HasLazyStarQuantifier", @"[^,:;[:space:]]+?"), "Legacy regex was rerouted.");

        string path = Path.Combine(Settings.ConfigDir, "unicode-regex-test.txt");
        string text = "\u300a\u7b2c\u4e00\u300b \u300a\u7b2c\u4e8c\u300b\r\n" +
            "MARK abc\r\n\u300a\u7b2c\u4e09\u300b\r\n\u300a\u300b\r\n";
        foreach (Encoding encoding in new Encoding[] { new UTF8Encoding(false), Encoding.Unicode })
        {
            File.WriteAllText(path, text, encoding);
            Parse(path);
            Source source = NewSource();
            Call("MapTags", source);
            List<Tag> tags = source.TagTypes["a"].Tags;
            Check(tags.Count == 3, "Expected three Chinese tags.");
            Check(tags[0].TagName == "\u7b2c\u4e00" && tags[0].Line == 1, "First tag mismatch.");
            Check(tags[1].TagName == "\u7b2c\u4e8c" && tags[1].Line == 1, "Second tag mismatch.");
            Check(tags[0].SortPosition < tags[1].SortPosition, "Same-line match positions are incorrect.");
            Check(tags[2].TagName == "\u7b2c\u4e09" && tags[2].Line == 3, "Third tag mismatch.");
            Settings.Configs.TagSort = Settings.TagSortMode.DocumentReverse;
            Tag[] reversed = tags.ToArray();
            Array.Sort(reversed, (a, b) => new TagNodeSorter().Compare(a, b));
            Check(reversed[0] == tags[2] && reversed[1] == tags[1] && reversed[2] == tags[0],
                "Parsed matches were not sorted in reverse document order.");
            Settings.Configs.TagSort = Settings.TagSortMode.Legacy;
            Check(source.TagTypes["b"].Tags.Count == 1 &&
                source.TagTypes["b"].Tags[0].TagName == "abc", "Legacy ctags rule failed.");

            IncludeFile include = new IncludeFile { QuickTags = new List<QuickTag>() };
            Call("MapQuickTags", include, "TXT", ".");
            Check(include.QuickTags.Count == 4, "Quick tags missed regex or ctags results.");
        }

        Settings.Configs.MaxNumShownSymbols = "3";
        Parse(path);
        try
        {
            Call("MapTags", NewSource());
            throw new Exception("Tag limit was not enforced.");
        }
        catch (Exception ex)
        {
            Check(ex.Message.Contains("Too many symbols: 4"), "Unexpected tag limit result: " + ex.Message);
        }

        Settings.Configs.MaxNumShownSymbols = "2048";
        legacy.Show = false;
        Parse(path);
        Source onlyManaged = NewSource();
        Call("MapTags", onlyManaged);
        Check(onlyManaged.TagTypes["a"].Tags.Count == 3, "Managed-only language failed.");

        chinese.RegexPatterns.Add("/^MARK[ ]+([[:alnum:]]+)/\\1/a/e");
        Parse(path);
        Source mixedKind = NewSource();
        Call("MapTags", mixedKind);
        Check(mixedKind.TagTypes["a"].Tags.Count == 4, "Mixed rules for one tag type failed.");
        chinese.RegexPatterns.RemoveAt(1);

        chinese.Show = false;
        legacy.Show = true;
        Parse(path);
        Source onlyLegacy = NewSource();
        Call("MapTags", onlyLegacy);
        Check(onlyLegacy.TagTypes["a"].Tags.Count == 0 &&
            onlyLegacy.TagTypes["b"].Tags.Count == 1, "Results leaked from a previous parse.");

        legacy.Show = false;
        chinese.Show = true;
        chinese.RegexPatterns[0] = "/\u300a(.*?)-(.*?)\u300b/\\2=\\1/a/e";
        File.WriteAllText(path, "\u300a\u7532-\u4e59\u300b", new UTF8Encoding(false));
        Parse(path);
        Source groups = NewSource();
        Call("MapTags", groups);
        Check(groups.TagTypes["a"].Tags.Count == 1 &&
            groups.TagTypes["a"].Tags[0].TagName == "\u4e59=\u7532", "Capture expansion failed.");

        chinese.RegexPatterns[0] = "/\u300a(.*?)-(.*?)\u300b/\\2" +
            (char)2 + "signature:\\1/a/e";
        Parse(path);
        Source extended = NewSource();
        Call("MapTags", extended);
        Check(extended.TagTypes["a"].Tags.Count == 1 &&
            extended.TagTypes["a"].Tags[0].TagName == "\u4e59" &&
            extended.TagTypes["a"].Tags[0].Signature == " \u7532", "Extended output failed.");

        chinese.RegexPatterns[0] = "/\u300a(.+)\u300b/\\1/a/ei";
        File.WriteAllText(path, "\u300a\u7b2c\u4e00\u300b\n\u300a\u7b2c\u4e8c\u300b",
            new UTF8Encoding(false));
        Parse(path);
        Source unicodeLegacy = NewSource();
        Call("MapTags", unicodeLegacy);
        Check(unicodeLegacy.TagTypes["a"].Tags.Count == 2 &&
            unicodeLegacy.TagTypes["a"].Tags[0].TagName == "\u7b2c\u4e00" &&
            unicodeLegacy.TagTypes["a"].Tags[1].TagName == "\u7b2c\u4e8c",
            "Unicode ctags rule failed.");

        File.WriteAllText(path, "startOneend", new UTF8Encoding(false));
        chinese.RegexPatterns[0] = "/START(.*?)END/\\1/a/e";
        Parse(path);
        Source sensitive = NewSource();
        Call("MapTags", sensitive);
        Check(sensitive.TagTypes["a"].Tags.Count == 0, "Case-sensitive rule matched incorrectly.");
        chinese.RegexPatterns[0] = "/START(.*?)END/\\1/a/ei";
        Parse(path);
        Source insensitive = NewSource();
        Call("MapTags", insensitive);
        Check(insensitive.TagTypes["a"].Tags.Count == 1 &&
            insensitive.TagTypes["a"].Tags[0].TagName == "One", "Case-insensitive rule failed.");

        File.WriteAllBytes(path, new byte[] { 0xff });
        try
        {
            Parse(path);
            throw new Exception("Invalid UTF-8 was accepted.");
        }
        catch (InvalidDataException ex)
        {
            Check(ex.Message.Contains("require UTF-8"), "Unexpected encoding error: " + ex.Message);
        }

        Console.WriteLine("Unicode regex, repeated matches, legacy rules, quick tags and limits passed.");
    }
}
