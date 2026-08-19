using Mint.AstNodes;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Mint.Semantics.Symbols
{
    public class XRefSymbol : IInstanciable
    {
        public required string FullName { get; init; }
        public required ObjectLocation Loc { get; init; }
        public Dictionary<string, VariableSymbol> Variables { get; } = new();
        public List<XRefFunctionSymbol> Functions { get; } = new();
        public List<XRefConstructorSymbol> Constructors { get; } = new();

        public bool FindFunction(string name, IList<ITypeNode> paramTypes, [NotNullWhen(true)] out XRefFunctionSymbol? funcSbl)
            => OverloadResolution.TryFind(Functions.Where(f => f.GetName() == name), paramTypes, out funcSbl);

        public bool FindConstructor(IList<ITypeNode> paramTypes, [NotNullWhen(true)] out XRefConstructorSymbol? ctSbl)
            => OverloadResolution.TryFind(Constructors, paramTypes, out ctSbl);

        public ObjectLocation GetLoc() => Loc;
    }
}
