using System.Globalization;

namespace RetailPOS.Core.Entities.HR.Payroll;

public static class PayrollSalaryFormulaRules
{
    public static IReadOnlySet<string> ParseReferences(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        if (expression.Length > 1000)
            throw new ArgumentException("Formula expressions must not exceed 1000 characters.");

        var parser = new FormulaParser(expression);
        return parser.Parse();
    }

    public static void ValidateComponentGraph(IReadOnlyCollection<PayrollSalaryComponent> components)
    {
        var byCode = components.ToDictionary(component => component.Code, StringComparer.OrdinalIgnoreCase);
        var dependencies = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var component in components.Where(component =>
                     component.IsActive && component.ValueType == "Formula"))
        {
            var references = ParseReferences(component.FormulaExpression
                ?? throw new ArgumentException($"Formula component '{component.Code}' requires an expression."));
            foreach (var reference in references)
            {
                if (!byCode.TryGetValue(reference, out var dependency) || !dependency.IsActive)
                    throw new ArgumentException(
                        $"Formula component '{component.Code}' references an unknown or inactive component '{reference}'.");
            }
            dependencies[component.Code] = references;
        }

        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in dependencies.Keys)
            Visit(code);

        void Visit(string code)
        {
            if (visited.Contains(code))
                return;
            if (!visiting.Add(code))
                throw new ArgumentException($"Formula dependency cycle detected at component '{code}'.");

            if (dependencies.TryGetValue(code, out var references))
            {
                foreach (var reference in references)
                {
                    if (dependencies.ContainsKey(reference))
                        Visit(reference);
                }
            }

            visiting.Remove(code);
            visited.Add(code);
        }
    }

    public static void ValidateStructureFormulas(
        IReadOnlyCollection<PayrollSalaryStructureLine> lines,
        IReadOnlyCollection<PayrollSalaryComponent> components)
    {
        var componentsById = components.ToDictionary(component => component.Id);
        var lineByComponentId = lines.ToDictionary(line => line.SalaryComponentId);
        var dependencies = new Dictionary<long, IReadOnlySet<long>>();

        foreach (var line in lines)
        {
            if (!componentsById.TryGetValue(line.SalaryComponentId, out var component)
                || !component.IsActive)
                throw new ArgumentException("Salary structures may include only active components from the same Business.");

            if (line.DefaultValue < 0)
                throw new ArgumentException("Salary structure defaults cannot be negative.");
            if (line.FormulaOverride is not null && component.ValueType != "Formula")
                throw new ArgumentException("Only Formula components may specify a formula override.");
            var expression = line.FormulaOverride ?? component.FormulaExpression;
            if (component.ValueType != "Formula" || expression is null)
                continue;

            var references = ParseReferences(expression);
            var referencedIds = new HashSet<long>();
            foreach (var reference in references)
            {
                var target = components.SingleOrDefault(candidate =>
                    string.Equals(candidate.Code, reference, StringComparison.OrdinalIgnoreCase));
                if (target is null || !target.IsActive || !lineByComponentId.ContainsKey(target.Id))
                    throw new ArgumentException(
                        $"Salary structure formula for '{component.Code}' references a component outside this structure: '{reference}'.");
                referencedIds.Add(target.Id);
            }
            dependencies[line.SalaryComponentId] = referencedIds;
        }

        var visiting = new HashSet<long>();
        var visited = new HashSet<long>();
        foreach (var componentId in dependencies.Keys)
            Visit(componentId);

        void Visit(long componentId)
        {
            if (visited.Contains(componentId))
                return;
            if (!visiting.Add(componentId))
                throw new ArgumentException("Salary structure formula dependency cycle detected.");

            if (dependencies.TryGetValue(componentId, out var references))
            {
                foreach (var reference in references)
                {
                    if (dependencies.ContainsKey(reference))
                        Visit(reference);
                }
            }

            visiting.Remove(componentId);
            visited.Add(componentId);
        }
    }

    private sealed class FormulaParser(string expression)
    {
        private int _position;
        private int _tokens;
        private readonly HashSet<string> _references = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlySet<string> Parse()
        {
            ParseExpression();
            SkipWhitespace();
            if (_position != expression.Length)
                throw InvalidFormula();
            return _references;
        }

        private void ParseExpression()
        {
            ParseTerm();
            while (Take('+') || Take('-'))
                ParseTerm();
        }

        private void ParseTerm()
        {
            ParseFactor();
            while (Take('*') || Take('/'))
                ParseFactor();
        }

        private void ParseFactor()
        {
            SkipWhitespace();
            if (Take('+') || Take('-'))
            {
                ParseFactor();
                return;
            }

            if (Take('('))
            {
                ParseExpression();
                if (!Take(')'))
                    throw InvalidFormula();
                return;
            }

            if (_position >= expression.Length)
                throw InvalidFormula();

            if (char.IsAsciiDigit(expression[_position]) || expression[_position] == '.')
            {
                ParseNumber();
                return;
            }

            if (char.IsAsciiLetter(expression[_position]) || expression[_position] == '_')
            {
                _references.Add(ParseIdentifier());
                return;
            }

            throw InvalidFormula();
        }

        private void ParseNumber()
        {
            var start = _position;
            var decimalPointSeen = false;
            while (_position < expression.Length)
            {
                var current = expression[_position];
                if (char.IsAsciiDigit(current))
                    _position++;
                else if (current == '.' && !decimalPointSeen)
                {
                    decimalPointSeen = true;
                    _position++;
                }
                else
                    break;
            }

            if (!decimal.TryParse(expression.AsSpan(start, _position - start),
                    NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _))
                throw InvalidFormula();
            CountToken();
        }

        private string ParseIdentifier()
        {
            var start = _position++;
            while (_position < expression.Length
                   && (char.IsAsciiLetterOrDigit(expression[_position]) || expression[_position] == '_'))
                _position++;
            CountToken();
            return expression[start.._position].ToUpperInvariant();
        }

        private bool Take(char expected)
        {
            SkipWhitespace();
            if (_position >= expression.Length || expression[_position] != expected)
                return false;
            _position++;
            if (expected is not '+' and not '-' and not '*' and not '/' and not '(' and not ')')
                return false;
            CountToken();
            return true;
        }

        private void CountToken()
        {
            if (++_tokens > 256)
                throw new ArgumentException("Formula expressions must not exceed 256 tokens.");
        }

        private void SkipWhitespace()
        {
            while (_position < expression.Length && char.IsWhiteSpace(expression[_position]))
                _position++;
        }

        private static ArgumentException InvalidFormula() =>
            new("Formula must use component codes, decimal literals, parentheses and the +, -, *, / operators only.");
    }
}
