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
    ) : MemberNode(Line, Column)
    {
        public string GetNameWithArgs()
        {
            StringBuilder sb = new($"{Name}(");
            for (int i = 0; i < ParamTypes.Count; i++)
            {
                if (ParamTypes[i].IsConst)
                    sb.Append("const ");
                if (ParamTypes[i].IsRef)
                    sb.Append("ref ");
                sb.Append(ParamTypes[i].Name);

                if (i != ParamTypes.Count - 1)
                    sb.Append(',');
            }
            sb.Append(')');
            return sb.ToString();
        }
    }
}
