using ASMForge.Core.Assembly; using ASMForge.Core.Cpu; using ASMForge.Core.Memory;
namespace ASMForge.Core.Execution;
public sealed class MipsMachine {
 public RegisterFile Registers{get;}=new(); public MipsMemory Memory{get;}=new(); public AssemblyProgram? Program{get;private set;} public int InstructionIndex{get;private set;} public bool Halted{get;private set;} public string ConsoleText{get;private set;}="";
 public uint PC=>0x00400000u+(uint)(InstructionIndex*4);
 public void Load(AssemblyProgram p){Program=p;InstructionIndex=0;Halted=false;ConsoleText="";Registers.Reset();Memory.Reset();Registers[29]=unchecked((int)0x1040FFF0);}
 int V(string s)=>s.StartsWith("$")?Registers[RegisterFile.Parse(s)]:ParseInt(s); static int ParseInt(string s)=>s.StartsWith("0x",StringComparison.OrdinalIgnoreCase)?Convert.ToInt32(s[2..],16):int.Parse(s);
 (int off,int reg) Mem(string s){var l=s.IndexOf('(');var r=s.IndexOf(')');return(ParseInt(s[..l]),RegisterFile.Parse(s[(l+1)..r]));}
 public void Step(){if(Halted||Program is null)return;if(InstructionIndex>=Program.Instructions.Count){Halted=true;return;}var x=Program.Instructions[InstructionIndex];var next=InstructionIndex+1;try{switch(x.Op){
  case "li": Registers[RegisterFile.Parse(x.Args[0])]=V(x.Args[1]);break; case "move": Registers[RegisterFile.Parse(x.Args[0])]=V(x.Args[1]);break;
  case "add": case "addu": Registers[RegisterFile.Parse(x.Args[0])]=unchecked(V(x.Args[1])+V(x.Args[2]));break; case "addi": case "addiu": Registers[RegisterFile.Parse(x.Args[0])]=unchecked(V(x.Args[1])+V(x.Args[2]));break;
  case "sub": case "subu": Registers[RegisterFile.Parse(x.Args[0])]=unchecked(V(x.Args[1])-V(x.Args[2]));break; case "div": { var divisor=V(x.Args[1]); if(divisor==0) throw new DivideByZeroException("Division by zero."); var dividend=V(x.Args[0]); Registers.LO=dividend/divisor; Registers.HI=dividend%divisor; break;} case "mfhi": Registers[RegisterFile.Parse(x.Args[0])]=Registers.HI;break; case "mflo": Registers[RegisterFile.Parse(x.Args[0])]=Registers.LO;break;
  case "mul": Registers[RegisterFile.Parse(x.Args[0])]=unchecked(V(x.Args[1])*V(x.Args[2]));break; case "rem": Registers[RegisterFile.Parse(x.Args[0])]=V(x.Args[1])%V(x.Args[2]);break;
  case "and": Registers[RegisterFile.Parse(x.Args[0])]=V(x.Args[1])&V(x.Args[2]);break; case "or": Registers[RegisterFile.Parse(x.Args[0])]=V(x.Args[1])|V(x.Args[2]);break; case "xor": Registers[RegisterFile.Parse(x.Args[0])]=V(x.Args[1])^V(x.Args[2]);break;
  case "lw": {var m=Mem(x.Args[1]);Registers[RegisterFile.Parse(x.Args[0])]=Memory.ReadWord(unchecked((uint)(Registers[m.reg]+m.off)));break;} case "sw": {var m=Mem(x.Args[1]);Memory.WriteWord(unchecked((uint)(Registers[m.reg]+m.off)),V(x.Args[0]));break;}
  case "beq": if(V(x.Args[0])==V(x.Args[1]))next=Program.Labels[x.Args[2]];break; case "bne": if(V(x.Args[0])!=V(x.Args[1]))next=Program.Labels[x.Args[2]];break; case "bgt": if(V(x.Args[0])>V(x.Args[1]))next=Program.Labels[x.Args[2]];break; case "blt": if(V(x.Args[0])<V(x.Args[1]))next=Program.Labels[x.Args[2]];break; case "j": next=Program.Labels[x.Args[0]];break;
  case "syscall": Syscall();break; case "nop": break; default: throw new NotSupportedException($"Instruction '{x.Op}' is not implemented yet.");}
  InstructionIndex=next;if(InstructionIndex>=Program.Instructions.Count)Halted=true;}catch(Exception e){Halted=true;throw new InvalidOperationException($"Line {x.Line}: {x.Source}\n{e.Message}",e);}}
 void Syscall(){switch(Registers[2]){case 1:ConsoleText+=Registers[4].ToString();break;case 4:ConsoleText+=Memory.ReadCString(unchecked((uint)Registers[4]));break;case 10:Halted=true;break;case 11:ConsoleText+=(char)(Registers[4]&0xff);break;case 34:ConsoleText+=$"{unchecked((uint)Registers[4]):X8}";break;case 35:ConsoleText+=Convert.ToString(Registers[4],2).PadLeft(32,'0');break;case 36:ConsoleText+=unchecked((uint)Registers[4]).ToString();break;default:throw new NotSupportedException($"Syscall {Registers[2]} is not implemented yet.");}}
 public void Run(int max=100000){for(int i=0;i<max&&!Halted;i++)Step();if(!Halted)throw new InvalidOperationException("Execution limit reached.");}
}
