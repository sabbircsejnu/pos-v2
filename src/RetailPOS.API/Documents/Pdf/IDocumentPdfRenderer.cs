namespace RetailPOS.API.Documents.Pdf;

public interface IDocumentPdfRenderer
{
    Task<byte[]> RenderAsync(DocumentPdfRequest request, CancellationToken cancellationToken = default);
}