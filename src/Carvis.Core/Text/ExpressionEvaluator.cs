using System.Globalization;

namespace Carvis.Core.Text;

/// <summary>
/// Evaluates arithmetic so the model doesn't have to (small models get sums wrong).
/// Supports + - * / ^, parentheses, "mod", postfix % ("20% de 150", "150 + 10%"), sqrt, sin, cos,
/// tan, log, ln, abs, round, floor, ceil, pi, e. Decimal comma or point.
/// </summary>
public sealed class ExpressionEvaluator
{
    private readonly string _text;
    private int _position;

    private ExpressionEvaluator(string text) => _text = text;

    public static double Evaluate(string expression)
    {
        var text = Prepare(expression);
        var parser = new ExpressionEvaluator(text);
        var value = parser.ParseSum();
        parser.SkipSpaces();
        if (parser._position < text.Length)
            throw new FormatException($"No entiendo «{text[parser._position..]}».");
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new ArithmeticException("El resultado no es un número (¿división entre cero?).");
        return value;
    }

    private static string Prepare(string expression)
    {
        var text = expression.ToLowerInvariant()
            .Replace("×", "*").Replace("·", "*").Replace("÷", "/").Replace("−", "-")
            .Replace("√", "sqrt").Replace("raiz", "sqrt").Replace("raíz", "sqrt")
            .Replace(" de ", " * ").Replace(" por ", " * ").Replace(" entre ", " / ")
            .Replace(" mas ", " + ").Replace(" más ", " + ").Replace(" menos ", " - ");
        // "1.234,5" (Spanish thousands) and "3,5" (decimal comma).
        if (text.Contains(',') && text.Contains('.') && text.LastIndexOf(',') > text.LastIndexOf('.'))
            text = text.Replace(".", string.Empty);
        return text.Replace(',', '.').Trim();
    }

    // sum := term (("+" | "-") term)*  — "a + b%" means a plus b percent of a.
    private double ParseSum()
    {
        var value = ParseTerm(out _);
        while (true)
        {
            SkipSpaces();
            double sign;
            if (TryTake('+'))
                sign = 1;
            else if (TryTake('-'))
                sign = -1;
            else
                return value;
            var right = ParseTerm(out var barePercent);
            value += sign * (barePercent ? value * right : right);
        }
    }

    // term := factor (("*" | "/" | "mod") factor)*  — "b%" is b/100.
    private double ParseTerm(out bool barePercent)
    {
        var value = ParsePower(out barePercent);
        if (barePercent)
            value /= 100;
        while (true)
        {
            SkipSpaces();
            bool percent;
            if (TryTake('*'))
            {
                value *= ParsePower(out percent) / (percent ? 100 : 1);
            }
            else if (TryTake('/'))
            {
                value /= ParsePower(out percent) / (percent ? 100 : 1);
            }
            else if (TryWord("mod"))
            {
                value %= ParsePower(out _);
            }
            else
            {
                return value;
            }
            barePercent = false;
        }
    }

    private double ParsePower(out bool percent)
    {
        var value = ParseUnary(out percent);
        SkipSpaces();
        if (TryTake('^') || TryTake("**"))
        {
            value = Math.Pow(value, ParsePower(out var p2));
            percent = p2;
        }
        return value;
    }

    private double ParseUnary(out bool percent)
    {
        SkipSpaces();
        if (TryTake('-'))
            return -ParseUnary(out percent);
        if (TryTake('+'))
            return ParseUnary(out percent);
        var value = ParseAtom();
        SkipSpaces();
        percent = TryTake('%');
        return value;
    }

    private double ParseAtom()
    {
        SkipSpaces();
        if (TryTake('('))
        {
            var inner = ParseSum();
            SkipSpaces();
            if (!TryTake(')'))
                throw new FormatException("Falta cerrar un paréntesis.");
            return inner;
        }

        if (_position < _text.Length && (char.IsDigit(_text[_position]) || _text[_position] == '.'))
        {
            var start = _position;
            while (_position < _text.Length && (char.IsDigit(_text[_position]) || _text[_position] == '.'))
                _position++;
            if (_position < _text.Length && _text[_position] == 'e' && _position + 1 < _text.Length && (char.IsDigit(_text[_position + 1]) || _text[_position + 1] is '-' or '+'))
            {
                _position += 2;
                while (_position < _text.Length && char.IsDigit(_text[_position]))
                    _position++;
            }
            return double.Parse(_text[start.._position], NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        var nameStart = _position;
        while (_position < _text.Length && char.IsLetter(_text[_position]))
            _position++;
        var name = _text[nameStart.._position];
        switch (name)
        {
            case "pi": return Math.PI;
            case "e": return Math.E;
            case "":
                throw new FormatException(_position < _text.Length ? $"No entiendo «{_text[_position]}»." : "La expresión está incompleta.");
        }

        var argument = ParseUnary(out _);
        return name switch
        {
            "sqrt" => Math.Sqrt(argument),
            "sin" or "sen" => Math.Sin(argument),
            "cos" => Math.Cos(argument),
            "tan" => Math.Tan(argument),
            "log" => Math.Log10(argument),
            "ln" => Math.Log(argument),
            "abs" => Math.Abs(argument),
            "round" or "redondea" => Math.Round(argument),
            "floor" => Math.Floor(argument),
            "ceil" => Math.Ceiling(argument),
            _ => throw new FormatException($"No conozco la función «{name}»."),
        };
    }

    private void SkipSpaces()
    {
        while (_position < _text.Length && char.IsWhiteSpace(_text[_position]))
            _position++;
    }

    private bool Peek(char c) => _position < _text.Length && _text[_position] == c;

    private bool TryTake(char c)
    {
        if (!Peek(c))
            return false;
        _position++;
        return true;
    }

    private bool TryTake(string s)
    {
        if (string.CompareOrdinal(_text, _position, s, 0, s.Length) != 0)
            return false;
        _position += s.Length;
        return true;
    }

    private bool TryWord(string word)
    {
        SkipSpaces();
        if (string.CompareOrdinal(_text, _position, word, 0, word.Length) != 0)
            return false;
        var end = _position + word.Length;
        if (end < _text.Length && char.IsLetter(_text[end]))
            return false;
        _position = end;
        return true;
    }
}
