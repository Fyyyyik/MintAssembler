using Mint.AstNodes;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Mint.Semantics.Symbols
{
    public static class OverloadResolution
    {
        public static bool TryFind<T>(
            IEnumerable<T> candidates,
            IList<ITypeNode> paramTypes,
            [NotNullWhen(true)] out T? match
        ) where T : CallableSymbol
        {
            foreach (T c in candidates)
            {
                ITypeNode[] cTypes = c.GetParamTypes();
                if (cTypes.Length != paramTypes.Count) continue;

                bool same = true;
                for (int i = 0; i < paramTypes.Count; i++)
                    if (!SemanticAnalyser.TypesMatch(paramTypes[i], cTypes[i]))
                    {
                        same = false;
                        break;
                    }

                if (same)
                {
                    match = c;
                    return true;
                }
            }
            match = null;
            return false;
        }
    }
}
