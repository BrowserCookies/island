using System;
using System.Globalization;

namespace Island.Core.Search;

/// <summary>
/// Quick calculations for the search bar: <c>+ - * / ^</c>, <c>× ÷</c>, <c>x</c> between operands,
/// parentheses, unary minus and postfix <c>%</c> (percent). Both <c>.</c> and <c>,</c> work as the
/// decimal separator. Input only counts as a calculation if it uses an operator or parentheses, so
/// plain numbers and names like "7zip" don't produce a result row.
/// </summary>
public static class Calculator
{
    public static bool TryEvaluate(string? input, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;
        var p = new Parser(input);
        try
        {
            value = p.ParseExpression();
            p.SkipSpaces();
            if (!p.AtEnd || !p.SawOperator) return false;
            return double.IsFinite(value);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string Format(double value)
    {
        if (Math.Abs(value) >= 1e15) return value.ToString("G10", CultureInfo.InvariantCulture);
        var rounded = Math.Round(value, 10);
        if (rounded == 0) return "0";
        return rounded.ToString("0.##########", CultureInfo.InvariantCulture);
    }

    private sealed class Parser
    {
        private readonly string _s;
        private int _i;

        public Parser(string s) => _s = s;

        public bool SawOperator { get; private set; }
        public bool AtEnd => _i >= _s.Length;

        public void SkipSpaces()
        {
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
        }

        private char Peek()
        {
            SkipSpaces();
            return _i < _s.Length ? _s[_i] : '\0';
        }

        // expression := term (('+' | '-') term)*
        public double ParseExpression()
        {
            double v = ParseTerm();
            while (true)
            {
                char c = Peek();
                if (c is '+' or '-')
                {
                    _i++;
                    SawOperator = true;
                    double r = ParseTerm();
                    v = c == '+' ? v + r : v - r;
                }
                else return v;
            }
        }

        // term := unary (('*' | '/' | '×' | '÷' | 'x') unary)*
        private double ParseTerm()
        {
            double v = ParseUnary();
            while (true)
            {
                char c = Peek();
                if (c is '*' or '/' or '×' or '÷' or 'x' or 'X')
                {
                    _i++;
                    SawOperator = true;
                    double r = ParseUnary();
                    v = c is '/' or '÷' ? v / r : v * r;
                }
                else return v;
            }
        }

        // unary := ('-' | '+') unary | power      (so -2^2 = -(2^2))
        private double ParseUnary()
        {
            char c = Peek();
            if (c is '-' or '+')
            {
                _i++;
                double v = ParseUnary();
                return c == '-' ? -v : v;
            }
            return ParsePower();
        }

        // power := postfix ('^' unary)?           (right-associative)
        private double ParsePower()
        {
            double b = ParsePostfix();
            if (Peek() == '^')
            {
                _i++;
                SawOperator = true;
                return Math.Pow(b, ParseUnary());
            }
            return b;
        }

        // postfix := primary '%'*
        private double ParsePostfix()
        {
            double v = ParsePrimary();
            while (Peek() == '%')
            {
                _i++;
                SawOperator = true;
                v /= 100;
            }
            return v;
        }

        // primary := number | '(' expression ')'
        private double ParsePrimary()
        {
            char c = Peek();
            if (c == '(')
            {
                _i++;
                SawOperator = true;
                double v = ParseExpression();
                if (Peek() != ')') throw new FormatException();
                _i++;
                return v;
            }
            return ParseNumber();
        }

        private double ParseNumber()
        {
            SkipSpaces();
            int start = _i;
            while (_i < _s.Length && char.IsAsciiDigit(_s[_i])) _i++;
            if (_i == start) throw new FormatException();
            if (_i < _s.Length && _s[_i] is '.' or ',')
            {
                int sep = _i++;
                int fracStart = _i;
                while (_i < _s.Length && char.IsAsciiDigit(_s[_i])) _i++;
                if (_i == fracStart) _i = sep; // "1." — leave the separator unparsed
            }
            var text = _s.Substring(start, _i - start).Replace(',', '.');
            return double.Parse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        }
    }
}
