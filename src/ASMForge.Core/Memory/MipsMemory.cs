namespace ASMForge.Core.Memory;
public sealed class MipsMemory {
    public const uint DataBase=0x10010000; public const int Size=4*1024*1024; private readonly byte[] _data=new byte[Size];
    private int Offset(uint a,int bytes){long o=(long)a-DataBase;if(o<0||o+bytes>Size)throw new InvalidOperationException($"Address 0x{a:X8} is outside simulated memory.");return (int)o;}
    public void Reset()=>Array.Clear(_data);
    public int ReadWord(uint a){if((a&3)!=0)throw new InvalidOperationException($"Unaligned word address 0x{a:X8}.");return BitConverter.ToInt32(_data,Offset(a,4));}
    public void WriteWord(uint a,int v){if((a&3)!=0)throw new InvalidOperationException($"Unaligned word address 0x{a:X8}.");BitConverter.GetBytes(v).CopyTo(_data,Offset(a,4));}
    public byte ReadByte(uint a)=>_data[Offset(a,1)]; public void WriteByte(uint a,byte v)=>_data[Offset(a,1)]=v;
    public string ReadCString(uint a){var c=new List<byte>();while(true){var b=ReadByte(a++);if(b==0)break;c.Add(b);if(c.Count>1_000_000)throw new InvalidOperationException("Unterminated string.");}return System.Text.Encoding.UTF8.GetString(c.ToArray());}
}
