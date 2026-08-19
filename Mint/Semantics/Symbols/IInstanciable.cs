using Mint.AstNodes;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.Semantics.Symbols
{
    public interface IInstanciable
    {
        public ObjectLocation GetLoc();
    }
}
