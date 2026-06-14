using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.CodeGenerators
{
    internal class RegisterManagerException : Exception
    {
        internal RegisterManagerException(string message)
            : base($"Register manager error : {message}") { }
    }
}
