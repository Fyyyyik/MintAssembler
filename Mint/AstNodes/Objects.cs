using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.AstNodes
{
    public abstract record ObjectNode(int Line, int Column) : AstNode(Line, Column);

    public record ClassNode(
        string FullName,
        List<MemberNode> Members,
        int Line,
        int Column
    ) : ObjectNode(Line, Column);
}
