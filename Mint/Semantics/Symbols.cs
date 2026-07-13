using Mint.AstNodes;
using System.Diagnostics.CodeAnalysis;

namespace Mint.Semantics
{
    public record ModuleSymbol
    {
        public required string Name { get; init; }
        public Dictionary<string, ObjectSymbol> LocalObjects { get; } = new();
        public Dictionary<string, XRefSymbol> XRefObjects { get; } = new();
    }

    public record ObjectSymbol
    {
        public required string FullName;
        public Dictionary<string, VariableSymbol> Variables { get; } = new();
        public List<FunctionSymbol> Functions { get; } = new(); // overloads exists, so no dictionnary

        public bool FindFunction(string name, IList<TypeNode> parameterTypes, [NotNullWhen(true)] out FunctionSymbol? funcSbl)
        {
            foreach (FunctionSymbol func in Functions)
            {
                if (func.Name == name)
                {
                    if (func.Parameters.Count != parameterTypes.Count)
                        continue;
                    bool sameParams = true;
                    for (int i = 0; i < parameterTypes.Count; i++)
                        if (parameterTypes[i].Name != func.Parameters[i].Type.Name)
                        {
                            sameParams = false;
                            break;
                        }
                    if (sameParams)
                    {
                        funcSbl = func;
                        return true;
                    }
                }
            }
            funcSbl = null;
            return false;
        }
    }

    // Like a class, but not from the module.
    public record XRefSymbol
    {
        public required string FullName;
        public Dictionary<string, VariableSymbol> Variables { get; } = new();
        public List<XRefFunctionSymbol> Functions { get; } = new();

        public bool FindFunction(string name, IList<TypeNode> parameterTypes, [NotNullWhen(true)] out XRefFunctionSymbol? funcSbl)
        {
            foreach (XRefFunctionSymbol func in Functions)
            {
                if (func.Name == name)
                {
                    if (func.ArgumentTypes.Count != parameterTypes.Count)
                        continue;
                    bool sameTypes = true;
                    for (int i = 0; i < parameterTypes.Count; i++)
                        if (parameterTypes[i].Name != func.ArgumentTypes[i].Name)
                        {
                            sameTypes = false;
                            break;
                        }
                    if (sameTypes)
                    {
                        funcSbl = func;
                        return true;
                    }
                }
            }
            funcSbl = null;
            return false;
        }
    }

    // Used for local and external references since external vars don't have different info
    public record VariableSymbol
    {
        public required string Name { get; init; }
        public required TypeNode Type { get; init; }
    }

    public record FunctionSymbol
    {
        public required string Name { get; init; }
        public required TypeNode? ReturnType { get; init; }
        public required bool HasThis { get; init; }
        public List<ParamNode> Parameters { get; } = new();
    }
    
    /*
    An external function doesn't have a body, and we only care about the types of
    the parameters.
    */
    public record XRefFunctionSymbol
    {
        public required string Name { get; init; }
        public required TypeNode? ReturnType { get; init; } = null;
        public List<TypeNode> ArgumentTypes { get; } = new();
    }
}
