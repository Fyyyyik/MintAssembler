using Mint.Util;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace Mint.CodeGenerators
{
    public static class OpcodeHelper
    {
        public static readonly Dictionary<byte[], Dictionary<string, byte>> CommonOpcodeByName = new(new ByteArrayComparer())
        {
            {
                new byte[] {0, 2, 0, 0},
                new Dictionary<string, byte>()
                {
                    { "ldsrsr", 0x05 },
                    { "stsrsr", 0x0C },
                    { "jmp", 0x30 },
                    { "arpshz", 0x3E },
                    { "ldsra4", 0x0A },
                    { "arirx", 0x3F },
                    { "arlen", 0x40 },
                    { "arpop", 0x41 }
                }
            }
        };
    }
}
