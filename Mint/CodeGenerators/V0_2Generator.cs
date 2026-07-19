using KirbyLib.Mint;
using Mint.AstNodes;
using Mint.Semantics;
using OneOf;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
using System.Threading.Tasks.Dataflow;
using System.Xml.Linq;

namespace Mint.CodeGenerators
{
    // Each version of the code generator inherits from the last version.
    // 0.2 being the first mint version we know of this code generator also
    // acts as a base and defines all the basic parameters needed everywhere.
    // Though as soon as we hit 64
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

        public ModuleRtDL GenerateRtDL(ModuleNode module)
        {
            ModuleRtDL rtdl = new()
            {
                Name = module.FullName
            };

            foreach (ObjectNode obj in module.Objects)
                if (obj.Location == ObjectLocation.Local)
                    rtdl.Objects.Add(GenerateObject(obj));
            
            rtdl.SData = _sdata;
            rtdl.XRef = _xrefs;
            return rtdl;
        }

        private MintObject GenerateObject(ObjectNode obj)
        {
            _currentObj = _semantic.Module.LocalObjects[obj.Name];

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
                throw new CodeGeneratorException("Cannot generate function outside of an object.", funcNode.Line, funcNode.Column);
            if (!_currentObj.FindFunction(funcNode.Name, Utility.ToTypeNodes(funcNode.Params), out _currentFunction))
                throw new CodeGeneratorException(
                    $"Could not find function symbol with name '{funcNode.Name}' in the current object.",
                    funcNode.Line,
                    funcNode.Column
                );
            _registers = new();
            _registers.PushNewBlock();

            if (_currentFunction.HasThis)
                _registers.AllocateRegister(); // should be r0

            foreach (ParamNode param in _currentFunction.Parameters)
                _registers.AllocateRegister(param.Name);

            string retTypeName = "void";
            if (_currentFunction.ReturnType != null)
                retTypeName = GetTypeName(_currentFunction.ReturnType);

            string funcName = funcNode.Name + '(';
            TypeNode[] paramTypes = Utility.ToTypeNodes(_currentFunction.Parameters);
            for (int i = 0; i < paramTypes.Length; i++)
            {
                if (i == paramTypes.Length - 1)
                    funcName += $"{GetTypeName(paramTypes[i])}";
                else
                    funcName += $"{GetTypeName(paramTypes[i])},";
            }
            funcName += ')';

            string mintName = $"{retTypeName} {funcName}";
            if (funcNode.IsConst)
                mintName += "const";
            MintFunction mintFunc = new(mintName)
            {
                Arguments = (uint)funcNode.Params.Count,
                Data = GenerateBlock(funcNode.Body, true).Data
            };

            _currentFunction = null;
            return mintFunc;
        }

        private CodeWriter.CodeResult GenerateBlock(BlockNode block, bool isBeginning = false)
        {
            _registers.PushNewBlock();
            CodeWriter writer = new();
            foreach (StmtNode stmt in block.Statements)
                writer.Append(GenerateStatement(stmt));
            writer.Append(GenerateFreeBlockResources(_registers.ExitBlock()));
            if (isBeginning)
            {
                writer.Insert(0, GenerateFunctionEnter());
                if (writer.Instructions[^1].Opcode != GetOpcode("fleave") &&
                    writer.Instructions[^1].Opcode != GetOpcode("fret"))
                    writer.Instructions.Add(new Instruction(GetOpcode("fleave")));
            }
            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateFunctionEnter()
        {
            if (_currentFunction == null)
                throw new CodeGeneratorException( // This is mainly to shut up C#, this shouldn't ever happen
                    "Tried to generate a function entrance outside of a function.",
                    0, 0
                );

            byte argCount = (byte)_currentFunction.Parameters.Count;
            if (_currentFunction.ReturnType != null) argCount++;
            if (_currentFunction.HasThis) argCount++;

            CodeWriter writer = new();
            writer.Instructions.Add(new Instruction(GetOpcode("fenter"), _registers.RegisterCount, argCount));
            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateFreeBlockResources(HashSet<byte> freedRegisters)
        {
            CodeWriter writer = new();

            foreach (byte reg in freedRegisters)
            {
                if (_arrayRegs.Contains(reg))
                {
                    writer.Instructions.Add(new Instruction(GetOpcode("arpop"), reg));
                    _arrayRegs.Remove(reg);
                }
                if (_instanceRegs.TryGetValue(reg, out string? obj))
                {
                    ushort v = (ushort)AddOrGetXRef(obj);
                    (byte, byte) vBytes = CodeWriter.ToBytes(v);
                    writer.Instructions.Add(new Instruction(GetOpcode("sppop"), reg, vBytes.Item1, vBytes.Item2));
                    _instanceRegs.Remove(reg);
                }
            }

            return writer.Result;
        }

        private CodeWriter.CodeResult GenerateStatement(StmtNode statement)
        {
            return statement switch
            {
                VarDeclNode vd => GenerateVarDecl(vd),
                AssignNode ass => GenerateAssign(ass),
                IfNode ifNode => GenerateIf(ifNode),
                WhileNode whileNode => whileNode.IsDoWhile ? GenerateDoWhile(whileNode) : GenerateWhile(whileNode),
                ForNode forNode => GenerateFor(forNode),
                ReturnNode returnNode => GenerateReturn(returnNode),
                ExprStmtNode expr => GenerateExprStmt(expr),
                YieldNode yield => GenerateYield(yield),

                _ => throw new NotImplementedException("Unknown statement type.")
            };
        }

        protected CodeWriter.CodeResult GenerateVarDecl(VarDeclNode varDecl)
        {
            byte destReg = _registers.AllocateRegister(varDecl.Name);
            return GenerateExpr(varDecl.Initializer, destReg);
        }

        // TODO : override in 7.X (no more mint array)
        protected CodeWriter.CodeResult GenerateAssign(AssignNode assign)
        {
            // Every kind of assign is separated for easy overrides in specific cases
            return assign.Target switch
            {
                IdentifierNode ident => GenerateExpr(assign.Value, _registers.VarToReg[ident.Name]),
                ArrayAccessNode aa => GenerateArrayAssign(assign, aa),
                MemberAccessNode ma => GenerateMemberAssign(assign, ma),
                QualifiedAccessNode qa => GenerateQualifiedAssign(assign, qa),
                DereferenceNode dr => GenerateDereferenceAssign(assign, dr),

                _ => throw new CodeGeneratorException($"Unknown assign target node : {assign.Target}", assign.Line, assign.Column)
            };
        }

        protected CodeWriter.CodeResult GenerateArrayAssign(AssignNode assign, ArrayAccessNode targetArray)
        {
            CodeWriter writer = new();
            byte arrReg = _registers.AllocateRegister(),
                 cpyReg = _registers.AllocateRegister(),
                 idxReg = _registers.AllocateRegister(),
                 valReg = _registers.AllocateRegister();

            writer.Append(GenerateExpr(targetArray.Array, arrReg));
            writer.Append(GenerateExpr(targetArray.Index, idxReg));
            writer.Append(GenerateExpr(assign.Value, valReg));

            writer.Instructions.Add(new Instruction(GetOpcode("ldsrsr"), cpyReg, arrReg));
            writer.Instructions.Add(new Instruction(GetOpcode("arirx"), cpyReg, idxReg));
            writer.Instructions.Add(new Instruction(GetOpcode("stsrsr"), cpyReg, valReg));

            if (targetArray.Array is ArrayCreationNode)
                writer.Instructions.Add(new Instruction(GetOpcode("arpop"), arrReg));

            writer.Append(GenerateFreeRegister(arrReg));
            writer.Append(GenerateFreeRegister(cpyReg));
            writer.Append(GenerateFreeRegister(idxReg));
            writer.Append(GenerateFreeRegister(valReg));

            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateMemberAssign(AssignNode assign, MemberAccessNode targetMember)
        {
            CodeWriter writer = new();

            byte objReg = _registers.AllocateRegister();
            writer.Append(GenerateExpr(targetMember.Object, objReg));

            byte valReg = _registers.AllocateRegister();
            writer.Append(GenerateExpr(assign.Value, valReg));

            string xref = $"{_semantic.ExprTypes[targetMember.Object]?.Name}.{targetMember.Member}";
            ushort v = (ushort)AddOrGetXRef(xref);
            (byte, byte) vBytes = CodeWriter.ToBytes(v);
            writer.Instructions.Add(new Instruction(GetOpcode("addofs"), objReg, vBytes.Item1, vBytes.Item2));
            writer.Instructions.Add(new Instruction(GetOpcode("stsrsr"), objReg, valReg));

            writer.Append(GenerateFreeRegister(objReg));
            writer.Append(GenerateFreeRegister(valReg));
            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateQualifiedAssign(AssignNode assign, QualifiedAccessNode targetQualified)
        {
            CodeWriter writer = new();

            byte valReg = _registers.AllocateRegister();
            writer.Append(GenerateExpr(assign.Value, valReg));

            ushort v = (ushort)AddOrGetXRef(targetQualified.FullName);
            (byte, byte) vBytes = CodeWriter.ToBytes(v);
            writer.Instructions.Add(new Instruction(GetOpcode("stsvsr"), valReg, vBytes.Item1, vBytes.Item2));

            writer.Append(GenerateFreeRegister(valReg));
            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateDereferenceAssign(AssignNode assign, DereferenceNode dereference)
        {
            CodeWriter writer = new();

            byte valReg = _registers.AllocateRegister();
            writer.Append(GenerateExpr(assign.Value, valReg));

            byte refReg = _registers.AllocateRegister();
            writer.Append(GenerateExpr(dereference.Reference, refReg));

            writer.Instructions.Add(new Instruction(GetOpcode("stsrsr"), refReg, valReg));

            writer.Append(GenerateFreeRegister(valReg));
            writer.Append(GenerateFreeRegister(refReg));
            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateIf(IfNode ifNode)
        {
            CodeWriter writer = new();

            byte condReg = _registers.AllocateRegister();
            writer.Append(GenerateExpr(ifNode.Condition, condReg));

            int jmpnegInstrIndex = writer.Instructions.Count;
            Instruction jmpnegInstr = new Instruction(GetOpcode("jmpneg"), condReg); // temporary until v is figured out
            writer.Instructions.Add(jmpnegInstr);

            writer.Append(GenerateFreeRegister(condReg));

            CodeWriter.CodeResult thenBlock = GenerateBlock(ifNode.Then);
            writer.Append(thenBlock);
            short jumpLength = (short)(thenBlock.Instructions.Length + 1);

            if (ifNode.ElseIf != null || ifNode.Else != null)
            {
                jumpLength++; // skip the additional jump that jumps over all the elses

                int elseJumpInstructionIndex = writer.Instructions.Count;
                Instruction elseJmpInstr = new Instruction(GetOpcode("jmp")); // temporary v, again
                writer.Instructions.Add(elseJmpInstr);

                CodeWriter.CodeResult els;
                if (ifNode.ElseIf != null)
                    els = GenerateIf(ifNode.ElseIf);
                else if (ifNode.Else != null)
                    els = GenerateBlock(ifNode.Else);
                else
                    // what
                    throw new CodeGeneratorException("What the fuck. How did you get here.", ifNode.Line, ifNode.Column);
                writer.Append(els);

                short elsJumpLength = (short)(els.Instructions.Length + 1);
                (byte, byte) elsJmpVBytes = CodeWriter.ToBytes(elsJumpLength);
                writer.Instructions[elseJumpInstructionIndex] = elseJmpInstr with { X = elsJmpVBytes.Item1, Y = elsJmpVBytes.Item2 };
            }

            (byte, byte) vBytes = CodeWriter.ToBytes(jumpLength);
            writer.Instructions[jmpnegInstrIndex] = jmpnegInstr with { X = vBytes.Item1, Y = vBytes.Item2 };

            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateWhile(WhileNode whileNode)
        {
            CodeWriter writer = new();

            byte condReg = _registers.AllocateRegister();
            writer.Append(GenerateExpr(whileNode.Condition, condReg));

            int jmpnegInstructionPos = writer.Instructions.Count;
            Instruction jmpnegInstr = new(GetOpcode("jmpneg"), condReg);
            writer.Instructions.Add(jmpnegInstr);

            writer.Append(GenerateFreeRegister(condReg));

            CodeWriter.CodeResult whileBody = GenerateBlock(whileNode.Body);
            writer.Append(whileBody);

            short endJmpLength = (short)(-writer.Instructions.Count);
            (byte, byte) vBytes = CodeWriter.ToBytes(endJmpLength);
            writer.Instructions.Add(new Instruction(GetOpcode("jmp"), 0xFF, vBytes.Item1, vBytes.Item2));

            short jmpnegLength = (short)(whileBody.Instructions.Length + 2);
            vBytes = CodeWriter.ToBytes(jmpnegLength);
            writer.Instructions[jmpnegInstructionPos] = jmpnegInstr with { X = vBytes.Item1, Y = vBytes.Item2 };

            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateDoWhile(WhileNode whileNode)
        {
            CodeWriter writer = new();

            writer.Append(GenerateBlock(whileNode.Body));

            byte condReg = _registers.AllocateRegister();
            writer.Append(GenerateExpr(whileNode.Condition, condReg));
            short endJmpLength = (short)(-writer.Instructions.Count);
            (byte, byte) vBytes = CodeWriter.ToBytes(endJmpLength);
            writer.Instructions.Add(new Instruction(GetOpcode("jmppos"), condReg, vBytes.Item1, vBytes.Item2));
            writer.Append(GenerateFreeRegister(condReg));

            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateFor(ForNode forNode)
        {
            CodeWriter writer = new();

            writer.Append(GenerateStatement(forNode.Initializer));

            int condPos = writer.Instructions.Count;
            byte condReg = _registers.AllocateRegister();
            writer.Append(GenerateExpr(forNode.Condition, condReg));

            int condJmpPos = writer.Instructions.Count;
            Instruction condJmpInstr = new(GetOpcode("jmpneg"), condReg); // temporary v
            writer.Instructions.Add(condJmpInstr);
            writer.Append(GenerateFreeRegister(condReg));

            CodeWriter.CodeResult bodyBlock = GenerateBlock(forNode.Body);
            writer.Append(bodyBlock);

            CodeWriter.CodeResult incBlock = GenerateStatement(forNode.Increment);
            writer.Append(incBlock);

            short bodyJmpV = (short)(condPos - writer.Instructions.Count);
            (byte, byte) vBytes = CodeWriter.ToBytes(bodyJmpV);
            writer.Instructions.Add(new Instruction(GetOpcode("jmp"), 0xFF, vBytes.Item1, vBytes.Item2));

            short condJmpV = (short)(writer.Instructions.Count - condJmpPos);
            vBytes = CodeWriter.ToBytes(condJmpV);
            writer.Instructions[condJmpPos] = condJmpInstr with { X = vBytes.Item1, Y = vBytes.Item2 };

            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateReturn(ReturnNode returnNode)
        {
            CodeWriter writer = new();

            if (returnNode.Value != null)
            {
                byte valReg = _registers.AllocateRegister();
                writer.Append(GenerateExpr(returnNode.Value, valReg));

                writer.Instructions.Add(new Instruction(GetOpcode("ldsrsr"), 0, valReg));
                writer.Instructions.Add(new Instruction(GetOpcode("fret"), 0xFF, 0, 0xFF));

                writer.Append(GenerateFreeRegister(valReg));
            }
            else writer.Instructions.Add(new Instruction(GetOpcode("fleave")));
            
            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateExprStmt(ExprStmtNode exprStmt)
        {
            CodeWriter writer = new();
            byte scratch = _registers.AllocateRegister();
            writer.Append(GenerateExpr(exprStmt.Expr, scratch));
            writer.Append(GenerateFreeRegister(scratch));
            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateYield(YieldNode yieldNode)
        {
            CodeWriter writer = new();

            byte countReg = _registers.AllocateRegister();
            writer.Append(GenerateExpr(yieldNode.FrameCount, countReg));

            writer.Instructions.Add(new Instruction(GetOpcode("yield"), countReg));

            writer.Append(GenerateFreeRegister(countReg));
            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateExpr(ExprNode expr, byte destRegister) => expr switch
        {
            IntLiteralNode intLit => GenerateIntLiteral(intLit, destRegister),
            FloatLiteralNode floatLit => GenerateFloatLiteral(floatLit, destRegister),
            BoolLiteralNode boolLit => GenerateBoolLiteral(boolLit, destRegister),
            StringLiteralNode stringLit => GenerateStringLiteral(stringLit, destRegister),
            IdentifierNode id => GenerateIdentifier(id, destRegister),
            QualifiedAccessNode qa => GenerateQualifiedAccess(qa, destRegister),
            MemberAccessNode ma => GenerateMemberAccess(ma, destRegister),
            ArrayAccessNode aa => GenerateArrayAccess(aa, destRegister),
            DereferenceNode dr => GenerateDereference(dr, destRegister),
            ThisNode => GenerateThis(destRegister),
            BinaryExprNode be => GenerateBinaryExpr(be, destRegister),
            UnaryExprNode ue => GenerateUnaryExpr(ue, destRegister),
            QualifiedCallNode qc => GenerateQualifiedCall(qc, destRegister),
            MemberCallNode mc => GenerateMemberCall(mc, destRegister),
            PushInstanceNode pi => GeneratePushInstance(pi, destRegister),
            ArrayCreationNode ac => GenerateArrayCreation(ac, destRegister),
            IncrementNode inc => GenerateIncrement(inc, destRegister),
            MemberOffsetNode mo => GenerateMemberOffset(mo, destRegister),
            TypeCastNode tc => GenerateTypeCast(tc, destRegister),

            _ => throw new CodeGeneratorException($"Invalid expression node for this version : {expr}", expr.Line, expr.Column)
        };

        protected CodeWriter.CodeResult GenerateIntLiteral(IntLiteralNode intLiteral, byte destRegister)
        {
            ushort v = (ushort)_sdata.Count;
            _sdata.AddRange(GetBytesFromInt(intLiteral.Value));

            CodeWriter writer = new();
            (byte, byte) vBytes = CodeWriter.ToBytes(v);
            writer.Instructions.Add(new Instruction(GetOpcode("ldsrc4"), destRegister, vBytes.Item1, vBytes.Item2));

            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateFloatLiteral(FloatLiteralNode floatLiteral, byte destRegister)
        {
            ushort v = (ushort)_sdata.Count;
            _sdata.AddRange(GetBytesFromInt(BitConverter.SingleToInt32Bits(floatLiteral.Value)));

            CodeWriter writer = new();
            (byte, byte) vBytes = CodeWriter.ToBytes(v);
            writer.Instructions.Add(new Instruction(GetOpcode("ldsrc4"), destRegister, vBytes.Item1, vBytes.Item2));

            return writer.Result;
        }

        // TODO : override in versions starting with 7.0.2 because they removed my goat ldsrbt
        protected CodeWriter.CodeResult GenerateBoolLiteral(BoolLiteralNode boolLiteral, byte destRegister)
        {
            CodeWriter writer = new();
            writer.Instructions.Add(new Instruction(GetOpcode(boolLiteral.Value ? "ldsrbt" : "ldsrzr"), destRegister));
            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateStringLiteral(StringLiteralNode stringLiteral, byte destRegister)
        {
            short v = (short)_sdata.Count;
            _sdata.AddRange(Encoding.UTF8.GetBytes(stringLiteral.Value + '\0'));

            CodeWriter writer = new();
            (byte, byte) vBytes = CodeWriter.ToBytes(v);
            writer.Instructions.Add(new Instruction(GetOpcode("ldsrca"), destRegister, vBytes.Item1, vBytes.Item2));

            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateIdentifier(IdentifierNode identifier, byte destRegister)
        {
            if (_registers.VarToReg.TryGetValue(identifier.Name, out byte reg))
            {
                CodeWriter writer = new();
                writer.Instructions.Add(new Instruction(GetOpcode("ldsrsr"), destRegister, reg));
                return writer.Result;
            }
            throw new CodeGeneratorException($"Register for identifier '{identifier.Name}' not found.", identifier.Line, identifier.Column);
        }

        protected CodeWriter.CodeResult GenerateQualifiedAccess(QualifiedAccessNode qualifiedAccess, byte destRegister)
        {
            ushort v = (ushort)AddOrGetXRef(qualifiedAccess.FullName);
            (byte, byte) vBytes = CodeWriter.ToBytes(v);

            CodeWriter writer = new();
            writer.Instructions.Add(new Instruction(GetOpcode("ldsrsv"), destRegister, vBytes.Item1, vBytes.Item2));

            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateMemberAccess(MemberAccessNode memberAccess, byte destRegister)
        {
            CodeWriter writer = new();

            byte objReg = _registers.AllocateRegister();
            writer.Append(GenerateMemberSetup(memberAccess, objReg));
            writer.Instructions.Add(new Instruction(GetOpcode("ldsra4"), destRegister, objReg));

            writer.Append(GenerateFreeRegister(objReg));
            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateArrayAccess(ArrayAccessNode arrayAccess, byte destRegister)
        {
            CodeWriter writer = new();

            byte arrReg = _registers.AllocateRegister();
            writer.Append(GenerateExpr(arrayAccess.Array, arrReg));

            byte idxReg = _registers.AllocateRegister();
            writer.Append(GenerateExpr(arrayAccess.Index, idxReg));

            byte cpyReg = _registers.AllocateRegister();
            writer.Instructions.Add(new Instruction(GetOpcode("ldsrsr"), cpyReg, arrReg));

            writer.Instructions.Add(new Instruction(GetOpcode("arirx"), cpyReg, idxReg));
            writer.Instructions.Add(new Instruction(GetOpcode("ldsra4"), destRegister, cpyReg));

            if (arrayAccess.Array is not ArrayCreationNode)
                writer.Append(GenerateFreeRegister(arrReg));
            writer.Append(GenerateFreeRegister(idxReg));
            writer.Append(GenerateFreeRegister(cpyReg));
            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateDereference(DereferenceNode dereference, byte destRegister)
        {
            CodeWriter writer = new();

            byte refReg = _registers.AllocateRegister();
            writer.Append(GenerateExpr(dereference.Reference, refReg));

            writer.Instructions.Add(new Instruction(GetOpcode("ldsra4"), destRegister, refReg));

            writer.Append(GenerateFreeRegister(refReg));
            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateThis(byte destRegister)
        {
            CodeWriter writer = new();
            writer.Instructions.Add(new Instruction(GetOpcode("ldsrsr"), destRegister, 0));
            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateBinaryExpr(BinaryExprNode binaryExpr, byte destRegister)
        {
            CodeWriter writer = new();

            byte leftReg = _registers.AllocateRegister();
            writer.Append(GenerateExpr(binaryExpr.Left, leftReg));

            byte rightReg;
            if (binaryExpr.Op is "&&" or "||")
            {
                // there is no logical 'and' and 'or' opcode in mint so gotta do it the nasty way

                byte jmpOpcode = GetOpcode(binaryExpr.Op == "&&" ? "jmpneg" : "jmppos");

                Instruction jmpLeft = new(jmpOpcode, leftReg);
                int jmpLeftIdx = writer.Instructions.Count;
                writer.Instructions.Add(jmpLeft);

                writer.Append(GenerateFreeRegister(leftReg));

                rightReg = _registers.AllocateRegister();
                writer.Append(GenerateExpr(binaryExpr.Right, rightReg));

                Instruction jmpRight = new(jmpOpcode, rightReg);
                int jmpRightIdx = writer.Instructions.Count;
                writer.Instructions.Add(jmpRight);

                writer.Append(GenerateFreeRegister(rightReg));

                writer.Instructions.Add(new Instruction(GetOpcode(binaryExpr.Op == "&&" ? "ldsrbt" : "ldsrzr"), destRegister));
                writer.Instructions.Add(new Instruction(GetOpcode("jmp"), 0xFF, 0, 2));

                short jmpLeftV = (short)(writer.Instructions.Count - jmpLeftIdx);
                (byte, byte) vBytes = CodeWriter.ToBytes(jmpLeftV);
                writer.Instructions[jmpLeftIdx] = jmpLeft with { X = vBytes.Item1, Y = vBytes.Item2 };

                short jmpRightV = (short)(writer.Instructions.Count - jmpRightIdx);
                vBytes = CodeWriter.ToBytes(jmpRightV);
                writer.Instructions[jmpRightIdx] = jmpRight with { X = vBytes.Item1, Y = vBytes.Item2 };

                writer.Instructions.Add(new Instruction(GetOpcode(binaryExpr.Op == "&&" ? "ldsrzr" : "ldsrbt"), destRegister));

                return writer.Result;
            }

            rightReg = _registers.AllocateRegister();
            writer.Append(GenerateExpr(binaryExpr.Right, rightReg));

            TypeNode? binType = _semantic.ExprTypes[binaryExpr];
            TypeNode? leftType = _semantic.ExprTypes[binaryExpr.Left];
            TypeNode? rightType = _semantic.ExprTypes[binaryExpr.Right];
            if (binType == null || leftType == null || rightType == null)
                throw new CodeGeneratorException("Cannot have a binary expression with type 'void'.", binaryExpr.Line, binaryExpr.Column);

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

            writer.Instructions.Add(new Instruction(
                GetOpcode(opcode),
                destRegister,
                invertOperands ? rightReg : leftReg,
                invertOperands ? leftReg : rightReg
            ));
            writer.Append(GenerateFreeRegister(leftReg));
            writer.Append(GenerateFreeRegister(rightReg));
            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateUnaryExpr(UnaryExprNode unaryExpr, byte destRegister)
        {
            CodeWriter writer = new();

            TypeNode? operandType = _semantic.ExprTypes[unaryExpr.Operand];
            if (operandType == null)
                throw new CodeGeneratorException(
                    "Operand must not be of type 'void' for unary expression.",
                    unaryExpr.Line,
                    unaryExpr.Column
                );

            byte operandReg = _registers.AllocateRegister();
            writer.Append(GenerateExpr(unaryExpr.Operand, operandReg));

            string opcode = unaryExpr.Op switch
            {
                "-" when operandType.Name == "int" => "negs32",
                "-" when operandType.Name == "float" => "negf32",
                "!" when operandType.Name == "bool" => "ntbool",

                _ => throw new CodeGeneratorException($"Unknown unary operation with operator '{unaryExpr.Op}'.", unaryExpr.Line, unaryExpr.Column)
            };

            writer.Instructions.Add(new Instruction(GetOpcode(opcode), destRegister, operandReg));

            writer.Append(GenerateFreeRegister(operandReg));
            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateQualifiedCall(QualifiedCallNode qualifiedCall, byte destRegister)
        {
            CodeWriter writer = new();

            TypeNode[] argTypes = GetTypesFromArgs(qualifiedCall.Args, qualifiedCall.Line, qualifiedCall.Column);

            writer.Append(TryGenerateReturnInstanceSetup(qualifiedCall.FullName, argTypes, destRegister, out bool isReturn));

            List<byte> regs = new();
            foreach (ExprNode arg in qualifiedCall.Args)
            {
                regs.Add(_registers.AllocateRegister());
                writer.Append(GenerateExpr(arg, regs[^1]));
            }

            for (int i = 0; i < regs.Count; i++)
                writer.Instructions.Add(new Instruction(GetOpcode("ldfrsr"), (byte)(isReturn ? i + 1 : i), regs[i]));

            ushort v = (ushort)AddOrGetXRef(AppendParamTypes(qualifiedCall.FullName, argTypes));
            (byte, byte) vBytes = CodeWriter.ToBytes(v);
            writer.Instructions.Add(new Instruction(GetOpcode("call"), 0xFF, vBytes.Item1, vBytes.Item2));

            if (isReturn)
                writer.Instructions.Add(new Instruction(GetOpcode("ldsrfz"), destRegister));

            foreach (byte reg in regs)
                writer.Append(GenerateFreeRegister(reg));
            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateMemberCall(MemberCallNode memberCall, byte destRegister)
        {
            CodeWriter writer = new();

            TypeNode[] argTypes = GetTypesFromArgs(memberCall.Args, memberCall.Line, memberCall.Column);

            TypeNode? objType = _semantic.ExprTypes[memberCall.Object];
            if (objType == null)
                throw new CodeGeneratorException(
                    "Cannot call from object of type 'void'.",
                    memberCall.Line,
                    memberCall.Column
                );

            string fullName = $"{objType.Name}.{memberCall.Name}";
            writer.Append(TryGenerateReturnInstanceSetup(fullName, argTypes, destRegister, out bool isReturn));

            byte objReg = _registers.AllocateRegister();
            writer.Append(GenerateExpr(memberCall.Object, objReg));

            List<byte> regs = new();
            foreach (ExprNode arg in memberCall.Args)
            {
                regs.Add(_registers.AllocateRegister());
                writer.Append(GenerateExpr(arg, regs[^1]));
            }

            writer.Instructions.Add(new Instruction(
                GetOpcode("ldfrsr"),
                (byte)(isReturn ? 1 : 0),
                objReg
            ));
            for (int i = 0; i < regs.Count; i++)
                writer.Instructions.Add(new Instruction(
                    GetOpcode("ldfrsr"),
                    (byte)(isReturn ? i + 2 : i + 1),
                    regs[i]
                ));

            ushort v = (ushort)AddOrGetXRef(AppendParamTypes(fullName, argTypes));
            (byte, byte) vBytes = CodeWriter.ToBytes(v);
            writer.Instructions.Add(new Instruction(GetOpcode("call"), 0xFF, vBytes.Item1, vBytes.Item2));

            if (isReturn)
                writer.Instructions.Add(new Instruction(GetOpcode("ldsrfz"), destRegister));

            writer.Append(GenerateFreeRegister(objReg));
            foreach (byte reg in regs)
                writer.Append(GenerateFreeRegister(reg));
            return writer.Result;
        }

        private CodeWriter.CodeResult TryGenerateReturnInstanceSetup(
            string funcName,
            IList<TypeNode> argTypes,
            byte destRegister,
            out bool isReturn)
        {
            CodeWriter writer = new();

            isReturn = false;
            if (DoesFunctionReturn(funcName, argTypes, out TypeNode? returnType))
            {
                isReturn = true;
                if (_semantic.Module.LocalObjects.ContainsKey(returnType.Name) ||
                    _semantic.Module.XRefObjects.ContainsKey(returnType.Name))
                {
                    if (_instanceRegs.TryGetValue(destRegister, out string? instName) && instName != returnType.Name)
                    {
                        // register has the wrong instance pushed, pop it
                        ushort popV = (ushort)AddOrGetXRef(instName);
                        (byte, byte) popVBytes = CodeWriter.ToBytes(popV);
                        writer.Instructions.Add(new Instruction(GetOpcode("sppop"), destRegister, popVBytes.Item1, popVBytes.Item2));
                    }
                    else if (instName != null)
                        return writer.Result; // register already has the correct instance pushed

                    ushort spV = (ushort)AddOrGetXRef(returnType.Name);
                    (byte, byte) spVBytes = CodeWriter.ToBytes(spV);
                    writer.Instructions.Add(new Instruction(GetOpcode("sppsh"), destRegister, spVBytes.Item1, spVBytes.Item2));

                    _instanceRegs[destRegister] = returnType.Name;
                }
            }

            return writer.Result;
        }

        private TypeNode[] GetTypesFromArgs(IList<ExprNode> args, int line, int col)
        {
            List<TypeNode> argTypes = new();
            foreach (ExprNode arg in args)
            {
                TypeNode? argType = _semantic.ExprTypes[arg];
                if (argType == null)
                    throw new CodeGeneratorException(
                        "Argument cannot be of type 'void'.",
                        line, col
                    );
                argTypes.Add(argType);
            }
            return argTypes.ToArray();
        }

        protected CodeWriter.CodeResult GeneratePushInstance(PushInstanceNode pushInstance, byte destRegister)
        {
            ushort v = (ushort)AddOrGetXRef(pushInstance.ObjectName);
            (byte, byte) vBytes = CodeWriter.ToBytes(v);

            CodeWriter writer = new();
            writer.Instructions.Add(new Instruction(GetOpcode("sppshz"), destRegister, vBytes.Item1, vBytes.Item2));
            _instanceRegs.Add(destRegister, pushInstance.ObjectName);

            if (pushInstance.CtArgs != null)
            {
                List<byte> regs = new();
                foreach (ExprNode arg in pushInstance.CtArgs)
                {
                    regs.Add(_registers.AllocateRegister());
                    writer.Append(GenerateExpr(arg, regs[^1]));
                }

                writer.Instructions.Add(new Instruction(GetOpcode("ldfrsr"), 0, destRegister));
                for (int i = 0; i < regs.Count; i++)
                    writer.Instructions.Add(new Instruction(GetOpcode("ldfrsr"), (byte)(i + 1), regs[i]));

                TypeNode[] argTypes = GetTypesFromArgs(pushInstance.CtArgs, pushInstance.Line, pushInstance.Column);
                if (_semantic.Module.LocalObjects.TryGetValue(pushInstance.ObjectName, out ObjectSymbol? objSbl))
                    if (objSbl.FindConstructor(argTypes, out ConstructorSymbol? ctSbl))
                        argTypes = Utility.ToTypeNodes(ctSbl.Parameters);
                if (_semantic.Module.XRefObjects.TryGetValue(pushInstance.ObjectName, out XRefSymbol? xrefSbl))
                    if (xrefSbl.FindConstructor(argTypes, out XRefConstructorSymbol? xrefCtSbl))
                        argTypes = xrefCtSbl.ArgumentTypes.ToArray();

                StringBuilder sb = new($"{pushInstance.ObjectName}.this(");
                for (int i = 0; i < argTypes.Length; i++)
                {
                    sb.Append(GetTypeName(argTypes[i]));
                    if (i != argTypes.Length - 1)
                        sb.Append(',');
                }
                sb.Append(')');

                v = (ushort)AddOrGetXRef(sb.ToString());
                vBytes = CodeWriter.ToBytes(v);
                writer.Instructions.Add(new Instruction(GetOpcode("call"), 0xFF, vBytes.Item1, vBytes.Item2));

                foreach (byte reg in regs)
                    writer.Append(GenerateFreeRegister(reg));
            }

            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateArrayCreation(ArrayCreationNode arrayCreation, byte destRegister)
        {
            CodeWriter writer = new();

            if (arrayCreation.Size != null)
                writer.Append(GenerateExpr(arrayCreation.Size, destRegister));
            else if (arrayCreation.Initializers != null)
                writer.Append(GenerateIntLiteral(new IntLiteralNode(
                    arrayCreation.Initializers.Count,
                    arrayCreation.Line,
                    arrayCreation.Column
                ), destRegister));
            else
                // nothing that could indicate size given... welp guess you're getting an array with 0 elements!
                writer.Append(GenerateIntLiteral(new IntLiteralNode(
                    0,
                    arrayCreation.Line,
                    arrayCreation.Column
                ), destRegister));
            writer.Instructions.Add(new Instruction(GetOpcode("arpshz"), destRegister));
            if (arrayCreation.Initializers != null) // yes I'm checking that 2 times fight me
            {
                byte initReg = _registers.AllocateRegister();
                byte idxReg = _registers.AllocateRegister();
                writer.Append(GenerateIntLiteral(new IntLiteralNode(
                    0,
                    arrayCreation.Line,
                    arrayCreation.Column
                ), idxReg));
                byte cpyReg = _registers.AllocateRegister();
                for (int i = 0; i < arrayCreation.Initializers.Count; i++)
                {
                    writer.Append(GenerateExpr(arrayCreation.Initializers[i], initReg));
                    writer.Instructions.Add(new Instruction(GetOpcode("ldsrsr"), cpyReg, destRegister));
                    writer.Instructions.Add(new Instruction(GetOpcode("arirx"), cpyReg, idxReg));
                    writer.Instructions.Add(new Instruction(GetOpcode("stsrsr"), cpyReg, initReg));
                    writer.Instructions.Add(new Instruction(GetOpcode("inci32"), idxReg));
                }
                writer.Append(GenerateFreeRegister(initReg));
                writer.Append(GenerateFreeRegister(idxReg));
                writer.Append(GenerateFreeRegister(cpyReg));
            }

            _arrayRegs.Add(destRegister);
            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateIncrement(IncrementNode increment, byte destRegister)
        {
            CodeWriter writer = new();

            if (!increment.IsPrefix)
                // Return the value (x++)
                writer.Append(GenerateExpr(increment.Target, destRegister));

            // Increment/Decrement it
            writer.Append(increment.Target switch
            {
                IdentifierNode id => GenerateIdentifierIncrement(increment, id),
                QualifiedAccessNode qa => GenerateQualifiedIncrement(increment, qa),
                MemberAccessNode ma => GenerateMemberIncrement(increment, ma),
                ArrayAccessNode aa => GenerateArrayIncrement(increment, aa),
                DereferenceNode dr => GenerateDereferenceIncrement(increment, dr),

                _ => throw new CodeGeneratorException($"Cannot increment target node {increment.Target}.", increment.Line, increment.Column)
            });

            if (increment.IsPrefix)
                // Return it after changing it (++x)
                writer.Append(GenerateExpr(increment.Target, destRegister));

            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateIdentifierIncrement(IncrementNode increment, IdentifierNode identifier)
            => GenerateIncrementRegister(increment, _registers.VarToReg[identifier.Name]);

        protected CodeWriter.CodeResult GenerateQualifiedIncrement(IncrementNode increment, QualifiedAccessNode qualified)
        {
            CodeWriter writer = new();

            byte valReg = _registers.AllocateRegister();
            writer.Append(GenerateQualifiedAccess(qualified, valReg));

            writer.Append(GenerateIncrementRegister(increment, valReg));

            ushort v = (ushort)AddOrGetXRef(qualified.FullName);
            (byte, byte) vBytes = CodeWriter.ToBytes(v);
            writer.Instructions.Add(new Instruction(GetOpcode("stsvsr"), valReg, vBytes.Item1, vBytes.Item2));

            writer.Append(GenerateFreeRegister(valReg));
            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateMemberIncrement(IncrementNode increment, MemberAccessNode member)
        {
            CodeWriter writer = new();

            byte valReg = _registers.AllocateRegister();
            writer.Append(GenerateMemberAccess(member, valReg));

            writer.Append(GenerateIncrementRegister(increment, valReg));

            byte objReg = _registers.AllocateRegister();
            writer.Append(GenerateMemberSetup(member, objReg));

            writer.Instructions.Add(new Instruction(GetOpcode("stsrsr"), objReg, valReg));

            writer.Append(GenerateFreeRegister(valReg));
            writer.Append(GenerateFreeRegister(objReg));
            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateArrayIncrement(IncrementNode increment, ArrayAccessNode array)
        {
            CodeWriter writer = new();

            byte valReg = _registers.AllocateRegister();
            writer.Append(GenerateArrayAccess(array, valReg));

            writer.Append(GenerateIncrementRegister(increment, valReg));

            byte arrReg = _registers.AllocateRegister();
            writer.Append(GenerateExpr(array.Array, arrReg));

            byte idxReg = _registers.AllocateRegister();
            writer.Append(GenerateExpr(array.Index, idxReg));

            byte cpyReg = _registers.AllocateRegister();
            writer.Instructions.Add(new Instruction(GetOpcode("ldsrsr"), cpyReg, arrReg));

            writer.Instructions.Add(new Instruction(GetOpcode("arirx"), cpyReg, idxReg));
            writer.Instructions.Add(new Instruction(GetOpcode("stsrsr"), cpyReg, valReg));

            if (array.Array is not ArrayCreationNode)
                writer.Append(GenerateFreeRegister(arrReg));
            writer.Append(GenerateFreeRegister(valReg));
            writer.Append(GenerateFreeRegister(idxReg));
            writer.Append(GenerateFreeRegister(cpyReg));
            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateDereferenceIncrement(IncrementNode increment, DereferenceNode dereference)
        {
            CodeWriter writer = new();

            byte valReg = _registers.AllocateRegister();
            writer.Append(GenerateDereference(dereference, valReg));

            writer.Append(GenerateIncrementRegister(increment, valReg));

            byte refReg = _registers.AllocateRegister();
            writer.Append(GenerateExpr(dereference.Reference, refReg));

            writer.Instructions.Add(new Instruction(GetOpcode("stsrsr"), refReg, valReg));

            writer.Append(GenerateFreeRegister(refReg));
            writer.Append(GenerateFreeRegister(valReg));
            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateMemberOffset(MemberOffsetNode memberOffset, byte destRegister)
        {
            CodeWriter writer = new();

            byte objReg = _registers.AllocateRegister();
            writer.Append(GenerateExpr(memberOffset.Object, objReg));

            ushort v = (ushort)AddOrGetXRef($"{_semantic.ExprTypes[memberOffset.Object]?.Name}.{memberOffset.Member}");
            (byte, byte) vBytes = CodeWriter.ToBytes(v);
            writer.Instructions.Add(new Instruction(GetOpcode("addofs"), destRegister, vBytes.Item1, vBytes.Item2));

            writer.Append(GenerateFreeRegister(objReg));
            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateTypeCast(TypeCastNode typeCast, byte destRegister)
        {
            CodeWriter writer = new();

            byte exprReg = _registers.AllocateRegister();
            writer.Append(GenerateExpr(typeCast.Expr, exprReg));

            TypeNode? ogType = _semantic.ExprTypes[typeCast.Expr];
            if (ogType == null)
                throw new CodeGeneratorException($"Type cast with 'void' expression encountered.", typeCast.Line, typeCast.Column);

            if (ogType.Name == typeCast.Type.Name)
            {
                writer.Instructions.Add(new Instruction(GetOpcode("ldsrsr"), destRegister, exprReg));

                writer.Append(GenerateFreeRegister(exprReg));
                return writer.Result;
            }

            writer.Instructions.Add(new Instruction(GetOpcode("ldfrsr"), 1, exprReg));

            string castFuncName = ogType.Name switch
            {
                "int" when typeCast.Type.Name == "float" => "HEL.Cast.I2F(int)",
                "float" when typeCast.Type.Name == "int" => "HEL.Cast.F2I(float)",

                _ => throw new CodeGeneratorException(
                    $"Unknown type cast from '{ogType.Name}' to '{typeCast.Type.Name}'.",
                    typeCast.Line, typeCast.Column
                )
            };
            ushort v = (ushort)AddOrGetXRef(castFuncName);
            (byte, byte) vBytes = CodeWriter.ToBytes(v);
            writer.Instructions.Add(new Instruction(GetOpcode("call"), 0xFF, vBytes.Item1, vBytes.Item2));
            writer.Instructions.Add(new Instruction(GetOpcode("ldsrfz"), destRegister));

            writer.Append(GenerateFreeRegister(exprReg));
            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateMemberSetup(MemberAccessNode member, byte destRegister)
        {
            CodeWriter writer = new();

            writer.Append(GenerateExpr(member.Object, destRegister));

            ushort v = (ushort)AddOrGetXRef($"{_semantic.ExprTypes[member.Object]?.Name}.{member.Member}");
            (byte, byte) vBytes = CodeWriter.ToBytes(v);
            writer.Instructions.Add(new Instruction(GetOpcode("addofs"), destRegister, vBytes.Item1, vBytes.Item2));

            return writer.Result;
        }

        protected CodeWriter.CodeResult GenerateIncrementRegister(IncrementNode increment, byte reg)
        {
            CodeWriter writer = new();

            switch (_semantic.ExprTypes[increment.Target]?.Name)
            {
                case "int":
                    writer.Instructions.Add(new Instruction(GetOpcode(increment.IsIncrement ? "inci32" : "deci32"), reg));
                    break;
                case "float":
                    writer.Instructions.Add(new Instruction(GetOpcode(increment.IsIncrement ? "incf32" : "decf32"), reg));
                    break;
                default:
                    throw new CodeGeneratorException(
                        $"Cannot increment value of type '{_semantic.ExprTypes[increment.Target]?.Name}'.",
                        increment.Line,
                        increment.Column
                    );
            }

            return writer.Result;
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

        protected byte GetOpcode(string name) => OpcodeHelper.OpcodeByName[Version][name];

        protected bool DoesFunctionReturn(string fullName, IList<TypeNode> paramTypes, [NotNullWhen(true)] out TypeNode? returnType)
        {
            bool isReturn = false;
            TypeNode? type = null;
            GetFuncSymbol(fullName, paramTypes)?.Switch(
                objFunc =>
                {
                    isReturn = objFunc.ReturnType != null;
                    type = objFunc.ReturnType;
                },
                xrefFunc =>
                {
                    isReturn = xrefFunc.ReturnType != null;
                    type = xrefFunc.ReturnType;
                }
            );
            returnType = type;
            return isReturn;
        }

        protected OneOf<FunctionSymbol, XRefFunctionSymbol>? GetFuncSymbol(string fullName, IList<TypeNode> paramTypes)
        {
            string[] names = fullName.Split('.');
            if (_semantic.Module.LocalObjects.TryGetValue(names[^2], out ObjectSymbol? objSbl))
                if (objSbl.FindFunction(names[^1], paramTypes, out FunctionSymbol? objFuncSbl))
                    return objFuncSbl;
            if (_semantic.Module.XRefObjects.TryGetValue(string.Join('.', names[..^1]), out XRefSymbol? xrefSbl))
                if (xrefSbl.FindFunction(names[^1], paramTypes, out XRefFunctionSymbol? xrefFuncSbl))
                    return xrefFuncSbl;
            return null;
        }

        protected string AppendParamTypes(string fullName, IList<TypeNode> paramTypes)
        {
            StringBuilder sb = new(fullName);

            TypeNode[]? funcArgTypes = null;
            GetFuncSymbol(fullName, paramTypes)?.Switch(
                (funcSbl) => funcArgTypes = Utility.ToTypeNodes(funcSbl.Parameters),
                (xrefSbl) => funcArgTypes = xrefSbl.ArgumentTypes.ToArray()
            );
            if (funcArgTypes == null)
                throw new CodeGeneratorException($"Could not get parameter types from function symbol with name '{fullName}'.", 0, 0);

            sb.Append('(');
            if (funcArgTypes.Length > 0)
                sb.Append(GetTypeName(funcArgTypes[0]));
            for (int i = 1; i < funcArgTypes.Length; i++)
                sb.Append($",{GetTypeName(funcArgTypes[i])}");
            sb.Append(')');
            return sb.ToString();
        }

        protected CodeWriter.CodeResult GenerateFreeRegister(byte reg)
        {
            CodeWriter writer = new();

            if (_instanceRegs.TryGetValue(reg, out string? xref))
            {
                ushort v = (ushort)AddOrGetXRef(xref);
                (byte, byte) vBytes = CodeWriter.ToBytes(v);
                writer.Instructions.Add(new Instruction(GetOpcode("sppop"), reg, vBytes.Item1, vBytes.Item2));

                _instanceRegs.Remove(reg);
            }

            if (_arrayRegs.Contains(reg))
            {
                writer.Instructions.Add(new Instruction(GetOpcode("arpop"), reg));

                _arrayRegs.Remove(reg);
            }

            _registers.FreeRegister(reg);

            return writer.Result;
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

        private static string GetTypeName(TypeNode type)
        {
            string name = type.Name;
            if (type.IsRef)
                name = "ref " + name;
            if (type.IsConst)
                name = "const " + name;
            return name;
        }
    }
}
