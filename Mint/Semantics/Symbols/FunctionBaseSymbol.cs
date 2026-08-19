using Mint.AstNodes;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.Semantics.Symbols
{
    public abstract class FunctionBaseSymbol : CallableSymbol
    {
        private ITypeNode? _returnType;
        private string _name;
        private bool _isConst;

        public FunctionBaseSymbol(ITypeNode? returnType, string name, bool isConst)
        {
            _returnType = returnType;
            _name = name;
            _isConst = isConst;
        }

        public override ITypeNode? GetReturnType() => _returnType;
        public override bool GetIsConst() => _isConst;
        public override string GetName() => _name;
    }
}
