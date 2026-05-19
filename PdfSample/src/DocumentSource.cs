using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;

namespace Curiosity.Library.Recipes;

// Generic, dataset-agnostic document loader. For each file under `root`,
// extracts text page-by-page and reads a sidecar `<filename>.json` (when
// present) for structured metadata. Reuse as-is for any directory of
// PDFs / Word documents with optional per-file metadata.
//
// PDFs are read with UglyToad.PdfPig (no native deps). DOCX files are
// read with the Microsoft OpenXML SDK. Add a branch here for other
// formats (PPTX, XLSX, EML, …) — the downstream contract (Pages: text
// per page) doesn't change.
public sealed record ExtractedDocument(
    string                       SourceFile,
    string                       SourceFileName,
    string                       ContentHash,
    IReadOnlyList<string>        Pages,
    string?                      MetadataJson);

public static class DocumentSource
{
    public static IEnumerable<ExtractedDocument> Load(string root)
    {
        if (!Directory.Exists(root)) yield break;

        foreach (var path in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            ExtractedDocument? doc = ext switch
            {
                ".pdf"  => ExtractPdf(path),
                ".docx" => ExtractDocx(path),
                _       => null,
            };
            if (doc is not null) yield return doc;
        }
    }

    private static ExtractedDocument ExtractPdf(string path)
    {
        using var pdf   = PdfDocument.Open(path);
        var       pages = new List<string>(pdf.NumberOfPages);
        foreach (var page in pdf.GetPages()) pages.Add(page.Text);

        return new ExtractedDocument(
            SourceFile:     path,
            SourceFileName: Path.GetFileName(path),
            ContentHash:    Sha256(string.Join("\n--PAGE--\n", pages)),
            Pages:          pages,
            MetadataJson:   ReadSidecarJson(path));
    }

    private static ExtractedDocument ExtractDocx(string path)
    {
        using var doc = WordprocessingDocument.Open(path, isEditable: false);
        var body = doc.MainDocumentPart?.Document?.Body?.InnerText ?? string.Empty;

        // DOCX has no built-in page concept until layout; split on the
        // explicit page-break marker that Word inserts.
        var pages = body.Split(new[] { "\f", "" }, StringSplitOptions.None);

        return new ExtractedDocument(
            SourceFile:     path,
            SourceFileName: Path.GetFileName(path),
            ContentHash:    Sha256(body),
            Pages:          pages.ToList(),
            MetadataJson:   ReadSidecarJson(path));
    }

    private static string? ReadSidecarJson(string path)
    {
        var sidecar = Path.ChangeExtension(path, ".json");
        return File.Exists(sidecar) ? File.ReadAllText(sidecar) : null;
    }

    private static string Sha256(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        var sb    = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    // Helper for chunking page text into model-friendly slices. 800 chars
    // with 80-char overlap is the conservative starting point for most
    // embedding models; tune per use case.
    public static IEnumerable<string> Chunk(string text, int chunkSize = 800, int overlap = 80)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;

        var i = 0;
        while (i < text.Length)
        {
            var end = Math.Min(i + chunkSize, text.Length);
            yield return text.Substring(i, end - i);
            if (end == text.Length) yield break;
            i = end - overlap;
            if (i < 0) i = 0;
        }
    }
}
