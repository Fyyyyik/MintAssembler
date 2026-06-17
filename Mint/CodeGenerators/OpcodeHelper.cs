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
                    { "ldsrzr", 0x01 },
                    { "ldsrbt", 0x02 },
                    { "ldsrc4", 0x03 },
                    { "ldsrca", 0x04 },
                    { "ldsrsr", 0x05 },
                    { "ldsrfz", 0x06 },
                    { "ldfrsr", 0x07 },
                    { "ldsrsv", 0x09 },
                    { "ldsra4", 0x0A },
                    { "stsrsr", 0x0C },
                    { "stsvsr", 0x0D },
                    { "addi32", 0x0E },
                    { "subi32", 0x0F },
                    { "muls32", 0x10 },
                    { "divs32", 0x11 },
                    { "mods32", 0x12 },
                    { "inci32", 0x13 },
                    { "deci32", 0x14 },
                    { "negs32", 0x15 },
                    { "addf32", 0x16 },
                    { "subf32", 0x17 },
                    { "mulf32", 0x18 },
                    { "divf32", 0x19 },
                    { "incf32", 0x1A },
                    { "decf32", 0x1B },
                    { "negf32", 0x1C },
                    { "lts32", 0x1D },
                    { "les32", 0x1E },
                    { "eqi32", 0x1F },
                    { "nei32", 0x20 },
                    { "ltf32", 0x21 },
                    { "lef32", 0x22 },
                    { "eqf32", 0x23 },
                    { "nef32", 0x24 },
                    { "eqbool", 0x27 },
                    { "nebool", 0x28 },
                    { "andi32", 0x29 },
                    { "ori32", 0x2A },
                    { "xori32", 0x2B },
                    { "ntbool", 0x2D },
                    { "slli32", 0x2E },
                    { "slri32", 0x2F },
                    { "jmp", 0x30 },
                    { "jmpneg", 0x32 },
                    { "fenter", 0x33 },
                    { "fleave", 0x34 },
                    { "fret", 0x35 },
                    { "call", 0x36 },
                    { "yield", 0x37 },
                    { "sppshz", 0x3B },
                    { "sppop", 0x3C },
                    { "addofs", 0x3D },
                    { "arpshz", 0x3E },
                    { "arirx", 0x3F },
                    { "arlen", 0x40 },
                    { "arpop", 0x41 }
                }
            }
        };
    }
}
