using System.Text;

namespace RetailPOS.Application.Expenses;

/// <summary>
/// Decides what an uploaded file really is from its extension and its leading bytes. The client's content type is never
/// trusted, and the type returned here is what the file is later served as.
/// </summary>
public static class ExpenseAttachmentContent
{
    /// <summary>How many leading bytes <see cref="ResolveContentType"/> needs.</summary>
    public const int HeaderLength = 12;

    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static string ResolveContentType(string extension, ReadOnlySpan<byte> header)
    {
        var (contentType, matches) = extension.ToLowerInvariant() switch
        {
            ".pdf" => ("application/pdf", header.StartsWith(PdfSignature)),
            ".jpg" or ".jpeg" => ("image/jpeg", header.StartsWith(JpegSignature)),
            ".png" => ("image/png", header.StartsWith(PngSignature)),
            ".webp" => ("image/webp", IsWebP(header)),
            _ => throw new InvalidOperationException("Only PDF, JPG, PNG and WebP attachments are allowed.")
        };

        if (!matches)
            throw new InvalidOperationException("The file's content does not match its type.");
        return contentType;
    }

    /// <summary>The bare file name, with any client-supplied path and control characters removed.</summary>
    public static string SanitizeFileName(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/'));
        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            if (!char.IsControl(character))
                builder.Append(character);
        }

        var clean = builder.ToString().Trim();
        if (clean.Length == 0)
            throw new InvalidOperationException("The attachment needs a file name.");
        if (clean.Length <= 255)
            return clean;

        var extension = Path.GetExtension(clean);
        return clean[..(255 - extension.Length)] + extension;
    }

    private static bool IsWebP(ReadOnlySpan<byte> header) =>
        header.Length >= HeaderLength
        && header[..4].SequenceEqual("RIFF"u8)
        && header[8..12].SequenceEqual("WEBP"u8);
}
