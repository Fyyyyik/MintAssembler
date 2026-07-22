using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.AstNodes
{
    public abstract record MemberNode(int Line, int Column) : AstNode(Line, Column);

    public record VariableNode(
        ITypeNode Type,
        string Name,
        int Line,
        int Column
    ) : MemberNode(Line, Column);

    public record FunctionNode(
        ITypeNode? ReturnType,
        string Name,
        bool IsConst,
        List<ParamNode> Params,
        bool HasThis,
        BlockNode Body,
        int Line,
        int Column
    ) : MemberNode(Line, Column);

    public record ConstructorNode(List<ParamNode> Params, BlockNode Body, int Line, int Column) : MemberNode(Line, Column);

    public record ParamNode(ITypeNode Type, string Name, int Line, int Column) : AstNode(Line, Column);

    public record ExternalFunctionNode(
        ITypeNode? ReturnType,
        string Name,
        bool IsConst,
        // A list of type instead of Params since here we only care
        // about what type goes where. Also in mint, params have no name.
        List<ITypeNode> ParamTypes,
        int Line,
        int Column
    ) : MemberNode(Line, Column)
    {
        public string GetNameWithArgs()
        {
            StringBuilder sb = new($"{Name}(");
            for (int i = 0; i < ParamTypes.Count; i++)
            {
                sb.Append(ParamTypes[i].GetTypeName());

                if (i != ParamTypes.Count - 1)
                    sb.Append(',');
            }
            sb.Append(')');
            return sb.ToString();
        }
    }

    public record ExternalConstructorNode(List<ITypeNode> ParamTypes, int Line, int Column) : MemberNode(Line, Column);
}
