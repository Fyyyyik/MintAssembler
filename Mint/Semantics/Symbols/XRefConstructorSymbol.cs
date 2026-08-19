using Mint.AstNodes;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.Semantics.Symbols
{
    public class XRefConstructorSymbol : ConstructorBaseSymbol
    {
        public List<ITypeNode> ParamTypes { get; } = new();

        public override ITypeNode[] GetParamTypes() => ParamTypes.ToArray();
    }
}
