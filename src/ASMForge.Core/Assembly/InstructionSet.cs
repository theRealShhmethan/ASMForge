using ASMForge.Core.Cpu;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ASMForge.Core.Assembly;

internal enum TokenKind { Register, Integer, Identifier, LeftParen, RightParen, Plus }

/// <summary>One operand token. Value is the register number or the 32-bit integer value.</summary>
internal readonly record struct OperandToken(TokenKind Kind, string Text, int Value);

/// <summary>Operand type expected by an instruction form, using MARS's integer size classes.</summary>
internal enum OperandSpec { Register, Int5, Int16, Int16U, Int32, Label, LeftParen, RightParen, Plus }

/// <summary>
/// One accepted syntax of a mnemonic. Basic forms execute directly; pseudo forms expand
/// into the basic instruction templates taken from MARS's PseudoOps.txt.
/// </summary>
internal sealed class InstructionForm
{
    public InstructionForm(string mnemonic, string example, OperandSpec[] operands, string[]? templates)
    {
        Mnemonic = mnemonic;
        Example = example;
        Operands = operands;
        Templates = templates;
    }

    public string Mnemonic { get; }
    public string Example { get; }
    public IReadOnlyList<OperandSpec> Operands { get; }
    public IReadOnlyList<string>? Templates { get; }
    public bool IsBasic => Templates is null;
    public int InstructionCount => Templates?.Count ?? 1;
}

/// <summary>
/// MARS-compatible instruction set: basic integer instructions plus the pseudo-instructions
/// defined in MARS's PseudoOps.txt (embedded resource). Matching is first-fit, basic forms first,
/// exactly like MARS, so expansions and text addresses match MARS.
/// </summary>
internal static class InstructionSet
{
    // Basic (native) integer instructions the simulator executes, in MARS example syntax.
    private static readonly string[] BasicExamples =
    {
        "nop", "syscall", "break", "break 100",
        "add $t1,$t2,$t3", "addu $t1,$t2,$t3", "sub $t1,$t2,$t3", "subu $t1,$t2,$t3",
        "and $t1,$t2,$t3", "or $t1,$t2,$t3", "xor $t1,$t2,$t3", "nor $t1,$t2,$t3",
        "slt $t1,$t2,$t3", "sltu $t1,$t2,$t3", "mul $t1,$t2,$t3",
        "sllv $t1,$t2,$t3", "srlv $t1,$t2,$t3", "srav $t1,$t2,$t3",
        "addi $t1,$t2,-100", "addiu $t1,$t2,-100", "slti $t1,$t2,-100", "sltiu $t1,$t2,-100",
        "andi $t1,$t2,100", "ori $t1,$t2,100", "xori $t1,$t2,100", "lui $t1,100",
        "sll $t1,$t2,10", "srl $t1,$t2,10", "sra $t1,$t2,10",
        "mult $t1,$t2", "multu $t1,$t2", "div $t1,$t2", "divu $t1,$t2",
        "madd $t1,$t2", "maddu $t1,$t2", "msub $t1,$t2", "msubu $t1,$t2",
        "clo $t1,$t2", "clz $t1,$t2",
        "mfhi $t1", "mflo $t1", "mthi $t1", "mtlo $t1",
        "lw $t1,-100($t2)", "lh $t1,-100($t2)", "lhu $t1,-100($t2)", "lb $t1,-100($t2)", "lbu $t1,-100($t2)",
        "lwl $t1,-100($t2)", "lwr $t1,-100($t2)", "ll $t1,-100($t2)",
        "sw $t1,-100($t2)", "sh $t1,-100($t2)", "sb $t1,-100($t2)",
        "swl $t1,-100($t2)", "swr $t1,-100($t2)", "sc $t1,-100($t2)",
        "beq $t1,$t2,label", "bne $t1,$t2,label",
        "bgez $t1,label", "bgezal $t1,label", "bgtz $t1,label", "blez $t1,label", "bltz $t1,label", "bltzal $t1,label",
        "j target", "jal target", "jr $t1", "jalr $t1", "jalr $t1,$t2"
    };

    // ASMForge additions not in MARS's table, in the same format.
    private static readonly string[] ExtraPseudoOps =
    {
        "bal label\tbgezal $0, LAB\t#Branch And Link : ASMForge extension"
    };

    private static readonly Regex Placeholder = new(
        @"\b(?:RG\d|NR\d|OP\d|LLPP\d|LLPU|LLP|LHPAP\d|LHPA|LHPN|LHL|LL\dP\dU?|LL\dU?|LH\dP\d|LH\d|VHL\dP\d|VHL\d|VH\dP\d|VH\d|VL\dP\dU?|VL\dU?|LAB|S32|BROFF\d\d)\b",
        RegexOptions.Compiled);

    private static readonly Regex NumberedRegister = new(@"\$(\d+)\b", RegexOptions.Compiled);

    private static readonly Dictionary<string, List<InstructionForm>> Forms = new(StringComparer.OrdinalIgnoreCase);

    static InstructionSet()
    {
        foreach (var example in BasicExamples) AddForm(example, null);
        var basic = new HashSet<string>(Forms.Keys, StringComparer.OrdinalIgnoreCase);

        foreach (var line in LoadPseudoOps().Concat(ExtraPseudoOps))
        {
            if (line.Length == 0 || char.IsWhiteSpace(line[0]) || line[0] == '#') continue;
            var fields = line.Split('\t');
            var templates = new List<string>();
            foreach (var raw in fields.Skip(1))
            {
                var field = raw.Trim();
                if (field.Length == 0) continue;
                if (field.StartsWith('#') || field == "COMPACT") break; // description, or the 16-bit-address alternative
                if (field == "DBNOP") continue;                          // only emitted with delayed branching
                templates.Add(field);
            }

            // Skip forms the simulator cannot execute yet (floating point / coprocessor templates).
            if (templates.Count == 0 || templates.Any(t => !basic.Contains(FirstWord(t)))) continue;
            AddForm(fields[0].Trim(), templates.ToArray());
        }
    }

    public static IEnumerable<string> Mnemonics => Forms.Keys;

    public static bool IsKnown(string mnemonic) => Forms.ContainsKey(mnemonic);

    public static IReadOnlyList<InstructionForm> FormsOf(string mnemonic) =>
        Forms.TryGetValue(mnemonic, out var list) ? list : (IReadOnlyList<InstructionForm>)Array.Empty<InstructionForm>();

    /// <summary>First form whose operand types and immediate ranges fit (MARS first-fit, basic forms first).</summary>
    public static InstructionForm? Match(string mnemonic, IReadOnlyList<OperandToken> tokens) =>
        FormsOf(mnemonic).FirstOrDefault(form => Fits(form, tokens, ignoreRange: false));

    /// <summary>True when some form has the right operand shape but an immediate is out of range.</summary>
    public static bool MatchesIgnoringRange(string mnemonic, IReadOnlyList<OperandToken> tokens) =>
        FormsOf(mnemonic).Any(form => Fits(form, tokens, ignoreRange: true));

    /// <summary>
    /// Produces the basic instructions for a matched form. <paramref name="argTokens"/> holds the tokens of each
    /// comma-separated operand; <paramref name="labelAddress"/> resolves labels (and throws for undefined ones).
    /// </summary>
    public static List<(string Op, string[] Args)> Expand(InstructionForm form, IReadOnlyList<IReadOnlyList<OperandToken>> argTokens,
        Func<string, uint> labelAddress)
    {
        var tokens = argTokens.SelectMany(t => t).ToList();
        for (var i = 0; i < form.Operands.Count; i++)
            if (form.Operands[i] == OperandSpec.Label) _ = labelAddress(tokens[i].Text); // report undefined labels now

        if (form.IsBasic)
            return new List<(string, string[])> { (form.Mnemonic.ToLowerInvariant(), argTokens.Select(RenderOperand).ToArray()) };

        var result = new List<(string, string[])>();
        foreach (var template in form.Templates!)
        {
            var text = Placeholder.Replace(template, m => Substitute(m.Value, tokens, labelAddress));
            var op = FirstWord(text);
            var args = text[op.Length..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(NormalizeRegisters).ToArray();
            result.Add((op.ToLowerInvariant(), args));
        }
        return result;
    }

    /// <summary>
    /// Splits one operand into MARS-style tokens: registers, integers, identifiers, '(', ')' and '+'.
    /// ".eqv" names become integers. "label-4" becomes label, '+', -4.
    /// </summary>
    public static List<OperandToken> Tokenize(string text, IReadOnlyDictionary<string, long>? equates)
    {
        var tokens = new List<OperandToken>();
        var negateNext = false;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c) || c == ',') { i++; continue; }
            if (c == '(') { tokens.Add(new OperandToken(TokenKind.LeftParen, "(", 0)); i++; continue; }
            if (c == ')') { tokens.Add(new OperandToken(TokenKind.RightParen, ")", 0)); i++; continue; }
            if (c == '+') { tokens.Add(new OperandToken(TokenKind.Plus, "+", 0)); i++; continue; }
            if (c == '-' && tokens.Count > 0 && tokens[^1].Kind is TokenKind.Identifier or TokenKind.Integer or TokenKind.RightParen)
            {
                tokens.Add(new OperandToken(TokenKind.Plus, "+", 0));
                negateNext = true;
                i++;
                continue;
            }

            if (c == '\'')
            {
                var end = i + 1;
                if (end < text.Length && text[end] == '\\') end++;
                end++;
                if (end >= text.Length || text[end] != '\'') throw new FormatException($"invalid character literal in '{text.Trim()}'.");
                var literal = text[i..(end + 1)];
                tokens.Add(IntegerToken(literal, ParseCharLiteral(literal), ref negateNext));
                i = end + 1;
                continue;
            }

            var start = i;
            if (c == '-') i++;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not (',' or '(' or ')' or '+' or '-')) i++;
            var word = text[start..i];

            if (word.StartsWith('$'))
            {
                if (negateNext) throw new FormatException($"cannot subtract a register in '{text.Trim()}'.");
                int number;
                try { number = RegisterFile.Parse(word); }
                catch (ArgumentException)
                {
                    var hint = word.StartsWith("$f", StringComparison.OrdinalIgnoreCase) ? " Floating-point registers are not supported yet." : string.Empty;
                    throw new FormatException($"invalid register '{word}'.{hint}");
                }
                tokens.Add(new OperandToken(TokenKind.Register, RegisterFile.Names[number], number));
            }
            else if (char.IsDigit(word.TrimStart('-').FirstOrDefault()))
            {
                tokens.Add(IntegerToken(word, ParseIntegerLiteral(word), ref negateNext));
            }
            else if (equates is not null && equates.TryGetValue(word, out var constant))
            {
                tokens.Add(IntegerToken(word, constant, ref negateNext));
            }
            else if (IsIdentifier(word))
            {
                if (negateNext) throw new FormatException($"cannot subtract a label in '{text.Trim()}'.");
                tokens.Add(new OperandToken(TokenKind.Identifier, word, 0));
            }
            else
            {
                throw new FormatException($"unexpected '{word}' in operand '{text.Trim()}'.");
            }
        }
        if (negateNext) throw new FormatException($"missing value after '-' in '{text.Trim()}'.");
        return tokens;
    }

    private static void AddForm(string example, string[]? templates)
    {
        var mnemonic = FirstWord(example);
        OperandSpec[] specs;
        try
        {
            specs = Tokenize(example[mnemonic.Length..], null).Select(ToSpec).ToArray();
        }
        catch (FormatException)
        {
            return; // e.g. floating-point register examples ($f2)
        }

        if (!Forms.TryGetValue(mnemonic, out var list)) Forms[mnemonic] = list = new List<InstructionForm>();
        list.Add(new InstructionForm(mnemonic, example, specs, templates));
    }

    // MARS classifies example immediates by value: 10 -> 5-bit, -100 -> 16-bit signed, 100 -> 16-bit unsigned, 100000 -> 32-bit.
    private static OperandSpec ToSpec(OperandToken token) => token.Kind switch
    {
        TokenKind.Register => OperandSpec.Register,
        TokenKind.Identifier => OperandSpec.Label,
        TokenKind.LeftParen => OperandSpec.LeftParen,
        TokenKind.RightParen => OperandSpec.RightParen,
        TokenKind.Plus => OperandSpec.Plus,
        _ => token.Value switch
        {
            >= 0 and <= 31 => OperandSpec.Int5,
            >= 0 and <= ushort.MaxValue => OperandSpec.Int16U,
            >= short.MinValue and <= short.MaxValue => OperandSpec.Int16,
            _ => OperandSpec.Int32
        }
    };

    private static bool Fits(InstructionForm form, IReadOnlyList<OperandToken> tokens, bool ignoreRange)
    {
        if (form.Operands.Count != tokens.Count) return false;
        for (var i = 0; i < tokens.Count; i++)
            if (!Fits(form.Operands[i], tokens[i], ignoreRange)) return false;
        return true;
    }

    private static bool Fits(OperandSpec spec, OperandToken token, bool ignoreRange)
    {
        switch (spec)
        {
            case OperandSpec.Register: return token.Kind == TokenKind.Register;
            case OperandSpec.Label: return token.Kind == TokenKind.Identifier;
            case OperandSpec.LeftParen: return token.Kind == TokenKind.LeftParen;
            case OperandSpec.RightParen: return token.Kind == TokenKind.RightParen;
            case OperandSpec.Plus: return token.Kind == TokenKind.Plus;
        }
        if (token.Kind != TokenKind.Integer) return false;
        if (ignoreRange) return true;
        return spec switch
        {
            OperandSpec.Int5 => token.Value is >= 0 and <= 31,
            OperandSpec.Int16 => token.Value is >= short.MinValue and <= short.MaxValue,
            OperandSpec.Int16U => token.Value is >= 0 and <= ushort.MaxValue,
            _ => true
        };
    }

    // Substitutes one MARS template placeholder. See the header of PseudoOps.txt for the definitions.
    private static string Substitute(string placeholder, IReadOnlyList<OperandToken> tokens, Func<string, uint> labelAddress)
    {
        OperandToken Token(int position) => tokens[position - 1]; // position 0 is the mnemonic
        uint LabelPlusOffset()
        {
            var label = tokens.First(t => t.Kind == TokenKind.Identifier);
            var plus = tokens.ToList().FindIndex(t => t.Kind == TokenKind.Plus);
            return unchecked(labelAddress(label.Text) + (uint)tokens[plus + 1].Value);
        }

        switch (placeholder)
        {
            case "LAB": return tokens[^1].Text;
            case "S32": return (32 - tokens[^1].Value).ToString(CultureInfo.InvariantCulture);
            case "LHL": return High16(labelAddress(Token(2).Text));
            case "LHPN": return High16(LabelPlusOffset());
            case "LHPA": return High16Adjusted(LabelPlusOffset());
            case "LLP": return Low16Signed(LabelPlusOffset());
            case "LLPU": return Low16Unsigned(LabelPlusOffset());
        }

        var n = placeholder[^1] - '0';
        if (placeholder.StartsWith("BROFF", StringComparison.Ordinal)) return placeholder[5].ToString(); // no delayed branching
        if (placeholder.StartsWith("LLPP", StringComparison.Ordinal)) return Low16Signed(unchecked(LabelPlusOffset() + (uint)n));
        if (placeholder.StartsWith("LHPAP", StringComparison.Ordinal)) return High16Adjusted(unchecked(LabelPlusOffset() + (uint)n));
        if (placeholder.StartsWith("RG", StringComparison.Ordinal)) return RegisterFile.Names[Token(n).Value];
        if (placeholder.StartsWith("NR", StringComparison.Ordinal))
        {
            var next = Token(n).Value + 1;
            if (next >= RegisterFile.Count) throw new FormatException($"{Token(n).Text} has no next register for a doubleword operation.");
            return RegisterFile.Names[next];
        }
        if (placeholder.StartsWith("OP", StringComparison.Ordinal)) return Token(n).Text;

        // LLn / LHn / VLn / VHLn / VHn, optionally followed by Pm (add m) and/or U (unsigned low half).
        var match = Regex.Match(placeholder, @"^(?<kind>LL|LH|VL|VHL|VH)(?<pos>\d)(?:P(?<add>\d))?(?<u>U)?$");
        if (!match.Success) throw new FormatException($"unsupported pseudo-instruction template '{placeholder}'.");
        var position = match.Groups["pos"].Value[0] - '0';
        var add = match.Groups["add"].Success ? (uint)(match.Groups["add"].Value[0] - '0') : 0u;
        var unsignedLow = match.Groups["u"].Success;
        var kind = match.Groups["kind"].Value;
        var baseValue = kind is "LL" or "LH" ? labelAddress(Token(position).Text) : unchecked((uint)Token(position).Value);
        var value = unchecked(baseValue + add);
        return kind switch
        {
            "LL" or "VL" => unsignedLow ? Low16Unsigned(value) : Low16Signed(value),
            "VHL" => High16(value),
            _ => High16Adjusted(value) // LH, VH: +1 when bit 15 is set, because the low half is sign-extended later
        };
    }

    private static string Low16Signed(uint value) => unchecked((short)(value & 0xffff)).ToString(CultureInfo.InvariantCulture);
    private static string Low16Unsigned(uint value) => (value & 0xffff).ToString(CultureInfo.InvariantCulture);
    private static string High16(uint value) => ((value >> 16) & 0xffff).ToString(CultureInfo.InvariantCulture);
    private static string High16Adjusted(uint value) => (((value >> 16) + ((value >> 15) & 1)) & 0xffff).ToString(CultureInfo.InvariantCulture);

    private static string RenderOperand(IReadOnlyList<OperandToken> tokens)
    {
        var text = new StringBuilder();
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            // "label" + "-4" renders as "label-4".
            if (token.Kind == TokenKind.Plus && i + 1 < tokens.Count && tokens[i + 1].Kind == TokenKind.Integer && tokens[i + 1].Value < 0) continue;
            text.Append(token.Kind == TokenKind.Integer ? token.Value.ToString(CultureInfo.InvariantCulture) : token.Text);
        }
        return text.ToString();
    }

    private static string NormalizeRegisters(string operand) =>
        NumberedRegister.Replace(operand, m => int.TryParse(m.Groups[1].Value, out var r) && r < RegisterFile.Count ? RegisterFile.Names[r] : m.Value);

    private static OperandToken IntegerToken(string text, long value, ref bool negate)
    {
        if (negate) { value = -value; negate = false; }
        if (value < int.MinValue || value > uint.MaxValue) throw new FormatException($"value {text} does not fit in 32 bits.");
        return new OperandToken(TokenKind.Integer, text, unchecked((int)value));
    }

    private static long ParseIntegerLiteral(string word)
    {
        var s = word;
        var negative = s.StartsWith('-');
        if (negative) s = s[1..];
        try
        {
            long value;
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) value = Convert.ToInt64(s[2..], 16);
            else if (s.StartsWith("0b", StringComparison.OrdinalIgnoreCase)) value = Convert.ToInt64(s[2..], 2);
            else value = long.Parse(s, NumberStyles.None, CultureInfo.InvariantCulture);
            return negative ? -value : value;
        }
        catch (Exception e) when (e is FormatException or OverflowException or ArgumentException)
        {
            throw new FormatException($"invalid number '{word}'.");
        }
    }

    private static long ParseCharLiteral(string literal)
    {
        var body = literal[1..^1];
        if (body.Length == 1) return body[0];
        return body[1] switch
        {
            'n' => '\n', 't' => '\t', 'r' => '\r', '0' => '\0', '\\' => '\\', '\'' => '\'', '"' => '"',
            _ => throw new FormatException($"invalid character literal {literal}.")
        };
    }

    private static bool IsIdentifier(string s) => Regex.IsMatch(s, @"^[A-Za-z_.][A-Za-z0-9_.$]*$");

    private static string FirstWord(string text)
    {
        var trimmed = text.TrimStart();
        var end = trimmed.IndexOfAny(new[] { ' ', '\t' });
        return end < 0 ? trimmed : trimmed[..end];
    }

    private static IEnumerable<string> LoadPseudoOps()
    {
        using var stream = typeof(InstructionSet).Assembly.GetManifestResourceStream("ASMForge.Core.PseudoOps.txt")
            ?? throw new InvalidOperationException("PseudoOps.txt resource is missing from ASMForge.Core.");
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line) lines.Add(line);
        return lines;
    }
}
