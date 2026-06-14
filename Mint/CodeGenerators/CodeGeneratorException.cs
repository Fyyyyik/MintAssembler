using Mint.AstNodes;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.CodeGenerators
{
    public class CodeGeneratorException : Exception
    {
        public int Line;
        public int Column;

        public CodeGeneratorException(string message, int line, int column)
            : base($"Code generator error at line {line} column {column} : {message}")
        {
            Line = line;
            Column = column;
        }
    }
}
