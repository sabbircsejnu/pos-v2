using System.Globalization;
using System.Text;

namespace RetailPOS.Application.BankStatements.Parsing;

public sealed class BankStatementCsvParser
{
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "yyyyMMdd", "dd/MM/yyyy", "MM/dd/yyyy"];
    private static readonly HashSet<string> RequiredHeaders = new(StringComparer.OrdinalIgnoreCase) { "transaction_date", "description", "debit", "credit" };

    public ParsedBankStatement Parse(Stream content)
    {
        using var reader = new StreamReader(content, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var headerLine = reader.ReadLine() ?? throw new InvalidOperationException("CSV file is empty.");
        var headers = ParseRow(headerLine).Select(NormalizeHeader).ToList();
        if (headers.Count != headers.Distinct(StringComparer.OrdinalIgnoreCase).Count()) throw new InvalidOperationException("CSV contains duplicate headers.");
        var missing = RequiredHeaders.Where(x => !headers.Contains(x, StringComparer.OrdinalIgnoreCase)).ToList();
        if (missing.Count > 0) throw new InvalidOperationException($"Unsupported CSV schema. Missing headers: {string.Join(", ", missing)}.");

        var rows = new List<ParsedBankStatementRow>();
        var sourceRow = 1;
        string? raw;
        while ((raw = reader.ReadLine()) != null)
        {
            sourceRow++;
            if (string.IsNullOrWhiteSpace(raw) || ParseRow(raw).All(string.IsNullOrWhiteSpace)) continue;
            var values = ParseRow(raw);
            if (values.Count != headers.Count) throw new InvalidOperationException($"CSV row {sourceRow} has {values.Count} fields; expected {headers.Count}.");
            var fields = headers.Select((header, index) => (header, value: values[index].Trim())).ToDictionary(x => x.header, x => x.value, StringComparer.OrdinalIgnoreCase);
            rows.Add(ParseRowValues(sourceRow, raw, fields));
        }
        if (rows.Count == 0) throw new InvalidOperationException("CSV contains no transaction rows.");
        return new ParsedBankStatement(rows);
    }

    private static ParsedBankStatementRow ParseRowValues(int row, string raw, IReadOnlyDictionary<string, string> fields)
    {
        var transactionDate = ParseDate(fields["transaction_date"], $"transaction date on row {row}");
        var description = fields["description"];
        if (string.IsNullOrWhiteSpace(description)) throw new InvalidOperationException($"Description is required on row {row}.");
        var debit = ParseAmount(fields["debit"], $"debit amount on row {row}");
        var credit = ParseAmount(fields["credit"], $"credit amount on row {row}");
        if (debit < 0 || credit < 0) throw new InvalidOperationException($"Debit and credit amounts cannot be negative on row {row}.");
        if (debit > 0 && credit > 0) throw new InvalidOperationException($"Row {row} cannot contain both debit and credit amounts.");
        var valueDate = OptionalDate(fields, "value_date", row);
        var running = OptionalAmount(fields, "running_balance", row);
        var opening = OptionalAmount(fields, "opening_balance", row);
        var closing = OptionalAmount(fields, "closing_balance", row);
        fields.TryGetValue("external_reference", out var external);
        fields.TryGetValue("currency", out var currency);
        return new ParsedBankStatementRow(row, raw, transactionDate, valueDate, EmptyToNull(external), description, debit, credit, credit - debit, running, opening, closing, EmptyToNull(currency));
    }

    private static DateTime ParseDate(string value, string label) => DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result) ? result.Date : throw new InvalidOperationException($"Invalid {label}; expected yyyy-MM-dd, yyyyMMdd, dd/MM/yyyy, or MM/dd/yyyy.");
    private static DateTime? OptionalDate(IReadOnlyDictionary<string, string> fields, string name, int row) => fields.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? ParseDate(value, $"{name.Replace('_', ' ')} on row {row}") : null;
    private static decimal ParseAmount(string value, string label) => string.IsNullOrWhiteSpace(value) ? 0m : decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var result) ? result : throw new InvalidOperationException($"Invalid {label}; expected an invariant decimal amount.");
    private static decimal? OptionalAmount(IReadOnlyDictionary<string, string> fields, string name, int row) => fields.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? ParseAmount(value, $"{name.Replace('_', ' ')} on row {row}") : null;
    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static string NormalizeHeader(string value) => value.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');

    private static List<string> ParseRow(string value)
    {
        var result = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];
            if (character == '"')
            {
                if (quoted && i + 1 < value.Length && value[i + 1] == '"') { field.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (character == ',' && !quoted) { result.Add(field.ToString()); field.Clear(); }
            else field.Append(character);
        }
        if (quoted) throw new InvalidOperationException("CSV contains an unterminated quoted field.");
        result.Add(field.ToString());
        return result;
    }
}

public sealed record ParsedBankStatement(IReadOnlyList<ParsedBankStatementRow> Rows);

public sealed record ParsedBankStatementRow(
    int SourceRowNumber,
    string RawRow,
    DateTime TransactionDate,
    DateTime? ValueDate,
    string? ExternalReference,
    string Description,
    decimal DebitAmount,
    decimal CreditAmount,
    decimal SignedAmount,
    decimal? RunningBalance,
    decimal? OpeningBalance,
    decimal? ClosingBalance,
    string? Currency);
