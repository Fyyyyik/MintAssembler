using System;
using System.Collections.Generic;
using System.Text;

namespace MintAssembler.AstNodes
{
    public record TypeNode(string Name, bool IsArray = false) : AstNode;
}
