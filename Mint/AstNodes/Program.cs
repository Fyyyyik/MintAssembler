using KirbyLib.Mint;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.AstNodes
{
    public record ModuleNode(
        string FullName,
        List<ObjectNode> Objects, // local and xrefs!
        int Line,
        int Column
    ) : AstNode(Line, Column);

    // Only the objects with Location set to Local get compiled
    // the rest are xrefs given to the compiler for context.
    public record ObjectNode(
        string Name, // full name with namespaces for xrefs
        List<MemberNode> Members,
        ObjectLocation Location,
        ObjectType ObjType,
        int Line,
        int Column
    ) : AstNode(Line, Column);

    public enum ObjectLocation
    {
        Local,
        Mint,
        Extern
    }
}
