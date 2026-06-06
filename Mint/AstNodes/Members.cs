using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.AstNodes
{
    public abstract record MemberNode(int Line, int Column) : AstNode(Line, Column);

    public record VariableNode(
        TypeNode Type,
        string Name,
        int Line,
        int Column
    ) : MemberNode(Line, Column);

    public record FunctionNode(
        TypeNode? ReturnType,
        string Name,
        List<ParamNode> Params,
        BlockNode Body,
        int Line,
        int Column
    ) : MemberNode(Line, Column);

    public record ParamNode(TypeNode Type, string Name, int Line, int Column) : AstNode(Line, Column);
}
