using KirbyLib.Mint;
using Mint.AstNodes;
using Mint.Util;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Mint.Semantics
{
    public interface ICallable
    {
        public string GetSignature();
        public string GetSignatureWithoutName();
        public ITypeNode? GetReturnType();
    }

    public interface IAccessible;

    public interface IInstanciable
    {
        public ObjectLocation GetLoc();
    }

    public record ModuleSymbol
    {
        public required string Name { get; init; }
        public Dictionary<string, ObjectSymbol> LocalObjects { get; } = new();
        public Dictionary<string, XRefSymbol> XRefObjects { get; } = new();
        public Dictionary<string, EnumSymbol> Enums { get; } = new();

        public IInstanciable GetInstanciable(string objName)
        {
            if (LocalObjects.TryGetValue(objName, out ObjectSymbol? objSbl))
                return objSbl;
            if (XRefObjects.TryGetValue(objName, out XRefSymbol? xRefSbl))
                return xRefSbl;

            foreach (ObjectSymbol obj in LocalObjects.Values)
                if (obj.FullName == objName)
                    return obj;

            throw new Exception($"No instanciable object with name '{objName}' has been found.");
        }
    }

    public record ObjectSymbol : IInstanciable
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

        public ObjectLocation GetLoc() => ObjectLocation.Mint;
    }

    // Like a class, but not from the module.
    public record XRefSymbol : IInstanciable
    {
        public required string FullName;
        public required ObjectLocation Loc;
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

        public ObjectLocation GetLoc() => Loc;
    }

    public record EnumSymbol
    {
        public required string Name { get; init; }
        public List<MintEnum> Elements { get; } = new();
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
        public required bool IsConst { get; init; }
        public List<ParamNode> Parameters { get; } = new();

        public ITypeNode? GetReturnType() => ReturnType;

        public string GetSignature()
        {
            StringBuilder sb = new(Name);

            sb.Append(GetSignatureWithoutName());

            return sb.ToString();
        }

        public string GetSignatureWithoutName()
        {
            StringBuilder sb = new(NameOperations.BuildCallParamTypes(Utility.ToTypeNodes(Parameters)));
            if (IsConst)
                sb.Append("const");
            return sb.ToString();
        }
    }

    public record ConstructorSymbol : ICallable
    {
        public List<ParamNode> Parameters { get; } = new();

        public string GetSignatureWithoutName() => NameOperations.BuildCallParamTypes(Utility.ToTypeNodes(Parameters));

        public string GetSignature()
        {
            StringBuilder sb = new("this");

            sb.Append(GetSignatureWithoutName());

            return sb.ToString();
        }

        public ITypeNode? GetReturnType() => null;
    }
    
    /*
    An external function doesn't have a body, and we only care about the types of
    the parameters.
    */
    public record XRefFunctionSymbol : ICallable
    {
        public required string Name { get; init; }
        public required ITypeNode? ReturnType { get; init; } = null;
        public required bool IsConst;
        public List<ITypeNode> ArgumentTypes { get; } = new();

        public string GetSignature()
        {
            StringBuilder sb = new(Name);

            sb.Append(GetSignatureWithoutName());

            return sb.ToString();
        }

        public string GetSignatureWithoutName()
        {
            StringBuilder sb = new(NameOperations.BuildCallParamTypes(ArgumentTypes));
            if (IsConst)
                sb.Append("const");
            return sb.ToString();
        }

        public ITypeNode? GetReturnType() => ReturnType;
    }

    public record XRefConstructorSymbol : ICallable
    {
        public List<ITypeNode> ArgumentTypes { get; } = new();

        public string GetSignatureWithoutName() => NameOperations.BuildCallParamTypes(ArgumentTypes);

        public string GetSignature()
        {
            StringBuilder sb = new("this");

            sb.Append(GetSignatureWithoutName());

            return sb.ToString();
        }

        public ITypeNode? GetReturnType() => null;
    }
}
