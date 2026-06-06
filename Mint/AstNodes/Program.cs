using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.AstNodes
{
    public record ModuleNode(
        NamespaceNode Namespace,
        List<ClassNode> Classes,
        List<ExternalVariableNode> extVariables,
        List<ExternalFunctionNode> extFunctions,
        int Line,
        int Column
    ) : AstNode(Line, Column);

    public record NamespaceNode(
        string FullName,
        int Line,
        int Column
    ) : AstNode(Line, Column);

    public record ExternalVariableNode(
        TypeNode Type,
        string FullName,
        int Line,
        int Column
    ) : AstNode(Line, Column);

    public record ExternalFunctionNode(
        TypeNode? ReturnType,
        string FullName,
        // A list of type instead of Params since here we only care
        // about what type goes where. Also in mint params have no name.
        List<TypeNode> ParamTypes,
        int Line,
        int Column
    ) : AstNode(Line, Column);

    public record ClassNode(
        string FullName,
        List<MemberNode> Members,
        int Line,
        int Column
    ) : AstNode(Line, Column);
}
