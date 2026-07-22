using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.AstNodes
{
    internal static class Utility
    {
        internal static ITypeNode[] ToTypeNodes(IList<ParamNode> paramNodes)
        {
            List<ITypeNode> typeNodes = new();
            foreach (ParamNode param in paramNodes)
                typeNodes.Add(param.Type);
            return typeNodes.ToArray();
        }
    }
}
