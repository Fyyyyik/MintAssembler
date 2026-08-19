using Mint.AstNodes;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.Semantics.Symbols
{
    public class XRefFunctionSymbol : FunctionBaseSymbol
    {
        public List<ITypeNode> ParamTypes { get; } = new();

        public XRefFunctionSymbol(ITypeNode? returnType, string name, bool isConst)
            : base(returnType, name, isConst) { }

        public override ITypeNode[] GetParamTypes() => ParamTypes.ToArray();
    }
}
