using Mint.AstNodes;

namespace Mint.Semantics
{
    public class SemanticAnalyser
    {
        private readonly VersionRules _rules;
        private readonly Dictionary<ExprNode, TypeNode> _exprTypes = new();
        private readonly List<SemanticError> _errors = new();
        private ModuleSymbol _module;
        private ObjectSymbol? _currentClass;
        private FunctionSymbol? _currentFunction;
        private readonly ScopeStack _scopeStack = new();

        public SemanticAnalyser(VersionRules rules) => _rules = rules;

        public SemanticResult Analyse(ModuleNode module)
        {
            _module = new() { Namespace = module.Namespace.FullName };

            // Pass 1
            BuildSymbolTable(module);

            // Pass 2 - type checking
            foreach (ObjectNode obj in module.Objects)
                AnalyseObject(obj);

            return new SemanticResult(_module, _exprTypes, _errors);
        }

        private void BuildSymbolTable(ModuleNode module)
        {
            foreach (ObjectNode obj in module.Objects)
            {
                ObjectSymbol objSymbol = new ObjectSymbol
                {
                    Name = obj.Name
                };

                foreach (MemberNode member in obj.Members)
                    switch (member)
                    {
                        case VariableNode variable:
                            objSymbol.Variables[variable.Name] = new VariableSymbol
                            {
                                Name = variable.Name,
                                Type = variable.Type
                            };
                            break;
                        case FunctionNode function:
                            objSymbol.Functions[function.Name] = new FunctionSymbol
                            {
                                Name = function.Name,
                                ReturnType = function.ReturnType
                            };
                            break;
                    }
                _module.Objects[obj.Name] = objSymbol;
            }

            foreach (ObjectNode xRef in module.XRefs)
            {
                XRefSymbol xRefSymbol = new XRefSymbol
                {
                    FullName = xRef.Name
                };

                foreach (MemberNode member in xRef.Members)
                    switch (member)
                    {
                        case VariableNode varNode:
                            xRefSymbol.Variables.Add(
                                varNode.Name,
                                new VariableSymbol() { Name = varNode.Name, Type = varNode.Type}
                            );
                            break;
                        case ExternalFunctionNode xFuncNode:
                            ExternalFunctionSymbol xFuncSbl = new()
                            {
                                Name = xFuncNode.Name,
                                ReturnType = xFuncNode.ReturnType
                            };
                            foreach (TypeNode paramType in xFuncNode.ParamTypes)
                                xFuncSbl.ArgumentTypes.Add(paramType);
                            xRefSymbol.Functions.Add(xFuncNode.Name, xFuncSbl);
                            break;
                    }
                _module.XRefs[xRef.Name] = xRefSymbol;
            }
        }

        private void AnalyseObject(ObjectNode obj)
        {
            _currentClass = _module.Objects[obj.Name];

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
            _currentFunction = _currentClass!.Functions[function.Name];
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
                    if (_module.Objects.ContainsKey(varDecl.Name))
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
                    if (!(assign.Target is IdentifierNode or QualifiedAccessNode or MemberAccessNode or ArrayAccessNode))
                    {
                        AddError("Assignement is not allowed.", assign);
                        break;
                    }
                    TypeNode? assignType = AnalyseExpr(assign.Value);
                    TypeNode? targetType = AnalyseExpr(assign.Target);
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
                        AddError($"For condition must be a bool", forStmt);
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

                case IncrementNode inc:
                    if (ResolveIdentifierType(inc.Name, inc)?.Name != "int")
                        AddError($"Cannot increment/decrement non-int variable '{inc.Name}'", inc);
                    break;
            }
        }

        private TypeNode? AnalyseExpr(ExprNode expr)
        {
            TypeNode? type = expr switch
            {
                IntLiteralNode _ => new TypeNode("int", expr.Line, expr.Column),
                FloatLiteralNode _ => new TypeNode("float", expr.Line, expr.Column),
                BoolLiteralNode _ => new TypeNode("bool", expr.Line, expr.Column),
                StringLiteralNode _ => new TypeNode("string", expr.Line, expr.Column),

                IdentifierNode id => ResolveIdentifierType(id.Name, id),
                QualifiedAccessNode qa => ResolveQualifiedAccessType(qa),
                MemberAccessNode ma => ResolveMemberAccessType(ma),
                ArrayAccessNode aa => ResolveArrayAccessType(aa),
                ThisNode ts => ResolveThisType(ts),
                BinaryExprNode be => ResolveBinaryExprType(be),
                UnaryExprNode ue => ResolveUnaryExprType(ue),
                QualifiedCallNode qc => ResolveQualifiedCallType(qc),
                MemberCallNode mc => ResolveMemberCallType(mc),
                NewObjectNode no => ResolveNewObjectType(no),
                ArrayCreationNode ac => ResolveArrayCreationType(ac),
                // TODO : add more types of expressions for versions with oop

                _ => null
            };

            if (type != null)
                _exprTypes[expr] = type;

            return type;
        }

        private TypeNode? ResolveIdentifierType(string name, AstNode context)
        {
            // In this house we refer to class variables with 'this' prefixed.
            // A single identifier is referring to a local variable.
            // It could also mean we're pushing an instance (sppsh and sppshz)
            TypeNode? type = _scopeStack.LookUp(name);
            if (type != null)
                return type;
            if (_module.Objects.ContainsKey(name))
                return new TypeNode(name, context.Line, context.Column);

            // ok i unno wth this is
            return null;
        }

        private TypeNode? ResolveQualifiedAccessType(QualifiedAccessNode qualifiedAccess)
        {
            // Here we get something like 'Identifier.Identifier.Identifier'
            // Have to figure out if it's a static variable from some xref
            // Or it could be just accessing a variable from an object

            string[] names = qualifiedAccess.FullName.Split('.');
            string leadup = string.Join('.', names[..^1]); // Could be a class name or a member chain
            string varName = names[^1];

            // Check in xrefs
            if (_module.XRefs.TryGetValue(leadup, out XRefSymbol? xrefObj))
                if (xrefObj.Variables.TryGetValue(varName, out VariableSymbol? xrefVar))
                    return xrefVar.Type;

            // Walk the member chain until we reach the final access
            TypeNode? type = null;

            // The first name can be the name of a class and the next one a static field
            if (_module.Objects.ContainsKey(names[0]))
                type = new TypeNode(names[0], qualifiedAccess.Line, qualifiedAccess.Column);
            else type = _scopeStack.LookUp(names[0]); // Probably a local variable starter

            if (type != null)
            {
                for (int i = 1; i < names.Length; i++)
                {
                    if (!_module.Objects.TryGetValue(type.Name, out ObjectSymbol? obj))
                    {
                        AddError($"'{type.Name}' is not a known object.", qualifiedAccess);
                        return null;
                    }
                    if (!obj.Variables.TryGetValue(names[i], out VariableSymbol? varSbl))
                    {
                        AddError($"Object '{obj.Name}' does not have a variable with the name '{names[i]}'.", qualifiedAccess);
                        return null;
                    }
                    type = varSbl.Type;
                }
                return type;
            }

            AddError($"Cannot resolve '{qualifiedAccess.FullName}'.", qualifiedAccess);
            return null;
        }

        private TypeNode? ResolveMemberAccessType(MemberAccessNode memberAccess)
        {
            TypeNode? exprType = AnalyseExpr(memberAccess.Object);
            if (exprType == null)
            {
                AddError("Can't access member from 'void' type.", memberAccess);
                return null;
            }

            // Array length property
            if (exprType.IsArray && memberAccess.Member == "Length")
                return new TypeNode("int", memberAccess.Line, memberAccess.Column);

            // Find it in the classes (or xrefs)
            if (_module.Objects.TryGetValue(exprType.Name, out ObjectSymbol? obj))
            {
                if (obj.Variables.TryGetValue(memberAccess.Member, out VariableSymbol? varSbl))
                    return varSbl.Type;

                AddError($"'{exprType.Name}' has no member '{memberAccess.Member}'.", memberAccess);
                return null;
            }

            if (_module.XRefs.TryGetValue(exprType.Name, out XRefSymbol? xrefObj))
            {
                if (xrefObj.Variables.TryGetValue(memberAccess.Member, out VariableSymbol? xrefVar))
                    return xrefVar.Type;

                AddError($"'{exprType.Name}' has no member '{memberAccess.Member}'. Did you forget to reference it in the xref ?", memberAccess);
                return null;
            }

            AddError($"'{exprType.Name}' is not a known object. Did you forget to reference it with the 'xref' keyword ?", memberAccess);
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
                return null;
            }

            return new TypeNode(arrayType.Name, arrayAccess.Line, arrayAccess.Column, true);
        }

        private TypeNode? ResolveThisType(ThisNode ts)
        {
            if (_currentClass == null)
            {
                AddError("Cannot use keyword 'this' outside of a class.", ts);
                return null;
            }
            return new TypeNode($"{_module.Namespace}.{_currentClass.Name}", ts.Line, ts.Column);
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
                return new TypeNode("bool", binaryExpr.Line, binaryExpr.Column);
            }

            // Bit and shift operations require int
            // Why the fuck would you want to do that to a float you psycho
            if (binaryExpr.Op is "&" or "|" or "<<" or ">>" or "^")
            {
                if (left?.Name != "int" || right?.Name != "int")
                    AddError($"Operator '{binaryExpr.Op}' requires int operands.", binaryExpr);
                return new TypeNode("int", binaryExpr.Line, binaryExpr.Column);
            }

            // Arithmetic operators
            if (!TypesMatch(left, right))
            {
                AddError($"Cannot apply operator '{binaryExpr.Op}' to operands of type '{left?.Name}' and '{right?.Name}'.", binaryExpr);
                return null;
            }

            return left;
        }

        private TypeNode? ResolveUnaryExprType(UnaryExprNode unaryExpr)
        {
            TypeNode? operand = AnalyseExpr(unaryExpr.Operand);

            if (unaryExpr.Op == "-" && operand?.Name is not ("int" or "float"))
                AddError("Unary operator '-' requires an int or a float.", unaryExpr);

            if (unaryExpr.Op == "!" && operand?.Name != "bool")
                AddError("Unary operator '!' requires a bool.", unaryExpr);

            return operand;
        }

        private TypeNode? ResolveQualifiedCallType(QualifiedCallNode qualifiedCall)
        {
            string[] names = qualifiedCall.FullName.Split('.');
            string leadup = string.Join('.', names[..^1]);
            string funcName = names[^1];

            if (_module.XRefs.TryGetValue(leadup, out XRefSymbol? xrefCls))
                if (xrefCls.Functions.TryGetValue(funcName, out ExternalFunctionSymbol? xrefFunc))
                {
                    CheckArguments(xrefFunc.ArgumentTypes, qualifiedCall.Args, qualifiedCall, funcName);
                    return xrefFunc.ReturnType;
                }

            TypeNode? type = null;
            if (_module.Objects.ContainsKey(names[0]))
                type = new TypeNode(names[0], qualifiedCall.Line, qualifiedCall.Column);
            else type = _scopeStack.LookUp(names[0]);

            if (type == null)
            {
                AddError($"Cannot resolve '{qualifiedCall.FullName}'.", qualifiedCall);
                return null;
            }

            // Walk the member chain like with a variable but this time stop before the
            // last name since we're actually checking for a function name.
            for (int i = 1; i < names.Length - 1; i++)
            {
                if (!_module.Objects.TryGetValue(type.Name, out ObjectSymbol? obj))
                {
                    AddError($"'{type.Name}' is not a known object.", qualifiedCall);
                    return null;
                }
                if (!obj.Variables.TryGetValue(names[i], out VariableSymbol? varSbl))
                {
                    AddError($"Object '{obj.Name}' does not have a variable with the name '{names[i]}'.", qualifiedCall);
                    return null;
                }
                type = varSbl.Type;
            }

            if (!_module.Objects.TryGetValue(type.Name, out ObjectSymbol? objWithFunc))
            {
                AddError($"'{type.Name}' is not a known object.", qualifiedCall);
                return null;
            }
            if (!objWithFunc.Functions.TryGetValue(funcName, out FunctionSymbol? funcSbl))
            {
                AddError($"Object '{objWithFunc.Name}' does not have a function with the name '{funcName}'.", qualifiedCall);
                return null;
            }

            CheckArguments(ToTypeNodes(funcSbl.Parameters), qualifiedCall.Args, qualifiedCall, funcName);
            return funcSbl.ReturnType;
        }

        private TypeNode? ResolveMemberCallType(MemberCallNode memberCall)
        {
            TypeNode? exprType = AnalyseExpr(memberCall.Object);
            if (exprType == null)
            {
                AddError("Can't call function from 'void' type.", memberCall);
                return null;
            }

            if (_module.Objects.TryGetValue(exprType.Name, out ObjectSymbol? obj))
            {
                if (obj.Functions.TryGetValue(memberCall.Name, out FunctionSymbol? funcSbl))
                {
                    CheckArguments(ToTypeNodes(funcSbl.Parameters), memberCall.Args, memberCall, memberCall.Name);
                    return funcSbl.ReturnType;
                }

                AddError($"Object '{obj.Name}' does not have a function with the name '{memberCall.Name}'.", memberCall);
                return null;
            }

            if (_module.XRefs.TryGetValue(exprType.Name, out XRefSymbol? xrefObj))
            {
                if (xrefObj.Functions.TryGetValue(memberCall.Name, out ExternalFunctionSymbol? xrefFunc))
                {
                    CheckArguments(xrefFunc.ArgumentTypes, memberCall.Args, memberCall, memberCall.Name);
                    return xrefFunc.ReturnType;
                }

                AddError($"XRef object '{xrefObj.FullName}' does not have a function with the name '{memberCall.Name}'.", memberCall);
                return null;
            }

            AddError($"'{exprType.Name}' is not a known object. Did you forget to reference it with the 'xref' keyword ?", memberCall);
            return null;
        }

        private TypeNode? ResolveNewObjectType(NewObjectNode newObject)
        {
            return new TypeNode(newObject.ClassName, newObject.Line, newObject.Column);
        }

        private TypeNode? ResolveArrayCreationType(ArrayCreationNode arrayCreation)
        {
            return new TypeNode(arrayCreation.ElementType.Name, arrayCreation.Line, arrayCreation.Column, true);
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

        private static TypeNode[] ToTypeNodes(IList<ParamNode> paramNodes)
        {
            List<TypeNode> typeNodes = new();
            foreach (ParamNode param in paramNodes)
                typeNodes.Add(param.Type);
            return typeNodes.ToArray();
        }
    }
}
