using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using System.Xml.Serialization;
using SourceCookifier;

class TreeFontTest
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static object Field(object obj, string name)
    {
        return obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic |
            BindingFlags.Public).GetValue(obj);
    }

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Expected setup and test directories.");
        string setup = args[0];
        string testDir = args[1];
        Directory.CreateDirectory(testDir);
        Settings.ConfigDir = testDir;
        Settings.PluginSubFolderBin = Path.Combine(setup, "bin");
        Settings.PluginSubFolderIcons = Path.Combine(setup, "icons");
        Settings.languagesModelFilePath = Path.Combine(setup, "setup", "SourceCookifier.languages.model.xml");
        Settings.languagesSettingsFilePath = Path.Combine(testDir, "languages.xml");

        XmlSerializer serializer = new XmlSerializer(typeof(Settings.Config));
        using (StringReader reader = new StringReader("<Config><SortTags>true</SortTags></Config>"))
        {
            Settings.Config old = (Settings.Config)serializer.Deserialize(reader);
            Check(old.CreateTreeFont() == null && old.SortTags, "Old settings changed the default font.");
        }

        Settings.Configs = new Settings.Config();
        Settings.Configs.ShowIcons = false;
        using (Font font = new Font("Arial", 18.5f, FontStyle.Bold))
            Settings.Configs.SetTreeFont(font);
        string xml;
        using (StringWriter writer = new StringWriter())
        {
            serializer.Serialize(writer, Settings.Configs);
            xml = writer.ToString();
        }
        using (StringReader reader = new StringReader(xml))
            Settings.Configs = (Settings.Config)serializer.Deserialize(reader);

        using (frmMain form = new frmMain())
        {
            TreeView tree = (TreeView)Field(form, "tvTags");
            IntPtr handle = tree.Handle;
            Check(tree.Font.Name == "Arial" && Math.Abs(tree.Font.SizeInPoints - 18.5f) < 0.01f &&
                tree.Font.Bold, "Saved font was not applied to the result tree.");
            Check(tree.ItemHeight >= tree.Font.Height, "Large font is clipped by tree row height.");

            ToolStripDropDownButton view = (ToolStripDropDownButton)Field(form, "tsDdbtnView");
            ToolStripItem choose = null, reset = null;
            foreach (ToolStripItem item in view.DropDownItems)
            {
                if (item.Text == "Font...") choose = item;
                if (item.Text == "Use default font") reset = item;
            }
            Check(choose != null && reset != null, "Font menu items are missing.");
            TreeNode includes = Source.GetIncludesRootNode(tree);
            IncludeFile visible = new IncludeFile();
            IncludeFile hidden = new IncludeFile();
            includes.Nodes.Add(visible);
            Source.invisibleIncludeFileNodes.Add(hidden);
            Source.UpdateIncludeFonts(tree);
            Check(includes.NodeFont.SizeInPoints == 18.5f && visible.NodeFont.Bold &&
                visible.NodeFont.Italic && hidden.NodeFont.Italic, "Include fonts were not updated.");

            Font changed = new Font("Arial", 28, FontStyle.Regular);
            form.GetType().GetMethod("ApplyTreeFont", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(form, new object[] { changed });
            Check(tree.Font.SizeInPoints == 28 && tree.ItemHeight >= tree.Font.Height &&
                visible.NodeFont.SizeInPoints == 28 && hidden.NodeFont.SizeInPoints == 28,
                "Changing font did not update existing rows.");

            reset.PerformClick();
            Check(Settings.Configs.CreateTreeFont() == null, "Default font was not restored in settings.");
            Check(tree.Font.SizeInPoints != 28 && includes.NodeFont.SizeInPoints == tree.Font.SizeInPoints &&
                visible.NodeFont.SizeInPoints == tree.Font.SizeInPoints &&
                hidden.NodeFont.SizeInPoints == tree.Font.SizeInPoints,
                "Default font was not applied to the tree and includes.");
            Source.invisibleIncludeFileNodes.Clear();
        }
        Console.WriteLine("Font persistence, tree height, menus and include fonts passed.");
    }
}
