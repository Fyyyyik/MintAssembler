using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.AstNodes
{
    public abstract record MemberNode : AstNode;

    public record VariableNode(
        TypeNode Type,
        string Name
    ) : MemberNode;

    public record FunctionNode(
        TypeNode ReturnType,
        string Name,
        List<ParamNode> Params,
        BlockNode Body
    ) : MemberNode;

    public record ParamNode(TypeNode Type, string Name) : AstNode;
}
