using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.CodeGenerators
{
    public record Instruction(byte Opcode, byte Z = 0xFF, byte X = 0xFF, byte Y = 0xFF)
    {
        public virtual byte[] Bytes => [Opcode, Z, X, Y];
    }

    public record Instruction64(
        byte Opcode,
        byte Z = 0xFF,
        byte X = 0xFF,
        byte Y = 0xFF,
        byte A = 0xFF,
        byte B = 0xFF,
        byte C = 0xFF
    ) : Instruction(Opcode, Z, X, Y)
    {
        public override byte[] Bytes => [Opcode, Z, X, Y, 0xFF, A, B, C];
    }
}
