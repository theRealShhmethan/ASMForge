namespace ASMForge.Core.Assembly;

public sealed record Instruction(int Line, string Op, string[] Args, string Source, string BasicSource)
{
    public uint Address { get; init; }

    /// <summary>The 32-bit MIPS machine word for this basic instruction.</summary>
    public uint MachineCode { get; init; }
}

public sealed class AssemblyProgram
{
    public const uint DefaultTextBase = 0x00400000;
    public const uint DefaultDataBase = 0x10010000;

    public AssemblyProgram(IReadOnlyList<Instruction> instructions, IReadOnlyDictionary<string, int> labels)
        : this(instructions, labels, new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<uint, byte>(), DefaultTextBase, DefaultDataBase, DefaultTextBase,
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase))
    {
    }

    public AssemblyProgram(
        IReadOnlyList<Instruction> instructions,
        IReadOnlyDictionary<string, int> labels,
        IReadOnlyDictionary<string, uint> symbols,
        IReadOnlyDictionary<uint, byte> initialMemory,
        uint textBase,
        uint dataBase,
        uint entryPoint,
        IReadOnlyDictionary<string, long>? constants = null)
    {
        Instructions = instructions;
        Labels = labels;
        Symbols = symbols;
        InitialMemory = initialMemory;
        TextBase = textBase;
        DataBase = dataBase;
        EntryPoint = entryPoint;
        Constants = constants ?? new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        AddressToInstruction = instructions.Select((instruction, index) => (instruction.Address, index))
            .ToDictionary(x => x.Address, x => x.index);
    }

    public IReadOnlyList<Instruction> Instructions { get; }
    public IReadOnlyDictionary<string, int> Labels { get; }
    public IReadOnlyDictionary<string, uint> Symbols { get; }
    public IReadOnlyDictionary<string, long> Constants { get; }
    public IReadOnlyDictionary<uint, byte> InitialMemory { get; }
    public IReadOnlyDictionary<uint, int> AddressToInstruction { get; }
    public uint TextBase { get; }
    public uint DataBase { get; }
    public uint EntryPoint { get; }
}
