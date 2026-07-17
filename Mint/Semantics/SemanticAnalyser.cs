using Mint.AstNodes;
using Mint.Util;

namespace Mint.Semantics
{
    public class SemanticAnalyser
    {
        private readonly VersionRules _rules;
        private readonly Dictionary<ExprNode, TypeNode?> _exprTypes = new();
        private readonly List<SemanticError> _errors = new();
        private ModuleSymbol _module;
        private ObjectSymbol? _currentClass;
        private FunctionSymbol? _currentFunction;
        private readonly ScopeStack _scopeStack = new();

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

            return new SemanticResult(_module, _exprTypes, _errors);
        }

        private void BuildSymbolTable(ModuleNode module)
        {
            _module = new() { Name = module.FullName };
            foreach (ObjectNode obj in module.Objects)
            {
                if (obj.Location == ObjectLocation.Local)
                {
                    ObjectSymbol objSbl = new()
                    {
                        FullName = GetFullObjectName(obj.Name)
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
                                FunctionSymbol funcSbl = new()
                                {
                                    Name = funcNode.Name,
                                    ReturnType = funcNode.ReturnType,
                                    HasThis = funcNode.HasThis
                                };
                                funcSbl.Parameters.AddRange(funcNode.Params);
                                objSbl.Functions.Add(funcSbl);
                                break;
                        }
                    _module.LocalObjects.Add(obj.Name, objSbl);
                }
                else
                {
                    XRefSymbol xrefSbl = new()
                    {
                        FullName = obj.Name
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
                                XRefFunctionSymbol xrefFuncSbl = new()
                                {
                                    Name = xrefFuncNode.Name,
                                    ReturnType = xrefFuncNode.ReturnType
                                };
                                xrefFuncSbl.ArgumentTypes.AddRange(xrefFuncNode.ParamTypes);
                                xrefSbl.Functions.Add(xrefFuncSbl);
                                break;
                        }
                    _module.XRefObjects.Add(obj.Name, xrefSbl);
                }
            }
        }

        private void AnalyseObject(ObjectNode obj)
        {
            _currentClass = _module.LocalObjects[obj.Name];

            // TODO : add check for base class

            foreach (MemberNode member in obj.Members)
                switch (member)
                {
                    case FunctionNode function:
                        AnalyseFunction(function);
                        break;
                }
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
                    TypeNode? initType = AnalyseExpr(varDecl.Initializer);
                    if (!TypesMatch(varDecl.Type, initType))
                        AddError(
                            $"Cannot assign '{initType?.Name}' to variable of type '{varDecl.Type.Name}'",
                            varDecl
                        );
                    _scopeStack.Define(varDecl.Name, varDecl.Type);
                    break;

                case AssignNode assign:
                    // First catch if we're trying to assign to something that can't be assigned to
                    if (!(assign.Target is IdentifierNode or QualifiedAccessNode or MemberAccessNode or ArrayAccessNode) ||
                        (assign.Target is IdentifierNode ident && IsObject(ident.Name)) ||
                        (assign.Target is QualifiedAccessNode qa && IsObject(qa.FullName)))
                    {
                        AddError("Assignment is not allowed.", assign);
                        break;
                    }
                    TypeNode? assignType = AnalyseExpr(assign.Value);
                    TypeNode? targetType = AnalyseExpr(assign.Target);
                    if (targetType == null)
                    {
                        AddError("Assignment to 'void' is not allowed.", assign);
                        break;
                    }
                    if (targetType.IsConst)
                    {
                        AddError("Assignment to a constant value isn't allowed.", assign);
                        break;
                    }
                    if (!TypesMatch(assignType, targetType))
                        AddError(
                            $"Cannot assign '{assignType?.Name}' to variable of type '{targetType?.Name}'",
                            assign
                        );
                    break;

                case IfNode ifStmt:
                    TypeNode? condType = AnalyseExpr(ifStmt.Condition);
                    if (condType?.Name != "bool")
                        AddError("If condition must be a bool", ifStmt);
                    AnalyseBlock(ifStmt.Then);
                    if (ifStmt.ElseIf != null)
                        AnalyseStatement(ifStmt.ElseIf);
                    if (ifStmt.Else != null)
                        AnalyseBlock(ifStmt.Else);
                    break;

                case WhileNode whileStmt:
                    TypeNode? whileCondType = AnalyseExpr(whileStmt.Condition);
                    if (whileCondType?.Name != "bool")
                        AddError("While condition must be a bool", whileStmt);
                    AnalyseBlock(whileStmt.Body);
                    break;

                case ForNode forStmt:
                    _scopeStack.PushScope();
                    AnalyseStatement(forStmt.Initializer);
                    TypeNode? forCondType = AnalyseExpr(forStmt.Condition);
                    if (forCondType?.Name != "bool")
                        AddError("For condition must be a bool", forStmt);
                    AnalyseStatement(forStmt.Increment);
                    AnalyseBlock(forStmt.Body);
                    _scopeStack.PopScope();
                    break;

                case ReturnNode ret:
                    if (_currentFunction?.ReturnType == null)
                        break;
                    TypeNode? retType = ret.Value != null ? AnalyseExpr(ret.Value) : null;
                    if (!TypesMatch(retType, _currentFunction?.ReturnType))
                        AddError(
                            $"Return type '{retType?.Name}' does not match function return type '{_currentFunction?.ReturnType?.Name}'",
                            ret
                        );
                    break;

                case ExprStmtNode exprStmt:
                    AnalyseExpr(exprStmt.Expr);
                    break;

                case YieldNode yield:
                    TypeNode? frameCountType = AnalyseExpr(yield.FrameCount);
                    if (frameCountType == null || frameCountType.Name != "int")
                        AddError("Yield frame count must be an int.", yield);
                    break;
            }
        }

        private TypeNode? AnalyseExpr(ExprNode expr)
        {
            TypeNode? type = expr switch
            {
                IntLiteralNode _ => new TypeNode("int", true, false, expr.Line, expr.Column),
                FloatLiteralNode _ => new TypeNode("float", true, false, expr.Line, expr.Column),
                BoolLiteralNode _ => new TypeNode("bool", true, false, expr.Line, expr.Column),
                StringLiteralNode _ => new TypeNode("string", true, false, expr.Line, expr.Column),

                IdentifierNode id => ResolveIdentifierType(id),
                QualifiedAccessNode qa => ResolveQualifiedAccessType(qa),
                MemberAccessNode ma => ResolveMemberAccessType(ma),
                ArrayAccessNode aa => ResolveArrayAccessType(aa),
                ThisNode ts => ResolveThisType(ts),
                BinaryExprNode be => ResolveBinaryExprType(be),
                UnaryExprNode ue => ResolveUnaryExprType(ue),
                QualifiedCallNode qc => ResolveQualifiedCallType(qc),
                MemberCallNode mc => ResolveMemberCallType(mc),
                NewObjectNode no => ResolveNewObjectType(no),
                PushInstanceNode pi => ResolvePushInstanceType(pi),
                ArrayCreationNode ac => ResolveArrayCreationType(ac),
                IncrementNode inc => ResolveIncrementType(inc),
                // TODO : add more types of expressions for versions with oop

                _ => null
            };

            if (!_exprTypes.ContainsKey(expr))
                _exprTypes.Add(expr, type);

            return type;
        }

        private TypeNode? ResolveIdentifierType(IdentifierNode identifier)
        {
            TypeNode? type = _scopeStack.LookUp(identifier.Name);
            _exprTypes[identifier] = type;
            return type;
        }

        private TypeNode? ResolveQualifiedAccessType(QualifiedAccessNode qualifiedAccess)
        {
            // Full qualified path to a static variable

            string[] names = qualifiedAccess.FullName.Split('.');
            string leadup = string.Join('.', names[..^1]);
            string varName = names[^1];

            // Check in objects
            if (_module.LocalObjects.TryGetValue(names[^2], out ObjectSymbol? locObj))
                if (locObj.Variables.TryGetValue(varName, out VariableSymbol? locVar))
                {
                    _exprTypes[qualifiedAccess] = locVar.Type;
                    return locVar.Type;
                }

            // Check in xrefs
            if (_module.XRefObjects.TryGetValue(leadup, out XRefSymbol? xrefObj))
                if (xrefObj.Variables.TryGetValue(varName, out VariableSymbol? xrefVar))
                {
                    _exprTypes[qualifiedAccess] = xrefVar.Type;
                    return xrefVar.Type;
                }

            AddError($"Cannot resolve '{qualifiedAccess.FullName}'.", qualifiedAccess);
            _exprTypes[qualifiedAccess] = null;
            return null;
        }

        private TypeNode? ResolveMemberAccessType(MemberAccessNode memberAccess)
        {
            TypeNode? exprType = AnalyseExpr(memberAccess.Object);
            if (exprType == null)
            {
                AddError("Can't access member from 'void' type.", memberAccess);
                _exprTypes[memberAccess] = null;
                return null;
            }

            // Array length property
            if (exprType.IsArray && memberAccess.Member == "Length")
            {
                TypeNode type = new("int", true, false, memberAccess.Line, memberAccess.Column);
                _exprTypes[memberAccess] = type;
                return type;
            }

            // Find it in the classes (or xrefs)
            if (_module.LocalObjects.TryGetValue(exprType.Name, out ObjectSymbol? obj))
            {
                if (obj.Variables.TryGetValue(memberAccess.Member, out VariableSymbol? varSbl))
                {
                    _exprTypes[memberAccess] = varSbl.Type;
                    return varSbl.Type;
                }

                AddError($"'{exprType.Name}' has no member '{memberAccess.Member}'.", memberAccess);
                _exprTypes[memberAccess] = null;
                return null;
            }

            if (_module.XRefObjects.TryGetValue(exprType.Name, out XRefSymbol? xrefObj))
            {
                if (xrefObj.Variables.TryGetValue(memberAccess.Member, out VariableSymbol? xrefVar))
                {
                    _exprTypes[memberAccess] = xrefVar.Type;
                    return xrefVar.Type;
                }

                AddError($"'{exprType.Name}' has no member '{memberAccess.Member}'. Did you forget to reference it in the xref ?", memberAccess);
                _exprTypes[memberAccess] = null;
                return null;
            }

            AddError($"'{exprType.Name}' is not a known object. Did you forget to reference it with the 'xref' keyword ?", memberAccess);
            _exprTypes[memberAccess] = null;
            return null;
        }

        private TypeNode? ResolveArrayAccessType(ArrayAccessNode arrayAccess)
        {
            TypeNode? arrayType = AnalyseExpr(arrayAccess.Array);
            TypeNode? indexType = AnalyseExpr(arrayAccess.Index);

            if (indexType?.Name != "int")
                AddError("Array index must be an int.", arrayAccess);

            if (arrayType == null || !arrayType.IsArray)
            {
                AddError("Cannot index into a non-array type.", arrayAccess);
                _exprTypes[arrayAccess] = null;
                return null;
            }

            //TypeNode type = new(arrayType.Name, ar, arrayAccess.Line, arrayAccess.Column, true);
            TypeNode type = arrayType with { IsArray = false };
            _exprTypes[arrayAccess] = type;
            return type;
        }

        private TypeNode? ResolveThisType(ThisNode ts)
        {
            if (_currentClass == null)
            {
                AddError("Cannot use keyword 'this' outside of a class.", ts);
                _exprTypes[ts] = null;
                return null;
            }

            TypeNode type = new(_currentClass.FullName, true, false, ts.Line, ts.Column);
            _exprTypes[ts] = type;
            return type;
        }

        private TypeNode? ResolveBinaryExprType(BinaryExprNode binaryExpr)
        {
            TypeNode? left = AnalyseExpr(binaryExpr.Left);
            TypeNode? right = AnalyseExpr(binaryExpr.Right);

            // Comparison operators produce bool
            if (binaryExpr.Op is "==" or "!=" or ">" or "<" or ">=" or "<=" or "&&" or "||")
            {
                if (!TypesMatch(left, right))
                    AddError($"Cannot compare '{left?.Name}' and '{right?.Name}'.", binaryExpr);
                TypeNode type = new("bool", true, false, binaryExpr.Line, binaryExpr.Column);
                _exprTypes[binaryExpr] = type;
                return type;
            }

            // Bit and shift operations require int
            // Why the fuck would you want to do that to a float you psycho
            if (binaryExpr.Op is "&" or "|" or "<<" or ">>" or "^")
            {
                if (left?.Name != "int" || right?.Name != "int")
                    AddError($"Operator '{binaryExpr.Op}' requires int operands.", binaryExpr);
                TypeNode type = new("int", true, false, binaryExpr.Line, binaryExpr.Column);
                _exprTypes[binaryExpr] = type;
                return type;
            }

            // Arithmetic operators
            if (!TypesMatch(left, right))
            {
                AddError($"Cannot apply operator '{binaryExpr.Op}' to operands of type '{left?.Name}' and '{right?.Name}'.", binaryExpr);
                _exprTypes[binaryExpr] = null;
                return null;
            }

            _exprTypes[binaryExpr] = left;
            return left;
        }

        private TypeNode? ResolveUnaryExprType(UnaryExprNode unaryExpr)
        {
            TypeNode? operand = AnalyseExpr(unaryExpr.Operand);

            if (unaryExpr.Op == "-" && operand?.Name is not ("int" or "float"))
                AddError("Unary operator '-' requires an int or a float.", unaryExpr);

            if (unaryExpr.Op == "!" && operand?.Name != "bool")
                AddError("Unary operator '!' requires a bool.", unaryExpr);

            _exprTypes[unaryExpr] = operand;
            return operand;
        }

        private TypeNode? ResolveQualifiedCallType(QualifiedCallNode qualifiedCall)
        {
            string[] names = qualifiedCall.FullName.Split('.');
            string leadup = string.Join('.', names[..^1]);
            string funcName = names[^1];

            List<TypeNode> argTypes = ResolveCallArgs(qualifiedCall.Args, qualifiedCall);

            if (_module.LocalObjects.TryGetValue(names[^2], out ObjectSymbol? locObj))
                if (locObj.FindFunction(funcName, argTypes, out FunctionSymbol? objFunc))
                {
                    CheckArguments(Utility.ToTypeNodes(objFunc.Parameters), qualifiedCall.Args, qualifiedCall, funcName);
                    _exprTypes[qualifiedCall] = objFunc.ReturnType;
                    return objFunc.ReturnType;
                }

            if (_module.XRefObjects.TryGetValue(leadup, out XRefSymbol? xrefCls))
                if (xrefCls.FindFunction(funcName, argTypes, out XRefFunctionSymbol? xrefFunc))
                {
                    CheckArguments(xrefFunc.ArgumentTypes, qualifiedCall.Args, qualifiedCall, funcName);
                    _exprTypes[qualifiedCall] = xrefFunc.ReturnType;
                    return xrefFunc.ReturnType;
                }

            AddError($"Cannot resolve '{qualifiedCall.FullName}'.", qualifiedCall);
            _exprTypes[qualifiedCall] = null;
            return null;
        }

        private TypeNode? ResolveMemberCallType(MemberCallNode memberCall)
        {
            TypeNode? exprType = AnalyseExpr(memberCall.Object);
            if (exprType == null)
            {
                AddError("Can't call function from 'void' type.", memberCall);
                _exprTypes[memberCall] = null;
                return null;
            }

            List<TypeNode> argTypes = ResolveCallArgs(memberCall.Args, memberCall);

            if (_module.LocalObjects.TryGetValue(exprType.Name.Split('.')[^1], out ObjectSymbol? obj))
            {
                if (obj.FindFunction(memberCall.Name, argTypes, out FunctionSymbol? funcSbl))
                {
                    CheckArguments(Utility.ToTypeNodes(funcSbl.Parameters), memberCall.Args, memberCall, memberCall.Name);
                    _exprTypes[memberCall] = funcSbl.ReturnType;
                    return funcSbl.ReturnType;
                }

                AddError($"Object '{obj.FullName}' does not have a function with the name '{memberCall.Name}'.", memberCall);
                _exprTypes[memberCall] = null;
                return null;
            }

            if (_module.XRefObjects.TryGetValue(exprType.Name, out XRefSymbol? xrefObj))
            {
                if (xrefObj.FindFunction(memberCall.Name, argTypes, out XRefFunctionSymbol? xrefFunc))
                {
                    CheckArguments(xrefFunc.ArgumentTypes, memberCall.Args, memberCall, memberCall.Name);
                    _exprTypes[memberCall] = xrefFunc.ReturnType;
                    return xrefFunc.ReturnType;
                }

                AddError($"XRef object '{xrefObj.FullName}' does not have a function with the name '{memberCall.Name}'.", memberCall);
                _exprTypes[memberCall] = null;
                return null;
            }

            AddError($"'{exprType.Name}' is not a known object. Did you forget to reference it with the 'xref' keyword ?", memberCall);
            _exprTypes[memberCall] = null;
            return null;
        }

        private TypeNode? ResolveNewObjectType(NewObjectNode newObject)
        {
            TypeNode type = new(newObject.ClassName, true, false, newObject.Line, newObject.Column);
            _exprTypes[newObject] = type;
            return type;
        }

        private TypeNode? ResolvePushInstanceType(PushInstanceNode pushInstance)
        {
            TypeNode type = new(pushInstance.ObjectName, true, false, pushInstance.Line, pushInstance.Column);
            _exprTypes[pushInstance] = type;
            return type;
        }

        private TypeNode? ResolveArrayCreationType(ArrayCreationNode arrayCreation)
        {
            TypeNode type = new(arrayCreation.ElementType.Name, true, false, arrayCreation.Line, arrayCreation.Column, true);
            _exprTypes[arrayCreation] = type;
            return type;
        }

        private TypeNode? ResolveIncrementType(IncrementNode increment)
        {
            TypeNode? type = AnalyseExpr(increment.Target);
            if (type == null)
            {
                AddError("Cannot increment or decrement type 'void'.", increment);
                _exprTypes[increment] = null;
                return null;
            }
            if (type.Name is "int" or "float")
            {
                _exprTypes[increment] = type;
                return type;
            }
            AddError($"Increment and decrement operations are only allowed on types 'int' and 'float'. Not type '{type.Name}'.", increment);
            _exprTypes[increment] = null;
            return null;
        }

        private List<TypeNode> ResolveCallArgs(IList<ExprNode> args, AstNode context)
        {
            List<TypeNode> argTypes = new();
            foreach (ExprNode arg in args)
            {
                TypeNode? argType = AnalyseExpr(arg);
                if (argType == null)
                {
                    AddError("Argument needs to have a defined type.", context);
                    continue;
                }
                argTypes.Add(argType);
            }
            return argTypes;
        }

        private void CheckArguments(IList<TypeNode> expectedTypes, IList<ExprNode> arguments, AstNode context, string funcName)
        {
            if (expectedTypes.Count != arguments.Count)
            {
                AddError($"Expected {expectedTypes.Count} arguments but got {arguments.Count} instead.", context);
                return;
            }

            for (int i = 0; i < expectedTypes.Count; i++)
            {
                TypeNode? argType = AnalyseExpr(arguments[i]);
                if (!TypesMatch(expectedTypes[i], argType))
                    AddError($"Argument {i + 1} of {funcName} expects '{expectedTypes[i].Name}' but got '{argType?.Name}'.", context);
            }
        }

        private static bool TypesMatch(TypeNode? a,  TypeNode? b)
        {
            if (a == null || b == null) return false;
            return a.Name == b.Name && a.IsArray == b.IsArray;
        }

        private void AddError(string message, AstNode node)
            => _errors.Add(new SemanticError(message, node));

        private bool IsObject(string name) => _module.LocalObjects.ContainsKey(name) || _module.XRefObjects.ContainsKey(name);

        private string GetFullObjectName(string objName) => $"{NameOperations.GetParent(_module.Name)}.{objName}";
    }
}
