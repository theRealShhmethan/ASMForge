namespace ASMForge.Core.Assembly;
public sealed record Instruction(int Line,string Op,string[] Args,string Source);
public sealed record AssemblyProgram(IReadOnlyList<Instruction> Instructions,IReadOnlyDictionary<string,int> Labels);
