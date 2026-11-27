using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;
using SourceCookifier;

class DefaultTxtTest
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Expected plugin and test directories.");
        string plugin = args[0];
        string testDir = args[1];
        Directory.CreateDirectory(testDir);
        Settings.ConfigDir = testDir;
        Settings.PluginSubFolderBin = Path.Combine(plugin, "bin");
        Settings.PluginSubFolderIcons = Path.Combine(plugin, "icons");
        Settings.languagesModelFilePath = Path.Combine(plugin, "setup", "SourceCookifier.languages.model.xml");
        Settings.languagesSettingsFilePath = Path.Combine(testDir, "languages.xml");
        Settings.Configs = new Settings.Config { ShowIcons = true };

        Check(typeof(Settings).Assembly.GetName().Version.ToString() == "0.11.0.0",
            "Assembly version is incorrect.");
        Type resources = typeof(Settings).Assembly.GetType("SourceCookifier.Properties.Resources", true);
        string help = (string)resources.GetProperty("help_rtf", BindingFlags.Static | BindingFlags.NonPublic)
            .GetValue(null, null);
        using (RichTextBox about = new RichTextBox())
        {
            about.Rtf = help;
            Check(about.Text.Contains("SourceCookifier v0.11.0.0"), "Help version is incorrect.");
            Check(about.Text.Contains("V0.11.0 updated by Ming-Tsung Huang, Taipei, 2026-11-25."),
                "English update credit is missing from Help & About.");
            Check(about.Text.Contains("V0.11.0 於 2026-11-25 由黃明聰在台北更新。"),
                "Chinese update credit is missing from Help & About.");
        }

        using (TreeView tree = new TreeView())
        {
            Settings.LoadSettings(tree);
            Language txt = Settings.Languages["TXT"];
            Check(!txt.BuildIn && txt.Extensions.Contains(".txt") && !txt.TagTypes["a"].BuildIn &&
                txt.TagTypes["a"].Show, "Default TXT language or tag type is incorrect.");
            int iconIndex = Settings.IconList.Images.IndexOfKey("LABEL_4.png");
            Check(txt.TagTypes["a"].IconFilename == "LABEL_4.png" && iconIndex > 0 &&
                txt.TagTypes["a"].ImageListIndex == iconIndex,
                "Default TXT icon was not loaded.");
            Check(txt.TagTypes["a"].RegexPatterns.Count == 1 &&
                txt.TagTypes["a"].RegexPatterns[0] == "/《(.*?)》(之《(.*?)》)?/《\\1》\\2/a/ei",
                "Default TXT regex is incorrect.");

            string path = Path.Combine(testDir, "sample.txt");
            File.WriteAllText(path, "《散文》\r\n《九州櫻花之旅》之《2023-03-28 福岡機場》\r\n" +
                "《甲》《乙》\r\n", new UTF8Encoding(false));
            Source source = new Source(tree, path, "");
            Check(source.Error == "" && source.Language == "TXT", "TXT file failed to parse: " + source.Error);
            List<Tag> tags = source.TagTypes["a"].Tags;
            string[] expected = { "《散文》", "《九州櫻花之旅》之《2023-03-28 福岡機場》", "《甲》", "《乙》" };
            Check(tags.Count == expected.Length, "Unexpected number of TXT results.");
            for (int i = 0; i < expected.Length; i++)
                Check(tags[i].TagName == expected[i], "Unexpected TXT result: " + tags[i].TagName);
            source.DrawTreeView(tree, "", Source.RefreshType.None);
            Check(tags[0].ImageIndex == iconIndex, "TXT result did not use the default icon.");

            txt.TagTypes["a"].RegexPatterns[0] = "/custom/\\1/a/e";
            txt.TagTypes["a"].IconFilename = "function.png";
            XmlSerializer serializer = new XmlSerializer(Settings.Languages.GetType());
            using (StreamWriter writer = new StreamWriter(Settings.languagesSettingsFilePath))
                serializer.Serialize(writer, Settings.Languages);
            Settings.LoadSettings(tree);
            Check(Settings.Languages["TXT"].TagTypes["a"].RegexPatterns[0] == "/custom/\\1/a/e",
                "Existing user language settings were overwritten.");
            Check(Settings.Languages["TXT"].TagTypes["a"].IconFilename == "function.png",
                "Existing user icon choice was overwritten.");
        }
        Console.WriteLine("Default TXT rule, actual output and existing settings passed.");
    }
}
