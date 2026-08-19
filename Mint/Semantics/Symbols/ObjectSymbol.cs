using Mint.AstNodes;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Mint.Semantics.Symbols
{
    public class ObjectSymbol : IInstanciable
    {
        public required string FullName;
        public Dictionary<string, VariableSymbol> Variables { get; } = new();
        public List<FunctionSymbol> Functions { get; } = new(); // overloads exists, so no dictionnary
        public List<ConstructorSymbol> Constructors { get; } = new();

        public bool FindFunction(string name, IList<ITypeNode> paramTypes, [NotNullWhen(true)] out FunctionSymbol? funcSbl)
            => OverloadResolution.TryFind(Functions.Where(f => f.GetName() == name), paramTypes, out funcSbl);

        public bool FindConstructor(IList<ITypeNode> paramTypes, [NotNullWhen(true)] out ConstructorSymbol? ctSbl)
            => OverloadResolution.TryFind(Constructors, paramTypes, out ctSbl);

        public ObjectLocation GetLoc() => ObjectLocation.Local;
    }
}
