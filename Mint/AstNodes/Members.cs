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
        bool IsConst,
        List<ParamNode> Params,
        bool HasThis,
        BlockNode Body,
        int Line,
        int Column
    ) : MemberNode(Line, Column);

    public record ParamNode(TypeNode Type, string Name, int Line, int Column) : AstNode(Line, Column);

    public record ExternalFunctionNode(
        TypeNode? ReturnType,
        string Name,
        bool IsConst,
        // A list of type instead of Params since here we only care
        // about what type goes where. Also in mint, params have no name.
        List<TypeNode> ParamTypes,
        int Line,
        int Column
    ) : MemberNode(Line, Column);
}
