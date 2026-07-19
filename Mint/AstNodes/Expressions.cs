using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.AstNodes
{
    public interface IAssignable { }

    public abstract record ExprNode(int Line, int Column) : AstNode(Line, Column);

    // Literals
    public record IntLiteralNode(int Value, int Line, int Column) : ExprNode(Line, Column);
    public record FloatLiteralNode(float Value, int Line, int Column) : ExprNode(Line, Column);
    public record BoolLiteralNode(bool Value, int Line, int Column) : ExprNode(Line, Column);
    public record StringLiteralNode(string Value, int Line, int Column) : ExprNode(Line, Column);

    // Assignables
    public record IdentifierNode(string Name, int Line, int Column) : ExprNode(Line, Column), IAssignable;
    public record QualifiedAccessNode(string FullName, int Line, int Column) : ExprNode(Line, Column), IAssignable;
    public record MemberAccessNode(ExprNode Object, string Member, int Line, int Column) : ExprNode(Line, Column), IAssignable;
    public record ArrayAccessNode(ExprNode Array, ExprNode Index, int Line, int Column) : ExprNode(Line, Column), IAssignable;
    public record DereferenceNode(ExprNode Reference, int Line, int Column) : ExprNode(Line, Column), IAssignable;

    // This
    public record ThisNode(int Line, int Column): ExprNode(Line, Column);

    // Operations
    public record BinaryExprNode(ExprNode Left, string Op, ExprNode Right, int Line, int Column) : ExprNode(Line, Column);
    public record UnaryExprNode(string Op, ExprNode Operand, int Line, int Column) : ExprNode(Line, Column);

    // Calls and construction
    public record QualifiedCallNode( // could either be calling a method from an object or a static method in some namespace
        string FullName,
        List<ExprNode> Args,
        int Line,
        int Column
    ) : ExprNode(Line, Column);
    public record MemberCallNode( // for when we're 100% sure we're calling from an object
        ExprNode Object,
        string Name,
        List<ExprNode> Args,
        int Line,
        int Column
    ) : ExprNode(Line, Column);
    public record NewObjectNode(string ClassName, int Line, int Column) : ExprNode(Line, Column);
    public record PushInstanceNode(string ObjectName, int Line, int Column, List<ExprNode>? CtArgs = null) : ExprNode(Line, Column);

    // size is null if no expression is put between the brackets
    // if there are initializers, figure out the size from that
    public record ArrayCreationNode(TypeNode ElementType, ExprNode? Size, int Line, int Column, List<ExprNode>? Initializers = null) : ExprNode(Line, Column);
    public record IncrementNode(ExprNode Target, bool IsPrefix, bool IsIncrement, int Line, int Column) : ExprNode(Line, Column);

    // Getting the offset of a member
    public record MemberOffsetNode(ExprNode Object, string Member, int Line, int Column) : ExprNode(Line, Column);

    // Casts
    public record TypeCastNode(TypeNode Type, ExprNode Expr, int Line, int Column) : ExprNode(Line, Column);
}
