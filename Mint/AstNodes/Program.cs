using KirbyLib.Mint;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.AstNodes
{
    public record ModuleNode(
        string FullName,
        List<ObjectBaseNode> Objects, // local and xrefs!
        int Line,
        int Column
    ) : AstNode(Line, Column);

    // Only the objects with Location set to Local get compiled
    // the rest are xrefs given to the compiler for context.
    public abstract record ObjectBaseNode(
        string Name,
        ObjectLocation Location,
        int Line,
        int Column
    ) : AstNode(Line, Column);

    public record ObjectNode(
        string Name, // full name with namespaces for xrefs
        List<MemberNode> Members,
        ObjectLocation Location,
        ObjectType ObjType,
        int Line,
        int Column
    ) : ObjectBaseNode(Name, Location, Line, Column);

    public record EnumNode(
        string Name,
        List<MintEnum> Elements,
        ObjectLocation Location,
        int Line,
        int Column
    ) : ObjectBaseNode(Name, Location, Line, Column);

    public enum ObjectLocation
    {
        Local, // signifies that the compiler should compile it as it is in the module
        Mint, // signifies that it is in mint but not in the same module
        Extern // signifies that it is native to the executable and therefore cannot be found in mint
    }
}
