using Mint.AstNodes;

namespace Mint.Semantics
{
    public record ModuleSymbol
    {
        public Dictionary<string, ClassSymbol> Classes { get; } = new();
        public Dictionary<string, ExternalVariableSymbol> XVars { get; } = new();
        public Dictionary<string, ExternalFunctionSymbol> XFuncs { get; } = new();
    }

    public record ClassSymbol
    {
        public required string Name;
        public Dictionary<string, VariableSymbol> Variables { get; } = new();
        public Dictionary<string, FunctionSymbol> Functions { get; } = new();
    }

    public record VariableSymbol
    {
        public required string Name { get; init; }
        public required TypeNode? Type { get; init; }
    }

    public record FunctionSymbol
    {
        public required string Name { get; init; }
        public required TypeNode? ReturnType { get; init; }
        public List<ParamNode> Parameters { get; } = new();
    }

    public record ExternalVariableSymbol
    {
        public required string FullName { get; init; }
        public required TypeNode? Type { get; init; }
    }

    public record ExternalFunctionSymbol
    {
        public required string FullName { get; init; }
        public TypeNode? ReturnType { get; init; } = null;
        public List<TypeNode> ArgumentTypes { get; } = new();
    }
}
