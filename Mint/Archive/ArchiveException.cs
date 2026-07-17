using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.Archive
{
    public class ArchiveException : Exception
    {
        public ArchiveException(string message) : base($"Archive analyser error : {message}") { }
    }
}
