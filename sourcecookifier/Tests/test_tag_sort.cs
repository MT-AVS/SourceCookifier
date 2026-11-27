using System;
using System.Collections;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Windows.Forms;
using System.Xml.Serialization;
using SourceCookifier;

class TagSortTest
{
    class OldTagProbe : Tag
    {
        public OldTagProbe() { }
        public OldTagProbe(SerializationInfo info, StreamingContext context) : base(info, context) { }
        public void Save(SerializationInfo info, StreamingContext context) { Serialize(info, context); }
    }

    static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    static Tag MakeTag(string name, int line, int position)
    {
        return new Tag {
            TagName = name,
            Language = "TXT",
            Identifier = "a",
            Line = line,
            SortPosition = position
        };
    }

    static void CheckOrder(Tag[] tags, string[] expected, string message)
    {
        Array.Sort((Array)tags, new TagNodeSorter());
        for (int i = 0; i < expected.Length; i++)
            Check(tags[i].TagName == expected[i], message + ": index " + i + " was " + tags[i].TagName);
    }

    static void Main()
    {
        Settings.Configs = new Settings.Config();
        Settings.Configs.ViewMode = Settings.TreeViewMode.Flat;

        Tag[] document = {
            MakeTag("third", 2, 0), MakeTag("second", 1, 6), MakeTag("first", 1, 0)
        };
        Settings.Configs.TagSort = Settings.TagSortMode.DocumentForward;
        CheckOrder(document, new string[] { "first", "second", "third" }, "Document forward");
        Settings.Configs.TagSort = Settings.TagSortMode.DocumentReverse;
        CheckOrder(document, new string[] { "third", "second", "first" }, "Document reverse");

        using (TreeView tree = new TreeView())
        {
            Settings.Configs.TagSort = Settings.TagSortMode.DocumentForward;
            tree.TreeViewNodeSorter = new TagNodeSorter();
            TreeNode root = tree.Nodes.Add("file");
            Tag first = MakeTag("first", 1, 0);
            Tag second = MakeTag("second", 1, 6);
            first.Text = first.TagName;
            second.Text = second.TagName;
            root.Nodes.Add(second);
            root.Nodes.Add(first);
            tree.Sort();
            Check(root.Nodes[0].Text == "first", "TreeView forward order failed.");
            Settings.Configs.TagSort = Settings.TagSortMode.DocumentReverse;
            tree.Sort();
            Check(root.Nodes[0].Text == "second", "TreeView reverse order failed.");
        }

        Tag[] zhuyin = {
            MakeTag("\u300a\u9b5a\u300b", 7, 0),
            MakeTag("\u300a\u5b89\u300b", 4, 0),
            MakeTag("\u300a\u516b\u300b", 1, 0),
            MakeTag("\u300a\u5c4b\u300b", 6, 0),
            MakeTag("\u300a\u5abd\u300b", 2, 0),
            MakeTag("\u300a\u5f35\u300b", 3, 0),
            MakeTag("\u300a\u8863\u300b", 5, 0)
        };
        string[] expected = {
            "\u300a\u516b\u300b", "\u300a\u5abd\u300b", "\u300a\u5f35\u300b",
            "\u300a\u5b89\u300b", "\u300a\u8863\u300b", "\u300a\u5c4b\u300b", "\u300a\u9b5a\u300b"
        };
        Settings.Configs.TagSort = Settings.TagSortMode.ZhuyinForward;
        CheckOrder(zhuyin, expected, "Zhuyin forward");
        Array.Reverse(expected);
        Settings.Configs.TagSort = Settings.TagSortMode.ZhuyinReverse;
        CheckOrder(zhuyin, expected, "Zhuyin reverse");

        XmlSerializer configSerializer = new XmlSerializer(typeof(Settings.Config));
        using (StringReader oldConfig = new StringReader("<Config><SortTags>true</SortTags></Config>"))
        {
            Settings.Config loaded = (Settings.Config)configSerializer.Deserialize(oldConfig);
            Check(loaded.SortTags && loaded.TagSort == Settings.TagSortMode.Legacy,
                "Existing configuration no longer uses legacy sorting.");
        }
        using (StringWriter writer = new StringWriter())
        {
            configSerializer.Serialize(writer, Settings.Configs);
            using (StringReader reader = new StringReader(writer.ToString()))
            {
                Settings.Config loaded = (Settings.Config)configSerializer.Deserialize(reader);
                Check(loaded.TagSort == Settings.TagSortMode.ZhuyinReverse, "Sort mode was not saved.");
            }
        }

        using (MemoryStream stream = new MemoryStream())
        {
            BinaryFormatter formatter = new BinaryFormatter();
            formatter.Serialize(stream, MakeTag("same line", 1, 12));
            stream.Position = 0;
            Tag loaded = (Tag)formatter.Deserialize(stream);
            Check(loaded.SortPosition == 12, "Match position was not saved.");
        }
        StreamingContext context = new StreamingContext(StreamingContextStates.All);
        SerializationInfo current = new SerializationInfo(typeof(OldTagProbe), new FormatterConverter());
        new OldTagProbe { TagName = "old", SortPosition = 12 }.Save(current, context);
        SerializationInfo old = new SerializationInfo(typeof(OldTagProbe), new FormatterConverter());
        foreach (SerializationEntry entry in current)
            if (entry.Name != "SortPosition")
                old.AddValue(entry.Name, entry.Value, entry.ObjectType);
        Check(new OldTagProbe(old, context).SortPosition == -1, "Old session tag was not restored.");
        Console.WriteLine("Document and Zhuyin order, reverse, and persistence passed.");
    }
}
