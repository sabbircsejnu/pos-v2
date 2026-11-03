using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RetailPOS.Application.AccountingJournals.Abstractions;

public interface IFrozenSystemJournalStore
{
    Task<long?> ResolveExistingAsync(
        FrozenSystemJournalRequest request, CancellationToken cancellationToken);
    Task<long> PostAsync(FrozenSystemJournalRequest request, long actorUserId, CancellationToken cancellationToken);
    Task<long> ReverseAsync(FrozenSystemJournalRequest request, long actorUserId, CancellationToken cancellationToken);
}

public sealed class FrozenSystemJournalRequest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new CanonicalDecimalConverter() }
    };

    public long BusinessId { get; }
    public string PostingReference { get; }
    public string Kind { get; }
    public long? OriginalJournalId { get; }
    public string CanonicalPayloadJson { get; }
    public string PayloadHash { get; }

    private FrozenSystemJournalRequest(long businessId, string reference, string kind, long? originalId, object payload)
    {
        if (businessId <= 0 || string.IsNullOrWhiteSpace(reference) || reference.Length > 80
            || reference != reference.Trim())
            throw new ArgumentException("A positive Business and stable, untrimmed, bounded posting reference are required.");
        BusinessId = businessId; PostingReference = reference; Kind = kind; OriginalJournalId = originalId;
        CanonicalPayloadJson = JsonSerializer.Serialize(new
            { ContractVersion = 1, BusinessId, PostingReference, Kind, OriginalJournalId, Payload = payload }, JsonOptions);
        PayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalPayloadJson)));
    }

    public static FrozenSystemJournalRequest ForPosting(long businessId, string reference, ManualJournalDraftInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.ReferenceId != reference || string.IsNullOrWhiteSpace(input.ReferenceType)
            || string.IsNullOrWhiteSpace(input.JournalType) || input.Lines is null)
            throw new ArgumentException("Frozen posting requires explicit type/reference and matching system reference identity.");
        return new(businessId, reference, "Post", null, input);
    }

    public static FrozenSystemJournalRequest ForReversal(long businessId, string correctionReference,
        long originalJournalId, ManualJournalReversalInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (originalJournalId <= 0 || string.IsNullOrWhiteSpace(input.Reason)
            || input.PostingDate is null || input.BusinessDate is null)
            throw new ArgumentException("Frozen correction requires original linkage, reason and explicit dates.");
        return new(businessId, correctionReference, "Reverse", originalJournalId, input);
    }

    public T ReadFrozenPayload<T>() where T : class
    {
        using var document = JsonDocument.Parse(CanonicalPayloadJson);
        return document.RootElement.GetProperty("Payload").Deserialize<T>(JsonOptions)
            ?? throw new InvalidDataException("Frozen journal payload is absent.");
    }

    private sealed class CanonicalDecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetDecimal();
        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
            writer.WriteRawValue(value.ToString("0.############################", CultureInfo.InvariantCulture));
    }
}

public sealed class FrozenJournalConflictException(string message) : InvalidOperationException(message);
