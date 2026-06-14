using Mint.AstNodes;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.CodeGenerators
{
    public abstract class CodeGenerator
    {
        private ModuleNode _module;

        public CodeGenerator(ModuleNode module) => _module = module;
    }
}
