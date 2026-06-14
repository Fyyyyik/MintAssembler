using Mint.AstNodes;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.Semantics
{
    public record SemanticResult(
        ModuleSymbol Module,
        Dictionary<ExprNode, TypeNode> ExprTypes,
        IReadOnlyList<SemanticError> Errors
    );

    public record SemanticError(string Message, AstNode Node);
}
