using System.Globalization;
using System.Text;

namespace ASMForge.Core.Pseudocode;

/// <summary>A pseudocode problem at a 1-based line and column.</summary>
public sealed record PseudocodeError(int Line, int Column, string Message);

internal enum TokenKind { Number, Char, String, Identifier, Keyword, Operator, End }

/// <summary>A token; Value holds the number or character code; Start/End are offsets into the source.</summary>
internal sealed record Token(TokenKind Kind, string Text, int Value, int Line, int Column, int Start, int End)
{
    public bool Is(string text) => Kind is TokenKind.Operator or TokenKind.Keyword && Text == text;
}

/// <summary>Turns pseudocode into tokens. Problems are collected rather than thrown.</summary>
internal sealed class PseudocodeLexer
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "int", "if", "else", "while", "for", "do", "break", "continue", "true", "false"
    };

    // Longest operators first so "<=" wins over "<".
    private static readonly string[] Operators =
    {
        "&&", "||", "==", "!=", "<=", ">=", "++", "--", "+=", "-=", "*=", "/=", "%=",
        "+", "-", "*", "/", "%", "<", ">", "=", "!", "(", ")", "{", "}", "[", "]", ",", ";"
    };

    private readonly string _source;
    private readonly List<PseudocodeError> _errors;
    private int _position;
    private int _line = 1;
    private int _lineStart;

    public PseudocodeLexer(string source, List<PseudocodeError> errors)
    {
        _source = source;
        _errors = errors;
    }

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();
        while (true)
        {
            SkipWhitespaceAndComments();
            if (_position >= _source.Length)
            {
                tokens.Add(new Token(TokenKind.End, "end of file", 0, _line, Column, _position, _position));
                return tokens;
            }

            var start = _position;
            var line = _line;
            var column = Column;
            var c = _source[_position];

            if (char.IsDigit(c))
            {
                tokens.Add(ReadNumber(start, line, column));
            }
            else if (char.IsLetter(c) || c == '_')
            {
                while (_position < _source.Length && (char.IsLetterOrDigit(_source[_position]) || _source[_position] == '_')) _position++;
                var word = _source[start.._position];
                tokens.Add(new Token(Keywords.Contains(word) ? TokenKind.Keyword : TokenKind.Identifier, word, 0, line, column, start, _position));
            }
            else if (c == '"')
            {
                tokens.Add(ReadString(start, line, column));
            }
            else if (c == '\'')
            {
                tokens.Add(ReadChar(start, line, column));
            }
            else if (Operators.FirstOrDefault(op => string.CompareOrdinal(_source, _position, op, 0, op.Length) == 0) is { } op)
            {
                _position += op.Length;
                tokens.Add(new Token(TokenKind.Operator, op, 0, line, column, start, _position));
            }
            else
            {
                _errors.Add(new PseudocodeError(line, column, $"Unexpected character '{c}'."));
                _position++;
            }
        }
    }

    private int Column => _position - _lineStart + 1;

    private void Advance()
    {
        if (_source[_position] == '\n') { _line++; _lineStart = _position + 1; }
        _position++;
    }

    private void SkipWhitespaceAndComments()
    {
        while (_position < _source.Length)
        {
            if (char.IsWhiteSpace(_source[_position])) { Advance(); continue; }
            if (Peek("//") || _source[_position] == '#')
            {
                while (_position < _source.Length && _source[_position] != '\n') _position++;
                continue;
            }
            if (Peek("/*"))
            {
                var (line, column) = (_line, Column);
                _position += 2;
                while (_position < _source.Length && !Peek("*/")) Advance();
                if (_position >= _source.Length) _errors.Add(new PseudocodeError(line, column, "Comment is never closed (missing */)."));
                else _position += 2;
                continue;
            }
            return;
        }
    }

    private bool Peek(string text) => string.CompareOrdinal(_source, _position, text, 0, text.Length) == 0;

    private Token ReadNumber(int start, int line, int column)
    {
        var hex = Peek("0x") || Peek("0X");
        if (hex) _position += 2;
        while (_position < _source.Length && (hex ? Uri.IsHexDigit(_source[_position]) : char.IsDigit(_source[_position]))) _position++;
        var text = _source[start.._position];
        var parsed = hex
            ? long.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var h) ? h : -1
            : long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var d) ? d : -1;
        if (parsed < 0 || parsed > uint.MaxValue)
        {
            _errors.Add(new PseudocodeError(line, column, $"Number {text} does not fit in 32 bits."));
            parsed = 0;
        }
        return new Token(TokenKind.Number, text, unchecked((int)parsed), line, column, start, _position);
    }

    // Strings keep their escape sequences as written; MARS's .asciiz understands the same ones.
    private Token ReadString(int start, int line, int column)
    {
        _position++;
        var text = new StringBuilder();
        while (_position < _source.Length && _source[_position] != '"' && _source[_position] != '\n')
        {
            if (_source[_position] == '\\' && _position + 1 < _source.Length)
            {
                var escape = _source[_position + 1];
                if ("ntr0\\\"'".IndexOf(escape) < 0)
                    _errors.Add(new PseudocodeError(_line, Column, $"Unknown escape sequence '\\{escape}'. Use \\n, \\t, \\\\, \\\" or \\0."));
                text.Append('\\').Append(escape);
                _position += 2;
                continue;
            }
            text.Append(_source[_position++]);
        }
        if (_position >= _source.Length || _source[_position] != '"')
            _errors.Add(new PseudocodeError(line, column, "String is never closed (missing \")."));
        else
            _position++;
        return new Token(TokenKind.String, text.ToString(), 0, line, column, start, _position);
    }

    private Token ReadChar(int start, int line, int column)
    {
        _position++;
        var value = 0;
        if (_position < _source.Length && _source[_position] == '\\' && _position + 1 < _source.Length)
        {
            var escape = _source[_position + 1];
            value = escape switch { 'n' => '\n', 't' => '\t', 'r' => '\r', '0' => 0, '\\' => '\\', '\'' => '\'', '"' => '"', _ => -1 };
            if (value < 0) { _errors.Add(new PseudocodeError(line, column, $"Unknown escape sequence '\\{escape}'.")); value = 0; }
            _position += 2;
        }
        else if (_position < _source.Length && _source[_position] != '\'')
        {
            value = _source[_position++];
        }
        if (_position >= _source.Length || _source[_position] != '\'')
            _errors.Add(new PseudocodeError(line, column, "Character literal must be one character in single quotes, like 'a' or '\\n'."));
        else
            _position++;
        return new Token(TokenKind.Char, _source[start..Math.Min(_position, _source.Length)], value, line, column, start, _position);
    }
}

// ---- Syntax tree ----

internal abstract record Node(int Line, int Column);

internal abstract record Expr(int Line, int Column) : Node(Line, Column);
internal sealed record NumberExpr(int Line, int Column, int Value, bool IsChar) : Expr(Line, Column);
internal sealed record VariableExpr(int Line, int Column, string Name) : Expr(Line, Column);
internal sealed record IndexExpr(int Line, int Column, string Name, Expr Index) : Expr(Line, Column);
internal sealed record UnaryExpr(int Line, int Column, string Op, Expr Operand) : Expr(Line, Column);
internal sealed record BinaryExpr(int Line, int Column, string Op, Expr Left, Expr Right) : Expr(Line, Column);
internal sealed record CallExpr(int Line, int Column, string Name) : Expr(Line, Column); // readInt() / readChar()

/// <summary>Statements carry Source: the statement (or its header) as written, used for comments in the output.</summary>
internal abstract record Stmt(int Line, int Column, string Source) : Node(Line, Column);
internal sealed record Declarator(int Line, int Column, string Name, Expr? Init, int? ArraySize, List<Expr>? ArrayInit);
internal sealed record DeclarationStmt(int Line, int Column, string Source, List<Declarator> Declarators) : Stmt(Line, Column, Source);
internal sealed record AssignStmt(int Line, int Column, string Source, Expr Target, string Op, Expr Value) : Stmt(Line, Column, Source);
internal sealed record IncrementStmt(int Line, int Column, string Source, Expr Target, int Delta) : Stmt(Line, Column, Source);
internal sealed record IfStmt(int Line, int Column, string Source, Expr Condition, Stmt Then, Stmt? Else) : Stmt(Line, Column, Source);
internal sealed record WhileStmt(int Line, int Column, string Source, Expr Condition, Stmt Body) : Stmt(Line, Column, Source);
internal sealed record DoWhileStmt(int Line, int Column, string Source, Stmt Body, Expr Condition, string ConditionSource) : Stmt(Line, Column, Source);
internal sealed record ForStmt(int Line, int Column, string Source, Stmt? Init, Expr? Condition, Stmt? Step, Stmt Body) : Stmt(Line, Column, Source);
internal sealed record BreakStmt(int Line, int Column, string Source) : Stmt(Line, Column, Source);
internal sealed record ContinueStmt(int Line, int Column, string Source) : Stmt(Line, Column, Source);
internal sealed record PrintStmt(int Line, int Column, string Source, List<object> Arguments, bool AsChar) : Stmt(Line, Column, Source); // Expr or string
internal sealed record ExitStmt(int Line, int Column, string Source) : Stmt(Line, Column, Source);
internal sealed record ExpressionStmt(int Line, int Column, string Source, Expr Expression) : Stmt(Line, Column, Source);
internal sealed record BlockStmt(int Line, int Column, string Source, List<Stmt> Statements) : Stmt(Line, Column, Source);
