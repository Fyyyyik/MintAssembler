using Mint.AstNodes;

namespace Mint.Semantics
{
    public record ModuleSymbol
    {
        public required string Namespace { get; init; }
        public Dictionary<string, ObjectSymbol> Objects { get; } = new();

        // NOTE : the xrefs are what the semantic analyser is aware of, NOT the
        // list of xrefs for the final mint module!
        public Dictionary<string, XRefSymbol> XRefs { get; } = new();
    }

    public record ObjectSymbol
    {
        public required string FullName;
        public Dictionary<string, VariableSymbol> Variables { get; } = new();
        public Dictionary<string, FunctionSymbol> Functions { get; } = new();
    }

    public record XRefSymbol
    {
        public required string FullName;
        public Dictionary<string, VariableSymbol> Variables { get; } = new();
        public Dictionary<string, ExternalFunctionSymbol> Functions { get; } = new();
    }

    public record VariableSymbol
    {
        public required string Name { get; init; }
        public required TypeNode Type { get; init; }
    }

    public record FunctionSymbol
    {
        public required string Name { get; init; }
        public required TypeNode? ReturnType { get; init; }
        public List<ParamNode> Parameters { get; } = new();
    }

    public record ExternalFunctionSymbol
    {
        public required string Name { get; init; }
        public TypeNode? ReturnType { get; init; } = null;
        public List<TypeNode> ArgumentTypes { get; } = new();
    }
}
