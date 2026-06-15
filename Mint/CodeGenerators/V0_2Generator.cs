using KirbyLib.Mint;
using Mint.AstNodes;
using Mint.Semantics;
using OneOf;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
using System.Xml.Linq;

namespace Mint.CodeGenerators
{
    // Each version of the code generator inherits from the last version.
    // 0.2 being the first mint version we know of this code generator also
    // acts as a base and defines all the basic parameters needed everywhere.
    public class V0_2Generator
    {
        protected virtual int InstructionSize => 4;
        protected virtual byte[] Version => [0, 2, 0, 0]; // must override for each version

        protected RegisterManager _registers;
        protected readonly SemanticResult _semantic;

        private readonly List<byte> _sdata = new();
        private readonly List<string> _xrefs = new();
        private string _currentObj;
        private string _currentFunction;
        private HashSet<byte> _arrayRegs = new();
        private Dictionary<byte, string> _instanceRegs = new();

        public V0_2Generator(SemanticResult semantic) => _semantic = semantic;

        public ModuleRtDL GenerateRtDL(ModuleNode module)
        {
            ModuleRtDL rtdl = new();

            foreach (ObjectNode obj in module.Objects)
                rtdl.Objects.Add(GenerateObject(obj));
            
            rtdl.SData = _sdata;
            rtdl.XRef = _xrefs;
            return rtdl;
        }

        public Module Generate(ModuleNode module)
        {
            Module mintModule = new();

            foreach (ObjectNode)
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

        // TODO : override in 7.X (no more mint array)
        protected byte[] GenerateAssign(AssignNode assign)
        {
            // Every kind of assign is separated for easy overrides in specific cases
            return assign.Target switch
            {
                IdentifierNode ident => GenerateExpr(assign.Value, _registers.VarToReg[ident.Name]),
                ArrayAccessNode aa => GenerateArrayAssign(assign, aa),
                MemberAccessNode ma => GenerateMemberAssign(assign, ma),
                QualifiedAccessNode qa => GenerateQualifiedAssign(assign, qa),

                _ => throw new CodeGeneratorException($"Unknown assign target node : {assign.Target}", assign.Line, assign.Column)
            };
        }

        protected byte[] GenerateArrayAssign(AssignNode assign, ArrayAccessNode targetArray)
        {
            List<byte> data = new();
            byte arrReg = _registers.AllocateRegister(),
                 cpyReg = _registers.AllocateRegister(),
                 idxReg = _registers.AllocateRegister(),
                 valReg = _registers.AllocateRegister();
            data.AddRange(GenerateExpr(targetArray.Array, arrReg));
            data.AddRange(GenerateExpr(targetArray.Index, idxReg));
            data.AddRange(GenerateExpr(assign.Value, valReg));
            data.AddRange([GetOpcode("ldsrsr"), cpyReg, arrReg, 0xFF]);
            data.AddRange([GetOpcode("arirx"), cpyReg, idxReg, 0xFF]);
            data.AddRange([GetOpcode("stsrsr"), cpyReg, valReg, 0xFF]);
            if (targetArray.Array is ArrayCreationNode)
                data.AddRange([GetOpcode("arpop"), arrReg, 0xFF, 0xFF]);
            _registers.FreeRegister(arrReg);
            _registers.FreeRegister(cpyReg);
            _registers.FreeRegister(idxReg);
            _registers.FreeRegister(valReg);
            return data.ToArray();
        }

        protected byte[] GenerateMemberAssign(AssignNode assign, MemberAccessNode targetMember)
        {
            List<byte> data = new();

            byte objReg = _registers.AllocateRegister();
            data.AddRange(GenerateExpr(targetMember.Object, objReg));
            byte valReg = _registers.AllocateRegister();
            data.AddRange(GenerateExpr(assign.Value, valReg));

            string xref = $"{_semantic.ExprTypes[targetMember.Object].Name}.{targetMember.Member}";
            ushort v = (ushort)AddOrGetXRef(xref);
            data.AddRange([GetOpcode("addofs"), objReg, (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF)]);
            data.AddRange([GetOpcode("stsrsr"), objReg, valReg]);

            _registers.FreeRegister(objReg);
            _registers.FreeRegister(valReg);
            return data.ToArray();
        }

        protected byte[] GenerateQualifiedAssign(AssignNode assign, QualifiedAccessNode targetQualified)
        {

        }

        protected byte[] GenerateIf(IfNode ifNode)
        {
            List<byte> data = new();
            byte condReg = _registers.AllocateRegister();
            data.AddRange(GenerateExpr(ifNode.Condition, condReg));
            int jumpInstructionIndex = data.Count;
            data.AddRange([GetOpcode("jmpneg"), condReg, 0x00, 0x00]); // temporary v
            byte[] thenBlock = GenerateBlock(ifNode.Then);
            data.AddRange(thenBlock);
            short jumpLength = (short)(thenBlock.Length / InstructionSize + 1);

            if (ifNode.ElseIf != null || ifNode.Else != null)
            {
                jumpLength++; // skip the additional jump that jumps over all the elses
                int elseJumpInstructionIndex = data.Count;
                data.AddRange([GetOpcode("jmp"), 0xFF, 0x00, 0x00]); // temporary v, again

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
            data.AddRange([GetOpcode("jmpneg"), condReg, 0x00, 0x00]);
            _registers.FreeRegister(condReg);

            byte[] whileBody = GenerateBlock(whileNode.Body);
            data.AddRange(whileBody);

            short endJmpLength = (short)(-data.Count / InstructionSize);
            data.AddRange([
                GetOpcode("jmp"),
                0xFF,
                (byte)((endJmpLength >> 8) & 0xFF),
                (byte)(endJmpLength & 0xFF)
            ]);

            short jmpnegLength = (short)(whileBody.Length / InstructionSize + 2);
            data[jmpnegInstructionPos + 2] = (byte)((jmpnegLength >> 8) & 0xFF);
            data[jmpnegInstructionPos + 3] = (byte)(jmpnegLength & 0xFF);

            return data.ToArray();
        }

        protected byte[] GenerateExpr(ExprNode expr, byte destRegister)
        {
            switch (expr)
            {

            }
        }

        protected byte[] GenerateIntLiteral(IntLiteralNode intLiteral, byte destRegister)
        {
            ushort v = (ushort)_sdata.Count;
            _sdata.AddRange(GetBytesFromInt(intLiteral.Value));
            return [
                GetOpcode("ldsrc4"),
                destRegister,
                (byte)((v >> 8) & 0xFF),
                (byte)(v & 0xFF)
            ];
        }

        // TODO : override in 7.0.2 to use the dedicated opcode
        protected byte[] GenerateFloatLiteral(FloatLiteralNode floatLiteral, byte destRegister)
        {
            ushort v = (ushort)_sdata.Count;
            _sdata.AddRange(GetBytesFromInt(BitConverter.SingleToInt32Bits(floatLiteral.Value)));
            return [
                GetOpcode("ldsrc4"),
                destRegister,
                (byte)((v >> 8) & 0xFF),
                (byte)(v & 0xFF)
            ];
        }

        // TODO : override in versions starting with 7.0.2 because they removed my goat ldsrbt
        protected byte[] GenerateBoolLiteral(BoolLiteralNode boolLiteral, byte destRegister)
        {
            return [
                GetOpcode(boolLiteral.Value ? "ldsrbt" : "ldsrzr"),
                destRegister,
                0xFF,
                0xFF
            ];
        }

        protected byte[] GenerateStringLiteral(StringLiteralNode stringLiteral, byte destRegister)
        {
            short v = (short)_sdata.Count;
            _sdata.AddRange(Encoding.UTF8.GetBytes(stringLiteral.Value + '\0'));
            return [
                GetOpcode("ldsrca"),
                destRegister,
                (byte)((v >> 8) & 0xFF),
                (byte)(v & 0xFF)
            ];
        }

        protected byte[] GenerateIdentifier(IdentifierNode identifier, byte destRegister)
        {
            // Either that's a local variable or we're trying to push an instance of something
            if (_registers.VarToReg.TryGetValue(identifier.Name, out byte reg))
                return [
                    GetOpcode("ldsrsr"),
                    destRegister,
                    reg,
                    0xFF
                ];

            List<byte> data = new();
            string name = identifier.Name;

            // If what we're referencing is in the same module, add the whole namespace to the path
            if (_semantic.Module.Objects.ContainsKey(identifier.Name))
                name = $"{_semantic.Module.Namespace}.{identifier.Name}";

            // Generate the code
            ushort v = (ushort)AddOrGetXRef(name);
            return [
                GetOpcode("sppshz"),
                destRegister,
                (byte)((v >> 8) & 0xFF),
                (byte)(v & 0xFF)
            ];
        }

        protected abstract byte[] GenerateQualifiedAccess(QualifiedAccessNode qualifiedAccess, byte destRegister);

        protected byte[] GenerateArrayCreation(ArrayCreationNode arrayCreation, byte? destRegister)
        {

        }

        // Everything below are various utility functions

        // returns the index in _xrefs of the xref
        protected int AddOrGetXRef(string xref)
        {
            int index = _xrefs.IndexOf(xref);
            if (index == -1)
            {
                index = _xrefs.Count;
                _xrefs.Add(xref);
            }
            return index;
        }

        protected byte GetOpcode(string name) => OpcodeHelper.CommonOpcodeByName[Version][name];

        private static byte[] GetBytesFromInt(int bits)
        {
            return [
                (byte)((bits >> 24) & 0xFF),
                (byte)((bits >> 16) & 0xFF),
                (byte)((bits >> 8) & 0xFF),
                (byte)(bits & 0xFF)
            ];
        }
    }
}
