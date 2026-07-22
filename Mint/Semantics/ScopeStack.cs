using Mint.AstNodes;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.Semantics
{
    public class ScopeStack
    {
        private readonly Stack<Dictionary<string, ITypeNode>> _scopes = new();

        public void PushScope() => _scopes.Push(new());
        public void PopScope() => _scopes.Pop();

        public void Define(string name, ITypeNode type)
            => _scopes.Peek()[name] = type;

        public ITypeNode? LookUp(string name)
        {
            foreach (Dictionary<string, ITypeNode> scope in _scopes)
                if (scope.TryGetValue(name, out ITypeNode? type))
                    return type;
            return null;
        }
    }
}
