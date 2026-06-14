using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.AstNodes
{
    public record ModuleNode(
        NamespaceNode Namespace,
        List<ObjectNode> Objects,
        List<ObjectNode> XRefs,
        int Line,
        int Column
    ) : AstNode(Line, Column);

    public record ObjectNode(
        string Name,
        List<MemberNode> Members,
        int Line,
        int Column
    ) : AstNode(Line, Column);

    public record NamespaceNode(
        string FullName,
        int Line,
        int Column
    ) : AstNode(Line, Column);
}
