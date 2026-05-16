using Mint.AstNodes;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.Semantics
{
    public class ScopeStack
    {
        private readonly Stack<Dictionary<string, TypeNode>> _scopes = new();

        public void PushScope() => _scopes.Push(new());
        public void PopScope() => _scopes.Pop();

        public void Define(string name, TypeNode type)
            => _scopes.Peek()[name] = type;

        public TypeNode? LookUp(string name)
        {
            foreach (Dictionary<string, TypeNode> scope in _scopes)
                if (scope.TryGetValue(name, out TypeNode? type))
                    return type;
            return null;
        }
    }
}
