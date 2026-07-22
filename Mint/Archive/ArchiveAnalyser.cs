using KirbyLib.Mint;
using Mint.AstNodes;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Mint.Archive
{
    public class ArchiveAnalyser
    {
        private ModuleNode _moduleNode;

        public ArchiveAnalyser(ModuleNode module) => _moduleNode = module;

        public void Analyse(ArchiveRtDL archive)
        {
            foreach (ModuleRtDL module in archive.Modules)
                AnalyseModule(module);
        }

        public void Analyse(KirbyLib.Mint.Archive archive)
        {
            foreach (Module module in archive.Modules)
                AnalyseModule(module);
        }

        private void AnalyseModule(ModuleRtDL module)
        {
            if (module.Name == _moduleNode.FullName) return;
            foreach (MintObject obj in module.Objects)
                AnalyseObject(obj);
        }

        private void AnalyseModule(Module module)
        {
            if (module.Name == _moduleNode.FullName) return;
            foreach (MintObject obj in module.Objects)
                AnalyseObject(obj);
        }

        private void AnalyseObject(MintObject obj)
        {
            ObjectNode? objNode;
            if (!TryGetObjectNode(obj, out objNode))
            {
                objNode = new(obj.Name, new List<MemberNode>(), ObjectLocation.Mint, obj.Type, 0, 0);
                _moduleNode.Objects.Add(objNode);
            }

            foreach (MintVariable mintVar in obj.Variables)
            {
                bool exists = false;
                foreach (MemberNode member in objNode.Members)
                    if (member is VariableNode varNode && varNode.Name == varNode.Name)
                    {
                        exists = true;
                        break;
                    }
                if (exists) continue; // trust the user, then the archive

                ITypeNode? type = AnalyseType(mintVar.Type);
                if (type == null)
                    throw new ArchiveException($"Variable {mintVar.Name} doesn't have a type ('void') in Mint object {obj.Name}.");
                objNode.Members.Add(new VariableNode(type, mintVar.Name, 0, 0));
            }

            foreach (MintFunction mintFunc in obj.Functions)
            {
                bool exists = false;
                foreach (MemberNode member in objNode.Members)
                    if (member is ExternalFunctionNode funcNode && funcNode.GetNameWithArgs() == mintFunc.NameWithoutType())
                    {
                        exists = true;
                        break;
                    }
                if (exists) continue;

                List<ITypeNode> paramTypes = new();
                string[] arguments = mintFunc.Name[(mintFunc.Name.LastIndexOf('(') + 1)..mintFunc.Name.LastIndexOf(')')].Split(',');
                if (arguments.Length == 1 && arguments[0] == "") arguments = Array.Empty<string>();
                foreach (string arg in arguments)
                {
                    ITypeNode? argType = AnalyseType(arg);
                    if (argType == null)
                        throw new ArchiveException($"Argument of function {mintFunc.GetShortName()} cannot be 'void'.");
                    paramTypes.Add(argType);
                }

                objNode.Members.Add(new ExternalFunctionNode(
                    AnalyseType(mintFunc.Name),
                    mintFunc.GetShortName(),
                    mintFunc.Name.EndsWith("const"),
                    paramTypes,
                    0, 0
                ));
            }
        }

        private static ITypeNode? AnalyseType(string type)
        {
            List<Token> tokens = new Lexer(type).Tokenize();
            return new Parser(tokens).ParseType();
        }

        private bool TryGetObjectNode(MintObject obj, [NotNullWhen(true)] out ObjectNode? node)
        {
            foreach (ObjectNode objNode in _moduleNode.Objects)
            {
                if (objNode.Location == ObjectLocation.Local) continue; // might be weird so ignore

                if (objNode.Name == obj.Name)
                {
                    node = objNode;
                    return true;
                }
            }

            node = null;
            return false;
        }
    }
}
