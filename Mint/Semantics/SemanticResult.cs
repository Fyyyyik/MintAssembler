using Mint.AstNodes;
using Mint.Semantics.Symbols;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.Semantics
{
    public record SemanticResult(
        ModuleSymbol Module,
        Dictionary<ExprNode, ITypeNode?> ExprTypes,
        Dictionary<ExprNode, CallableSymbol> ExprCalls,
        Dictionary<ExprNode, VariableSymbol> ExprAccesses,
        IReadOnlyList<SemanticError> Errors
    );

    public record SemanticError(string Message, AstNode Node);
}
