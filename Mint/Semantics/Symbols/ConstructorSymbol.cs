using Mint.AstNodes;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.Semantics.Symbols
{
    public class ConstructorSymbol : ConstructorBaseSymbol
    {
        public List<ParamNode> Parameters { get; } = new();

        public override ITypeNode[] GetParamTypes() => Utility.ToTypeNodes(Parameters);
    }
}
