using System;
using System.Collections.Generic;
using System.Text;

namespace Mint
{
    public class LexerException : Exception
    {
        public int Line { get; }
        public int Column { get; }

        public LexerException(string message, int line, int column)
            : base($"Lexer error at line {line} column {column} : {message}")
        {
            Line = line;
            Column = column;
        }
    }
}
