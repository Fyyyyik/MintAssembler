using System;
using System.Collections.Generic;
using System.Text;

namespace MintAssembler.AstNodes
{
    public abstract record StmtNode : AstNode;

    public record BlockNode(List<StmtNode> Statements) : AstNode;

    public record VarDeclNode(
        TypeNode Type,
        string Name,
        ExprNode Initializer
    ) : StmtNode;

    public record AssignNode(
        string Name,
        ExprNode Value
    ) : StmtNode;

    public record ArrayAssignNode(
        ExprNode Array,
        ExprNode Index,
        ExprNode Value
    ) : StmtNode;

    public record IfNode(
        ExprNode Condition,
        BlockNode Then,
        BlockNode? Else
    ) : StmtNode;

    public record WhileNode(
        ExprNode Condition,
        BlockNode Body
    ) : StmtNode;

    public record ForNode(
        StmtNode Initializer,
        ExprNode Condition,
        StmtNode Increment,
        BlockNode Body
    ) : StmtNode;

    public record ReturnNode(ExprNode? Value) : StmtNode;

    public record ExprStmtNode(ExprNode Expr) : StmtNode;
}
