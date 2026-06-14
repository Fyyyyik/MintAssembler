using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.AstNodes
{
    public record ModuleNode(
        NamespaceNode Namespace,
        List<ClassNode> Classes,
        List<ClassNode> XRefs,
        int Line,
        int Column
    ) : AstNode(Line, Column);

    public record NamespaceNode(
        string FullName,
        int Line,
        int Column
    ) : AstNode(Line, Column);
}
