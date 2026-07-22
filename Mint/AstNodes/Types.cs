using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.AstNodes
{
    public interface ITypeNode
    {
        public string GetTypeName();
        public TypeNode GetBaseType();
        public ITypeNode[] ToArray();
        public ITypeNode GetChildType();

        public ITypeNode GetDereference();
        public ITypeNode GetArrayAccess();

        public int GetArraySize();

        public bool IsRef();
        public bool IsConst();
        public bool IsArray();
    }

    public record TypeNode(string Name, int Line, int Column) : AstNode(Line, Column), ITypeNode
    {
        public string GetTypeName() => Name;
        public TypeNode GetBaseType() => this;
        public ITypeNode[] ToArray() => [this];
        public ITypeNode GetChildType() => this;

        public ITypeNode GetDereference() => throw new InvalidOperationException("Tried to get dereference type of non-reference type.");
        public ITypeNode GetArrayAccess() => throw new InvalidOperationException("Tried to get array access type from non-array type.");

        public int GetArraySize() => throw new InvalidOperationException("Tried to get size of non-array type.");

        public bool IsRef() => false;
        public bool IsConst() => false;
        public bool IsArray() => false;
    }

    public record RefTypeNode(ITypeNode Type, int Line, int Column) : AstNode(Line, Column), ITypeNode
    {
        public string GetTypeName() => $"ref {Type.GetTypeName()}";
        public TypeNode GetBaseType() => Type.GetBaseType();
        public ITypeNode[] ToArray()
        {
            List<ITypeNode> types = new() { this };
            types.AddRange(Type.ToArray());
            return types.ToArray();
        }
        public ITypeNode GetChildType() => Type;

        public ITypeNode GetDereference() => Type;
        public ITypeNode GetArrayAccess() => throw new InvalidOperationException("Tried to get array access type from non-array type.");

        public int GetArraySize() => throw new InvalidOperationException("Tried to get size of non-array type.");

        public bool IsRef() => true;
        public bool IsConst() => false;
        public bool IsArray() => false;
    }

    public record ConstTypeNode(ITypeNode Type, int Line, int Column) : AstNode(Line, Column), ITypeNode
    {
        public string GetTypeName() => $"const {Type.GetTypeName()}";
        public TypeNode GetBaseType() => Type.GetBaseType();
        public ITypeNode[] ToArray()
        {
            List<ITypeNode> types = new() { this };
            types.AddRange(Type.ToArray());
            return types.ToArray();
        }
        public ITypeNode GetChildType() => Type;

        public ITypeNode GetDereference() => Type.GetDereference();
        public ITypeNode GetArrayAccess() => Type.GetArrayAccess();

        public int GetArraySize() => Type.GetArraySize();

        public bool IsRef() => Type.IsRef();
        public bool IsConst() => true;
        public bool IsArray() => Type.IsArray();
    }

    public record ArrayTypeNode(ITypeNode Type, int Size, int Line, int Column) : AstNode(Line, Column), ITypeNode
    {
        public string GetTypeName() => $"{Type.GetTypeName()}[{Size}]";
        public TypeNode GetBaseType() => Type.GetBaseType();
        public ITypeNode[] ToArray()
        {
            List<ITypeNode> types = new() { this };
            types.AddRange(Type.ToArray());
            return types.ToArray();
        }
        public ITypeNode GetChildType() => Type;

        public ITypeNode GetDereference() => throw new InvalidOperationException("Tried to get dereference type of non-reference type.");
        public ITypeNode GetArrayAccess() => Type;

        public int GetArraySize() => Size;

        public bool IsRef() => false;
        public bool IsConst() => false;
        public bool IsArray() => true;
    }
}
