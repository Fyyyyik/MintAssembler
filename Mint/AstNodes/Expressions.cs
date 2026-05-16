using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.AstNodes
{
    public abstract record ExprNode(int Line, int Column) : AstNode(Line, Column);

    // Literals
    public record IntLiteralNode(int Value, int Line, int Column) : ExprNode(Line, Column);
    public record FloatLiteralNode(float Value, int Line, int Column) : ExprNode(Line, Column);
    public record BoolLiteralNode(bool Value, int Line, int Column) : ExprNode(Line, Column);
    public record StringLiteralNode(string Value, int Line, int Column) : ExprNode(Line, Column);

    // Variables and access
    public record IdentifierNode(string Name, int Line, int Column) : ExprNode(Line, Column);
    public record MemberAccessNode(ExprNode Object, string Member, int Line, int Column) : ExprNode(Line, Column);
    public record ArrayAccessNode(ExprNode Array, ExprNode Index, int Line, int Column) : ExprNode(Line, Column);

    // Operations
    public record BinaryExprNode(ExprNode Left, string Op, ExprNode Right, int Line, int Column) : ExprNode(Line, Column);
    public record UnaryExprNode(string Op, ExprNode Operand, int Line, int Column) : ExprNode(Line, Column);

    // Calls and construction
    public record FunctionCallNode(
        string Function,
        List<ExprNode> Args,
        int Line,
        int Column
    ) : ExprNode(Line, Column);
    public record NewObjectNode(string ClassName, int Line, int Column) : ExprNode(Line, Column);
    public record ArrayCreationNode(TypeNode ElementType, ExprNode Size, int Line, int Column) : ExprNode(Line, Column);
}
