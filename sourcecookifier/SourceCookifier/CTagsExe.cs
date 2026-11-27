using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace SourceCookifier
{
    class CTagsExe
    {
        static string cTagsVersion = "5.8.10.SC";
        static string cTagsExePath;
        static string tagsFilePath;
        static string stdOut;
        static int numTagsWritten;
        static readonly List<ManagedTag> managedTags = new List<ManagedTag>();
        public const char FieldSeparator = (char)0x01; // '☺'
        public const char FieldSeparatorExtended = (char)0x02; // '☻'

        class ManagedRegexRule
        {
            public Regex Pattern;
            public string Output;
            public string Identifier;
        }

        class ManagedTag
        {
            public string Line;
            public int Position;
        }
        
        public static void Init()
        {
            cTagsExePath = Path.Combine(Settings.PluginSubFolderBin, "ctags.exe");
            tagsFilePath = Path.Combine(Settings.ConfigDir, "SourceCookifier.tags");
            stdOut = "";
            Main.TRACE("CTags.exe path = " + cTagsExePath);
            if (!File.Exists(cTagsExePath)) throw new Exception("'ctags.exe' not found!");
            if (!CheckCtagsVersion()) throw new Exception("'ctags.exe' version does not fit SourceCookifier version!");
        }
        static bool CheckCtagsVersion()
        {
            Environment.SetEnvironmentVariable("SourceCookifierVersion",
                Assembly.GetExecutingAssembly().GetName().Version.ToString(), EnvironmentVariableTarget.Process);
            RunCTagsExe("--version", false);
            Main.TRACE(stdOut.Substring(0, stdOut.IndexOf('\n')));
            return (stdOut.Contains(string.Format("Exuberant Ctags {0}", cTagsVersion)));
        }
        public static void GetLangMaps()
        {
            Main.TRACE("-START-");
            string args = "--list-maps";
            RunCTagsExe(args, false);

            using (StreamReader r = new StreamReader(new MemoryStream(Encoding.ASCII.GetBytes(stdOut))))
            {
                string line = null;
                while ((line = r.ReadLine()) != null)
                {
                    string[] toks = line.Split(new char[] {' '}, StringSplitOptions.RemoveEmptyEntries);
                    Settings.Languages[toks[0]] = new Language();
                    for (int i = 1; i < toks.Length; i++)
                    {
                        //TODO: "*" ?
                        Settings.Languages[toks[0]].Extensions.Add(toks[i].Replace("*", ""));
                        Main.TRACE(string.Format("lang={0} ext={1}", toks[0], toks[i].Replace("*", "")));
                    }
                }
            }
            Main.TRACE("-END-");
        }
        public static void GetLangTagTypes()
        {
            Main.TRACE("-START-");
            string args = "--list-kinds=all";
            RunCTagsExe(args, false);

            using (StreamReader r = new StreamReader(new MemoryStream(Encoding.ASCII.GetBytes(stdOut))))
            {
                string line = r.ReadLine();
                while (line != null)
                {
                    string lang = line;
                    while (((line = r.ReadLine()) != null) && (line[0] == ' '))
                    {
                        line = line.Trim();
                        string identifier = line[0].ToString();
                        Settings.Languages[lang].TagTypes[identifier] = new TagType();
                        Settings.Languages[lang].TagTypes[identifier].Description = line.Substring(3);
                        Main.TRACE(string.Format("lang={0} tagtype={1} desc={2}", lang, identifier, line.Substring(3)));
                    }
                }
            }
            Main.TRACE("-END-");
        }

        public static void DoTags(string sourcefile, string language, string extension, string filesizelimit)
        {
            Main.TRACE("-START-");
            managedTags.Clear();

            if (!string.IsNullOrEmpty(filesizelimit))
            {
                bool bMB = filesizelimit.EndsWith("mb");
                long iLimit = Convert.ToInt64(filesizelimit.Replace(bMB ? "mb" : "kb", ""));
                long iFileSize = new FileInfo(sourcefile.Replace("\"", "")).Length / (bMB ? 1048576 : 1024);
                string sFileSize = string.Format("{0}{1}", iFileSize, (bMB ? "mb" : "kb"));
                if (iFileSize > iLimit)
                {
                    throw new Exception(string.Format("Custom filesize limit [{0}] reached: {1}!",
                        filesizelimit, sFileSize));
                }
            }
            
            StringBuilder args = new StringBuilder();
            StringBuilder typeFlags = new StringBuilder();
            List<ManagedRegexRule> managedRules = new List<ManagedRegexRule>();
            
            args.AppendFormat("-f \"{0}\" ", tagsFilePath);
            args.Append("--excmd=number ");
            args.Append("--fields=aksST ");
            args.Append("--sort=no ");
            
            if (!Settings.Languages[language].BuildIn)
                args.AppendFormat("--langdef={0} ", language);
            
            args.AppendFormat("--langmap={0}:{1} ", language, (!string.IsNullOrEmpty(extension)) ? extension : ".");

            foreach (string tagtypename in Settings.Languages[language].TagTypes.Keys)
            {
                if (Settings.Languages[language].TagTypes[tagtypename].Show)
                {
                    bool hasManagedRule = false;
                    bool hasCtagsRule = false;
                    foreach (string regexpattern in Settings.Languages[language].TagTypes[tagtypename].RegexPatterns)
                    {
                        ManagedRegexRule rule = CreateManagedRegexRule(regexpattern, tagtypename);
                        if (rule != null)
                        {
                            managedRules.Add(rule);
                            hasManagedRule = true;
                        }
                        else
                        {
                            args.Append(string.Format("--regex-{0}=\"{1}\" ",
                                language, EscapeUnicodeForCTags(regexpattern)));
                            hasCtagsRule = true;
                        }
                    }
                    if (!hasManagedRule || hasCtagsRule || Settings.Languages[language].TagTypes[tagtypename].BuildIn)
                        typeFlags.Append(tagtypename);
                }
            }

            args.AppendFormat("--{0}-kinds={1} ", language, typeFlags.ToString());
            args.Append(sourcefile);

            RunCTagsExe(args.ToString(), true);
            if (managedRules.Count > 0)
                FindManagedTags(sourcefile.Replace("\"", ""), managedRules);
            Main.TRACE("-END-");
        }

        static string ReadRegexPart(string entry, ref int index, char separator)
        {
            StringBuilder part = new StringBuilder();
            while (index < entry.Length)
            {
                char ch = entry[index++];
                if (ch == separator)
                    return part.ToString();
                if (ch == '\\' && index < entry.Length)
                {
                    char next = entry[index++];
                    if (next == separator)
                        part.Append(separator);
                    else if (next == 't')
                        part.Append('\t');
                    else
                        part.Append('\\').Append(next);
                }
                else
                    part.Append(ch);
            }
            throw new FormatException("Invalid ctags regex rule: missing separator.");
        }

        static bool HasLazyStarQuantifier(string pattern)
        {
            bool inClass = false;
            int classStart = -1;
            for (int i = 0; i < pattern.Length; i++)
            {
                char ch = pattern[i];
                if (ch == '\\')
                {
                    i++;
                    continue;
                }
                if (ch == '[' && !inClass)
                {
                    inClass = true;
                    classStart = i + 1;
                    continue;
                }
                if (inClass)
                {
                    if (ch == '[' && i + 1 < pattern.Length &&
                        (pattern[i + 1] == ':' || pattern[i + 1] == '.' || pattern[i + 1] == '='))
                    {
                        string close = new string(pattern[i + 1], 1) + "]";
                        int end = pattern.IndexOf(close, i + 2, StringComparison.Ordinal);
                        if (end >= 0)
                            i = end + 1;
                    }
                    else if (ch == ']' && i > classStart &&
                        !(i == classStart + 1 && pattern[classStart] == '^'))
                        inClass = false;
                    continue;
                }
                if (ch == '*' && i + 1 < pattern.Length && pattern[i + 1] == '?')
                    return true;
            }
            return false;
        }

        static ManagedRegexRule CreateManagedRegexRule(string entry, string identifier)
        {
            if (string.IsNullOrEmpty(entry))
                throw new FormatException("Empty ctags regex rule.");
            int index = 1;
            char separator = entry[0];
            string input = ReadRegexPart(entry, ref index, separator);
            if (!HasLazyStarQuantifier(input))
                return null;

            string output = ReadRegexPart(entry, ref index, separator);
            ReadRegexPart(entry, ref index, separator);
            string flags = entry.Substring(index);
            if (flags.Contains("b"))
                throw new FormatException("Lazy quantifiers require extended regex syntax.");
            foreach (char flag in flags)
                if (flag != 'e' && flag != 'i')
                    throw new FormatException("Unknown regex flag: " + flag);
            RegexOptions options = RegexOptions.CultureInvariant;
            if (flags.Contains("i"))
                options |= RegexOptions.IgnoreCase;
            return new ManagedRegexRule {
                Pattern = new Regex(input, options, TimeSpan.FromSeconds(1)),
                Output = output,
                Identifier = identifier
            };
        }

        static string ExpandManagedTag(Match match, string output)
        {
            StringBuilder result = new StringBuilder();
            for (int i = 0; i < output.Length; i++)
            {
                if (output[i] == '\\' && i + 1 < output.Length)
                {
                    char next = output[++i];
                    if (next >= '0' && next <= '9')
                    {
                        int group = next - '0';
                        if (group > 0 && group < match.Groups.Count && match.Groups[group].Success)
                            result.Append(match.Groups[group].Value);
                    }
                    else
                        result.Append(next);
                }
                else if (output[i] != '\r' && output[i] != '\n')
                    result.Append(output[i]);
            }
            return result.ToString().Trim();
        }

        static void FindManagedTags(string path, List<ManagedRegexRule> rules)
        {
            try
            {
                using (StreamReader reader = new StreamReader(path, new UTF8Encoding(false, true), true))
                {
                    string line;
                    int lineNumber = 0;
                    while ((line = reader.ReadLine()) != null)
                    {
                        lineNumber++;
                        foreach (ManagedRegexRule rule in rules)
                        {
                            foreach (Match match in rule.Pattern.Matches(line))
                            {
                                string name = ExpandManagedTag(match, rule.Output);
                                if (name.Length == 0)
                                {
                                    if (Settings.Configs.HandleCtagsWarnings)
                                        throw new Exception(string.Format(
                                            "{0}:{1}: null expansion of name pattern \"{2}\"",
                                            path, lineNumber, rule.Output));
                                    continue;
                                }
                                if (name.IndexOf(FieldSeparator) >= 0)
                                    throw new FormatException("A regex result contains the tag field separator.");
                                managedTags.Add(new ManagedTag {
                                    Line = string.Format("{0}{1}{2}{1}{3};\"{1}{4}",
                                        name, FieldSeparator, path, lineNumber, rule.Identifier),
                                    Position = match.Index
                                });
                            }
                        }
                    }
                }
            }
            catch (DecoderFallbackException ex)
            {
                throw new InvalidDataException(
                    "Lazy regex rules require UTF-8 or a Unicode file with a byte order mark: " + path, ex);
            }
        }

        static string EscapeUnicodeForCTags(string regexPattern)
        {
            StringBuilder result = new StringBuilder(regexPattern.Length);
            for (int i = 0; i < regexPattern.Length; i++)
            {
                char ch = regexPattern[i];
                if (char.IsHighSurrogate(ch) && i + 1 < regexPattern.Length &&
                    char.IsLowSurrogate(regexPattern[i + 1]))
                {
                    int codePoint = char.ConvertToUtf32(ch, regexPattern[++i]);
                    result.Append("\\U").Append(codePoint.ToString("X8",
                        System.Globalization.CultureInfo.InvariantCulture));
                }
                else if (ch > 0x7F)
                {
                    result.Append("\\u").Append(((int)ch).ToString("X4",
                        System.Globalization.CultureInfo.InvariantCulture));
                }
                else
                {
                    result.Append(ch);
                }
            }
            return result.ToString();
        }

        public enum MapTagsResult { OK, MoveToIncludes }

        static StringReader OpenTagFileReader()
        {
            byte[] data = File.ReadAllBytes(tagsFilePath);
            try
            {
                return new StringReader(new UTF8Encoding(false, true).GetString(data));
            }
            catch (DecoderFallbackException)
            {
                return new StringReader(Encoding.GetEncoding(1252).GetString(data));
            }
        }

        public static MapTagsResult MapTags(Source source)
        {
            Main.TRACE("-START-");
            HandleCTagsStdOut();
            numTagsWritten += managedTags.Count;

            if (numTagsWritten > Convert.ToInt32(Settings.Configs.MaxNumShownSymbols))
            {
                string msg = string.Format("Too many symbols: {0} [custom limit: {1}]",
                        numTagsWritten, Settings.Configs.MaxNumShownSymbols);
                Main.TRACE(msg);
                if (Settings.Configs.SessionMode == Settings.SessionMode.Cookie)
                {
                    return MapTagsResult.MoveToIncludes;
                }
                else
                {
                    throw new Exception(msg);
                }
            }

            using (StringReader r = OpenTagFileReader())
            {
                string line = null;
                Main.TRACE(string.Format("Language={0}", source.Language));
                while ((line = r.ReadLine()) != null)
                {
                    source.AddTag(new Tag(line, source.Language, source.ScopeOperator));
                }
            }
            foreach (ManagedTag managedTag in managedTags)
            {
                Tag tag = new Tag(managedTag.Line, source.Language, source.ScopeOperator);
                tag.SortPosition = managedTag.Position;
                source.AddTag(tag);
            }
            Main.TRACE("-END-");
            return MapTagsResult.OK;
        }
        public static void MapQuickTags(IncludeFile incFile, string language, string scopeOperator)
        {
            Main.TRACE("-START-");
            HandleCTagsStdOut();
            using (StringReader r = OpenTagFileReader())
            {
                string line = null;
                Main.TRACE(string.Format("Language={0}", language));
                while ((line = r.ReadLine()) != null)
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    if (line.StartsWith("!_TAG_")) continue;
                    incFile.QuickTags.Add(new QuickTag(line, language, scopeOperator));
                }
            }
            foreach (ManagedTag managedTag in managedTags)
                incFile.QuickTags.Add(new QuickTag(managedTag.Line, language, scopeOperator));
            Main.TRACE("-END-");
        }

        static void HandleCTagsStdOut()
        {
            Main.TRACE("-START-");
            Main.TRACE("StdOut = " + stdOut);
            numTagsWritten = 0;
            if (!string.IsNullOrEmpty(stdOut))
            {
                foreach (string line in stdOut.Split(
                    new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.Contains("SC-Info:"))
                    {
                        string[] toks = line.Split(
                            new string[] { "SC-Info:" }, StringSplitOptions.RemoveEmptyEntries);
                        string[] info = toks[1].Trim().Split('=');
                        if (info[0] == "NUM_TAGS")
                        {
                            numTagsWritten = Convert.ToInt32(info[1]);
                        }
                    }
                    else if (Settings.Configs.HandleCtagsWarnings)
                    {
                        throw new Exception(line);
                    }
                }
            }
            Main.TRACE("numTagsWritten = " + numTagsWritten);
            Main.TRACE("-END-");
        }
        
        static void RunCTagsExe(string args, bool usetagfile)
        {
            Main.TRACE("-START-");
            
            Process p = new Process();
            p.StartInfo.WorkingDirectory = Settings.PluginSubFolderBin;
            p.StartInfo.FileName = cTagsExePath;
            p.StartInfo.Arguments = "--options=NONE " + args;
            p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.StandardOutputEncoding = Encoding.GetEncoding(1252);

            Main.TRACE(string.Format("Running ctags.exe with args={0}", args));
            p.Start();
            
            stdOut = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (usetagfile && !File.Exists(tagsFilePath)) throw new Exception("ERROR: Tagfile not found");
            
            if (p.ExitCode != 0) throw new Exception("ERROR: " + stdOut);
            Main.TRACE("-END-");
        }
    }
}
