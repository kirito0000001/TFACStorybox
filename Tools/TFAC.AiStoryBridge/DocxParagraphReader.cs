using System.IO.Compression;
using System.Xml.Linq;

namespace Tfac.AiStoryBridge;

internal static class DocxParagraphReader
{
    private static readonly XNamespace Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static List<DocxParagraph> Read(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Word 原稿不存在。", path);
        }

        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry("word/document.xml")
            ?? throw new InvalidDataException("DOCX 缺少 word/document.xml。");
        using var stream = entry.Open();
        var document = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        return document
            .Descendants(Word + "p")
            .Select((paragraph, index) => new DocxParagraph(
                index + 1,
                string.Concat(paragraph.Descendants(Word + "t").Select(text => text.Value)).Trim()))
            .ToList();
    }
}
