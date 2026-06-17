using KirbyLib.Mint;
using Mint.AstNodes;
using Mint.Semantics;
using OneOf;
using System;
using System.Collections.Generic;
using System.Data;
using System.Runtime.CompilerServices;
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

        protected RegisterManager _registers = new();
        protected readonly SemanticResult _semantic;

        private readonly List<byte> _sdata = new();
        private readonly List<string> _xrefs = new();
        private ObjectSymbol? _currentObj = null;
        private FunctionSymbol? _currentFunction = null;
        private HashSet<byte> _arrayRegs = new();
        private Dictionary<byte, string> _instanceRegs = new();

        public V0_2Generator(SemanticResult semantic) => _semantic = semantic;

        public ModuleRtDL GenerateRtDL(ModuleNode module, string name)
        {
            ModuleRtDL rtdl = new()
            {
                Name = name
            };

            foreach (ObjectNode obj in module.Objects)
                rtdl.Objects.Add(GenerateObject(obj));
            
            rtdl.SData = _sdata;
            rtdl.XRef = _xrefs;
            return rtdl;
        }

        private MintObject GenerateObject(ObjectNode obj)
        {
            _currentObj = _semantic.Module.Objects[obj.Name];

            MintObject mintObj = new()
            {
                Name = _currentObj.FullName,
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

            _currentObj = null;
            return mintObj;
        }

        private MintFunction GenerateFunction(FunctionNode funcNode)
        {
            if (_currentObj == null)
                throw new CodeGeneratorException("Cannot generate function outside of an object.", 0, 0);
            _currentFunction = _currentObj.Functions[funcNode.Name];
            _registers = new();
            _registers.PushNewBlock();

            if (_currentFunction.HasThis)
                _registers.AllocateRegister(); // should be r0

            foreach (ParamNode param in _currentFunction.Parameters)
                _registers.AllocateRegister(param.Name);

            string retTypeName = _currentFunction.ReturnType == null ? "void" : _currentFunction.ReturnType.Name;
            MintFunction mintFunc = new($"{retTypeName} {AppendParamTypes(funcNode.Name, SemanticAnalyser.ToTypeNodes(_currentFunction.Parameters))}")
            {
                Arguments = (uint)funcNode.Params.Count,
                Data = GenerateBlock(funcNode.Body, true)
            };

            _currentFunction = null;
            return mintFunc;
        }

        private byte[] GenerateBlock(BlockNode block, bool isBeginning = false)
        {
            _registers.PushNewBlock();
            List<byte> data = new();
            foreach (StmtNode stmt in block.Statements)
                data.AddRange(GenerateStatement(stmt));
            data.AddRange(GenerateFreeBlockResources(_registers.ExitBlock()));
            if (isBeginning)
            {
                data.InsertRange(0, GenerateFunctionEnter());
                if (data[^4] != GetOpcode("fleave") && data[^4] != GetOpcode("fret"))
                    data.AddRange([GetOpcode("fleave"), 0xFF, 0xFF, 0xFF]);
            }
            return data.ToArray();
        }

        protected byte[] GenerateFunctionEnter()
        {
            if (_currentFunction == null)
                throw new CodeGeneratorException( // This is mainly to shut up C#, this shouldn't ever happen
                    "Tried to generate a function entrance outside of a function.",
                    0, 0
                );
            byte argCount = (byte)_currentFunction.Parameters.Count;
            if (_currentFunction.ReturnType != null) argCount++;
            if (_currentFunction.HasThis) argCount++;
            return [GetOpcode("fenter"), (byte)_registers.RegisterCount, argCount, 0xFF];
        }

        protected byte[] GenerateFreeBlockResources(HashSet<byte> freedRegisters)
        {
            List<byte> data = new();

            foreach (byte reg in freedRegisters)
            {
                if (_arrayRegs.Contains(reg))
                {
                    data.AddRange([GetOpcode("arpop"), reg, 0xFF, 0xFF]);
                    _arrayRegs.Remove(reg);
                }
                if (_instanceRegs.TryGetValue(reg, out string? obj))
                {
                    ushort v = (ushort)AddOrGetXRef(obj);
                    data.AddRange([GetOpcode("sppop"), reg, (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF)]);
                    _instanceRegs.Remove(reg);
                }
            }

            return data.ToArray();
        }

        private byte[] GenerateStatement(StmtNode statement)
        {
            return statement switch
            {
                VarDeclNode vd => GenerateVarDecl(vd),
                AssignNode ass => GenerateAssign(ass),
                IfNode ifNode => GenerateIf(ifNode),
                WhileNode whileNode => GenerateWhile(whileNode),
                ForNode forNode => GenerateFor(forNode),
                ReturnNode returnNode => GenerateReturn(returnNode),
                ExprStmtNode expr => GenerateExprStmt(expr),
                YieldNode yield => GenerateYield(yield),

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

            string xref = $"{_semantic.ExprTypes[targetMember.Object]?.Name}.{targetMember.Member}";
            ushort v = (ushort)AddOrGetXRef(xref);
            data.AddRange([GetOpcode("addofs"), objReg, (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF)]);
            data.AddRange([GetOpcode("stsrsr"), objReg, valReg, 0xFF]);

            _registers.FreeRegister(objReg);
            _registers.FreeRegister(valReg);
            return data.ToArray();
        }

        protected byte[] GenerateQualifiedAssign(AssignNode assign, QualifiedAccessNode targetQualified)
        {
            List<byte> data = new();

            byte valReg = _registers.AllocateRegister();
            data.AddRange(GenerateExpr(assign.Value, valReg));

            ushort v = (ushort)AddOrGetXRef(targetQualified.FullName);
            data.AddRange([GetOpcode("stsvsr"), valReg, (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF)]);

            _registers.FreeRegister(valReg);
            return data.ToArray();
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

        protected byte[] GenerateFor(ForNode forNode)
        {
            List<byte> data = new();

            data.AddRange(GenerateStatement(forNode.Initializer));

            int condPos = data.Count;
            byte condReg = _registers.AllocateRegister();
            data.AddRange(GenerateExpr(forNode.Condition, condReg));

            int condJmpPos = data.Count;
            data.AddRange([GetOpcode("jmpneg"), condReg, 0x00, 0x00]); // temporary v
            _registers.FreeRegister(condReg);

            byte[] bodyBlock = GenerateBlock(forNode.Body);
            data.AddRange(bodyBlock);

            byte[] incBlock = GenerateStatement(forNode.Increment);
            data.AddRange(incBlock);

            short bodyJmpV = (short)((condPos - data.Count) / InstructionSize);
            data.AddRange([GetOpcode("jmp"), 0xFF, (byte)((bodyJmpV >> 8) & 0xFF), (byte)(bodyJmpV & 0xFF)]);

            short condJmpV = (short)((data.Count - condJmpPos) / InstructionSize);
            data[condJmpPos + 2] = (byte)((condJmpV >> 8) & 0xFF);
            data[condJmpPos + 3] = (byte)(condJmpV & 0xFF);

            return data.ToArray();
        }

        protected byte[] GenerateReturn(ReturnNode returnNode)
        {
            if (returnNode.Value != null)
            {
                List<byte> data = new();

                byte valReg = _registers.AllocateRegister();
                data.AddRange(GenerateExpr(returnNode.Value, valReg));

                data.AddRange([GetOpcode("ldsrsr"), 0, valReg, 0xFF]);
                data.AddRange([GetOpcode("fret"), 0xFF, 0, 0xFF]);

                _registers.FreeRegister(valReg);
                return data.ToArray();
            }
            else return [GetOpcode("fleave"), 0xFF, 0xFF, 0xFF];
        }

        protected byte[] GenerateExprStmt(ExprStmtNode exprStmt)
        {
            byte scratch = _registers.AllocateRegister();
            byte[] data = GenerateExpr(exprStmt.Expr, scratch);
            _registers.FreeRegister(scratch);
            return data;
        }

        protected byte[] GenerateYield(YieldNode yieldNode)
        {
            List<byte> data = new();

            byte countReg = _registers.AllocateRegister();
            data.AddRange(GenerateExpr(yieldNode.FrameCount, countReg));

            data.AddRange([GetOpcode("yield"), countReg, 0xFF, 0xFF]);

            _registers.FreeRegister(countReg);
            return data.ToArray();
        }

        protected byte[] GenerateExpr(ExprNode expr, byte destRegister) => expr switch
        {
            IntLiteralNode intLit => GenerateIntLiteral(intLit, destRegister),
            FloatLiteralNode floatLit => GenerateFloatLiteral(floatLit, destRegister),
            BoolLiteralNode boolLit => GenerateBoolLiteral(boolLit, destRegister),
            StringLiteralNode stringLit => GenerateStringLiteral(stringLit, destRegister),
            IdentifierNode id => GenerateIdentifier(id, destRegister),
            QualifiedAccessNode qa => GenerateQualifiedAccess(qa, destRegister),
            MemberAccessNode ma => GenerateMemberAccess(ma, destRegister),
            ArrayAccessNode aa => GenerateArrayAccess(aa, destRegister),
            ThisNode => GenerateThis(destRegister),
            BinaryExprNode be => GenerateBinaryExpr(be, destRegister),
            UnaryExprNode ue => GenerateUnaryExpr(ue, destRegister),
            QualifiedCallNode qc => GenerateQualifiedCall(qc, destRegister),
            MemberCallNode mc => GenerateMemberCall(mc, destRegister),
            PushInstanceNode pi => GeneratePushInstance(pi, destRegister),
            ArrayCreationNode ac => GenerateArrayCreation(ac, destRegister),
            IncrementNode inc => GenerateIncrement(inc, destRegister),

            _ => throw new CodeGeneratorException($"Invalid expression node for this version : {expr}", expr.Line, expr.Column)
        };

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
            if (_registers.VarToReg.TryGetValue(identifier.Name, out byte reg))
                return [
                    GetOpcode("ldsrsr"),
                    destRegister,
                    reg,
                    0xFF
                ];
            throw new CodeGeneratorException($"Register for identifier '{identifier.Name}' not found.", identifier.Line, identifier.Column);
        }

        protected byte[] GenerateQualifiedAccess(QualifiedAccessNode qualifiedAccess, byte destRegister)
        {
            ushort v = (ushort)AddOrGetXRef(qualifiedAccess.FullName);
            return [GetOpcode("ldsrsv"), destRegister, (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF)];
        }

        protected byte[] GenerateMemberAccess(MemberAccessNode memberAccess, byte destRegister)
        {
            List<byte> data = new();

            byte objReg = _registers.AllocateRegister();
            data.AddRange(GenerateMemberSetup(memberAccess, objReg));
            data.AddRange([GetOpcode("ldsra4"), destRegister, objReg, 0xFF]);

            _registers.FreeRegister(objReg);
            return data.ToArray();
        }

        protected byte[] GenerateArrayAccess(ArrayAccessNode arrayAccess, byte destRegister)
        {
            List<byte> data = new();

            byte arrReg = _registers.AllocateRegister();
            data.AddRange(GenerateExpr(arrayAccess.Array, arrReg));

            byte idxReg = _registers.AllocateRegister();
            data.AddRange(GenerateExpr(arrayAccess.Index, idxReg));

            byte cpyReg = _registers.AllocateRegister();
            data.AddRange([GetOpcode("ldsrsr"), cpyReg, arrReg, 0xFF]);

            data.AddRange([GetOpcode("arirx"), cpyReg, idxReg, 0xFF]);
            data.AddRange([GetOpcode("ldsra4"), destRegister, cpyReg, 0xFF]);

            if (arrayAccess.Array is not ArrayCreationNode)
                _registers.FreeRegister(arrReg);
            _registers.FreeRegister(idxReg);
            _registers.FreeRegister(cpyReg);
            return data.ToArray();
        }

        protected byte[] GenerateThis(byte destRegister) => [GetOpcode("ldsrsr"), destRegister, 0, 0xFF];

        protected byte[] GenerateBinaryExpr(BinaryExprNode binaryExpr, byte destRegister)
        {
            List<byte> data = new();

            TypeNode? binType = _semantic.ExprTypes[binaryExpr];
            TypeNode? leftType = _semantic.ExprTypes[binaryExpr.Left];
            TypeNode? rightType = _semantic.ExprTypes[binaryExpr.Right];
            if (binType == null || leftType == null || rightType == null)
                throw new CodeGeneratorException("Cannot have a binary expression with type 'void'.", binaryExpr.Line, binaryExpr.Column);

            byte leftReg = _registers.AllocateRegister();
            data.AddRange(GenerateExpr(binaryExpr.Left, leftReg));

            byte rightReg = _registers.AllocateRegister();
            data.AddRange(GenerateExpr(binaryExpr.Right, rightReg));

            string opcode = binaryExpr.Op switch
            {
                "+" when AreBothType(leftType, rightType, "int") => "addi32",
                "+" when AreBothType(leftType, rightType, "float") => "addf32",
                "-" when AreBothType(leftType, rightType, "int") => "subi32",
                "-" when AreBothType(leftType, rightType, "float") => "subf32",
                "*" when AreBothType(leftType, rightType, "int") => "muls32",
                "*" when AreBothType(leftType, rightType, "float") => "mulf32",
                "/" when AreBothType(leftType, rightType, "int") => "divs32",
                "/" when AreBothType(leftType, rightType, "float") => "divf32",
                "%" when AreBothType(leftType, rightType, "int") => "mods32",
                "<" or ">" when AreBothType(leftType, rightType, "int") => "lts32",
                "<=" or ">=" when AreBothType(leftType, rightType, "int") => "les32",
                "==" when AreBothType(leftType, rightType, "int") => "eqi32",
                "!=" when AreBothType(leftType, rightType, "int") => "nei32",
                ">" or "<" when AreBothType(leftType, rightType, "float") => "ltf32",
                ">=" or "<=" when AreBothType(leftType, rightType, "float") => "lef32",
                "==" when AreBothType(leftType, rightType, "float") => "eqf32",
                "!=" when AreBothType(leftType, rightType, "float") => "nef32",
                "==" when AreBothType(leftType, rightType, "bool") => "eqbool",
                "!=" when AreBothType(leftType, rightType, "bool") => "nebool",
                "&" when AreBothType(leftType, rightType, "int") => "andi32",
                "|" when AreBothType(leftType, rightType, "int") => "ori32",
                "^" when AreBothType(leftType, rightType, "int") => "xori32",
                "<<" when AreBothType(leftType, rightType, "int") => "slli32",
                ">>" when AreBothType(leftType, rightType, "int") => "slri32",

                _ => throw new CodeGeneratorException($"Unknown binary operation with operator '{binaryExpr.Op}'.", binaryExpr.Line, binaryExpr.Column)
            };

            // Special case for comparing stuff
            bool invertOperands = false;
            if ((opcode is "lts32" or "les32" && binaryExpr.Op is ">" or ">=") ||
                (opcode is "ltf32" or "lef32" && binaryExpr.Op is "<" or "<="))
                invertOperands = true;

            data.AddRange([GetOpcode(opcode), destRegister, invertOperands ? rightReg : leftReg, invertOperands ? leftReg : rightReg]);
            _registers.FreeRegister(leftReg);
            _registers.FreeRegister(rightReg);
            return data.ToArray();
        }

        protected byte[] GenerateUnaryExpr(UnaryExprNode unaryExpr, byte destRegister)
        {
            List<byte> data = new();

            TypeNode? operandType = _semantic.ExprTypes[unaryExpr.Operand];
            if (operandType == null)
                throw new CodeGeneratorException(
                    "Operand must not be of type 'void' for unary expression.",
                    unaryExpr.Line,
                    unaryExpr.Column
                );

            byte operandReg = _registers.AllocateRegister();
            data.AddRange(GenerateExpr(unaryExpr.Operand, operandReg));

            string opcode = unaryExpr.Op switch
            {
                "-" when operandType.Name == "int" => "negs32",
                "-" when operandType.Name == "float" => "negf32",
                "!" when operandType.Name == "bool" => "ntbool",

                _ => throw new CodeGeneratorException($"Unknown unary operation with operator '{unaryExpr.Op}'.", unaryExpr.Line, unaryExpr.Column)
            };

            data.AddRange([GetOpcode(opcode), destRegister, operandReg, 0xFF]);
            _registers.FreeRegister(operandReg);
            return data.ToArray();
        }

        protected byte[] GenerateQualifiedCall(QualifiedCallNode qualifiedCall, byte destRegister)
        {
            List<byte> data = new();

            List<byte> regs = new();
            List<TypeNode> argTypes = new();
            foreach (ExprNode arg in qualifiedCall.Args)
            {
                regs.Add(_registers.AllocateRegister());
                data.AddRange(GenerateExpr(arg, regs[^1]));

                TypeNode? argType = _semantic.ExprTypes[arg];
                if (argType == null)
                    throw new CodeGeneratorException(
                        "Argument cannot be of type 'void'.",
                        qualifiedCall.Line,
                        qualifiedCall.Column
                    );
                argTypes.Add(argType);
            }

            bool isReturn = DoesFunctionReturn(qualifiedCall.FullName, argTypes);

            for (int i = 0; i < regs.Count; i++)
                data.AddRange([GetOpcode("ldfrsr"), (byte)(isReturn ? i + 1 : i), regs[i], 0xFF]);

            ushort v = (ushort)AddOrGetXRef(AppendParamTypes(qualifiedCall.FullName, argTypes));
            data.AddRange([GetOpcode("call"), 0xFF, (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF)]);

            if (isReturn)
                data.AddRange([GetOpcode("ldsrfz"), destRegister, 0xFF, 0xFF]);

            foreach (byte reg in regs)
                _registers.FreeRegister(reg);
            return data.ToArray();
        }

        protected byte[] GenerateMemberCall(MemberCallNode memberCall, byte destRegister)
        {
            List<byte> data = new();

            byte objReg = _registers.AllocateRegister();
            data.AddRange(GenerateExpr(memberCall.Object, objReg));

            List<byte> regs = new();
            List<TypeNode> argTypes = new();
            foreach (ExprNode arg in memberCall.Args)
            {
                regs.Add(_registers.AllocateRegister());
                data.AddRange(GenerateExpr(arg, regs[^1]));

                TypeNode? argType = _semantic.ExprTypes[arg];
                if (argType == null)
                    throw new CodeGeneratorException(
                        "Argument cannot be of type 'void'.",
                        memberCall.Line,
                        memberCall.Column
                    );
                argTypes.Add(argType);
            }

            TypeNode? objType = _semantic.ExprTypes[memberCall.Object];
            if (objType == null)
                throw new CodeGeneratorException(
                    "Cannot call from object of type 'void'.",
                    memberCall.Line,
                    memberCall.Column
                );
            string fullName = $"{objType.Name}.{memberCall.Name}";
            bool isReturn = DoesFunctionReturn(fullName, argTypes);

            data.AddRange([
                GetOpcode("ldfrsr"),
                (byte)(isReturn ? 1 : 0),
                (byte)(_currentFunction != null && _currentFunction.ReturnType != null ? 1 : 0),
                0xFF
            ]);
            for (int i = 0; i < regs.Count; i++)
                data.AddRange([GetOpcode("ldfrsr"), (byte)(isReturn ? i + 2 : i + 1), regs[i], 0xFF]);

            ushort v = (ushort)AddOrGetXRef(AppendParamTypes(fullName, argTypes));
            data.AddRange([GetOpcode("call"), 0xFF, (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF)]);

            if (isReturn)
                data.AddRange([GetOpcode("ldsrfz"), destRegister, 0xFF, 0xFF]);

            foreach (byte reg in regs)
                _registers.FreeRegister(reg);
            return data.ToArray();
        }

        protected byte[] GeneratePushInstance(PushInstanceNode pushInstance, byte destRegister)
        {
            ushort v = (ushort)AddOrGetXRef(pushInstance.ObjectName);
            _instanceRegs.Add(destRegister, pushInstance.ObjectName);
            return [GetOpcode("sppshz"), destRegister, (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF)];
        }

        protected byte[] GenerateArrayCreation(ArrayCreationNode arrayCreation, byte destRegister)
        {
            List<byte> data = new();

            if (arrayCreation.Size != null)
                data.AddRange(GenerateExpr(arrayCreation.Size, destRegister));
            else if (arrayCreation.Initializers != null)
                data.AddRange(GenerateIntLiteral(new IntLiteralNode(
                    arrayCreation.Initializers.Count,
                    arrayCreation.Line,
                    arrayCreation.Column
                ), destRegister));
            else
                // nothing that could indicate size given... welp guess you're getting an array with 0 elements!
                data.AddRange(GenerateIntLiteral(new IntLiteralNode(
                    0,
                    arrayCreation.Line,
                    arrayCreation.Column
                ), destRegister));
            data.AddRange([GetOpcode("arpshz"), destRegister, 0xFF, 0xFF]);
            if (arrayCreation.Initializers != null) // yes I'm checking that 2 times fight me
            {
                byte initReg = _registers.AllocateRegister();
                byte idxReg = _registers.AllocateRegister();
                data.AddRange(GenerateIntLiteral(new IntLiteralNode(
                    0,
                    arrayCreation.Line,
                    arrayCreation.Column
                ), idxReg));
                byte cpyReg = _registers.AllocateRegister();
                for (int i = 0; i < arrayCreation.Initializers.Count; i++)
                {
                    data.AddRange(GenerateExpr(arrayCreation.Initializers[i], initReg));
                    data.AddRange([GetOpcode("ldsrsr"), cpyReg, destRegister, 0xFF]);
                    data.AddRange([GetOpcode("arirx"), cpyReg, idxReg, 0xFF]);
                    data.AddRange([GetOpcode("stsrsr"), cpyReg, initReg, 0xFF]);
                    data.AddRange([GetOpcode("inci32"), idxReg, 0xFF, 0xFF]);
                }
                _registers.FreeRegister(initReg);
                _registers.FreeRegister(idxReg);
                _registers.FreeRegister(cpyReg);
            }

            _arrayRegs.Add(destRegister);
            return data.ToArray();
        }

        protected byte[] GenerateIncrement(IncrementNode increment, byte destRegister)
        {
            List<byte> data = new();

            if (!increment.IsPrefix)
                // Return the value (x++)
                data.AddRange(GenerateExpr(increment.Target, destRegister));

            // Increment/Decrement it
            data.AddRange(increment.Target switch
            {
                IdentifierNode id => GenerateIdentifierIncrement(increment, id),
                QualifiedAccessNode qa => GenerateQualifiedIncrement(increment, qa),
                MemberAccessNode ma => GenerateMemberIncrement(increment, ma),
                ArrayAccessNode aa => GenerateArrayIncrement(increment, aa),

                _ => throw new CodeGeneratorException($"Cannot increment target node {increment.Target}.", increment.Line, increment.Column)
            });

            if (increment.IsPrefix)
                // Return it after changing it (++x)
                data.AddRange(GenerateExpr(increment.Target, destRegister));

            return data.ToArray();
        }

        protected byte[] GenerateIdentifierIncrement(IncrementNode increment, IdentifierNode identifier)
            => GenerateIncrementRegister(increment, _registers.VarToReg[identifier.Name]);

        protected byte[] GenerateQualifiedIncrement(IncrementNode increment, QualifiedAccessNode qualified)
        {
            List<byte> data = new();

            byte valReg = _registers.AllocateRegister();
            data.AddRange(GenerateQualifiedAccess(qualified, valReg));

            data.AddRange(GenerateIncrementRegister(increment, valReg));

            ushort v = (ushort)AddOrGetXRef(qualified.FullName);
            data.AddRange([GetOpcode("stsvsr"), valReg, (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF)]);

            _registers.FreeRegister(valReg);
            return data.ToArray();
        }

        protected byte[] GenerateMemberIncrement(IncrementNode increment, MemberAccessNode member)
        {
            List<byte> data = new();

            byte valReg = _registers.AllocateRegister();
            data.AddRange(GenerateMemberAccess(member, valReg));

            data.AddRange(GenerateIncrementRegister(increment, valReg));

            byte objReg = _registers.AllocateRegister();
            data.AddRange(GenerateMemberSetup(member, objReg));

            data.AddRange([GetOpcode("stsrsr"), objReg, valReg, 0xFF]);

            _registers.FreeRegister(valReg);
            _registers.FreeRegister(objReg);
            return data.ToArray();
        }

        protected byte[] GenerateArrayIncrement(IncrementNode increment, ArrayAccessNode array)
        {
            List<byte> data = new();

            byte valReg = _registers.AllocateRegister();
            data.AddRange(GenerateArrayAccess(array, valReg));

            data.AddRange(GenerateIncrementRegister(increment, valReg));

            byte arrReg = _registers.AllocateRegister();
            data.AddRange(GenerateExpr(array.Array, arrReg));

            byte idxReg = _registers.AllocateRegister();
            data.AddRange(GenerateExpr(array.Index, idxReg));

            byte cpyReg = _registers.AllocateRegister();
            data.AddRange([GetOpcode("ldsrsr"), cpyReg, arrReg, 0xFF]);

            data.AddRange([GetOpcode("arirx"), cpyReg, idxReg, 0xFF]);
            data.AddRange([GetOpcode("stsrsr"), cpyReg, valReg, 0xFF]);

            if (array.Array is not ArrayCreationNode)
                _registers.FreeRegister(arrReg);
            _registers.FreeRegister(valReg);
            _registers.FreeRegister(idxReg);
            _registers.FreeRegister(cpyReg);
            return data.ToArray();
        }

        protected byte[] GenerateMemberSetup(MemberAccessNode member, byte destRegister)
        {
            List<byte> data = new();

            data.AddRange(GenerateExpr(member.Object, destRegister));

            ushort v = (ushort)AddOrGetXRef($"{_semantic.ExprTypes[member.Object]?.Name}.{member.Member}");
            data.AddRange([GetOpcode("addofs"), destRegister, (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF)]);

            return data.ToArray();
        }

        protected byte[] GenerateIncrementRegister(IncrementNode increment, byte reg) => _semantic.ExprTypes[increment.Target]?.Name switch
        {
            "int" => [GetOpcode(increment.IsIncrement ? "inci32" : "deci32"), reg, 0xFF, 0xFF],
            "float" => [GetOpcode(increment.IsIncrement ? "incf32" : "decf32"), reg, 0xFF, 0xFF],

            _ => throw new CodeGeneratorException(
                $"Cannot increment value of type '{_semantic.ExprTypes[increment.Target]?.Name}'.",
                increment.Line,
                increment.Column
            )
        };

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

        protected bool DoesFunctionReturn(string fullName, IList<TypeNode> paramTypes)
        {
            bool isReturn = false;
            GetFuncSymbol(fullName, paramTypes)?.Switch(
                objFunc => isReturn = objFunc.ReturnType != null,
                xrefFunc => isReturn = xrefFunc.ReturnType != null
            );
            return isReturn;
        }

        protected OneOf<FunctionSymbol, ExternalFunctionSymbol>? GetFuncSymbol(string fullName, IList<TypeNode> paramTypes)
        {
            string[] names = fullName.Split('.');
            if (_semantic.Module.Objects.TryGetValue(names[^2], out ObjectSymbol? objSbl))
                if (objSbl.Functions.TryGetValue(names[^1], out FunctionSymbol? objFuncSbl))
                    return objFuncSbl;
            if (_semantic.Module.XRefs.TryGetValue(string.Join('.', names[..^1]), out XRefSymbol? xrefSbl))
                if (xrefSbl.Functions.TryGetValue(names[^1], out ExternalFunctionSymbol? xrefFuncSbl))
                    return xrefFuncSbl;
            return null;
        }

        protected string AppendParamTypes(string fullName, IList<TypeNode> paramTypes)
        {
            StringBuilder sb = new(fullName);
            sb.Append('(');
            if (paramTypes.Count > 0)
                sb.Append(paramTypes[0].Name);
            for (int i = 1; i < paramTypes.Count; i++)
                sb.Append($",{paramTypes[i].Name}");
            sb.Append(')');
            return sb.ToString();
        }

        protected static bool AreBothType(TypeNode a, TypeNode b, string type) => a.Name == type && b.Name == type;

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
