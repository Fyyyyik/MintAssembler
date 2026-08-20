using Mint.AstNodes;
using Mint.Util;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.Semantics.Symbols
{
    public abstract class CallableSymbol
    {
        public required ObjectLocation CallLoc { get; init; }

        public string GetSignatureWithoutName()
        {
            StringBuilder sb = new(NameOperations.BuildCallParamTypes(GetParamTypes()));
            if (GetIsConst()) sb.Append("const");
            return sb.ToString();
        }

        public string GetSignature() => GetName() + GetSignatureWithoutName();

        public abstract ITypeNode? GetReturnType();
        public abstract string GetName();
        public abstract ITypeNode[] GetParamTypes();
        public abstract bool GetIsConst();
    }
}
