using System.Text.RegularExpressions;

namespace ASMForge.Core.Pseudocode;

/// <summary>
/// Recursive-descent parser for the pseudocode language (C-like). Errors are collected; after an error the
/// parser skips to the next ';' or block boundary so later mistakes are still reported.
/// </summary>
internal sealed class PseudocodeParser
{
    private sealed class ParseError : Exception
    {
        public ParseError(int line, int column, string message) : base(message) { Line = line; Column = column; }
        public int Line { get; }
        public int Column { get; }
    }

    private static readonly HashSet<string> StatementKeywords = new() { "if", "while", "for", "do", "int", "break", "continue" };
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private readonly List<Token> _tokens;
    private readonly string _source;
    private readonly List<PseudocodeError> _errors;
    private int _index;

    public PseudocodeParser(List<Token> tokens, string source, List<PseudocodeError> errors)
    {
        _tokens = tokens;
        _source = source;
        _errors = errors;
    }

    public List<Stmt> ParseProgram()
    {
        var statements = new List<Stmt>();
        while (!AtEnd)
        {
            if (Current.Is("}"))
            {
                _errors.Add(new PseudocodeError(Current.Line, Current.Column, "Unexpected '}' with no matching '{'."));
                _index++;
                continue;
            }
            if (ParseStatementSafely() is { } statement) statements.Add(statement);
        }
        return statements;
    }

    // ---- Statements ----

    private Stmt? ParseStatementSafely()
    {
        var start = _index;
        try
        {
            return ParseStatement();
        }
        catch (ParseError e)
        {
            _errors.Add(new PseudocodeError(e.Line, e.Column, e.Message));
            Synchronize(start);
            return null;
        }
    }

    private void Synchronize(int start)
    {
        if (_index == start && !AtEnd && !Current.Is("}")) _index++; // always make progress
        while (!AtEnd)
        {
            if (Current.Is(";")) { _index++; return; }
            if (Current.Is("}") || Current.Is("{")) return;
            if (Current.Kind == TokenKind.Keyword && StatementKeywords.Contains(Current.Text)) return;
            _index++;
        }
    }

    private Stmt ParseStatement()
    {
        var first = Current;
        if (first.Is("{")) return ParseBlock();
        if (first.Is(";")) { _index++; return new BlockStmt(first.Line, first.Column, string.Empty, new List<Stmt>()); }
        if (first.Is("if")) return ParseIf(elsePrefix: false);
        if (first.Is("while")) return ParseWhile();
        if (first.Is("do")) return ParseDoWhile();
        if (first.Is("for")) return ParseFor();
        if (first.Is("else")) throw Error(first, "'else' without a matching 'if'.");
        if (first.Is("break") || first.Is("continue"))
        {
            _index++;
            ExpectSemicolon();
            return first.Text == "break"
                ? new BreakStmt(first.Line, first.Column, SourceFrom(first))
                : new ContinueStmt(first.Line, first.Column, SourceFrom(first));
        }

        var statement = first.Is("int") ? ParseDeclaration() : ParseSimple();
        ExpectSemicolon();
        return statement with { Source = SourceFrom(first) };
    }

    private BlockStmt ParseBlock()
    {
        var open = Expect("{", "to start a block");
        var statements = new List<Stmt>();
        while (!Current.Is("}"))
        {
            if (AtEnd) throw Error(open, "Block is never closed (missing '}').");
            if (ParseStatementSafely() is { } statement) statements.Add(statement);
        }
        _index++;
        return new BlockStmt(open.Line, open.Column, string.Empty, statements);
    }

    private DeclarationStmt ParseDeclaration()
    {
        var keyword = Expect("int", "to declare a variable");
        var declarators = new List<Declarator>();
        do
        {
            var name = ExpectIdentifier("as the variable name");
            if (Match("["))
            {
                var sizeToken = Current;
                var size = ParseExpression();
                Expect("]", "to close the array size");
                if (size is not NumberExpr { Value: > 0 } number)
                    throw Error(sizeToken, "Array size must be a positive number, like int a[10];");
                List<Expr>? values = null;
                if (Match("="))
                {
                    Expect("{", "to start the array's values, like = {1, 2, 3}");
                    values = new List<Expr>();
                    if (!Current.Is("}"))
                        do values.Add(ParseExpression()); while (Match(","));
                    Expect("}", "to close the array's values");
                }
                declarators.Add(new Declarator(name.Line, name.Column, name.Text, null, number.Value, values));
            }
            else
            {
                var init = Match("=") ? ParseExpression() : null;
                declarators.Add(new Declarator(name.Line, name.Column, name.Text, init, null, null));
            }
        } while (Match(","));
        return new DeclarationStmt(keyword.Line, keyword.Column, string.Empty, declarators);
    }

    private IfStmt ParseIf(bool elsePrefix)
    {
        var keyword = Expect("if", "");
        Expect("(", "after 'if'");
        var condition = ParseExpression();
        var close = Expect(")", "to close the if condition");
        var header = (elsePrefix ? "else " : string.Empty) + SourceFrom(keyword, close);
        var then = ParseStatement();
        Stmt? otherwise = null;
        if (Match("else"))
            otherwise = Current.Is("if") ? ParseIf(elsePrefix: true) : ParseStatement();
        return new IfStmt(keyword.Line, keyword.Column, header, condition, then, otherwise);
    }

    private WhileStmt ParseWhile()
    {
        var keyword = Expect("while", "");
        Expect("(", "after 'while'");
        var condition = ParseExpression();
        var close = Expect(")", "to close the while condition");
        return new WhileStmt(keyword.Line, keyword.Column, SourceFrom(keyword, close), condition, ParseStatement());
    }

    private DoWhileStmt ParseDoWhile()
    {
        var keyword = Expect("do", "");
        var body = ParseStatement();
        var whileKeyword = Expect("while", "after the do { ... } body");
        Expect("(", "after 'while'");
        var condition = ParseExpression();
        var close = Expect(")", "to close the while condition");
        ExpectSemicolon();
        return new DoWhileStmt(keyword.Line, keyword.Column, "do", body, condition, SourceFrom(whileKeyword, close));
    }

    private ForStmt ParseFor()
    {
        var keyword = Expect("for", "");
        Expect("(", "after 'for'");
        Stmt? init = null;
        if (!Current.Is(";"))
        {
            var initStart = Current;
            var parsed = Current.Is("int") ? ParseDeclaration() : ParseSimple();
            init = parsed with { Source = SourceFrom(initStart) };
        }
        Expect(";", "after the for loop's start");
        var condition = Current.Is(";") ? null : ParseExpression();
        Expect(";", "after the for loop's condition");
        Stmt? step = null;
        if (!Current.Is(")"))
        {
            var stepStart = Current;
            step = ParseSimple() with { Source = SourceFrom(stepStart) };
        }
        var close = Expect(")", "to close the for loop header");
        return new ForStmt(keyword.Line, keyword.Column, SourceFrom(keyword, close), init, condition, step, ParseStatement());
    }

    // Assignments, ++/--, and built-in calls (print, printChar, exit, readInt, readChar). No trailing ';'.
    private Stmt ParseSimple()
    {
        var first = Current;
        if (first.Is("++") || first.Is("--"))
        {
            _index++;
            var operand = ParseTarget();
            return new IncrementStmt(first.Line, first.Column, string.Empty, operand, first.Text == "++" ? 1 : -1);
        }
        if (first.Kind != TokenKind.Identifier)
            throw Error(first, first.Kind == TokenKind.End ? "Expected a statement, but the file ended." : $"Expected a statement, but found '{first.Text}'.");

        if (Next.Is("("))
        {
            switch (first.Text)
            {
                case "print": return ParsePrint(asChar: false);
                case "printChar": return ParsePrint(asChar: true);
                case "exit":
                    _index++; Expect("(", ""); Expect(")", "- exit() takes no arguments");
                    return new ExitStmt(first.Line, first.Column, string.Empty);
                case "readInt":
                case "readChar":
                    return new ExpressionStmt(first.Line, first.Column, string.Empty, ParsePrimary());
                default:
                    throw Error(first, $"Unknown function '{first.Text}'. Available: print, printChar, readInt, readChar, exit.");
            }
        }

        var target = ParseTarget();
        var op = Current;
        if (op.Is("++") || op.Is("--"))
        {
            _index++;
            return new IncrementStmt(first.Line, first.Column, string.Empty, target, op.Text == "++" ? 1 : -1);
        }
        if (op.Is("=") || op.Is("+=") || op.Is("-=") || op.Is("*=") || op.Is("/=") || op.Is("%="))
        {
            _index++;
            var value = ParseExpression();
            return new AssignStmt(first.Line, first.Column, string.Empty, target, op.Text == "=" ? "=" : op.Text[..1], value);
        }
        throw Error(op, $"Expected '=', '++' or '--' after '{first.Text}', but found '{op.Text}'.");
    }

    private Expr ParseTarget()
    {
        var name = ExpectIdentifier("as the variable to change");
        if (!Match("[")) return new VariableExpr(name.Line, name.Column, name.Text);
        var index = ParseExpression();
        Expect("]", "to close the array index");
        return new IndexExpr(name.Line, name.Column, name.Text, index);
    }

    private PrintStmt ParsePrint(bool asChar)
    {
        var name = _tokens[_index++];
        Expect("(", $"after '{name.Text}'");
        var arguments = new List<object>();
        if (Current.Is(")")) throw Error(Current, $"{name.Text}(...) needs something to print.");
        do
        {
            if (Current.Kind == TokenKind.String)
            {
                if (asChar) throw Error(Current, "printChar prints one character value; use print(\"...\") for text.");
                arguments.Add(_tokens[_index++].Text);
            }
            else
            {
                arguments.Add(ParseExpression());
            }
        } while (Match(","));
        Expect(")", $"to close {name.Text}(...)");
        return new PrintStmt(name.Line, name.Column, string.Empty, arguments, asChar);
    }

    // ---- Expressions (C precedence) ----

    private Expr ParseExpression() => ParseBinary(0);

    private static readonly string[][] Precedence =
    {
        new[] { "||" }, new[] { "&&" }, new[] { "==", "!=" }, new[] { "<", "<=", ">", ">=" }, new[] { "+", "-" }, new[] { "*", "/", "%" }
    };

    private Expr ParseBinary(int level)
    {
        if (level == Precedence.Length) return ParseUnary();
        var left = ParseBinary(level + 1);
        while (Current.Kind == TokenKind.Operator && Precedence[level].Contains(Current.Text))
        {
            var op = _tokens[_index++];
            var right = ParseBinary(level + 1);
            left = new BinaryExpr(op.Line, op.Column, op.Text, left, right);
        }
        return left;
    }

    private Expr ParseUnary()
    {
        var token = Current;
        if (token.Is("-") || token.Is("!") || token.Is("+"))
        {
            _index++;
            var operand = ParseUnary();
            return token.Text == "+" ? operand : new UnaryExpr(token.Line, token.Column, token.Text, operand);
        }
        return ParsePrimary();
    }

    private Expr ParsePrimary()
    {
        var token = Current;
        switch (token.Kind)
        {
            case TokenKind.Number:
                _index++;
                return new NumberExpr(token.Line, token.Column, token.Value, false);
            case TokenKind.Char:
                _index++;
                return new NumberExpr(token.Line, token.Column, token.Value, true);
            case TokenKind.Keyword when token.Text is "true" or "false":
                _index++;
                return new NumberExpr(token.Line, token.Column, token.Text == "true" ? 1 : 0, false);
            case TokenKind.String:
                throw Error(token, "Text in quotes can only be used inside print(...).");
            case TokenKind.Identifier:
                _index++;
                if (Current.Is("("))
                {
                    if (token.Text is not ("readInt" or "readChar"))
                        throw Error(token, token.Text is "print" or "printChar" or "exit"
                            ? $"{token.Text}(...) is a statement and cannot be used as a value."
                            : $"Unknown function '{token.Text}'. Values can come from readInt() or readChar().");
                    _index++;
                    Expect(")", $"- {token.Text}() takes no arguments");
                    return new CallExpr(token.Line, token.Column, token.Text);
                }
                Expr result = new VariableExpr(token.Line, token.Column, token.Text);
                if (Match("["))
                {
                    var index = ParseExpression();
                    Expect("]", "to close the array index");
                    result = new IndexExpr(token.Line, token.Column, token.Text, index);
                }
                if (Current.Is("++") || Current.Is("--"))
                    throw Error(Current, $"{Current.Text} can only be used as its own statement, like {token.Text}{Current.Text};");
                return result;
            case TokenKind.Operator when token.Text == "(":
                _index++;
                var inner = ParseExpression();
                Expect(")", "to close the parentheses");
                return inner;
            case TokenKind.Operator when token.Text is "++" or "--":
                throw Error(token, $"{token.Text} can only be used as its own statement, like i{token.Text};");
            case TokenKind.End:
                throw Error(token, "Expected a value, but the file ended.");
            default:
                throw Error(token, $"Expected a value, but found '{token.Text}'.");
        }
    }

    // ---- Helpers ----

    private Token Current => _tokens[_index];
    private Token Next => _tokens[Math.Min(_index + 1, _tokens.Count - 1)];
    private Token Previous => _tokens[Math.Max(_index - 1, 0)];
    private bool AtEnd => Current.Kind == TokenKind.End;

    private bool Match(string text)
    {
        if (!Current.Is(text)) return false;
        _index++;
        return true;
    }

    private Token Expect(string text, string context)
    {
        if (Current.Is(text)) return _tokens[_index++];
        if (text == ")" && Current.Is("="))
            throw Error(Current, "Use '==' to compare values; '=' assigns a value.");
        var found = Current.Kind == TokenKind.End ? "the end of the file" : $"'{Current.Text}'";
        throw Error(Current, $"Expected '{text}'{(context.Length > 0 ? " " + context : "")}, but found {found}.");
    }

    private Token ExpectIdentifier(string context)
    {
        if (Current.Kind == TokenKind.Identifier) return _tokens[_index++];
        var found = Current.Kind == TokenKind.End ? "the end of the file" : $"'{Current.Text}'";
        throw Error(Current, $"Expected a name {context}, but found {found}.");
    }

    // A missing ';' is reported right after the previous token, where it belongs, not on the next line.
    private void ExpectSemicolon()
    {
        if (Match(";")) return;
        var previous = Previous;
        throw new ParseError(previous.Line, previous.Column + (previous.End - previous.Start), "Missing ';' at the end of the statement.");
    }

    private static ParseError Error(Token token, string message) => new(token.Line, token.Column, message);

    private string SourceFrom(Token first) => SourceFrom(first, Previous);

    private string SourceFrom(Token first, Token last)
    {
        var text = Whitespace.Replace(_source[first.Start..Math.Max(first.Start, last.End)], " ").Trim();
        return text.Length > 100 ? text[..97] + "..." : text;
    }
}
