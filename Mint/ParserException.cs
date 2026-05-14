using System;
using System.Collections.Generic;
using System.Text;

namespace Mint
{
    public class ParserException : Exception
    {
        public int Line { get; }
        public int Column { get; }

        public ParserException(string message, int line, int column)
            : base($"Parser error at line {line} column {column} : {message}")
        {
            Line = line;
            Column = column;
        }
    }
}
