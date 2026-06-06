using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.AstNodes
{
    public abstract record StmtNode(int Line, int Column) : AstNode(Line, Column);

    public record BlockNode(List<StmtNode> Statements, int Line, int Column) : AstNode(Line, Column);

    public record VarDeclNode(
        TypeNode Type,
        string Name,
        ExprNode Initializer,
        int Line,
        int Column
    ) : StmtNode(Line, Column);

    public record AssignNode(
        string Name,
        ExprNode Value,
        int Line,
        int Column
    ) : StmtNode(Line, Column);

    public record ArrayAssignNode(
        ExprNode Array,
        ExprNode Index,
        ExprNode Value,
        int Line,
        int Column
    ) : StmtNode(Line, Column);

    public record IfNode(
        ExprNode Condition,
        BlockNode Then,
        BlockNode? Else,
        int Line,
        int Column
    ) : StmtNode(Line, Column);

    public record WhileNode(
        ExprNode Condition,
        BlockNode Body,
        int Line,
        int Column
    ) : StmtNode(Line, Column);

    public record ForNode(
        StmtNode Initializer,
        ExprNode Condition,
        StmtNode Increment,
        BlockNode Body,
        int Line,
        int Column
    ) : StmtNode(Line, Column);

    public record ReturnNode(ExprNode? Value, int Line, int Column) : StmtNode(Line, Column);

    public record ExprStmtNode(ExprNode Expr, int Line, int Column) : StmtNode(Line, Column);

    public record QualifiedAssignNode(
        string FullName,
        ExprNode Value,
        int Line,
        int Column
    ) : StmtNode(Line, Column);

    public record IncrementNode(string Name, bool IsPrefix, bool IsIncrement, int Line, int Column) : StmtNode(Line, Column);
}
