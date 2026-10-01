namespace ASMForge.Core.Assembly;

public sealed class SimpleAssembler
{
    private sealed record SourceStatement(int Line, string Op, string[] Args, string Source, List<string> Labels);

    public AssemblyProgram Assemble(string source)
    {
        var statements = Parse(source);
        var instructions = new List<Instruction>();
        var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var statement in statements)
        {
            foreach (var label in statement.Labels)
                labels[label] = instructions.Count;

            foreach (var expanded in Expand(statement))
                instructions.Add(expanded);
        }

        return new AssemblyProgram(instructions, labels);
    }

    private static List<SourceStatement> Parse(string source)
    {
        var result = new List<SourceStatement>();
        var pendingLabels = new List<string>();
        var lines = source.Replace("\r", "").Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var s = raw.Split('#')[0].Trim();
            if (s.Length == 0 || s.StartsWith('.')) continue;

            while (s.Contains(':'))
            {
                var p = s.IndexOf(':');
                var label = s[..p].Trim();
                if (label.Length > 0) pendingLabels.Add(label);
                s = s[(p + 1)..].Trim();
                if (s.Length == 0) break;
            }
            if (s.Length == 0) continue;

            var parts = s.Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
            var args = parts.Length > 1
                ? parts[1].Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray()
                : Array.Empty<string>();
            result.Add(new SourceStatement(i + 1, parts[0].ToLowerInvariant(), args, raw.Trim(), new List<string>(pendingLabels)));
            pendingLabels.Clear();
        }
        return result;
    }

    private static IEnumerable<Instruction> Expand(SourceStatement s)
    {
        Instruction I(string op, string[] args) => new(s.Line, op, args, s.Source, $"{op} {string.Join(", ", args)}".Trim());
        switch (s.Op)
        {
            case "move" when s.Args.Length == 2:
                yield return I("addu", new[] { s.Args[0], s.Args[1], "$zero" });
                yield break;
            case "rem" when s.Args.Length == 3:
                yield return I("div", new[] { s.Args[1], s.Args[2] });
                yield return I("mfhi", new[] { s.Args[0] });
                yield break;
            case "li" when s.Args.Length == 2 && TryImmediate(s.Args[1], out var imm) && imm is >= short.MinValue and <= ushort.MaxValue:
                yield return I("addiu", new[] { s.Args[0], "$zero", s.Args[1] });
                yield break;
            default:
                yield return I(s.Op, s.Args);
                yield break;
        }
    }

    private static bool TryImmediate(string s, out int value)
    {
        try
        {
            value = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToInt32(s[2..], 16)
                : int.Parse(s);
            return true;
        }
        catch { value = 0; return false; }
    }
}
