using Mint.AstNodes;
using System.Diagnostics.CodeAnalysis;

namespace Mint.Semantics
{
    public interface ICallable;

    public interface IAccessible;

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
        public List<ConstructorSymbol> Constructors { get; } = new();

        public bool FindFunction(string name, IList<ITypeNode> parameterTypes, [NotNullWhen(true)] out FunctionSymbol? funcSbl)
        {
            foreach (FunctionSymbol func in Functions)
            {
                if (func.Name == name)
                {
                    if (func.Parameters.Count != parameterTypes.Count)
                        continue;
                    bool sameParams = true;
                    for (int i = 0; i < parameterTypes.Count; i++)
                        if (parameterTypes[i].GetBaseType().Name != func.Parameters[i].Type.GetBaseType().Name)
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

        public bool FindConstructor(IList<ITypeNode> parameterTypes, [NotNullWhen(true)] out ConstructorSymbol? ctSbl)
        {
            foreach (ConstructorSymbol ct in Constructors)
            {
                if (ct.Parameters.Count != parameterTypes.Count)
                    continue;
                bool sameParams = true;
                for (int i = 0; i < parameterTypes.Count; i++)
                    if (parameterTypes[i].GetBaseType().Name != ct.Parameters[i].Type.GetBaseType().Name)
                    {
                        sameParams = false;
                        break;
                    }
                if (sameParams)
                {
                    ctSbl = ct;
                    return true;
                }
            }
            ctSbl = null;
            return false;
        }
    }

    // Like a class, but not from the module.
    public record XRefSymbol
    {
        public required string FullName;
        public Dictionary<string, VariableSymbol> Variables { get; } = new();
        public List<XRefFunctionSymbol> Functions { get; } = new();
        public List<XRefConstructorSymbol> Constructors { get; } = new();

        public bool FindFunction(string name, IList<ITypeNode> parameterTypes, [NotNullWhen(true)] out XRefFunctionSymbol? funcSbl)
        {
            foreach (XRefFunctionSymbol func in Functions)
            {
                if (func.Name == name)
                {
                    if (func.ArgumentTypes.Count != parameterTypes.Count)
                        continue;
                    bool sameTypes = true;
                    for (int i = 0; i < parameterTypes.Count; i++)
                        if (parameterTypes[i].GetBaseType().Name != func.ArgumentTypes[i].GetBaseType().Name)
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

        public bool FindConstructor(IList<ITypeNode> parameterTypes, [NotNullWhen(true)] out XRefConstructorSymbol? ctSbl)
        {
            foreach (XRefConstructorSymbol ct in Constructors)
            {
                if (ct.ArgumentTypes.Count != parameterTypes.Count)
                    continue;
                bool sameParams = true;
                for (int i = 0; i < parameterTypes.Count; i++)
                    if (parameterTypes[i].GetBaseType().Name != ct.ArgumentTypes[i].GetBaseType().Name)
                    {
                        sameParams = false;
                        break;
                    }
                if (sameParams)
                {
                    ctSbl = ct;
                    return true;
                }
            }
            ctSbl = null;
            return false;
        }
    }

    // Used for local and external references since external vars don't have different info
    public record VariableSymbol : IAccessible
    {
        public required string Name { get; init; }
        public required ITypeNode Type { get; init; }
    }

    public record FunctionSymbol : ICallable
    {
        public required string Name { get; init; }
        public required ITypeNode? ReturnType { get; init; }
        public required bool HasThis { get; init; }
        public List<ParamNode> Parameters { get; } = new();
    }

    public record ConstructorSymbol
    {
        public List<ParamNode> Parameters { get; } = new();
    }
    
    /*
    An external function doesn't have a body, and we only care about the types of
    the parameters.
    */
    public record XRefFunctionSymbol : ICallable
    {
        public required string Name { get; init; }
        public required ITypeNode? ReturnType { get; init; } = null;
        public List<ITypeNode> ArgumentTypes { get; } = new();
    }

    public record XRefConstructorSymbol
    {
        public List<ITypeNode> ArgumentTypes { get; } = new();
    }
}
