using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.AstNodes
{
    internal static class Utility
    {
        internal static TypeNode[] ToTypeNodes(IList<ParamNode> paramNodes)
        {
            List<TypeNode> typeNodes = new();
            foreach (ParamNode param in paramNodes)
                typeNodes.Add(param.Type);
            return typeNodes.ToArray();
        }
    }
}
