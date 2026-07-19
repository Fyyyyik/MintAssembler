using System;
using System.Collections.Generic;
using System.Text;

namespace MintAssembler
{
    internal record CompileOptions
    {
        internal required FileInfo InputFile { get; init; }
        internal required string Version { get; init; }
        internal required bool IsVerbose { get; init; }
        internal FileInfo? Output { get; init; }
        internal List<FileInfo>? Archives { get; init; }
    }
}
