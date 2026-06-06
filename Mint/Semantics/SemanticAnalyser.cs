using Mint.AstNodes;

namespace Mint.Semantics
{
    public class SemanticAnalyser
    {
        private readonly VersionRules _rules;
        private readonly Dictionary<ExprNode, TypeNode> _exprTypes = new();
        private readonly List<SemanticError> _errors = new();
        private ModuleSymbol _module = new();
        private ClassSymbol? _currentClass;
        private FunctionSymbol? _currentFunction;
        private readonly ScopeStack _scopeStack = new();

        public SemanticAnalyser(VersionRules rules) => _rules = rules;

        public SemanticResult Analyse(ModuleNode module)
        {
            // Pass 1
            BuildSymbolTable(module);

            // Pass 2 - type checking
            foreach (ClassNode cls in module.Classes)
                AnalyseClass(cls);

            return new SemanticResult(_module, _exprTypes, _errors);
        }

        private void BuildSymbolTable(ModuleNode module)
        {
            foreach (ClassNode cls in module.Classes)
            {
                ClassSymbol clsSymbol = new ClassSymbol
                {
                    Name = cls.FullName
                };

                foreach (MemberNode member in cls.Members)
                    switch (member)
                    {
                        case VariableNode variable:
                            clsSymbol.Variables[variable.Name] = new VariableSymbol
                            {
                                Name = variable.Name,
                                Type = variable.Type
                            };
                            break;
                        case FunctionNode function:
                            clsSymbol.Functions[function.Name] = new FunctionSymbol
                            {
                                Name = function.Name,
                                ReturnType = function.ReturnType
                            };
                            break;
                    }
                _module.Classes[cls.FullName] = clsSymbol;
            }

            foreach (ExternalVariableNode extVar in module.extVariables)
                _module.XVars[extVar.FullName] = new ExternalVariableSymbol
                {
                    FullName = extVar.FullName,
                    Type = extVar.Type
                };

            foreach (ExternalFunctionNode extFunc in module.extFunctions)
            {
                ExternalFunctionSymbol xFuncSymbol = new ExternalFunctionSymbol
                {
                    FullName = extFunc.FullName,
                    ReturnType = extFunc.ReturnType
                };
                foreach (TypeNode argType in extFunc.ParamTypes)
                    xFuncSymbol.ArgumentTypes.Add(argType);
                _module.XFuncs[extFunc.FullName] = xFuncSymbol;
            }
        }

        private void AnalyseClass(ClassNode cls)
        {
            _currentClass = _module.Classes[cls.FullName];

            // TODO : add check for base class

            foreach (MemberNode member in cls.Members)
                switch (member)
                {
                    // we already built the symbol table, do nothing if it's a variable

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
                    TypeNode? initType = AnalyseExpr(varDecl.Initializer);
                    if (!TypesMatch(varDecl.Type, initType))
                        AddError(
                            $"Cannot assign '{initType?.Name}' to variable of type '{varDecl.Type.Name}'",
                            varDecl
                        );
                    _scopeStack.Define(varDecl.Name, varDecl.Type);
                    break;
                case AssignNode assign:
                    TypeNode? assignType = AnalyseExpr(assign.Value);
                    TypeNode? varType = ResolveIdentifierType(assign.Name, assign);
                    if (!TypesMatch(assignType, varType))
                        AddError(
                            $"Cannot assign '{assignType?.Name}' to '{assign.Name}' of type '{varType?.Name}'",
                            assign
                        );
                    break;
                case ArrayAssignNode arrayAssign:
                    TypeNode? arrayType = AnalyseExpr(arrayAssign.Array);
                    TypeNode? indexType = AnalyseExpr(arrayAssign.Index);
                    TypeNode? valueType = AnalyseExpr(arrayAssign.Value);
                    if (indexType?.Name != "int")
                        AddError("Array index must be an int", arrayAssign);
                    if (arrayType != null &&
                        !TypesMatch(valueType, new TypeNode(arrayType.Name, arrayType.Line, arrayType.Column))
                    )
                        AddError(
                            $"Cannot assign '{valueType?.Name}' to array of type '{arrayType.Name}'",
                            arrayAssign
                        );
                    break;
                case IfNode ifStmt:
                    TypeNode? condType = AnalyseExpr(ifStmt.Condition);
                    if (condType?.Name != "bool")
                        AddError("If condition must be a bool", ifStmt);
                    AnalyseBlock(ifStmt.Then);
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
                    TypeNode? retType = ret.Value != null ? AnalyseExpr(ret.Value) : null;
                    if (!TypesMatch(retType, _currentFunction?.ReturnType))
                        AddError(
                            $"Return type '{retType?.Name}' does not match function return type '{_currentFunction?.ReturnType.Name}'",
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
                MemberAccessNode ma => ResolveMemberAccessType(ma),
                ArrayAccessNode aa => ResolveArrayAccessType(aa),
                BinaryExprNode be => ResolveBinaryExprType(be),
                UnaryExprNode ue => ResolveUnaryExprType(ue),
                FunctionCallNode fc => ResolveFunctionCallType(fc),
                ArrayCreationNode ac => ResolveA
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
            return _scopeStack.LookUp(name);
        }

        private TypeNode? ResolveMemberAccessType(MemberAccessNode memberAccess)
        {
            TypeNode? objType = AnalyseExpr(memberAccess.Object);
            if (objType == null) return null;

            // Array length property
            if (objType.IsArray && memberAccess.Member == "Length")
                return new TypeNode("int", memberAccess.Line, memberAccess.Column);

            // Find it in the class
            if (_module.Classes.TryGetValue(objType.Name, out ClassSymbol? cls))
            {
                if (cls.Variables.TryGetValue(memberAccess.Member, out VariableSymbol? varSbl))
                    return varSbl.Type;

                AddError($"'{objType.Name}' has no variable '{memberAccess.Member}'. Did you forget to reference it with the 'xref' keyword ?", memberAccess);
                return null;
            }

            AddError($"'{objType.Name}' is not a known class. Did you forget to reference it with the 'xref' keyword ?", memberAccess);
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

        private static bool TypesMatch(TypeNode? a,  TypeNode? b)
        {
            if (a == null || b == null) return false;
            return a.Name == b.Name && a.IsArray == b.IsArray;
        }

        private void AddError(string message, AstNode node)
            => _errors.Add(new SemanticError(message, node));
    }
}
