using ASMForge.Core.Cpu;
using ASMForge.Core.Memory;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ASMForge.Core.Assembly;

/// <summary>
/// MARS-compatible source assembler used by ASMForge's execution engine.
/// It intentionally keeps source-level instructions instead of emitting machine
/// words so the editor can retain rich source mappings while still using MIPS
/// virtual addresses, data directives and labels correctly.
/// </summary>
public sealed class SimpleAssembler
{
    private enum Section { Text, Data }
    private sealed record ParsedLine(int Line, string Source, Section Section, List<string> Labels, string? Op, string[] Args, bool IsDirective);

    // Directives that reserve or initialize data; they are only valid in the .data section.
    private static readonly HashSet<string> DataDirectives = new(StringComparer.OrdinalIgnoreCase)
    {
        ".byte", ".half", ".word", ".float", ".double", ".ascii", ".asciiz", ".space"
    };

    private static readonly Regex RegisterToken = new(@"\$[A-Za-z0-9]+", RegexOptions.Compiled);
    private readonly Dictionary<string, long> _equates = new(StringComparer.OrdinalIgnoreCase);

    public AssemblyProgram Assemble(string source)
    {
        _equates.Clear();
        var lines = Parse(source);
        Validate(lines);

        // Collect constants before address calculation so forward .eqv references
        // do not change pseudo-instruction expansion counts between passes.
        foreach (var line in lines.Where(x => x.IsDirective && string.Equals(x.Op, ".eqv", StringComparison.OrdinalIgnoreCase)))
        {
            if (line.Args.Length < 2) Error(line, ".eqv requires a name and value.");
            _equates[line.Args[0]] = ParseNumber(line.Args[1]);
        }

        var symbols = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var textAddress = AssemblyProgram.DefaultTextBase;
        var dataAddress = AssemblyProgram.DefaultDataBase;
        var instructionIndex = 0;
        var autoAlign = true;

        // Pass 1: establish all source symbols and segment addresses.
        // MARS automatically aligns numeric data directives unless .align 0 has disabled
        // automatic alignment. Labels on numeric declarations point at the aligned value.
        foreach (var line in lines)
        {
            var op = line.Op?.ToLowerInvariant();
            if (line.IsDirective && op == ".eqv")
                continue;

            if (line.IsDirective && op is ".data" or ".kdata")
                autoAlign = true;

            var currentAddress = line.Section == Section.Text ? textAddress : dataAddress;
            if (line.Section == Section.Data && line.IsDirective && autoAlign)
            {
                var natural = NaturalDataAlignment(op);
                if (natural > 1) currentAddress = AlignAddress(currentAddress, natural);
            }

            foreach (var label in line.Labels)
            {
                if (symbols.ContainsKey(label))
                    Error(line, $"symbol '{label}' is already defined.");
                symbols[label] = currentAddress;
                if (line.Section == Section.Text)
                    labels[label] = instructionIndex;
            }

            if (line.IsDirective)
            {
                if (line.Section == Section.Data)
                {
                    if (op == ".align")
                    {
                        var exponent = checked((int)ParseNumber(Require(line.Args, 0, op)));
                        if (exponent < 0) Error(line, ".align requires a non-negative integer.");
                        if (exponent == 0) autoAlign = false;
                        else dataAddress = AlignAddress(dataAddress, 1u << exponent);
                    }
                    else
                    {
                        if (autoAlign)
                        {
                            var natural = NaturalDataAlignment(op);
                            if (natural > 1) dataAddress = AlignAddress(dataAddress, natural);
                        }
                        dataAddress = checked(dataAddress + (uint)DirectivePayloadSize(op!, line.Args));
                    }
                }
                continue;
            }

            if (line.Op is not null && line.Section == Section.Text)
            {
                var count = MatchLine(line).Form.InstructionCount;
                textAddress = checked(textAddress + (uint)(count * 4));
                instructionIndex += count;
            }
        }

        // Pass 2: emit initialized data and source-level executable instructions.
        var memory = new Dictionary<uint, byte>();
        dataAddress = AssemblyProgram.DefaultDataBase;
        autoAlign = true;
        foreach (var line in lines)
        {
            var op = line.Op?.ToLowerInvariant();
            if (line.IsDirective && op is ".data" or ".kdata")
            {
                autoAlign = true;
                continue;
            }
            if (!line.IsDirective || line.Section != Section.Data)
                continue;

            if (op == ".align")
            {
                var exponent = checked((int)ParseNumber(Require(line.Args, 0, op)));
                if (exponent < 0) Error(line, ".align requires a non-negative integer.");
                if (exponent == 0) autoAlign = false;
                else dataAddress = AlignAddress(dataAddress, 1u << exponent);
                continue;
            }

            EmitDirective(line, memory, symbols, ref dataAddress, autoAlign);
        }

        var instructions = new List<Instruction>();
        textAddress = AssemblyProgram.DefaultTextBase;
        foreach (var line in lines)
        {
            if (line.IsDirective || line.Section != Section.Text || line.Op is null)
                continue;

            foreach (var instruction in Expand(line, symbols))
            {
                uint machineCode;
                try
                {
                    machineCode = InstructionEncoder.Encode(instruction.Op, instruction.Args, textAddress, symbols);
                }
                catch (Exception e) when (e is FormatException or OverflowException or ArgumentException or NotSupportedException)
                {
                    throw LineError(line, $"cannot encode '{instruction.BasicSource}': {e.Message}");
                }
                instructions.Add(instruction with { Address = textAddress, MachineCode = machineCode });
                textAddress += 4;
            }
        }

        var entryPoint = symbols.TryGetValue("main", out var main) ? main : AssemblyProgram.DefaultTextBase;
        return new AssemblyProgram(instructions, labels, symbols, memory,
            AssemblyProgram.DefaultTextBase, AssemblyProgram.DefaultDataBase, entryPoint, new Dictionary<string, long>(_equates, StringComparer.OrdinalIgnoreCase));
    }

    private static void Validate(IEnumerable<ParsedLine> lines)
    {
        foreach (var line in lines)
        {
            if (line.IsDirective && line.Op is not null)
            {
                var directive = line.Op;
                if (directive is ".include")
                    Error(line, ".include is not supported yet.");
                if (directive is ".macro" or ".end_macro")
                    Error(line, "MARS macros (.macro/.end_macro) are not supported yet.");
                if (line.Section == Section.Text && DataDirectives.Contains(directive))
                    Error(line, $"{directive} declares data but appears in the .text section. Add .data before it.");
                if (line.Section == Section.Text && directive is not (".text" or ".ktext" or ".data" or ".kdata" or ".globl" or ".global" or ".extern" or ".set" or ".eqv" or ".align"))
                    Error(line, $"unknown assembler directive '{directive}'.");
                continue;
            }
            if (line.Op is null) continue;
            if (line.Section == Section.Data)
                Error(line, $"instruction '{line.Op}' appears in the .data section. Add .text before it.");
            if (!InstructionSet.IsKnown(line.Op))
            {
                var suggestion = FindClosestOperation(line.Op);
                var hint = suggestion is null ? string.Empty : $"\nDid you mean '{suggestion}'?";
                Error(line, $"unknown instruction '{line.Op}'.{hint}");
            }

            foreach (var arg in line.Args)
            {
                foreach (Match match in RegisterToken.Matches(arg))
                {
                    try { _ = RegisterFile.Parse(match.Value); }
                    catch (ArgumentException) { Error(line, $"invalid register '{match.Value}'."); }
                }
            }
        }
    }

    private List<ParsedLine> Parse(string source)
    {
        var result = new List<ParsedLine>();
        var section = Section.Text;
        var pendingLabels = new List<string>();
        var lines = source.Replace("\r", string.Empty).Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var s = StripComment(raw).Trim();
            if (s.Length == 0) continue;

            var labelsHere = new List<string>(pendingLabels);
            pendingLabels.Clear();
            while (TryTakeLeadingLabel(ref s, out var label))
            {
                if (!IsIdentifier(label))
                    throw new InvalidOperationException($"Line {i + 1}: invalid label '{label}'.\n    {raw.Trim()}");
                labelsHere.Add(label);
                if (s.Length == 0) break;
            }

            if (s.Length == 0)
            {
                pendingLabels.AddRange(labelsHere);
                continue;
            }

            var firstSpace = s.IndexOfAny(new[] { ' ', '\t' });
            var op = (firstSpace < 0 ? s : s[..firstSpace]).Trim().ToLowerInvariant();
            var rest = firstSpace < 0 ? string.Empty : s[(firstSpace + 1)..].Trim();
            var args = SplitArguments(rest);
            var isDirective = op.StartsWith('.');

            if (isDirective && op is ".text" or ".ktext")
            {
                result.Add(new ParsedLine(i + 1, raw.Trim(), section, labelsHere, op, args, true));
                section = Section.Text;
                continue;
            }
            if (isDirective && op is ".data" or ".kdata")
            {
                result.Add(new ParsedLine(i + 1, raw.Trim(), section, labelsHere, op, args, true));
                section = Section.Data;
                continue;
            }

            result.Add(new ParsedLine(i + 1, raw.Trim(), section, labelsHere, op, args, isDirective));
        }

        if (pendingLabels.Count > 0)
        {
            var fake = new ParsedLine(lines.Length, pendingLabels[^1] + ":", section, pendingLabels, null, Array.Empty<string>(), false);
            result.Add(fake);
        }
        return result;
    }

    private static string StripComment(string line)
    {
        var inString = false;
        var quote = '\0';
        var escape = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (escape) { escape = false; continue; }
            if (inString && c == '\\') { escape = true; continue; }
            if (c is '"' or '\'')
            {
                if (!inString) { inString = true; quote = c; }
                else if (quote == c) inString = false;
                continue;
            }
            if (!inString && c == '#') return line[..i];
        }
        return line;
    }

    private static bool TryTakeLeadingLabel(ref string text, out string label)
    {
        label = string.Empty;
        var colon = text.IndexOf(':');
        if (colon < 0) return false;
        var candidate = text[..colon].Trim();
        if (candidate.IndexOfAny(new[] { ' ', '\t', ',', '(', ')' }) >= 0) return false;
        label = candidate;
        text = text[(colon + 1)..].Trim();
        return candidate.Length > 0;
    }

    private static string[] SplitArguments(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        var result = new List<string>();
        var current = new StringBuilder();
        var inString = false;
        var quote = '\0';
        var escape = false;
        var parenDepth = 0;
        foreach (var c in text)
        {
            if (escape) { current.Append(c); escape = false; continue; }
            if (inString && c == '\\') { current.Append(c); escape = true; continue; }
            if (c is '"' or '\'')
            {
                current.Append(c);
                if (!inString) { inString = true; quote = c; }
                else if (quote == c) inString = false;
                continue;
            }
            if (!inString)
            {
                if (c == '(') parenDepth++;
                if (c == ')') parenDepth--;
                if (c == ',' && parenDepth == 0)
                {
                    if (current.ToString().Trim().Length > 0) result.Add(current.ToString().Trim());
                    current.Clear();
                    continue;
                }
            }
            current.Append(c);
        }
        if (current.ToString().Trim().Length > 0) result.Add(current.ToString().Trim());

        // MARS allows whitespace in .eqv and several directive forms.
        if (result.Count == 1 && !inString)
        {
            var one = result[0];
            var parts = Regex.Split(one, @"\s+").Where(x => x.Length > 0).ToArray();
            if (parts.Length > 1 && !one.Contains('"') && !one.Contains('\'')) return parts;
        }
        return result.ToArray();
    }

    private int DirectivePayloadSize(string op, string[] args)
    {
        return op.ToLowerInvariant() switch
        {
            ".text" or ".data" or ".ktext" or ".kdata" or ".globl" or ".global" or ".set" or ".eqv" or ".extern" or ".align" => 0,
            ".space" => checked((int)ParseNumber(Require(args, 0, op))),
            ".byte" => args.Length,
            ".half" => args.Length * 2,
            ".word" => args.Length * 4,
            ".float" => args.Length * 4,
            ".double" => args.Length * 8,
            ".ascii" => args.Sum(x => Encoding.UTF8.GetByteCount(ParseStringLiteral(x))),
            ".asciiz" => args.Sum(x => Encoding.UTF8.GetByteCount(ParseStringLiteral(x)) + 1),
            ".include" => throw new NotSupportedException(".include requires file-system-aware assembly and is not enabled in this build yet."),
            ".macro" or ".end_macro" => throw new NotSupportedException("MARS macros are not enabled in this build yet."),
            _ => throw new InvalidOperationException($"Unknown assembler directive '{op}'.")
        };
    }

    private static uint NaturalDataAlignment(string? op) => op?.ToLowerInvariant() switch
    {
        ".half" => 2,
        ".word" or ".float" => 4,
        ".double" => 8,
        _ => 1
    };

    private static uint AlignAddress(uint address, uint alignment) =>
        checked(address + (uint)AlignmentPadding(address, alignment));

    private void EmitDirective(ParsedLine line, Dictionary<uint, byte> memory, IReadOnlyDictionary<string, uint> symbols, ref uint address, bool autoAlign)
    {
        var op = line.Op!.ToLowerInvariant();
        switch (op)
        {
            case ".text": case ".data": case ".ktext": case ".kdata": case ".globl": case ".global": case ".set": case ".eqv": case ".extern":
                return;
            case ".align":
                return;
            case ".space":
                address += checked((uint)ParseNumber(Require(line.Args, 0, op)));
                return;
            case ".byte":
                foreach (var a in line.Args) WriteByte(memory, ref address, unchecked((byte)ResolveValue(a, symbols)));
                return;
            case ".half":
                if (autoAlign) AlignForData(ref address, 2);
                foreach (var a in line.Args) WriteHalf(memory, ref address, unchecked((ushort)ResolveValue(a, symbols)));
                return;
            case ".word":
                if (autoAlign) AlignForData(ref address, 4);
                foreach (var a in line.Args) WriteWord(memory, ref address, unchecked((uint)ResolveValue(a, symbols)));
                return;
            case ".float":
                if (autoAlign) AlignForData(ref address, 4);
                foreach (var a in line.Args)
                {
                    var bits = unchecked((uint)BitConverter.SingleToInt32Bits(float.Parse(a, CultureInfo.InvariantCulture)));
                    WriteWord(memory, ref address, bits);
                }
                return;
            case ".double":
                if (autoAlign) AlignForData(ref address, 8);
                foreach (var a in line.Args)
                {
                    var bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(double.Parse(a, CultureInfo.InvariantCulture)));
                    WriteWord(memory, ref address, (uint)bits);
                    WriteWord(memory, ref address, (uint)(bits >> 32));
                }
                return;
            case ".ascii": case ".asciiz":
                foreach (var a in line.Args)
                {
                    foreach (var b in Encoding.UTF8.GetBytes(ParseStringLiteral(a))) WriteByte(memory, ref address, b);
                    if (op == ".asciiz") WriteByte(memory, ref address, 0);
                }
                return;
            default:
                _ = DirectivePayloadSize(op, line.Args); // throws useful error
                return;
        }
    }

    // Tokenizes a text-section line and finds its MARS instruction form (basic forms first, then pseudo-instructions).
    private (InstructionForm Form, List<IReadOnlyList<OperandToken>> Args) MatchLine(ParsedLine line)
    {
        List<IReadOnlyList<OperandToken>> args;
        try
        {
            args = line.Args.Select(a => (IReadOnlyList<OperandToken>)InstructionSet.Tokenize(a, _equates)).ToList();
        }
        catch (FormatException e)
        {
            throw LineError(line, e.Message);
        }

        var flat = args.SelectMany(t => t).ToList();
        var form = InstructionSet.Match(line.Op!, flat);
        if (form is not null) return (form, args);

        var op = line.Op!;
        var message = new StringBuilder($"Line {line.Line}: invalid operands for '{op}'.");
        if (InstructionSet.MatchesIgnoringRange(op, flat))
            message.Append(" An immediate value is out of range (shift amount: 0..31; 16-bit: -32768..32767 or 0..65535).");
        if (op.Equals("li", StringComparison.OrdinalIgnoreCase) && flat.Any(t => t.Kind == TokenKind.Identifier))
            message.Append(" Use 'la' to load a label's address.");
        message.AppendLine().Append("    ").Append(line.Source);
        message.AppendLine().Append("Accepted forms:");
        foreach (var example in InstructionSet.FormsOf(op).Select(f => f.Example).Distinct())
            message.AppendLine().Append("    ").Append(example);
        throw new InvalidOperationException(message.ToString().Replace("\r\n", "\n"));
    }

    // Expands a source line into basic instructions exactly as MARS would.
    private IEnumerable<Instruction> Expand(ParsedLine line, IReadOnlyDictionary<string, uint> symbols)
    {
        var (form, args) = MatchLine(line);
        List<(string Op, string[] Args)> expanded;
        try
        {
            expanded = InstructionSet.Expand(form, args, label => symbols.TryGetValue(label, out var address)
                ? address
                : throw new FormatException($"undefined label '{label}'."));
        }
        catch (FormatException e)
        {
            throw LineError(line, e.Message);
        }
        return expanded.Select(x => new Instruction(line.Line, x.Op, x.Args, line.Source, $"{x.Op} {string.Join(", ", x.Args)}".Trim()));
    }

    private long ResolveValue(string token, IReadOnlyDictionary<string, uint> symbols)
    {
        var s = token.Trim();
        if (_equates.TryGetValue(s, out var eq)) return eq;
        if (symbols.TryGetValue(s, out var address)) return address;
        var plus = FindTopLevelPlusMinus(s);
        if (plus > 0)
        {
            var left = s[..plus].Trim();
            var right = s[plus..].Trim();
            if (symbols.TryGetValue(left, out address)) return unchecked((long)address + ParseNumber(right));
            if (_equates.TryGetValue(left, out eq)) return eq + ParseNumber(right);
        }
        return ParseNumber(s);
    }

    private long ParseNumber(string token)
    {
        var s = token.Trim();
        if (_equates.TryGetValue(s, out var eq)) return eq;
        if (s.Length >= 3 && s[0] == '\'' && s[^1] == '\'')
        {
            var decoded = ParseStringLiteral("\"" + s[1..^1].Replace("\"", "\\\"") + "\"");
            if (decoded.Length != 1) throw new FormatException($"Invalid character literal {token}.");
            return decoded[0];
        }
        var sign = 1L;
        if (s.StartsWith('-')) { sign = -1; s = s[1..]; }
        else if (s.StartsWith('+')) s = s[1..];
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return sign * Convert.ToInt64(s[2..], 16);
        if (s.StartsWith("0b", StringComparison.OrdinalIgnoreCase)) return sign * Convert.ToInt64(s[2..], 2);
        return sign * long.Parse(s, CultureInfo.InvariantCulture);
    }

    private static string ParseStringLiteral(string token)
    {
        var s = token.Trim();
        if (s.Length < 2 || s[0] != '"' || s[^1] != '"')
            throw new FormatException($"Expected quoted string, got {token}.");
        var body = s[1..^1];
        var result = new StringBuilder();
        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (c != '\\') { result.Append(c); continue; }
            if (++i >= body.Length) throw new FormatException("Incomplete escape sequence.");
            c = body[i];
            result.Append(c switch
            {
                'n' => '\n', 'r' => '\r', 't' => '\t', '0' => '\0', '\\' => '\\', '\'' => '\'', '"' => '"',
                _ => c
            });
        }
        return result.ToString();
    }

    private static int AlignmentPadding(uint address, uint alignment)
    {
        if (alignment <= 1) return 0;
        var mod = address % alignment;
        return mod == 0 ? 0 : checked((int)(alignment - mod));
    }

    private static void AlignForData(ref uint address, uint alignment) => address += (uint)AlignmentPadding(address, alignment);
    private static void WriteByte(Dictionary<uint, byte> memory, ref uint address, byte value) { memory[address++] = value; }
    private static void WriteHalf(Dictionary<uint, byte> memory, ref uint address, ushort value)
    {
        WriteByte(memory, ref address, (byte)value); WriteByte(memory, ref address, (byte)(value >> 8));
    }
    private static void WriteWord(Dictionary<uint, byte> memory, ref uint address, uint value)
    {
        WriteByte(memory, ref address, (byte)value); WriteByte(memory, ref address, (byte)(value >> 8));
        WriteByte(memory, ref address, (byte)(value >> 16)); WriteByte(memory, ref address, (byte)(value >> 24));
    }

    private static int FindTopLevelPlusMinus(string s)
    {
        for (var i = 1; i < s.Length; i++) if (s[i] is '+' or '-') return i;
        return -1;
    }
    private static bool IsIdentifier(string s) => Regex.IsMatch(s, @"^[A-Za-z_.$][A-Za-z0-9_.$]*$");
    private static string Require(string[] args, int index, string op) => index < args.Length ? args[index] : throw new InvalidOperationException($"{op} is missing an operand.");
    private static void Error(ParsedLine line, string message) => throw LineError(line, message);
    private static InvalidOperationException LineError(ParsedLine line, string message) => new($"Line {line.Line}: {message}\n    {line.Source}");

    private static string? FindClosestOperation(string value)
    {
        var best = InstructionSet.Mnemonics.Select(op => (Op: op, Distance: EditDistance(value, op)))
            .OrderBy(x => x.Distance).ThenBy(x => x.Op, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        return best.Op is not null && best.Distance <= Math.Max(2, value.Length / 3) ? best.Op : null;
    }

    private static int EditDistance(string a, string b)
    {
        a = a.ToLowerInvariant(); b = b.ToLowerInvariant();
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }
}
