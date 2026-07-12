using Mint.AstNodes;

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
        public Dictionary<string, FunctionSymbol> Functions { get; } = new();
    }

    // Like a class, but not from the module.
    public record XRefSymbol
    {
        public required string FullName;
        public Dictionary<string, VariableSymbol> Variables { get; } = new();
        public Dictionary<string, XRefFunctionSymbol> Functions { get; } = new();
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
