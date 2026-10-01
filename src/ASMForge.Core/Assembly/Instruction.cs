namespace ASMForge.Core.Assembly;

public sealed record Instruction(int Line, string Op, string[] Args, string Source, string BasicSource);
public sealed record AssemblyProgram(IReadOnlyList<Instruction> Instructions, IReadOnlyDictionary<string, int> Labels);
