using Mint.AstNodes;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.Semantics.Symbols
{
    public class VariableSymbol
    {
        public required string Name { get; init; }
        public required ITypeNode Type { get; init; }
    }
}
