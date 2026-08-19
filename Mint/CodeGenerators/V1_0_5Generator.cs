using KirbyLib.Mint;
using KirbyLib.Crypto;
using KirbyLib.IO;
using Mint.AstNodes;
using Mint.Semantics;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.CodeGenerators
{
    public class V1_0_5Generator : V0_2Generator
    {
        protected override byte[] Version => [1, 0, 5, 0];

        public V1_0_5Generator(SemanticResult semantic) : base(semantic) { }

        public Module Generate(ModuleNode module)
        {
            Module mintModule = new()
            {
                Name = module.FullName,
                Format = ModuleFormat.TDX
            };
            mintModule.XData.Endianness = Endianness.Little;
            mintModule.XData.Version = [2, 0];

            foreach (ObjectBaseNode objBase in module.Objects)
                if (objBase.Location == ObjectLocation.Local)
                    mintModule.Objects.Add(GenerateObjectBase(objBase));

            mintModule.SData = new(_sdata);

            mintModule.XRef = new();
            foreach (string xref in _xrefs)
                mintModule.XRef.Add(Crc32C.CalculateInv(xref));

            return mintModule;
        }

        protected override MintObject GenerateObjectBase(ObjectBaseNode objBase) => objBase switch
        {
            EnumNode enumNode => GenerateEnum(enumNode),

            _ => base.GenerateObjectBase(objBase)
        };

        protected override MintObject GenerateObject(ObjectNode obj)
        {
            _currentObj = _semantic.Module.LocalObjects[obj.Name];

            MintObject mintObj = new()
            {
                Name = _currentObj.FullName,
                Type = obj.ObjType
            };

            List<VariableNode> varList = new();
            foreach (MemberNode member in obj.Members)
                switch (member)
                {
                    case VariableNode varNode:
                        varList.Add(varNode); // process those later
                        mintObj.Variables.Add(new MintVariable(varNode.Type.GetTypeName(), varNode.Name));
                        break;
                    case FunctionNode funcNode:
                        mintObj.Functions.Add(GenerateFunction(funcNode));
                        break;
                }

            mintObj.Functions.Add(GenerateVarInit(varList.ToArray()));

            _currentObj = null;
            return mintObj;
        }

        protected override Instruction BasicFEnter => new Instruction(GetOpcode("fenter"), _registers.RegisterCount, 0, 0);

        protected override CodeWriter.CodeResult GeneratePushInstanceVarInit(PushInstanceNode pushInstance, string varXRef)
        {
            CodeWriter writer = new();

            if (pushInstance.CtArgs == null) return writer.Result;

            byte instReg = _registers.AllocateRegister();
            ushort v = (ushort)AddOrGetXRef(varXRef);
            (byte, byte) vBytes = CodeWriter.ToBytes(v);
            writer.Instructions.Add(new Instruction(GetOpcode("ldsrsv"), instReg, vBytes.Item1, vBytes.Item2));

            List<byte> regs = new();
            foreach (ExprNode arg in pushInstance.CtArgs)
            {
                regs.Add(_registers.AllocateRegister());
                writer.Append(GenerateExpr(arg, regs[^1]));
            }

            writer.Append(GenerateParamLoading(regs.ToArray(), instReg));

            v = (ushort)AddOrGetXRef($"{pushInstance.ObjectName}.{GetFuncSymbol(pushInstance).GetSignature()}");
            vBytes = CodeWriter.ToBytes(v);
            writer.Instructions.Add(new Instruction(GetCallOpcode(
                _semantic.Module.GetInstanciable(pushInstance.ObjectName).GetLoc(),
                false
            ), 0xFF, vBytes.Item1, vBytes.Item2));

            foreach (byte reg in regs) writer.Append(GenerateFreeRegister(reg));
            _registers.FreeRegister(instReg);

            return writer.Result;
        }

        protected MintObject GenerateEnum(EnumNode enumNode) => new()
        {
            Name = _semantic.Module.Enums[enumNode.Name].Name,
            Flags = 0,
            Type = ObjectType.Enum,
            Enums = new(enumNode.Elements)
        };

        protected override CodeWriter.CodeResult GenerateFunctionEnter()
        {
            if (_currentFunction == null)
                throw new CodeGeneratorException( // This is mainly to shut up C#, this shouldn't ever happen
                    "Tried to generate a function entrance outside of a function.",
                    0, 0
                );

            byte argCount = (byte)_currentFunction.Parameters.Count;

            OpcodeHelper.FEnterFlags flags = OpcodeHelper.FEnterFlags.None;
            if (_currentFunction.HasThis) flags |= OpcodeHelper.FEnterFlags.Member;
            if (_currentFunction.GetReturnType() != null) flags |= OpcodeHelper.FEnterFlags.Return;

            CodeWriter writer = new();
            writer.Instructions.Add(new Instruction(GetOpcode("fenter"), _registers.RegisterCount, argCount, (byte)flags));
            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateParamLoading(byte[] paramRegs, byte? memberReg = null)
        {
            CodeWriter writer = new();

            int nextFrIdx = 0;
            if (memberReg.HasValue)
            {
                byte memRegVal = memberReg.Value;

                if (paramRegs.Length >= 2)
                {
                    writer.Instructions.Add(new Instruction(GetOpcode("ldfs3f"), memRegVal, paramRegs[0], paramRegs[1]));
                    nextFrIdx = 2;
                }
                else if (paramRegs.Length == 1)
                {
                    writer.Instructions.Add(new Instruction(GetOpcode("ldfs2"), 15, memRegVal, paramRegs[0]));
                    return writer.Result;
                }
                else
                {
                    writer.Instructions.Add(new Instruction(GetOpcode("ldfrsr"), 15, memRegVal));
                    return writer.Result;
                }
            }

            while (nextFrIdx < paramRegs.Length)
            {
                if (nextFrIdx + 1 < paramRegs.Length)
                {
                    writer.Instructions.Add(new Instruction(GetOpcode("ldfs2"), (byte)nextFrIdx, paramRegs[nextFrIdx], paramRegs[nextFrIdx + 1]));
                    nextFrIdx += 2;
                }
                else
                {
                    writer.Instructions.Add(new Instruction(GetOpcode("ldfrsr"), (byte)nextFrIdx, paramRegs[nextFrIdx]));
                    nextFrIdx++;
                }
            }

            return writer.Result;
        }

        protected virtual byte GetCallOpcode(ObjectLocation loc, bool isFunc) => loc switch
        {
            ObjectLocation.Local or ObjectLocation.Mint => GetOpcode("call"),
            ObjectLocation.Extern when isFunc => GetOpcode("callnv"),
            ObjectLocation.Extern when !isFunc => GetOpcode("callnt"),

            _ => GetOpcode("call") // shouldn't ever happen but just in case
        };
    }
}
