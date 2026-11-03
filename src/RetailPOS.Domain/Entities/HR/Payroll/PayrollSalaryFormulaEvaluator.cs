using System.Globalization;

namespace RetailPOS.Core.Entities.HR.Payroll;

public static class PayrollSalaryFormulaEvaluator
{
    public static decimal Evaluate(string expression, IReadOnlyDictionary<string, decimal> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        PayrollSalaryFormulaRules.ParseReferences(expression);
        return new Evaluator(expression, values).Evaluate();
    }

    private sealed class Evaluator(string expression, IReadOnlyDictionary<string, decimal> values)
    {
        private int position;

        public decimal Evaluate()
        {
            var value = ParseExpression();
            SkipWhitespace();
            if (position != expression.Length)
                throw InvalidFormula();
            return value;
        }

        private decimal ParseExpression()
        {
            var value = ParseTerm();
            while (true)
            {
                SkipWhitespace();
                if (Take('+'))
                    value += ParseTerm();
                else if (Take('-'))
                    value -= ParseTerm();
                else
                    return value;
            }
        }

        private decimal ParseTerm()
        {
            var value = ParseFactor();
            while (true)
            {
                SkipWhitespace();
                if (Take('*'))
                    value *= ParseFactor();
                else if (Take('/'))
                {
                    var divisor = ParseFactor();
                    if (divisor == 0)
                        throw new ArgumentException("Formula division by zero is not allowed.");
                    value /= divisor;
                }
                else
                    return value;
            }
        }

        private decimal ParseFactor()
        {
            SkipWhitespace();
            if (Take('+'))
                return ParseFactor();
            if (Take('-'))
                return -ParseFactor();
            if (Take('('))
            {
                var value = ParseExpression();
                SkipWhitespace();
                if (!Take(')'))
                    throw InvalidFormula();
                return value;
            }

            if (position >= expression.Length)
                throw InvalidFormula();
            if (char.IsAsciiDigit(expression[position]) || expression[position] == '.')
                return ParseNumber();
            if (char.IsAsciiLetter(expression[position]) || expression[position] == '_')
                return ParseReference();
            throw InvalidFormula();
        }

        private decimal ParseNumber()
        {
            var start = position;
            var hasDecimalPoint = false;
            while (position < expression.Length)
            {
                var current = expression[position];
                if (char.IsAsciiDigit(current))
                    position++;
                else if (current == '.' && !hasDecimalPoint)
                {
                    hasDecimalPoint = true;
                    position++;
                }
                else
                    break;
            }

            if (!decimal.TryParse(expression.AsSpan(start, position - start),
                    NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
                throw InvalidFormula();
            return value;
        }

        private decimal ParseReference()
        {
            var start = position++;
            while (position < expression.Length
                   && (char.IsAsciiLetterOrDigit(expression[position]) || expression[position] == '_'))
                position++;
            var code = expression[start..position];
            if (!values.TryGetValue(code, out var value))
                throw new ArgumentException($"Formula references unavailable component '{code}'.");
            return value;
        }

        private bool Take(char expected)
        {
            if (position >= expression.Length || expression[position] != expected)
                return false;
            position++;
            return true;
        }

        private void SkipWhitespace()
        {
            while (position < expression.Length && char.IsWhiteSpace(expression[position]))
                position++;
        }

        private static ArgumentException InvalidFormula() =>
            new("Formula must use component codes, decimal literals, parentheses and the +, -, *, / operators only.");
    }
}
