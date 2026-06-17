using Mint.AstNodes;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.Semantics
{
    // Goes through the AST and swaps some nodes with the info that the parser doesn't have.
    public class Rewriter
    {
        private readonly ModuleSymbol _moduleSymbols;
        private readonly ScopeStack _scope = new();
        private ObjectSymbol? _currentObj;

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
            _currentObj = _moduleSymbols.Objects[obj.Name];

            List<MemberNode> rewrittenMembers = new();
            foreach (MemberNode member in obj.Members)
                rewrittenMembers.Add(RewriteMember(member));

            _currentObj = null;
            return obj with { Members = rewrittenMembers };
        }

        private MemberNode RewriteMember(MemberNode member) => member switch
        {
            FunctionNode func => RewriteFunction(func),

            _ => member
        };

        private FunctionNode RewriteFunction(FunctionNode func)
        {
            _scope.PushScope();
            foreach (ParamNode param in func.Params)
                _scope.Define(param.Name, param.Type);

            FunctionNode rewritten = func with { Body = RewriteBlock(func.Body) };

            _scope.PopScope();
            return rewritten;
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
            VarDeclNode vd => vd with { Initializer = RewriteExpression(vd.Initializer) },
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
            IdentifierNode id when IsKnownObject(id.Name)
                => new PushInstanceNode(id.Name, id.Line, id.Column),
            QualifiedAccessNode qa => RewriteQualifiedAccess(qa),
            MemberAccessNode ma => ma with { Object = RewriteExpression(ma.Object) },
            ArrayAccessNode aa => aa with
            {
                Array = RewriteExpression(aa.Array),
                Index = RewriteExpression(aa.Index)
            },
            BinaryExprNode be => be with
            {
                Left = RewriteExpression(be.Left),
                Right = RewriteExpression(be.Right)
            },
            UnaryExprNode ue => ue with { Operand = RewriteExpression(ue.Operand) },
            QualifiedCallNode qc => RewriteQualifiedCall(qc),
            MemberCallNode mc => RewriteMemberCall(mc),
            ArrayCreationNode ac => RewriteArrayCreation(ac),
            IncrementNode inc => inc with { Target = RewriteExpression(inc.Target) },

            _ => expr
        };

        private ExprNode RewriteQualifiedAccess(QualifiedAccessNode qa)
        {
            if (IsKnownObject(qa.FullName))
                return new PushInstanceNode(qa.FullName, qa.Line, qa.Column);

            string[] names = qa.FullName.Split('.');

            if (IsLocalVariable(names[0]))
                return BuildMemberAccess(new IdentifierNode(names[0], qa.Line, qa.Column), names);

            // Variable from the current class, written as a short-hand
            if (_currentObj != null && _currentObj.Variables.ContainsKey(names[0]))
                return BuildMemberAccess(new QualifiedAccessNode(GetQualifiedName(names[0]), qa.Line, qa.Column), names);

            // Static access
            int qaIndex = FindQualifiedIndex(names);

            if (qaIndex == -1) return qa; // Unknown, probably an error

            string joinedQualif = string.Join('.', names[..(qaIndex + 1)]);
            QualifiedAccessNode starterQualified = new QualifiedAccessNode(joinedQualif, qa.Line, qa.Column);
            if (IsLocalClassQualified(qaIndex, names[0]))
                starterQualified = new QualifiedAccessNode(
                    $"{_moduleSymbols.Namespace}.{joinedQualif}",
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

            string[] names = qc.FullName.Split('.');

            if (IsLocalVariable(names[0]))
                return BuildMemberCall(new IdentifierNode(names[0], qc.Line, qc.Column), names, rewrittenArgs);

            if (_currentObj != null && _currentObj.Variables.ContainsKey(names[0]))
                return BuildMemberCall(new QualifiedAccessNode(names[0], qc.Line, qc.Column), names, rewrittenArgs);

            int qcIndex = FindQualifiedIndex(names);
            if (qcIndex == -1)
                return qc with { FullName = GetQualifiedName(qc.FullName), Args = rewrittenArgs };

            string joinedQualif = string.Join('.', names[..(qcIndex + 1)]);
            if (IsLocalClassQualified(qcIndex, names[0]))
                joinedQualif = $"{_moduleSymbols.Namespace}.{joinedQualif}";

            if (qcIndex == names.Length - 1)
                return qc with { FullName = joinedQualif };

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

        private ExprNode RewriteArrayCreation(ArrayCreationNode ac)
        {
            ExprNode? rewrittenSize = null;
            List<ExprNode>? rewrittenInits = null;

            if (ac.Size != null)
                rewrittenSize = RewriteExpression(ac.Size);

            if (ac.Initializers != null)
            {
                rewrittenInits = new();
                foreach (ExprNode init in ac.Initializers)
                    rewrittenInits.Add(RewriteExpression(init));
            }

            return ac with { Size = rewrittenSize, Initializers = rewrittenInits };
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

        private string GetQualifiedName(string member) => $"{_currentObj?.FullName}.{member}";

        // fuck my stupid chungus life
        private bool IsKnownObject(string fullName) =>
            _moduleSymbols.Objects.ContainsKey(fullName) ||
            _moduleSymbols.XRefs.ContainsKey(fullName) || (
                string.Join('.', fullName.Split('.')[..^1]) == _moduleSymbols.Namespace &&
                _moduleSymbols.Objects.ContainsKey(fullName.Split('.')[^1]));

        private bool IsLocalVariable(string name) => _scope.LookUp(name) != null;

        private bool IsLocalClassQualified(int qualIndex, string firstName)
            => qualIndex == 1 && _moduleSymbols.Objects.ContainsKey(firstName);

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
