using Mint.AstNodes;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.Semantics.Symbols
{
    public abstract class ConstructorBaseSymbol : CallableSymbol
    {
        public override ITypeNode? GetReturnType() => null;
        public override string GetName() => "this";
        public override bool GetIsConst() => false;
    }
}
