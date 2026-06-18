using System;
using System.Collections.Generic;
using System.Text;

namespace MintAssembler
{
    internal record CompileOptions
    {
        internal required FileInfo InputFile { get; init; }
        internal required string ModuleName { get; init; }
        internal required string Version { get; init; }
        internal required bool IsVerbose { get; init; }
        internal string? OutputPath { get; init; }
        internal FileInfo? TargetArchive { get; init; }
    }
}
