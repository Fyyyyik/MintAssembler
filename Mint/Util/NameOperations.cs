using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.Util
{
    internal static class NameOperations
    {
        internal static string GetParent(string fullName)
        {
            return string.Join('.', fullName.Split('.')[..^1]);
        }
    }
}
