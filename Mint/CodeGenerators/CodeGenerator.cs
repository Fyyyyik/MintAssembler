using Mint.AstNodes;
using System;
using System.Collections.Generic;
using System.Text;
using KirbyLib.Mint;
using OneOf;
using Mint.Semantics;

namespace Mint.CodeGenerators
{
    public abstract class CodeGenerator
    {
        private SemanticResult _semantic;
        private List<byte> _sdata = new();
        private List<string> _xrefs = new();
        private string _currentObj;

        public CodeGenerator(SemanticResult semantic)
        {
            _semantic = semantic;
        }

        public ModuleRtDL GenerateRtDL(ModuleNode module)
        {
            ModuleRtDL rtdl = new();

            foreach (ObjectNode obj in module.Objects)
                rtdl.Objects.Add(GenerateObject(obj));
            
            rtdl.SData = _sdata;
            rtdl.XRef = _xrefs;
            return rtdl;
        }

        private MintObject GenerateObject(ObjectNode obj)
        {
            return obj switch
            {
                ClassNode cls => GenerateClass(cls),

                _ => throw new CodeGeneratorException("Unhandled object type.", obj)
            };
        }

        private MintObject GenerateClass(ClassNode cls)
        {
            MintObject mintObj = new()
            {
                Name = cls.FullName,
                Type = ObjectType.Class
            };

            foreach (MemberNode member in cls.Members)
            {
                switch (member)
                {
                    case VariableNode varNode:
                        mintObj.Variables.Add(new MintVariable(varNode.Type.Name, varNode.Name));
                        break;
                    case FunctionNode funcNode:
                        mintObj.Functions.Add(GenerateFunction(funcNode));
                        break;
                }
            }
        }

        private MintFunction GenerateFunction(FunctionNode funcNode)
        {
            MintFunction mintFunc = new(funcNode.Name)
            {
                Arguments = (uint)funcNode.Params.Count,
                Data = GenerateBlock(funcNode.Body)
            };
        }

        private byte[] GenerateBlock(BlockNode block)
        {
            List<byte> data = new();
            _semantic.Module.C
        }
    }
}
