using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.AstNodes
{
    public abstract record ExprNode : AstNode;

    // Literals
    public record IntLiteralNode(int Value) : ExprNode;
    public record FloatLiteralNode(float Value) : ExprNode;
    public record BoolLiteralNode(bool Value) : ExprNode;
    public record StringLiteralNode(string Value) : ExprNode;

    // Variables and access
    public record IdentifierNode(string Name) : ExprNode;
    public record MemberAccessNode(ExprNode Object, string Member) : ExprNode;
    public record ArrayAccessNode(ExprNode Array, ExprNode Index) : ExprNode;

    // Operations
    public record BinaryExprNode(ExprNode Left, string Op, ExprNode Right) : ExprNode;
    public record UnaryExprNode(string Op, ExprNode Operand) : ExprNode;

    // Calls and construction
    public record FunctionCallNode(
        string Function,
        List<ExprNode> Args
    ) : ExprNode;
    public record NewObjectNode(string ClassName) : ExprNode;
    public record ArrayCreationNode(TypeNode ElementType, ExprNode Size) : ExprNode;
}
