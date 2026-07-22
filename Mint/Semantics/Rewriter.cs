using Mint.AstNodes;
using Mint.Util;
using OneOf;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Mint.Semantics
{
    // Goes through the AST and swaps some nodes with the info that the parser doesn't have.
    // Also handles type names to have full paths so that semantic and the generator don't
    // have to handle that mess.
    public class Rewriter
    {
        private readonly ModuleSymbol _moduleSymbols;
        private readonly ScopeStack _scope = new();
        private readonly Dictionary<string, ExprNode> _constants = new();
        private ObjectSymbol? _currentObj = null;
        private XRefSymbol? _currentXRef = null;

        public Rewriter(ModuleSymbol moduleSymbols) => _moduleSymbols = moduleSymbols;

        public ModuleNode Rewrite(ModuleNode module)
        {
            List<ObjectNode> rewrittenObjs = new();
            foreach (ObjectNode obj in module.Objects)
                rewrittenObjs.Add(RewriteObject(obj));

            return module with { Objects = rewrittenObjs };
        }

        private ObjectNode RewriteObject(ObjectNode obj)
        {
            if (obj.Location == ObjectLocation.Local)
                _currentObj = _moduleSymbols.LocalObjects[obj.Name];
            else
                _currentXRef = _moduleSymbols.XRefObjects[obj.Name];

            List<MemberNode> rewrittenMembers = new();
            foreach (MemberNode member in obj.Members)
                rewrittenMembers.Add(RewriteMember(member));

            _currentObj = null;
            _currentXRef = null;
            return obj with { Members = rewrittenMembers };
        }

        private MemberNode RewriteMember(MemberNode member) => member switch
        {
            VariableNode var => var with { Type = RewriteType(var.Type) },
            FunctionNode func => RewriteFunction(func),
            ConstructorNode ct => RewriteConstructor(ct),
            ExternalFunctionNode xrefFunc => RewriteExternalFunction(xrefFunc),
            ExternalConstructorNode xct => RewriteExternalConstructor(xct),

            _ => member
        };

        private ITypeNode RewriteType(ITypeNode type)
        {
            ITypeNode[] typeTreeArray = type.ToArray();
            if (typeTreeArray[^1] is not TypeNode typed)
                throw new Exception("Last type in tree is not a TypeNode... somehow...");

            if (IsNeighborObject(typed.Name))
            {
                if (_currentObj != null)
                    return typed with { Name = $"{NameOperations.GetParent(_moduleSymbols.Name)}.{typed.Name}" };
                if (_currentXRef != null)
                    return typed with { Name = $"{NameOperations.GetParent(_currentXRef.FullName)}.{typed.Name}" };
            }

            // Rebuild the tree
            ITypeNode newTypeTree = typed;
            for (int i = typeTreeArray.Length - 2; i >= 0; i--)
            {
                switch (typeTreeArray[i])
                {
                    case RefTypeNode refType:
                        newTypeTree = refType with { Type = newTypeTree };
                        break;
                    case ConstTypeNode constType:
                        newTypeTree = constType with { Type = newTypeTree };
                        break;
                    case ArrayTypeNode arrayType:
                        newTypeTree = arrayType with { Type = newTypeTree };
                        break;
                    default:
                        throw new Exception("Unknown type type.");
                }
            }

            return newTypeTree;
        }

        private FunctionNode RewriteFunction(FunctionNode func)
        {
            _scope.PushScope();

            List<ParamNode> rewrittenParams = new();
            foreach (ParamNode param in func.Params)
            {
                rewrittenParams.Add(param with { Type = RewriteType(param.Type) });
                _scope.Define(param.Name, param.Type);
            }

            FunctionNode rewritten = func with { Body = RewriteBlock(func.Body), Params = rewrittenParams };
            if (rewritten.ReturnType != null)
                rewritten = rewritten with { ReturnType = RewriteType(rewritten.ReturnType) };

            _scope.PopScope();
            return rewritten;
        }

        private ExternalFunctionNode RewriteExternalFunction(ExternalFunctionNode func)
        {
            List<ITypeNode> rewrittenParamTypes = new();
            foreach (ITypeNode type in func.ParamTypes)
                rewrittenParamTypes.Add(RewriteType(type));
            
            ExternalFunctionNode rewritten = func with { ParamTypes = rewrittenParamTypes };
            if (rewritten.ReturnType != null)
                rewritten = rewritten with { ReturnType = RewriteType(rewritten.ReturnType) };

            return rewritten;
        }

        private ConstructorNode RewriteConstructor(ConstructorNode constructor)
        {
            _scope.PushScope();

            List<ParamNode> rewrittenParams = new();
            foreach (ParamNode param in constructor.Params)
            {
                rewrittenParams.Add(param with { Type = RewriteType(param.Type) });
                _scope.Define(param.Name, param.Type);
            }

            ConstructorNode rewritten = constructor with { Body = RewriteBlock(constructor.Body), Params = rewrittenParams };

            _scope.PopScope();
            return rewritten;
        }

        private ExternalConstructorNode RewriteExternalConstructor(ExternalConstructorNode constructor)
        {
            List<ITypeNode> rewrittenParamTypes = new();
            foreach (ITypeNode type in constructor.ParamTypes)
                rewrittenParamTypes.Add(RewriteType(type));

            return constructor with { ParamTypes = rewrittenParamTypes };
        }

        private BlockNode RewriteBlock(BlockNode block)
        {
            _scope.PushScope();

            List<StmtNode> rewrittenStatements = new();
            foreach (StmtNode stmt in block.Statements)
            {
                if (stmt is VarDeclNode varDecl)
                    _scope.Define(varDecl.Name, varDecl.Type);
                rewrittenStatements.Add(RewriteStatement(stmt));
            }

            _scope.PopScope();
            return block with { Statements = rewrittenStatements };
        }

        private StmtNode RewriteStatement(StmtNode stmt) => stmt switch
        {
            VarDeclNode vd => vd.Initializer != null ?
                vd with { Initializer = RewriteExpression(vd.Initializer), Type = RewriteType(vd.Type) } :
                vd with { Type = RewriteType(vd.Type) },
            AssignNode a => a with { Target = RewriteExpression(a.Target), Value = RewriteExpression(a.Value) },
            IfNode i => RewriteIf(i),
            WhileNode w => w with { Condition = RewriteExpression(w.Condition), Body = RewriteBlock(w.Body) },
            ForNode f => f with
            {
                Initializer = RewriteStatement(f.Initializer),
                Condition = RewriteExpression(f.Condition),
                Increment = RewriteStatement(f.Increment),
                Body = RewriteBlock(f.Body)
            },
            ReturnNode r => r with { Value = r.Value != null ? RewriteExpression(r.Value) : null },
            ExprStmtNode e => e with { Expr = RewriteExpression(e.Expr) },
            YieldNode y => y with { FrameCount = RewriteExpression(y.FrameCount) },

            _ => stmt
        };

        private IfNode RewriteIf(IfNode ifNode)
        {
            IfNode rewritten = ifNode with
            {
                Condition = RewriteExpression(ifNode.Condition),
                Then = RewriteBlock(ifNode.Then)
            };

            if (rewritten.ElseIf != null)
                rewritten = rewritten with { ElseIf = RewriteIf(rewritten.ElseIf) };
            if (rewritten.Else != null)
                rewritten = rewritten with { Else = RewriteBlock(rewritten.Else) };

            return rewritten;
        }

        private ExprNode RewriteExpression(ExprNode expr) => expr switch
        {
            IdentifierNode id => RewriteIdentifier(id),
            QualifiedAccessNode qa => RewriteQualifiedAccess(qa),
            MemberAccessNode ma => ma with { Object = RewriteExpression(ma.Object) },
            ArrayAccessNode aa => aa with
            {
                Array = RewriteExpression(aa.Array),
                Index = RewriteExpression(aa.Index)
            },
            DereferenceNode dr => dr with { Reference = RewriteExpression(dr.Reference) },
            BinaryExprNode be => be with
            {
                Left = RewriteExpression(be.Left),
                Right = RewriteExpression(be.Right)
            },
            UnaryExprNode ue => ue with { Operand = RewriteExpression(ue.Operand) },
            QualifiedCallNode qc => RewriteQualifiedCall(qc),
            MemberCallNode mc => RewriteMemberCall(mc),
            PushInstanceNode pi => RewritePushInstance(pi),
            ArrayInitNode ai => RewriteArrayInit(ai),
            IncrementNode inc => inc with { Target = RewriteExpression(inc.Target) },
            MemberOffsetNode mo => mo with { Object = RewriteExpression(mo.Object) },
            TypeCastNode tc => tc with { Expr = RewriteExpression(tc.Expr) },

            _ => expr
        };

        private ExprNode RewriteIdentifier(IdentifierNode id)
        {
            if (IsNeighborObject(id.Name))
                return new PushInstanceNode($"{NameOperations.GetParent(_moduleSymbols.Name)}.{id.Name}", id.Line, id.Column);
            if (IsKnownObject(id.Name))
                return new PushInstanceNode(id.Name, id.Line, id.Column);
            if (_currentObj != null && _currentObj.Variables.ContainsKey(id.Name))
                return new QualifiedAccessNode(GetLocalQualifiedName(id.Name), id.Line, id.Column);
            return id;
        }

        /*
        The 2 following functions are incomprehensible horrors, they work but I have no idea how.
        TODO if you have the sanity : clean up
        */

        private ExprNode RewriteQualifiedAccess(QualifiedAccessNode qa)
        {
            if (IsKnownObject(qa.FullName))
                return new PushInstanceNode(qa.FullName, qa.Line, qa.Column);

            string[] names = qa.FullName.Split('.');

            if (IsNeighborObject(names[0]))
                return BuildMemberAccess(
                    new QualifiedAccessNode($"{NameOperations.GetParent(_moduleSymbols.Name)}.{names[0]}", qa.Line, qa.Column),
                    names[1..]
                );

            if (IsLocalVariable(names[0]))
                return BuildMemberAccess(new IdentifierNode(names[0], qa.Line, qa.Column), names);

            // Variable from the current class, written as a short-hand
            if (_currentObj != null && _currentObj.Variables.ContainsKey(names[0]))
                return BuildMemberAccess(new QualifiedAccessNode(GetLocalQualifiedName(names[0]), qa.Line, qa.Column), names);

            // Static access
            int qaIndex = FindQualifiedIndex(names);

            if (qaIndex == -1) return qa; // Unknown, probably an error

            string joinedQualif = string.Join('.', names[..(qaIndex + 1)]);
            QualifiedAccessNode starterQualified = new QualifiedAccessNode(joinedQualif, qa.Line, qa.Column);
            if (IsLocalClassQualified(qaIndex, names[0]))
                starterQualified = new QualifiedAccessNode(
                    $"{NameOperations.GetParent(_moduleSymbols.Name)}.{joinedQualif}",
                    qa.Line,
                    qa.Column
                );
            return BuildMemberAccess(starterQualified, names[qaIndex..]);
        }

        private ExprNode RewriteQualifiedCall(QualifiedCallNode qc)
        {
            List<ExprNode> rewrittenArgs = new();
            foreach (ExprNode arg in qc.Args)
                rewrittenArgs.Add(RewriteExpression(arg));

            if (IsNeighborObject(qc.FullName))
                return new PushInstanceNode($"{NameOperations.GetParent(_moduleSymbols.Name)}.{qc.FullName}", qc.Line, qc.Column, rewrittenArgs);
            if (IsKnownObject(qc.FullName))
                return new PushInstanceNode(qc.FullName, qc.Line, qc.Column, rewrittenArgs);

            string[] names = qc.FullName.Split('.');

            if (IsLocalVariable(names[0]))
                return BuildMemberCall(new IdentifierNode(names[0], qc.Line, qc.Column), names, rewrittenArgs);

            if (_currentObj != null && _currentObj.Variables.ContainsKey(names[0]))
                return BuildMemberCall(new QualifiedAccessNode(names[0], qc.Line, qc.Column), names, rewrittenArgs);

            int qcIndex = FindQualifiedIndex(names);
            if (qcIndex == -1)
                return qc with { FullName = GetLocalQualifiedName(qc.FullName), Args = rewrittenArgs };

            string joinedQualif = string.Join('.', names[..(qcIndex + 1)]);
            if (IsLocalClassQualified(qcIndex, names[0]))
                joinedQualif = $"{NameOperations.GetParent(_moduleSymbols.Name)}.{joinedQualif}";

            if (qcIndex == names.Length - 1)
                return qc with { FullName = joinedQualif, Args = rewrittenArgs };

            return BuildMemberCall(new QualifiedAccessNode(
                string.Join('.', names[..(qcIndex + 1)]),
                qc.Line,
                qc.Column
            ), names, rewrittenArgs);
        }

        private ExprNode RewriteMemberCall(MemberCallNode mc)
        {
            List<ExprNode> rewrittenArgs = new();
            foreach (ExprNode arg in mc.Args)
                rewrittenArgs.Add(RewriteExpression(arg));
            return mc with { Args = rewrittenArgs, Object = RewriteExpression(mc.Object) };
        }

        private ExprNode RewritePushInstance(PushInstanceNode pi)
        {
            List<ExprNode>? rewrittenArgs = null;
            if (pi.CtArgs != null)
            {
                rewrittenArgs = new();
                foreach (ExprNode arg in pi.CtArgs)
                    rewrittenArgs.Add(RewriteExpression(arg));
            }
            return pi with { CtArgs = rewrittenArgs };
        }

        private ExprNode RewriteArrayInit(ArrayInitNode ai)
        {
            List<ExprNode>? rewrittenInits = null;

            rewrittenInits = new();
            foreach (ExprNode init in ai.Initializers)
                rewrittenInits.Add(RewriteExpression(init));

            return ai with { Initializers = rewrittenInits };
        }

        private int FindQualifiedIndex(string[] chain)
        {
            StringBuilder sb = new(chain[0]);
            int index = -1;
            for (int i = 1; i < chain.Length; i++)
            {
                if (IsKnownObject(sb.ToString()))
                    index = i;
                sb.Append('.' + chain[i]);
            }
            return index;
        }

        private string GetLocalQualifiedName(string member) => $"{_currentObj?.FullName}.{member}";

        // fuck my stupid chungus life
        private bool IsKnownObject(string fullName) =>
            _moduleSymbols.LocalObjects.ContainsKey(fullName) ||
            _moduleSymbols.XRefObjects.ContainsKey(fullName) || (
                string.Join('.', fullName.Split('.')[..^1]) == NameOperations.GetParent(_moduleSymbols.Name) &&
                _moduleSymbols.LocalObjects.ContainsKey(fullName.Split('.')[^1]));

        private bool IsLocalVariable(string name) => _scope.LookUp(name) != null;

        private bool IsLocalClassQualified(int qualIndex, string firstName)
            => qualIndex == 1 && _moduleSymbols.LocalObjects.ContainsKey(firstName);

        private bool IsNeighborObject(string name)
        {
            if (_currentObj != null)
                return _moduleSymbols.LocalObjects.ContainsKey(name);

            if (_currentXRef != null)
            {
                string[] names = _currentXRef.FullName.Split('.');
                string xrefNamespace = string.Join('.', names[..^1]);
                return _moduleSymbols.XRefObjects.ContainsKey($"{xrefNamespace}.{name}");
            }

            return false;
        }

        private static ExprNode BuildMemberAccess(ExprNode starter, string[] chain)
        {
            ExprNode expr = starter;
            for (int i = 1; i < chain.Length; i++)
                expr = new MemberAccessNode(expr, chain[i], starter.Line, starter.Column);
            return expr;
        }

        private static ExprNode BuildMemberCall(ExprNode starter, string[] chain, List<ExprNode> args)
        {
            ExprNode expr = BuildMemberAccess(starter, chain[..^1]);
            return new MemberCallNode(expr, chain[^1], args, starter.Line, starter.Column);
        }
    }
}
