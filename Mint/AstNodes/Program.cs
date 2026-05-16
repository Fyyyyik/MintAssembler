using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.AstNodes
{
    public record ModuleNode(List<ClassNode> Classes, int Line, int Column) : AstNode(Line, Column);

    public record ClassNode(
        string Name,
        List<MemberNode> Members,
        int Line,
        int Column
    ) : AstNode(Line, Column);
}
