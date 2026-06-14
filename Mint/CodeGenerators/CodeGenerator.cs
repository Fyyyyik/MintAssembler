using Mint.AstNodes;
using System;
using System.Collections.Generic;
using System.Text;
using KirbyLib.Mint;
using OneOf;
using Mint.Semantics;
using System.Runtime.InteropServices.Marshalling;

namespace Mint.CodeGenerators
{
    public abstract class CodeGenerator
    {
        protected virtual int InstructionSize => 4;

        private readonly SemanticResult _semantic;
        private readonly List<byte> _sdata = new();
        private readonly List<string> _xrefs = new();
        private string _currentObj;
        private string _currentFunction;
        private RegisterManager _registers;
        private HashSet<byte> _arrayRegs = new();
        private Dictionary<byte, string> _instanceRegs = new();
        private readonly byte[] _version;

        public CodeGenerator(SemanticResult semantic, byte[] version)
        {
            _semantic = semantic;
            _version = version;

            if (!OpcodeHelper.CommonOpcodeByName.ContainsKey(_version))
                throw new NotImplementedException($"Unknown Mint version {_version}");
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
            _currentObj = obj.Name;

            MintObject mintObj = new()
            {
                Name = obj.Name,
                Type = ObjectType.Class // TODO : make obj carry the type over
            };

            foreach (MemberNode member in obj.Members)
                switch (member)
                {
                    case VariableNode varNode:
                        mintObj.Variables.Add(new MintVariable(varNode.Type.Name, varNode.Name));
                        break;
                    case FunctionNode funcNode:
                        mintObj.Functions.Add(GenerateFunction(funcNode));
                        break;
                }

            _currentObj = string.Empty;
            return mintObj;
        }

        private MintFunction GenerateFunction(FunctionNode funcNode)
        {
            _currentFunction = funcNode.Name;
            _registers = new();

            MintFunction mintFunc = new(funcNode.Name)
            {
                Arguments = (uint)funcNode.Params.Count,
                Data = GenerateBlock(funcNode.Body, true)
            };

            _currentFunction = string.Empty;
            return mintFunc;
        }

        private byte[] GenerateBlock(BlockNode block, bool isBeginning = false)
        {
            _registers.PushNewBlock();
            List<byte> data = new();
            foreach (StmtNode stmt in block.Statements)
                data.AddRange(GenerateStatement(stmt));
            return data.ToArray();
        }

        private byte[] GenerateStatement(StmtNode statement)
        {
            return statement switch
            {
                VarDeclNode vd => GenerateVarDecl(vd),

                _ => throw new NotImplementedException("Unknown statement type.")
            };
        }

        protected byte[] GenerateVarDecl(VarDeclNode varDecl)
        {
            byte destReg = _registers.AllocateRegister(varDecl.Name);
            return GenerateExpr(varDecl.Initializer, destReg);
        }

        protected byte[] GenerateAssign(AssignNode assign)
        {
            switch (assign.Target)
            {
                case IdentifierNode ident:
                    return GenerateExpr(assign.Value, _registers.VarToReg[ident.Name]);

                case ArrayAccessNode aa:
                    List<byte> aaData = new();
                    byte arrReg = _registers.AllocateRegister(),
                         cpyReg = _registers.AllocateRegister(),
                         idxReg = _registers.AllocateRegister(),
                         valReg = _registers.AllocateRegister();
                    aaData.AddRange(GenerateExpr(aa.Array, arrReg));
                    aaData.AddRange(GenerateExpr(aa.Index, idxReg));
                    aaData.AddRange(GenerateExpr(assign.Value, valReg));
                    aaData.AddRange([OpcodeHelper.CommonOpcodeByName[_version]["ldsrsr"], cpyReg, arrReg, 0xFF]);
                    aaData.AddRange([OpcodeHelper.CommonOpcodeByName[_version]["arirx"], cpyReg, idxReg, 0xFF]);
                    aaData.AddRange([OpcodeHelper.CommonOpcodeByName[_version]["stsrsr"], cpyReg, valReg, 0xFF]);
                    // We don't free arrReg, it will get cleaned up when we leave the block or we encounter a return
                    _registers.FreeRegister(cpyReg);
                    _registers.FreeRegister(idxReg);
                    _registers.FreeRegister(valReg);
                    return aaData.ToArray();

                case MemberAccessNode ma:
                    return;

                case QualifiedAccessNode qa:
                    return;

                default:
                    throw new CodeGeneratorException($"Unknown assign target node : {assign.Target}", assign.Line, assign.Column);
            }
        }

        protected byte[] GenerateIf(IfNode ifNode)
        {
            List<byte> data = new();
            byte condReg = _registers.AllocateRegister();
            data.AddRange(GenerateExpr(ifNode.Condition, condReg));
            int jumpInstructionIndex = data.Count;
            data.AddRange([OpcodeHelper.CommonOpcodeByName[_version]["jmpneg"], condReg, 0x00, 0x00]); // temporary v
            byte[] thenBlock = GenerateBlock(ifNode.Then);
            data.AddRange(thenBlock);
            short jumpLength = (short)(thenBlock.Length / InstructionSize + 1);

            if (ifNode.ElseIf != null || ifNode.Else != null)
            {
                jumpLength++; // skip the additional jump that jumps over all the elses
                int elseJumpInstructionIndex = data.Count;
                data.AddRange([OpcodeHelper.CommonOpcodeByName[_version]["jmp"], 0xFF, 0x00, 0x00]); // temporary v, again

                byte[] els = [];
                if (ifNode.ElseIf != null)
                    els = GenerateIf(ifNode.ElseIf);
                else if (ifNode.Else != null)
                    els = GenerateBlock(ifNode.Else);
                data.AddRange(els);
                short elsJumpLength = (short)(els.Length / InstructionSize + 1);
                data[elseJumpInstructionIndex + 2] = (byte)((elsJumpLength >> 8) & 0xFF);
                data[elseJumpInstructionIndex + 3] = (byte)(elsJumpLength & 0xFF);
            }

            data[jumpInstructionIndex + 2] = (byte)((jumpLength >> 8) & 0xFF);
            data[jumpInstructionIndex + 3] = (byte)(jumpLength & 0xFF);
            
            return data.ToArray();
        }

        protected byte[] GenerateWhile(WhileNode whileNode)
        {
            List<byte> data = new();
            byte condReg = _registers.AllocateRegister();
            data.AddRange(GenerateExpr(whileNode.Condition, condReg));
            int jmpnegInstructionPos = data.Count;
            data.AddRange([OpcodeHelper.CommonOpcodeByName[_version]["jmpneg"], condReg, 0x00, 0x00]);
            _registers.FreeRegister(condReg);

            byte[] whileBody = GenerateBlock(whileNode.Body);
            data.AddRange(whileBody);

            short endJmpLength = (short)(-data.Count / InstructionSize);
            data.AddRange([
                OpcodeHelper.CommonOpcodeByName[_version]["jmp"],
                0xFF,
                (byte)((endJmpLength >> 8) & 0xFF),
                (byte)(endJmpLength & 0xFF)
            ]);

            short jmpnegLength = (short)(whileBody.Length / InstructionSize + 2);
            data[jmpnegInstructionPos + 2] = (byte)((jmpnegLength >> 8) & 0xFF);
            data[jmpnegInstructionPos + 3] = (byte)(jmpnegLength & 0xFF);

            return data.ToArray();
        }

        protected byte[] GenerateExpr(ExprNode expr, byte? destRegister = null)
        {
            switch (expr)
            {

            }
        }

        protected byte[] GenerateArrayCreation(ArrayCreationNode arrayCreation, byte? destRegister)
        {

        }
    }
}
