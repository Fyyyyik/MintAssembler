using Mint.AstNodes;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.CodeGenerators
{
    public class CodeGeneratorException : Exception
    {
        public AstNode Context;

        public CodeGeneratorException(string message, AstNode context)
            : base($"Code generator error with context {context} : {message}")
            => Context = context;
    }
}
