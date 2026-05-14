using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.AstNodes
{
    public record ProgramNode(List<ClassNode> Classes) : AstNode;

    public record ClassNode(
        string Name,
        List<MemberNode> Members
    ) : AstNode;
}
