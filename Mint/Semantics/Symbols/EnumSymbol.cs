using KirbyLib.Mint;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.Semantics.Symbols
{
    public class EnumSymbol
    {
        public required string Name { get; init; }
        public List<MintEnum> Elements { get; } = new();
    }
}
