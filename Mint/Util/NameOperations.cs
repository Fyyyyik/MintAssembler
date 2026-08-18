using Mint.AstNodes;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.Util
{
    public static class NameOperations
    {
        public static string GetParent(string fullName)
        {
            return string.Join('.', fullName.Split('.')[..^1]);
        }

        public static string BuildCallParamTypes(IList<ITypeNode> paramTypes)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append('(');
            for (int i = 0; i < paramTypes.Count; i++)
            {
                sb.Append(paramTypes[i].GetTypeName());
                if (i != paramTypes.Count - 1)
                    sb.Append(',');
            }
            sb.Append(')');
            return sb.ToString();
        }
    }
}
