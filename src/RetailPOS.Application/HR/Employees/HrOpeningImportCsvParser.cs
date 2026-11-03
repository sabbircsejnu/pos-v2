using System.Globalization;
using System.Text;
using RetailPOS.Application.HR.Payroll;

namespace RetailPOS.Application.HR.Employees;

public sealed class HrOpeningImportCsvParser : IHrOpeningImportParser
{
    public const string Version = "1";
    private const string EmployeeHeader =
        "ExternalKey,EmployeeCode,FirstName,DisplayName,EmploymentTypeId,EmploymentStatus,JoiningDate,PositionId,OrgUnitId,LocationProjectionId,AssignmentType,NationalId,Mobile";
    private const string AssignmentHeader =
        "ExternalKey,EmployeeCode,LocationProjectionId,AssignmentType,EffectiveFrom";
    private const string SalaryHeader =
        "ExternalKey,EmployeeCode,SalaryStructureId,EffectiveFrom,ComponentsJson,Reason,Reference";
    private const string LoanHeader =
        "ExternalKey,EmployeeCode,LoanType,PrincipalAmount,OutstandingAmount,DisbursedDate,RecoveryStartDate,RecoveryMode,FixedInstallment,ScheduleJson,Reason,Reference";

    public string Template(string importType, string templateVersion)
    {
        if (templateVersion != Version)
            throw new ArgumentException("Unsupported opening import template version.");
        return importType switch
        {
            "Employee" => EmployeeHeader + "\r\n",
            "Assignment" => AssignmentHeader + "\r\n",
            "Salary" => SalaryHeader + "\r\n",
            "LoanOpening" => LoanHeader + "\r\n",
            _ => throw new ArgumentException(
                "Only Employee, Assignment, Salary, and LoanOpening imports are supported.")
        };
    }

    public IReadOnlyList<HrOpeningImportRow> Parse(ReadOnlyMemory<byte> content,
        string importType, string templateVersion, int maxRows)
    {
        if (maxRows is < 1 or > 1000)
            throw new InvalidOperationException("An approved bounded import row limit is required.");
        var expectedHeader = Template(importType, templateVersion).TrimEnd('\r', '\n');
        string text;
        try { text = new UTF8Encoding(false, true).GetString(content.Span).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException exception)
        {
            throw new ArgumentException("Import must be valid UTF-8 CSV.", exception);
        }
        if (text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            throw new ArgumentException("Import contains unsupported control characters.");
        var records = ReadCsv(text);
        if (records.Count == 0 || string.Join(',', records[0]) != expectedHeader)
            throw new ArgumentException("CSV columns must exactly match the selected versioned template.");
        if (records.Count < 2 || records.Count - 1 > maxRows)
            throw new ArgumentException("Import is empty or exceeds the approved row limit.");
        var rows = new List<HrOpeningImportRow>();
        var keys = records.Skip(1).GroupBy(r => r.ElementAtOrDefault(0)?.Trim(), StringComparer.Ordinal)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var codes = importType is "Employee" or "Assignment"
            ? records.Skip(1).GroupBy(r => r.ElementAtOrDefault(1)?.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string?>(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index < records.Count; index++)
        {
            var cells = records[index];
            var errors = new List<string>();
            string Cell(int n) => cells.ElementAtOrDefault(n)?.Trim() ?? "";
            long? Id(int n)
            {
                if (Cell(n).Length == 0) return null;
                if (long.TryParse(Cell(n), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0)
                    return value;
                errors.Add($"Column {n + 1} requires a positive reference ID.");
                return null;
            }
            DateOnly Date(int n)
            {
                if (DateOnly.TryParseExact(Cell(n), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var value)) return value;
                errors.Add($"Column {n + 1} requires an ISO date.");
                return default;
            }
            decimal? Amount(int n, string name, bool optional = false, bool allowZero = false)
            {
                if (Cell(n).Length == 0)
                {
                    if (!optional) errors.Add($"{name} is required.");
                    return null;
                }
                if (!decimal.TryParse(Cell(n), NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var value)
                    || value < (allowZero ? 0m : 0.0001m)
                    || value > 99999999999999.9999m
                    || value != decimal.Round(value, 4))
                {
                    errors.Add($"{name} must be within the supported range and precision.");
                    return null;
                }
                return value;
            }
            if (cells.Count != expectedHeader.Split(',').Length)
                errors.Add("Row column count does not match the template.");
            if (Cell(0).Length is < 1 or > 100) errors.Add("ExternalKey is required and limited to 100 characters.");
            if (Cell(1).Length is < 1 or > 50) errors.Add("EmployeeCode is required and limited to 50 characters.");
            if (keys.Contains(Cell(0))) errors.Add("Duplicate ExternalKey in file.");
            if ((importType is "Employee" or "Assignment") && codes.Contains(Cell(1)))
                errors.Add("Duplicate EmployeeCode in file.");
            var date = importType switch
            {
                "Employee" => Date(6),
                "Assignment" => Date(4),
                "Salary" => Date(3),
                "LoanOpening" => Date(6),
                _ => default
            };
            HrEmployeeCreateData? employee = null;
            long? position = null, org = null;
            long? location = null;
            string assignment = "";
            PayrollSalaryOpeningImportData? salary = null;
            PayrollLoanOpeningImportData? loan = null;
            if (importType == "Employee")
            {
                var employmentType = Id(4);
                employee = new HrEmployeeCreateData(Cell(1), Cell(2), null, null, Cell(3), null,
                    null, Blank(Cell(11)), null, null, null, Blank(Cell(12)), null, null, null, null,
                    employmentType ?? 0, Cell(5), date, null);
                try { CreateHrEmployeeCommandHandler.Validate(employee); }
                catch (ArgumentException exception) { errors.Add(exception.Message); }
                position = Id(7);
                org = Id(8);
                if (position.HasValue != org.HasValue)
                    errors.Add("PositionId and OrgUnitId must be supplied together.");
                location = Id(9);
                assignment = Cell(10);
            }
            else if (importType == "Assignment")
            {
                location = Id(2);
                assignment = Cell(3);
                if (!location.HasValue) errors.Add("LocationProjectionId is required.");
            }
            else if (importType == "Salary")
            {
                var structureId = Id(2);
                IReadOnlyList<PayrollSalarySetupComponentInput> components = [];
                try
                {
                    components = System.Text.Json.JsonSerializer.Deserialize<PayrollSalarySetupComponentInput[]>(
                        Cell(4), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
                        ?? throw new System.Text.Json.JsonException();
                }
                catch (System.Text.Json.JsonException)
                {
                    errors.Add("ComponentsJson must be a valid component-value array.");
                }
                if (components.Count == 0) errors.Add("At least one salary component value is required.");
                if (Cell(5).Length is < 1 or > 1000) errors.Add("Reason is required and limited to 1000 characters.");
                if (Cell(6).Length > 150) errors.Add("Reference is limited to 150 characters.");
                if (structureId.HasValue && components.Count > 0)
                    salary = new(structureId.Value, date, components, Cell(5));
            }
            else if (importType == "LoanOpening")
            {
                var loanType = Cell(2);
                if (loanType is not ("Loan" or "Advance"))
                    errors.Add("LoanType must be Loan or Advance.");
                var principal = Amount(3, "PrincipalAmount");
                var outstanding = Amount(4, "OutstandingAmount", allowZero: true);
                var disbursedDate = Date(5);
                var recoveryDate = Date(6);
                var recoveryMode = Cell(7);
                var fixedInstallment = Amount(8, "FixedInstallment", optional: true);
                IReadOnlyList<PayrollLoanScheduleLineInput> schedule = [];
                if (Cell(9).Length > 0)
                {
                    try
                    {
                        schedule = System.Text.Json.JsonSerializer.Deserialize<PayrollLoanScheduleLineInput[]>(
                            Cell(9), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
                            ?? throw new System.Text.Json.JsonException();
                    }
                    catch (System.Text.Json.JsonException)
                    {
                        errors.Add("ScheduleJson must be a valid schedule-line array.");
                    }
                }
                if (recoveryMode is not ("FixedMonthly" or "Manual" or "CustomSchedule"))
                    errors.Add("RecoveryMode must be FixedMonthly, Manual, or CustomSchedule.");
                if (recoveryMode == "FixedMonthly" && !fixedInstallment.HasValue)
                    errors.Add("FixedMonthly recovery requires FixedInstallment.");
                if (recoveryMode != "FixedMonthly" && fixedInstallment.HasValue)
                    errors.Add("FixedInstallment is only valid for FixedMonthly recovery.");
                if (recoveryMode == "Manual" && schedule.Count != 0)
                    errors.Add("Manual recovery cannot include schedule lines.");
                if (Cell(10).Length is < 1 or > 1000) errors.Add("Reason is required and limited to 1000 characters.");
                if (Cell(11).Length > 150) errors.Add("Reference is limited to 150 characters.");
                if (principal.HasValue && outstanding.HasValue && outstanding > principal)
                    errors.Add("OutstandingAmount cannot exceed PrincipalAmount.");
                if (principal.HasValue && outstanding.HasValue && (loanType is "Loan" or "Advance")
                    && (recoveryMode is "FixedMonthly" or "Manual" or "CustomSchedule"))
                {
                    var request = new CreatePayrollEmployeeLoanRequest(
                        1, loanType, principal.Value, recoveryDate, recoveryMode,
                        fixedInstallment, schedule, Cell(10),
                        Cell(11).Length == 0 ? null : Cell(11), $"import-row-{index}");
                    loan = new(loanType, principal.Value, outstanding.Value, disbursedDate, request);
                }
            }
            if (location.HasValue && assignment is not ("Primary" or "Additional"))
                errors.Add("Opening location AssignmentType must be Primary or Additional.");
            if (!location.HasValue && assignment.Length != 0)
                errors.Add("AssignmentType requires a LocationProjectionId.");
            var externalKey = Cell(0);
            rows.Add(new HrOpeningImportRow(index, externalKey[..Math.Min(externalKey.Length, 100)], Cell(1).ToUpperInvariant(),
                employee, position, org, location, assignment, date, errors, salary, loan));
        }
        return rows;
    }

    private static string? Blank(string value) => value.Length == 0 ? null : value;

    private static List<List<string>> ReadCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var closed = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else { quoted = false; closed = true; }
                }
                else cell.Append(c);
            }
            else if (c == '"' && cell.Length == 0 && !closed) quoted = true;
            else if (c == ',' || c is '\r' or '\n')
            {
                row.Add(cell.ToString()); cell.Clear(); closed = false;
                if (c != ',')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    rows.Add(row); row = [];
                }
            }
            else if (closed || c == '"') throw new ArgumentException("CSV quoting is invalid.");
            else cell.Append(c);
        }
        if (quoted) throw new ArgumentException("CSV quoted field is not terminated.");
        if (cell.Length != 0 || row.Count != 0 || closed) { row.Add(cell.ToString()); rows.Add(row); }
        return rows;
    }
}
