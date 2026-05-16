using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.AstNodes
{
    public record TypeNode(string Name, int Line, int Column, bool IsArray = false) : AstNode(Line, Column);
}
