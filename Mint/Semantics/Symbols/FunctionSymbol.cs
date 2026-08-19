using Mint.AstNodes;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.Semantics.Symbols
{
    public class FunctionSymbol : FunctionBaseSymbol
    {
        public bool HasThis { get; }
        public List<ParamNode> Parameters { get; } = new();

        public FunctionSymbol(ITypeNode? returnType, string name, bool isConst, bool hasThis)
            : base(returnType, name, isConst)
        {
            HasThis = hasThis;
        }

        public override ITypeNode[] GetParamTypes() => Utility.ToTypeNodes(Parameters);
    }
}
