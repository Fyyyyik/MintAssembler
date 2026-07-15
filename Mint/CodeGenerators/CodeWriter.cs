using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.CodeGenerators
{
    public class CodeWriter
    {
        public struct CodeResult
        {
            public required Instruction[] Instructions;
            public required byte[] Data;
        }

        public List<Instruction> Instructions { get; } = new();

        public CodeResult Result => new CodeResult
        {
            Instructions = this.Instructions.ToArray(),
            Data = InstructionsToBytes()
        };

        public void Append(CodeResult result) => Instructions.AddRange(result.Instructions);

        public void Insert(int index, CodeResult result)
            => Instructions.InsertRange(index, result.Instructions);

        private byte[] InstructionsToBytes()
        {
            List<byte> data = new();
            foreach (Instruction i in Instructions)
                data.AddRange(i.Bytes);
            return data.ToArray();
        }

        public static (byte, byte) ToBytes(short value)
            => ((byte)((value >> 8) & 0xFF), (byte)(value & 0xFF));

        public static (byte, byte) ToBytes(ushort value)
            => ((byte)((value >> 8) & 0xFF), (byte)(value & 0xFF));
    }
}
