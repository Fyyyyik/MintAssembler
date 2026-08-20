using Mint.AstNodes;
using Mint.Util;
using Mint.Semantics.Symbols;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Mint.Semantics
{
    public class SemanticAnalyser
    {
        // Semantic results
        private readonly Dictionary<ExprNode, ITypeNode?> _exprTypes = new();
        private readonly Dictionary<ExprNode, CallableSymbol> _exprCalls = new();
        private readonly Dictionary<ExprNode, VariableSymbol> _exprAccesses = new();
        private readonly List<SemanticError> _errors = new();

        // The other stuff
        private readonly VersionRules _rules;
        private ModuleSymbol _module;
        private ObjectSymbol? _currentClass;
        private FunctionSymbol? _currentFunction;
        private readonly ScopeStack _scopeStack = new();
        private readonly Stack<IBreakable> _currentBreakables = new();
        private readonly Stack<IContinuable> _currentContinuables = new();

        public SemanticAnalyser(VersionRules rules) => _rules = rules;

        public SemanticResult Analyse(ModuleNode module, out ModuleNode rewrittenModule)
        {
            // Pass 1 - Initial symbol table for rewriter
            BuildSymbolTable(module);

            // Pass 2
            rewrittenModule = new Rewriter(_module).Rewrite(module);

            // Pass 3 - New symbol table with new information from rewriter
            BuildSymbolTable(rewrittenModule);

            // Pass 4 - type checking
            foreach (ObjectNode obj in rewrittenModule.Objects)
                if (obj.Location == ObjectLocation.Local)
                    AnalyseObject(obj);

            return new SemanticResult(_module, _exprTypes, _exprCalls, _exprAccesses, _errors);
        }

        private void BuildSymbolTable(ModuleNode module)
        {
            _module = new() { Name = module.FullName };
            foreach (ObjectBaseNode objBase in module.Objects)
            {
                if (objBase is EnumNode enumNode)
                {
                    EnumSymbol enumSbl = new()
                    {
                        Name = enumNode.Location == ObjectLocation.Local ? GetFullObjectName(enumNode.Name) : enumNode.Name
                    };
                    enumSbl.Elements.AddRange(enumNode.Elements);
                    _module.Enums.Add(enumNode.Name, enumSbl);
                }
                else if (objBase is ObjectNode obj)
                {
                    if (objBase.Location == ObjectLocation.Local)
                    {
                        ObjectSymbol objSbl = new()
                        {
                            FullName = GetFullObjectName(objBase.Name)
                        };

                        foreach (MemberNode member in obj.Members)
                            switch (member)
                            {
                                case VariableNode varNode:
                                    objSbl.Variables.Add(varNode.Name, new VariableSymbol()
                                    {
                                        Name = varNode.Name,
                                        Type = varNode.Type
                                    });
                                    break;
                                case FunctionNode funcNode:
                                    FunctionSymbol funcSbl = new(funcNode.ReturnType, funcNode.Name, funcNode.IsConst, funcNode.HasThis)
                                    {
                                        CallLoc = objBase.Location
                                    };
                                    funcSbl.Parameters.AddRange(funcNode.Params);
                                    objSbl.Functions.Add(funcSbl);
                                    break;
                                case ConstructorNode ctNode:
                                    ConstructorSymbol ctSbl = new()
                                    {
                                        CallLoc = objBase.Location
                                    };
                                    ctSbl.Parameters.AddRange(ctNode.Params);
                                    objSbl.Constructors.Add(ctSbl);
                                    break;
                            }
                        _module.LocalObjects.Add(obj.Name, objSbl);
                    }
                    else
                    {
                        XRefSymbol xrefSbl = new()
                        {
                            FullName = obj.Name,
                            Loc = obj.Location
                        };

                        foreach (MemberNode member in obj.Members)
                            switch (member)
                            {
                                case VariableNode varNode:
                                    xrefSbl.Variables.Add(varNode.Name, new VariableSymbol()
                                    {
                                        Name = varNode.Name,
                                        Type = varNode.Type
                                    });
                                    break;
                                case ExternalFunctionNode xrefFuncNode:
                                    XRefFunctionSymbol xrefFuncSbl = new(xrefFuncNode.ReturnType, xrefFuncNode.Name, xrefFuncNode.IsConst)
                                    {
                                        CallLoc = objBase.Location
                                    };
                                    xrefFuncSbl.ParamTypes.AddRange(xrefFuncNode.ParamTypes);
                                    xrefSbl.Functions.Add(xrefFuncSbl);
                                    break;
                                case ExternalConstructorNode xrefCtNode:
                                    XRefConstructorSymbol xrefCtSbl = new()
                                    {
                                        CallLoc = objBase.Location
                                    };
                                    xrefCtSbl.ParamTypes.AddRange(xrefCtNode.ParamTypes);
                                    xrefSbl.Constructors.Add(xrefCtSbl);
                                    break;
                            }
                        if (_module.XRefObjects.TryGetValue(obj.Name, out XRefSymbol? preXRefSbl))
                        {
                            foreach (KeyValuePair<string, VariableSymbol> varSbl in xrefSbl.Variables)
                                if (!preXRefSbl.Variables.ContainsKey(varSbl.Key))
                                    preXRefSbl.Variables.Add(varSbl.Key, varSbl.Value);
                            foreach (XRefFunctionSymbol xrefFuncSbl in xrefSbl.Functions)
                                if (!preXRefSbl.FindFunction(xrefFuncSbl.GetName(), xrefFuncSbl.GetParamTypes(), out _))
                                    preXRefSbl.Functions.Add(xrefFuncSbl);
                            foreach (XRefConstructorSymbol xrefCtSbl in xrefSbl.Constructors)
                                if (!preXRefSbl.FindConstructor(xrefCtSbl.GetParamTypes(), out _))
                                    preXRefSbl.Constructors.Add(xrefCtSbl);
                        }
                        else _module.XRefObjects.Add(obj.Name, xrefSbl);
                    }
                }
                else throw new NotImplementedException($"Could not build symbols for '{objBase.Name}'. Unknown object type.");
            }
        }

        private void AnalyseObject(ObjectNode obj)
        {
            _currentClass = _module.LocalObjects[obj.Name];

            // TODO : add check for base class

            foreach (MemberNode member in obj.Members)
                switch (member)
                {
                    case VariableNode varNode:
                        AnalyseVariable(varNode);
                        break;
                    case FunctionNode function:
                        AnalyseFunction(function);
                        break;
                }
        }

        private void AnalyseVariable(VariableNode varNode)
        {
            if (varNode.Initializer == null) return;

            ITypeNode? initType = AnalyseExpr(varNode.Initializer);
            if (!TypesMatch(varNode.Type, initType))
                AddError($"Cannot initialize variable '{varNode.Name}' with type '{varNode.Type.GetTypeName()}' " +
                    $"with value of type '{initType?.GetTypeName()}'.", varNode);
        }

        private void AnalyseFunction(FunctionNode function)
        {
            _currentClass!.FindFunction(function.Name, Utility.ToTypeNodes(function.Params), out _currentFunction);
            _scopeStack.PushScope();

            // Define every argument in the scope
            foreach (ParamNode param in function.Params)
                _scopeStack.Define(param.Name, param.Type);

            AnalyseBlock(function.Body);
            _scopeStack.PopScope();
        }

        private void AnalyseBlock(BlockNode block)
        {
            _scopeStack.PushScope();
            foreach (StmtNode stmt in block.Statements)
                AnalyseStatement(stmt);
            _scopeStack.PopScope();
        }

        private void AnalyseStatement(StmtNode stmt)
        {
            switch (stmt)
            {
                case VarDeclNode varDecl:
                    // Check if the name is proper
                    if (IsObject(varDecl.Name))
                    {
                        AddError("Expected a name that's not an object.", varDecl);
                        break;
                    }

                    if (_scopeStack.LookUp(varDecl.Name) != null)
                    {
                        AddError($"A variable with the name '{varDecl.Name}' has already been declared.", varDecl);
                        break;
                    }

                    if (varDecl.Initializer != null)
                    {
                        if (varDecl.Type.IsArray() && varDecl.Initializer is ArrayInitNode arrayInit)
                        {
                            int size = varDecl.Type.GetArraySize();
                            if (size != 0 && size != arrayInit.Initializers.Count)
                                AddError($"Array initialization contains {arrayInit.Initializers.Count} elements" +
                                    $"but variable declaration expects {size} elements.", varDecl);
                            AnalyseExpr(varDecl.Initializer);
                        }
                        else
                        {
                            ITypeNode? initType = AnalyseExpr(varDecl.Initializer);
                            if (initType == null)
                            {
                                AddError($"Cannot assign 'void' to variable '{varDecl.Name}'.", varDecl);
                                break;
                            }

                            if (!CheckReferenceAssign(varDecl.Type, initType))
                                if (!TypesMatch(varDecl.Type, initType))
                                    AddError(
                                        $"Cannot assign '{initType?.GetTypeName()}' to variable of type '{varDecl.Type.GetTypeName()}'",
                                        varDecl
                                    );
                        }
                    }

                    _scopeStack.Define(varDecl.Name, varDecl.Type);
                    break;

                case AssignNode assign:
                    // First catch if we're trying to assign to something that can't be assigned to
                    if (assign.Target is not IAssignable)
                    {
                        AddError("Assignment is not allowed.", assign);
                        break;
                    }
                    ITypeNode? assignType = AnalyseExpr(assign.Value);
                    ITypeNode? targetType = AnalyseExpr(assign.Target);
                    if (targetType == null)
                    {
                        AddError("Assignment to 'void' is not allowed.", assign);
                        break;
                    }
                    if (assignType == null)
                    {
                        AddError("Cannot assign 'void' to assignable.", assign);
                        break;
                    }
                    if (targetType is ConstTypeNode)
                    {
                        AddError("Assignment to a constant value isn't allowed.", assign);
                        break;
                    }
                    if (CheckReferenceAssign(targetType, assignType))
                        break;
                    if (!TypesMatch(assignType, targetType))
                        AddError(
                            $"Cannot assign '{assignType?.GetTypeName()}' to variable of type '{targetType?.GetTypeName()}'",
                            assign
                        );
                    break;

                case IfNode ifStmt:
                    ITypeNode? condType = AnalyseExpr(ifStmt.Condition);
                    if (condType?.GetBaseType().Name != "bool")
                        AddError("If condition must be a bool", ifStmt);
                    AnalyseBlock(ifStmt.Then);
                    if (ifStmt.ElseIf != null)
                        AnalyseStatement(ifStmt.ElseIf);
                    if (ifStmt.Else != null)
                        AnalyseBlock(ifStmt.Else);
                    break;

                case WhileNode whileStmt:
                    ITypeNode? whileCondType = AnalyseExpr(whileStmt.Condition);
                    if (whileCondType?.GetBaseType().Name != "bool")
                        AddError("While condition must be a bool", whileStmt);

                    _currentBreakables.Push(whileStmt);
                    _currentContinuables.Push(whileStmt);
                    AnalyseBlock(whileStmt.Body);
                    _currentContinuables.Pop();
                    _currentBreakables.Pop();
                    break;

                case ForNode forStmt:
                    AnalyseStatement(forStmt.Initializer);
                    ITypeNode? forCondType = AnalyseExpr(forStmt.Condition);
                    if (forCondType?.GetBaseType().Name != "bool")
                        AddError("For condition must be a bool", forStmt);
                    AnalyseStatement(forStmt.Increment);

                    _currentBreakables.Push(forStmt);
                    _currentContinuables.Push(forStmt);
                    AnalyseBlock(forStmt.Body);
                    _currentContinuables.Pop();
                    _currentBreakables.Pop();
                    break;

                case ReturnNode ret:
                    ITypeNode? funcRetType = _currentFunction?.GetReturnType();
                    if (funcRetType == null)
                    {
                        if (ret.Value != null)
                            AddError($"Local function with name '{_currentFunction?.GetName()}' shouldn't return anything.", ret);
                        break;
                    }
                    ITypeNode? retType = ret.Value != null ? AnalyseExpr(ret.Value) : null;
                    if (!TypesMatch(retType, funcRetType))
                        AddError(
                            $"Return type '{retType?.GetTypeName()}' does not match function return type '{funcRetType.GetTypeName()}'",
                            ret
                        );
                    break;

                case ExprStmtNode exprStmt:
                    AnalyseExpr(exprStmt.Expr);
                    break;

                case YieldNode yield:
                    ITypeNode? frameCountType = AnalyseExpr(yield.FrameCount);
                    if (frameCountType == null || frameCountType.GetBaseType().Name != "int")
                        AddError("Yield frame count must be an int.", yield);
                    break;

                case SwitchNode swi:
                    ITypeNode? valueType = AnalyseExpr(swi.Value);
                    if (valueType == null)
                    {
                        AddError("Switch compared value must not be 'void'.", swi);
                        break;
                    }

                    if (valueType.IsRef())
                        AddError("Switch statement value cannot be a reference.", swi);
                    if (valueType.IsArray())
                        AddError("Switch statement value cannot be an array.", swi);

                    _currentBreakables.Push(swi);

                    _scopeStack.PushScope();
                    if (swi.Default != null)
                        foreach (StmtNode defaultStmt in swi.Default)
                            AnalyseStatement(defaultStmt);
                    _scopeStack.PopScope();

                    foreach (var pair in swi.Cases)
                    {
                        foreach (IUnalterable unalt in pair.Key)
                        {
                            if (unalt is not ExprNode caseEntryExpr)
                            {
                                AddError("Non-unalterable expression is not an expression.", swi);
                                continue;
                            }

                            ITypeNode? caseValueType = AnalyseExpr(caseEntryExpr);
                            if (caseValueType == null)
                            {
                                AddError("Case entry value must not be 'void'.", caseEntryExpr);
                                continue;
                            }

                            if (!TypesMatch(valueType, caseValueType))
                                AddError($"Expected type '{valueType.GetTypeName()}' for case entry " +
                                    $"value but got '{caseValueType.GetTypeName()}'.", caseEntryExpr);

                            if (!caseValueType.IsConst())
                                AddError("Case entry value must be constant.", caseEntryExpr);
                        }

                        _scopeStack.PushScope();
                        foreach (StmtNode caseStmt in pair.Value)
                            AnalyseStatement(caseStmt);
                        _scopeStack.PopScope();
                    }

                    _currentBreakables.Pop();

                    break;

                case BreakNode breakNode:
                    if (_currentBreakables.Count == 0)
                        AddError("No enclosing breakable statement to break out of.", breakNode);
                    break;

                case ContinueNode continueNode:
                    if (_currentContinuables.Count == 0)
                        AddError("No enclosing continuable statement to continue in.", continueNode);
                    break;
            }
        }

        private ITypeNode? AnalyseExpr(ExprNode expr)
        {
            ITypeNode? type = expr switch
            {
                IntLiteralNode _ => new ConstTypeNode(new TypeNode("int", expr.Line, expr.Column), expr.Line, expr.Column),
                FloatLiteralNode _ => new ConstTypeNode(new TypeNode("float", expr.Line, expr.Column), expr.Line, expr.Column),
                BoolLiteralNode _ => new ConstTypeNode(new TypeNode("bool", expr.Line, expr.Column), expr.Line, expr.Column),
                StringLiteralNode _ => new ConstTypeNode(new TypeNode("string", expr.Line, expr.Column), expr.Line, expr.Column),

                IdentifierNode id => ResolveIdentifierType(id),
                QualifiedAccessNode qa => ResolveQualifiedAccessType(qa),
                MemberAccessNode ma => ResolveMemberAccessType(ma),
                ArrayAccessNode aa => ResolveArrayAccessType(aa),
                DereferenceNode dr => ResolveDereferenceType(dr),
                ThisNode ts => ResolveThisType(ts),
                BinaryExprNode be => ResolveBinaryExprType(be),
                UnaryExprNode ue => ResolveUnaryExprType(ue),
                ConditionalNode cd => ResolveConditionalType(cd),
                QualifiedCallNode qc => ResolveQualifiedCallType(qc),
                MemberCallNode mc => ResolveMemberCallType(mc),
                NewObjectNode no => ResolveNewObjectType(no),
                PushInstanceNode pi => ResolvePushInstanceType(pi),
                ArrayInitNode ai => ResolveArrayInitType(ai),
                IncrementNode inc => ResolveIncrementType(inc),
                MemberOffsetNode mo => ResolveMemberOffsetType(mo),
                TypeCastNode tc => ResolveTypeCastType(tc),
                SwitchExprNode se => ResolveSwitchExprType(se),

                _ => null
            };

            if (!_exprTypes.ContainsKey(expr))
                _exprTypes.Add(expr, type);

            return type;
        }

        private ITypeNode? ResolveIdentifierType(IdentifierNode identifier)
        {
            ITypeNode? type = _scopeStack.LookUp(identifier.Name);
            _exprTypes[identifier] = type;
            return type;
        }

        private ITypeNode? ResolveQualifiedAccessType(QualifiedAccessNode qualifiedAccess)
        {
            // Full qualified path to a static variable

            string[] names = qualifiedAccess.FullName.Split('.');
            string leadup = string.Join('.', names[..^1]);
            string varName = names[^1];

            // Check in objects
            if (_module.LocalObjects.TryGetValue(names[^2], out ObjectSymbol? locObj))
                if (locObj.Variables.TryGetValue(varName, out VariableSymbol? locVar))
                {
                    _exprAccesses[qualifiedAccess] = locVar;
                    _exprTypes[qualifiedAccess] = locVar.Type;
                    return locVar.Type;
                }

            // Check in xrefs
            if (_module.XRefObjects.TryGetValue(leadup, out XRefSymbol? xrefObj))
                if (xrefObj.Variables.TryGetValue(varName, out VariableSymbol? xrefVar))
                {
                    _exprAccesses[qualifiedAccess] = xrefVar;
                    _exprTypes[qualifiedAccess] = xrefVar.Type;
                    return xrefVar.Type;
                }

            AddError($"Cannot resolve '{qualifiedAccess.FullName}'.", qualifiedAccess);
            _exprTypes[qualifiedAccess] = null;
            return null;
        }

        private ITypeNode? ResolveMemberAccessType(MemberAccessNode memberAccess)
        {
            ITypeNode? exprType = AnalyseExpr(memberAccess.Object);
            if (exprType == null)
            {
                AddError("Can't access member from 'void' type.", memberAccess);
                _exprTypes[memberAccess] = null;
                return null;
            }

            // Array length property
            if (exprType.IsArray() && memberAccess.Member == "Length")
            {
                ITypeNode type = new ConstTypeNode(
                    new TypeNode("int", memberAccess.Line, memberAccess.Column),
                    memberAccess.Line,
                    memberAccess.Column
                );
                _exprTypes[memberAccess] = type;
                return type;
            }

            ITypeNode? accessType = ResolveMemberType(exprType.GetBaseType(), memberAccess.Member, out VariableSymbol? accSbl);
            if (accSbl != null)
                _exprAccesses[memberAccess] = accSbl;
            _exprTypes[memberAccess] = accessType;
            return accessType;
        }

        private ITypeNode? ResolveArrayAccessType(ArrayAccessNode arrayAccess)
        {
            ITypeNode? arrayType = AnalyseExpr(arrayAccess.Array);
            ITypeNode? indexType = AnalyseExpr(arrayAccess.Index);

            if (indexType == null)
            {
                AddError("Array index must not be 'void'.", arrayAccess);
                _exprTypes[arrayAccess] = null;
                return null;
            }

            if (indexType.IsRef() || indexType.IsArray())
                AddError("Index type cannot be a reference or an array.", arrayAccess);

            if (indexType.GetBaseType().Name != "int")
                AddError("Array index must be an int.", arrayAccess);

            if (arrayType == null || !arrayType.IsArray())
            {
                AddError("Cannot index into a non-array type.", arrayAccess);
                _exprTypes[arrayAccess] = null;
                return null;
            }

            ITypeNode type = arrayType.GetArrayAccess();
            _exprTypes[arrayAccess] = type;
            return type;
        }

        private ITypeNode? ResolveDereferenceType(DereferenceNode dereference)
        {
            ITypeNode? type = AnalyseExpr(dereference.Reference);
            if (type == null)
            {
                AddError("Cannot dereference 'void'.", dereference);
                _exprTypes[dereference] = null; 
                return null;
            }
            if (!_rules.Dereferenceable.Contains(type.GetBaseType().Name))
            {
                AddError($"Cannot dereference type '{type.GetTypeName()}' in this version.", dereference);
                _exprTypes[dereference] = null;
                return null;
            }
            if (!type.IsRef())
            {
                AddError("Cannot dereference non-reference type.", dereference);
                _exprTypes[dereference] = null;
                return null;
            }
            type = type.GetDereference();
            _exprTypes[dereference] = type;
            return type;
        }

        private ITypeNode? ResolveThisType(ThisNode ts)
        {
            if (_currentClass == null)
            {
                AddError("Cannot use keyword 'this' outside of a class.", ts);
                _exprTypes[ts] = null;
                return null;
            }

            ITypeNode type = new ConstTypeNode(
                new RefTypeNode(
                    new TypeNode(_currentClass.FullName, ts.Line, ts.Column),
                    ts.Line,
                    ts.Column
                ),
                ts.Line,
                ts.Column
            );
            _exprTypes[ts] = type;
            return type;
        }

        private ITypeNode? ResolveBinaryExprType(BinaryExprNode binaryExpr)
        {
            ITypeNode? left = AnalyseExpr(binaryExpr.Left);
            ITypeNode? right = AnalyseExpr(binaryExpr.Right);

            // Comparison operators produce bool
            if (binaryExpr.Op is "==" or "!=" or ">" or "<" or ">=" or "<=" or "&&" or "||")
            {
                if (!TypesMatch(left, right))
                    AddError($"Cannot compare '{left?.GetTypeName()}' and '{right?.GetTypeName()}'.", binaryExpr);

                ITypeNode type = new ConstTypeNode(
                    new TypeNode("bool", binaryExpr.Line, binaryExpr.Column),
                    binaryExpr.Line,
                    binaryExpr.Column
                );
                _exprTypes[binaryExpr] = type;
                return type;
            }

            // Bit and shift operations require int
            // Why the fuck would you want to do that to a float you psycho
            if (binaryExpr.Op is "&" or "|" or "<<" or ">>" or "^")
            {
                if (left?.GetBaseType().Name != "int" || right?.GetBaseType().Name != "int")
                    AddError($"Operator '{binaryExpr.Op}' requires int operands.", binaryExpr);
                ITypeNode type = new ConstTypeNode(
                    new TypeNode("int", binaryExpr.Line, binaryExpr.Column),
                    binaryExpr.Line,
                    binaryExpr.Column
                );
                _exprTypes[binaryExpr] = type;
                return type;
            }

            // Arithmetic operators
            if (!TypesMatch(left, right))
            {
                AddError($"Cannot apply operator '{binaryExpr.Op}' to operands of type '{left?.GetTypeName()}' and '{right?.GetTypeName()}'.", binaryExpr);
                _exprTypes[binaryExpr] = null;
                return null;
            }

            _exprTypes[binaryExpr] = left;
            return left;
        }

        private ITypeNode? ResolveUnaryExprType(UnaryExprNode unaryExpr)
        {
            ITypeNode? operand = AnalyseExpr(unaryExpr.Operand);

            if (operand == null)
            {
                AddError("Cannot use unary operator on 'void'.", unaryExpr);
                _exprTypes[unaryExpr] = null;
                return null;
            }

            if (unaryExpr.Op == "-" && operand.GetBaseType().Name is not ("int" or "float"))
                AddError("Unary operator '-' requires an int or a float.", unaryExpr);

            if (unaryExpr.Op == "!" && operand.GetBaseType().Name != "bool")
                AddError("Unary operator '!' requires a bool.", unaryExpr);

            _exprTypes[unaryExpr] = operand;
            return operand;
        }

        private ITypeNode? ResolveConditionalType(ConditionalNode conditional)
        {
            ITypeNode? condType = AnalyseExpr(conditional.Condition);
            if (condType == null)
            {
                AddError("Condition of conditional cannot be 'void'.", conditional);
                _exprTypes[conditional] = null;
                return null;
            }

            if (condType.IsRef())
                AddError("Condition of conditional cannot be a reference.", conditional);
            if (condType.IsArray())
                AddError("Condition of conditional cannot be an array.", conditional);

            if (condType.GetBaseType().Name != "bool")
                AddError($"Expect type 'bool' for condition of conditional but got '{condType.GetBaseType().Name}'.", conditional);

            ITypeNode? trueType = AnalyseExpr(conditional.ValueIfTrue);
            ITypeNode? falseType = AnalyseExpr(conditional.ValueIfFalse);
            if (!TypesMatch(trueType, falseType))
                AddError("Type of values from conditional expression must match.", conditional);

            _exprTypes[conditional] = trueType;
            return trueType;
        }

        private ITypeNode? ResolveQualifiedCallType(QualifiedCallNode qualifiedCall)
        {
            string[] names = qualifiedCall.FullName.Split('.');
            string leadup = string.Join('.', names[..^1]);
            string funcName = names[^1];

            List<ITypeNode> argTypes = ResolveCallArgs(qualifiedCall.Args, qualifiedCall);

            if (_module.LocalObjects.TryGetValue(names[^2], out ObjectSymbol? locObj))
                if (locObj.FindFunction(funcName, argTypes, out FunctionSymbol? objFunc))
                {
                    CheckArguments(objFunc.GetParamTypes(), qualifiedCall.Args, qualifiedCall, funcName);
                    _exprCalls[qualifiedCall] = objFunc;
                    ITypeNode? retType = objFunc.GetReturnType();
                    _exprTypes[qualifiedCall] = retType;
                    return retType;
                }

            if (_module.XRefObjects.TryGetValue(leadup, out XRefSymbol? xrefCls))
                if (xrefCls.FindFunction(funcName, argTypes, out XRefFunctionSymbol? xrefFunc))
                {
                    CheckArguments(xrefFunc.GetParamTypes(), qualifiedCall.Args, qualifiedCall, funcName);
                    _exprCalls[qualifiedCall] = xrefFunc;
                    ITypeNode? retType = xrefFunc.GetReturnType();
                    _exprTypes[qualifiedCall] = retType;
                    return retType;
                }

            AddError($"Cannot resolve '{qualifiedCall.FullName}'.", qualifiedCall);
            _exprTypes[qualifiedCall] = null;
            return null;
        }

        private ITypeNode? ResolveMemberCallType(MemberCallNode memberCall)
        {
            ITypeNode? exprType = AnalyseExpr(memberCall.Object);
            if (exprType == null)
            {
                AddError("Can't call function from 'void' type.", memberCall);
                _exprTypes[memberCall] = null;
                return null;
            }

            List<ITypeNode> argTypes = ResolveCallArgs(memberCall.Args, memberCall);

            if (_module.LocalObjects.TryGetValue(exprType.GetBaseType().Name.Split('.')[^1], out ObjectSymbol? obj))
            {
                if (obj.FindFunction(memberCall.Name, argTypes, out FunctionSymbol? funcSbl))
                {
                    CheckArguments(funcSbl.GetParamTypes(), memberCall.Args, memberCall, memberCall.Name);
                    _exprCalls[memberCall] = funcSbl;
                    ITypeNode? retType = funcSbl.GetReturnType();
                    _exprTypes[memberCall] = retType;
                    return retType;
                }
            }

            if (_module.XRefObjects.TryGetValue(exprType.GetBaseType().Name, out XRefSymbol? xrefObj))
            {
                if (xrefObj.FindFunction(memberCall.Name, argTypes, out XRefFunctionSymbol? xrefFunc))
                {
                    CheckArguments(xrefFunc.GetParamTypes(), memberCall.Args, memberCall, memberCall.Name);
                    _exprCalls[memberCall] = xrefFunc;
                    ITypeNode? retType = xrefFunc.GetReturnType();
                    _exprTypes[memberCall] = retType;
                    return retType;
                }
            }

            AddError($"'{exprType.GetTypeName()}' is not a known object.", memberCall);
            _exprTypes[memberCall] = null;
            return null;
        }

        private ITypeNode? ResolveNewObjectType(NewObjectNode newObject)
        {
            ITypeNode type = new RefTypeNode(
                new TypeNode(newObject.ClassName, newObject.Line, newObject.Column),
                newObject.Line, newObject.Column
            );
            _exprTypes[newObject] = type;
            return type;
        }

        private ITypeNode? ResolvePushInstanceType(PushInstanceNode pushInstance)
        {
            ITypeNode type = new RefTypeNode(
                new TypeNode(pushInstance.ObjectName, pushInstance.Line, pushInstance.Column),
                pushInstance.Line,
                pushInstance.Column
            );
            _exprTypes[pushInstance] = type;

            if (pushInstance.CtArgs == null)
                return type;

            List<ITypeNode> argTypes = ResolveCallArgs(pushInstance.CtArgs, pushInstance);

            if (_module.LocalObjects.TryGetValue(pushInstance.ObjectName, out ObjectSymbol? objSbl))
                if (objSbl.FindConstructor(argTypes, out ConstructorSymbol? ctSbl))
                {
                    _exprCalls[pushInstance] = ctSbl;
                    return type;
                }
            if (_module.XRefObjects.TryGetValue(pushInstance.ObjectName, out XRefSymbol? xrefSbl))
                if (xrefSbl.FindConstructor(argTypes, out XRefConstructorSymbol? xrefCtSbl))
                {
                    _exprCalls[pushInstance] = xrefCtSbl;
                    return type;
                }

            AddError($"'{type.GetTypeName()}' has no constructor with the specified parameter types.", pushInstance);
            return type;
        }

        private ITypeNode? ResolveArrayInitType(ArrayInitNode arrayInit)
        {
            ITypeNode? arrayEleType = arrayInit.Initializers.Count > 0 ? AnalyseExpr(arrayInit.Initializers[0]) : null;
            ITypeNode? nextType = arrayEleType;
            for (int i = 1; i <= arrayInit.Initializers.Count; i++)
            {
                if (nextType == null)
                {
                    AddError($"Element {i} of array initialization is 'void'.", arrayInit);
                    _exprTypes[arrayInit] = null;
                    return null;
                }
                if (i == arrayInit.Initializers.Count)
                    break;

                nextType = AnalyseExpr(arrayInit.Initializers[i]);
                if (!TypesMatch(arrayEleType, nextType))
                    AddError(
                        $"Expected type '{arrayEleType?.GetTypeName()}' for element {i} of array initialization but got type '{nextType?.GetTypeName()}'.",
                        arrayInit
                    );
            }

            if (arrayEleType == null)
                arrayEleType = new TypeNode("int", arrayInit.Line, arrayInit.Column); // array has 0 elements so type is whatever

            ITypeNode type = new ConstTypeNode(
                new ArrayTypeNode(arrayEleType, arrayInit.Initializers.Count, arrayInit.Line, arrayInit.Column),
                arrayInit.Line,
                arrayInit.Column
            );
            _exprTypes[arrayInit] = type;
            return type;
        }

        private ITypeNode? ResolveIncrementType(IncrementNode increment)
        {
            ITypeNode? type = AnalyseExpr(increment.Target);
            if (type == null)
            {
                AddError("Cannot increment or decrement type 'void'.", increment);
                _exprTypes[increment] = null;
                return null;
            }

            if (type.IsRef())
                AddError("Cannot increment or decrement reference.", increment);
            if (type.IsConst())
                AddError("Cannot increment or decrement constant.", increment);
            if (type.IsArray())
                AddError("Cannot increment or decrement array.", increment);

            if (type.GetBaseType().Name is "int" or "float")
            {
                _exprTypes[increment] = type;
                return type;
            }

            AddError($"Increment and decrement operations are only allowed on types 'int' and 'float'. Not type '{type.GetTypeName()}'.", increment);
            _exprTypes[increment] = null;
            return null;
        }

        private ITypeNode? ResolveMemberOffsetType(MemberOffsetNode memberOffset)
        {
            ITypeNode? objType = AnalyseExpr(memberOffset.Object);
            if (objType == null)
            {
                AddError($"Cannot get offset for member '{memberOffset.Member}' from 'void'.", memberOffset);
                _exprTypes[memberOffset] = null;
                return null;
            }

            ITypeNode? memberType = ResolveMemberType(objType.GetBaseType(), memberOffset.Member, out VariableSymbol? accSbl);

            if (accSbl != null)
                _exprAccesses[memberOffset] = accSbl;

            if (memberType == null)
            {
                _exprTypes[memberOffset] = null;
                return null;
            }

            memberType = new RefTypeNode(memberType, memberOffset.Line, memberOffset.Column);
            _exprTypes[memberOffset] = memberType;
            return memberType;
        }

        private ITypeNode? ResolveTypeCastType(TypeCastNode typeCast)
        {
            ITypeNode? exprType = AnalyseExpr(typeCast.Expr);
            if (exprType == null)
            {
                AddError($"Cannot cast 'void' to type '{typeCast.Type}'.", typeCast);
                _exprTypes[typeCast] = null;
                return null;
            }

            if (exprType.IsRef())
                AddError("Cannot cast reference type.", typeCast);
            if (exprType.IsArray())
                AddError("Cannot cast array type.", typeCast);

            TypeNode castTypeNode = new TypeNode(typeCast.Type, typeCast.Line, typeCast.Column);

            if (exprType.GetBaseType().Name == typeCast.Type)
            {
                _exprTypes[typeCast] = castTypeNode;
                return castTypeNode;
            }

            if (!_rules.AllowedCasts.TryGetValue(exprType.GetBaseType().Name, out string[]? allowedTypes))
            {
                AddError($"Type '{exprType.GetTypeName()}' cannot be casted to another type.", typeCast);
                _exprTypes[typeCast] = null;
                return null;
            }

            if (!allowedTypes.Contains(typeCast.Type))
            {
                AddError($"Type '{exprType.GetTypeName()}' cannot be casted to type '{typeCast.Type}'.", typeCast);
                _exprTypes[typeCast] = null;
                return null;
            }

            _exprTypes[typeCast] = castTypeNode;
            return castTypeNode;
        }

        private ITypeNode? ResolveSwitchExprType(SwitchExprNode switchExpr)
        {
            ITypeNode? valueType = AnalyseExpr(switchExpr.Value);
            if (valueType == null)
            {
                AddError("Value for switch expression cannot be 'void'.", switchExpr);
                _exprTypes[switchExpr] = null;
                return null;
            }

            if (valueType.IsRef())
                AddError("Value for switch expression cannot be a reference.", switchExpr);
            if (valueType.IsArray())
                AddError("Value for switch expression cannot be an array.", switchExpr);

            ITypeNode? returnType = null;
            foreach (var pair in switchExpr.Cases)
            {
                foreach (IUnalterable unalterable in pair.Key)
                {
                    ITypeNode? entryType = AnalyseExpr(unalterable.GetExpr());
                    if (entryType == null)
                    {
                        AddError("Entry case expression cannot be 'void'.", switchExpr);
                        continue;
                    }

                    if (!TypesMatch(valueType, entryType))
                        AddError($"Expected type '{valueType.GetTypeName()}' but got '{entryType.GetTypeName()}' for case entry expression.", switchExpr);
                }

                ITypeNode? caseValueType = AnalyseExpr(pair.Value);
                if (caseValueType == null)
                    AddError("Case value for switch expression cannot be 'void'.", switchExpr);
                else if (returnType == null)
                    returnType = caseValueType;
                else if (!TypesMatch(returnType, caseValueType))
                    AddError($"Expected established type '{returnType}' for switch expression case value but got '{caseValueType}'.", switchExpr);
            }

            ITypeNode? defaultType = AnalyseExpr(switchExpr.Default);
            if (defaultType == null)
                AddError("Default case value in switch expression cannot be 'void'.", switchExpr);

            if (returnType == null)
                returnType = defaultType; // last chance to actually figure out the type of this thing if we haven't

            if (returnType == null)
                AddError("Couldn't resolve switch expression type, no case could specify a proper type.", switchExpr);
            _exprTypes[switchExpr] = returnType;
            return returnType;
        }

        private ITypeNode? ResolveMemberType(TypeNode objType, string member, out VariableSymbol? accessSbl)
        {
            accessSbl = null;

            // Find it in the classes (or xrefs)
            if (_module.LocalObjects.TryGetValue(objType.Name, out ObjectSymbol? obj))
            {
                if (obj.Variables.TryGetValue(member, out VariableSymbol? varSbl))
                {
                    accessSbl = varSbl;
                    return varSbl.Type;
                }

                AddError($"'{objType.Name}' has no member '{member}'.", objType);
                return null;
            }

            if (_module.XRefObjects.TryGetValue(objType.Name, out XRefSymbol? xrefObj))
            {
                if (xrefObj.Variables.TryGetValue(member, out VariableSymbol? xrefVar))
                {
                    accessSbl = xrefVar;
                    return xrefVar.Type;
                }

                AddError($"'{objType.Name}' has no member '{member}'. Did you forget to reference it ?", objType);
                return null;
            }

            AddError($"'{objType.Name}' is not a known object. Did you forget to reference it ?", objType);
            return null;
        }

        private List<ITypeNode> ResolveCallArgs(IList<ExprNode> args, AstNode context)
        {
            List<ITypeNode> argTypes = new();
            foreach (ExprNode arg in args)
            {
                ITypeNode? argType = AnalyseExpr(arg);
                if (argType == null)
                {
                    AddError("Argument needs to have a defined type.", context);
                    continue;
                }
                argTypes.Add(argType);
            }
            return argTypes;
        }

        private void CheckArguments(IList<ITypeNode> expectedTypes, IList<ExprNode> arguments, AstNode context, string funcName)
        {
            if (expectedTypes.Count != arguments.Count)
            {
                AddError($"Expected {expectedTypes.Count} arguments but got {arguments.Count} instead.", context);
                return;
            }

            for (int i = 0; i < expectedTypes.Count; i++)
            {
                ITypeNode? argType = AnalyseExpr(arguments[i]);
                if (!TypesMatch(expectedTypes[i], argType))
                    AddError($"Argument {i + 1} of {funcName} expects '{expectedTypes[i].GetTypeName()}' but got '{argType?.GetTypeName()}'.", context);
            }
        }

        internal static bool TypesMatch(ITypeNode? a, ITypeNode? b)
        {
            if (a == null || b == null) return false;

            ITypeNode tempA = a, tempB = b;
            while (tempA is not TypeNode && tempB is not TypeNode)
            {
                if (tempA is ConstTypeNode constTypeA)
                {
                    tempA = constTypeA.Type;
                    continue;
                }

                if (tempB is ConstTypeNode constTypeB)
                {
                    tempB = constTypeB.Type;
                    continue;
                }

                if (tempA.GetType() != tempB.GetType())
                    return false;

                if (tempA is ArrayTypeNode arrayTypeA && tempB is ArrayTypeNode arrayTypeB && arrayTypeA.Size != arrayTypeB.Size)
                    return false;

                tempA = tempA.GetChildType();
                tempB = tempB.GetChildType();
            }

            while (tempA is ConstTypeNode)
                tempA = tempA.GetChildType();
            while (tempB is ConstTypeNode)
                tempB = tempB.GetChildType();

            if ((tempA is TypeNode || tempB is TypeNode) && tempA.GetType() != tempB.GetType())
                return false;

            return a.GetBaseType().Name == b.GetBaseType().Name;
        }

        private void AddError(string message, AstNode node)
            => _errors.Add(new SemanticError(message, node));

        private bool IsObject(string name) => _module.LocalObjects.ContainsKey(name) || _module.XRefObjects.ContainsKey(name);

        private string GetFullObjectName(string objName) => $"{NameOperations.GetParent(_module.Name)}.{objName}";

        // Special exceptions related to assigning references
        private bool CheckReferenceAssign(ITypeNode targetType, ITypeNode valueType)
        {
            if (targetType.IsRef() && !valueType.IsRef() && valueType.GetBaseType().Name == "int")
                return true;

            // automatic reinterpretation to ref
            if (targetType.IsRef() && !valueType.IsRef() && IsObject(valueType.GetBaseType().Name))
                return true;

            return false;
        }
    }
}
