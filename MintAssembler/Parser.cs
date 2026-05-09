using MintAssembler.AstNodes;
using System;
using System.Collections.Generic;
using System.Text;

namespace MintAssembler
{
    public class Parser
    {
        private readonly List<Token> _tokens;
        private int _pos;

        public Parser(List<Token> tokens) => _tokens = tokens;

        private Token Current => _tokens[_pos];

        private Token Peek(int offset = 1) => _tokens[Math.Min(_pos + offset, _tokens.Count - 1)];

        private bool Check(TokenType type) => Current.Type == type;

        private Token Expect(TokenType type)
        {
            if (!Check(type))
                throw new ParserException(
                    $"Expected {type} but got '{Current.Value}'",
                    Current.Line, Current.Column);
            return _tokens[_pos++];
        }

        private bool Match(TokenType type)
        {
            if (!Check(type)) return false;
            _pos++;
            return true;
        }

        public ProgramNode Parse()
        {
            List<ClassNode> classes = new();

            while (!Check(TokenType.EOF))
                classes.Add(ParseClass());

            return new ProgramNode(classes);
        }

        private ClassNode ParseClass()
        {
            Expect(TokenType.Class);
            string name = Expect(TokenType.Identifier).Value;
            Expect(TokenType.OpenBrace);

            List<MemberNode> members = new();
            while (!Check(TokenType.CloseBrace) && !Check(TokenType.EOF))
                members.Add(ParseMember());

            Expect(TokenType.CloseBrace);

            return new ClassNode(name, members);
        }

        private MemberNode ParseMember()
        {
            // TODO for future versions : handle enums

            TypeNode type = ParseType();
            string name = Expect(TokenType.Identifier).Value;

            // Check whether it's a variable or a function.
            // If we find a '(' then it's a function.
            if (Check(TokenType.OpenParen))
                return ParseFunction(type, name);
            else
                return ParseVariable(type, name);
        }

        private VariableNode ParseVariable(TypeNode type, string name)
        {
            Expect(TokenType.Semicolon);
            return new VariableNode(type, name);
        }

        private FunctionNode ParseFunction(TypeNode returnType, string name)
        {
            List<ParamNode> parameters = ParseParameterList();
            BlockNode block = ParseBlock();
            return new FunctionNode(returnType, name, parameters, block);
        }

        private List<ParamNode> ParseParameterList()
        {
            Expect(TokenType.OpenParen);
            List<ParamNode> parameters = new();

            while (!Check(TokenType.CloseParen) && !Check(TokenType.EOF))
            {
                TypeNode type = ParseType();
                string name = Expect(TokenType.Identifier).Value;
                parameters.Add(new ParamNode(type, name));

                if (!Match(TokenType.Comma)) break;
            }

            Expect(TokenType.CloseParen);
            return parameters;
        }

        private TypeNode ParseType()
        {
            string name = Current.Type switch
            {
                TokenType.Int => "int",
                TokenType.Float => "float",
                TokenType.Bool => "bool",
                TokenType.String => "string",
                TokenType.Identifier => Current.Value,
                _ => throw new ParserException(
                        $"Expected a type but got '{Current.Value}'",
                        Current.Line, Current.Column)
            };
            _pos++;

            // Check for array suffix []
            bool isArray = false;
            if (Check(TokenType.OpenBracket) && Peek().Type == TokenType.CloseBracket)
            {
                _pos += 2;
                isArray = true;
            }

            return new TypeNode(name, isArray);
        }

        private BlockNode ParseBlock()
        {
            Expect(TokenType.OpenBrace);
            List<StmtNode> statements = new();

            while (!Check(TokenType.CloseBrace) && !Check(TokenType.EOF))
                statements.Add(ParseStatement());

            Expect(TokenType.CloseBrace);
            return new BlockNode(statements);
        }

        private StmtNode ParseStatement()
        {
            // if
            if (Check(TokenType.If))
                return ParseIf();

            // while
            if (Check(TokenType.While))
                return ParseWhile();

            // for
            if (Check(TokenType.For))
                return ParseFor();

            // return
            if (Check(TokenType.Return))
                return ParseReturn();

            // Var declaration
            if (IsVarDecl())
                return ParseVarDecl();

            // Array assignement
            if (IsArrayAssign())
                return ParseArrayAssign();

            return ParseAssignOrExpr();
        }

        private bool IsVarDecl()
        {
            // Check for the basic 4
            if (Current.Type is TokenType.Int or TokenType.Float or TokenType.Bool or TokenType.String)
                return true;

            // If we have Identifier immediately followed by another Identifier
            // then that is a declaration.
            if (Check(TokenType.Identifier) && Peek().Type == TokenType.Identifier)
                return true;

            // Also check for arrays like "MyClass[] classes"
            if (Check(TokenType.Identifier) &&
                Peek().Type == TokenType.OpenBracket &&
                Peek(2).Type == TokenType.CloseBracket &&
                Peek(3).Type == TokenType.Identifier
            )
                return true;

            return false;
        }

        private bool IsArrayAssign()
        {
            // check for shit like "arr[i] = value"
            return Check(TokenType.Identifier)
                && Peek().Type == TokenType.OpenBracket;
        }

        private IfNode ParseIf()
        {
            Expect(TokenType.If);
            Expect(TokenType.OpenParen);
            ExprNode condition = ParseExpression();
            Expect(TokenType.CloseParen);
            BlockNode then = ParseBlock();

            BlockNode? els = null;
            if (Match(TokenType.Else))
                els = ParseBlock();

            return new IfNode(condition, then, els);
        }

        private WhileNode ParseWhile()
        {
            Expect(TokenType.While);
            Expect(TokenType.OpenParen);
            ExprNode condition = ParseExpression();
            Expect(TokenType.CloseParen);
            BlockNode body = ParseBlock();
            return new WhileNode(condition, body);
        }

        private ForNode ParseFor()
        {
            Expect(TokenType.For);
            Expect(TokenType.OpenParen);
            
            StmtNode initializer = ParseStatement();

            ExprNode condition = ParseExpression();
            Expect(TokenType.Semicolon);

            // Can't use ParseStatement() since that one expects a semicolon
            StmtNode increment = ParseAssignOrExpr(expectSemicolon: false);

            Expect(TokenType.CloseParen);

            BlockNode body = ParseBlock();
            return new ForNode(initializer, condition, increment, body);
        }

        private ReturnNode ParseReturn()
        {
            Expect(TokenType.Return);
            ExprNode? value = null;
            if (!Check(TokenType.Semicolon))
                value = ParseExpression();
            Expect(TokenType.Semicolon);
            return new ReturnNode(value);
        }

        private VarDeclNode ParseVarDecl()
        {
            TypeNode type = ParseType();
            string name = Expect(TokenType.Identifier).Value;
            Expect(TokenType.Equals);
            ExprNode initializer = ParseExpression();
            Expect(TokenType.Semicolon);
            return new VarDeclNode(type, name, initializer);
        }

        private ArrayAssignNode ParseArrayAssign()
        {
            IdentifierNode array = new(Expect(TokenType.Identifier).Value);
            Expect(TokenType.OpenBracket);
            ExprNode index = ParseExpression();
            Expect(TokenType.CloseBracket);
            Expect(TokenType.Equals);
            ExprNode value = ParseExpression();
            Expect(TokenType.Semicolon);
            return new ArrayAssignNode(array, index, value);
        }

        private StmtNode ParseAssignOrExpr(bool expectSemicolon = true)
        {
            // Prefix like ++x or --x
            if (Current.Type is TokenType.DoublePlus or TokenType.DoubleMinus)
            {
                bool isIncrement = Current.Type == TokenType.DoublePlus;
                _pos++;
                string name = Expect(TokenType.Identifier).Value;
                if (expectSemicolon) Expect(TokenType.Semicolon);
                return new IncrementNode(name, true, isIncrement);
            }

            ExprNode expr = ParseExpression();

            // Postfix like x++ or x--
            if (expr is IdentifierNode postfixIdent &&
                Current.Type is TokenType.DoublePlus or TokenType.DoubleMinus)
            {
                bool isIncrement = Current.Type == TokenType.DoublePlus;
                _pos++;
                if (expectSemicolon) Expect(TokenType.Semicolon);
                return new IncrementNode(postfixIdent.Name, false, isIncrement);
            }
            
            // If the expression is an identifier followed by = it's an assignement
            if (expr is IdentifierNode ident && Check(TokenType.Equals))
            {
                _pos++; // consume =
                ExprNode value = ParseExpression();
                if (expectSemicolon) Expect(TokenType.Semicolon);
                return new AssignNode(ident.Name, value);
            }

            // Compound assignements like x += 5
            if (expr is IdentifierNode identCompound && IsCompoundAssign())
            {
                string op = Current.Type switch
                {
                    TokenType.PlusEquals => "+",
                    TokenType.MinusEquals => "-",
                    TokenType.StarEquals => "*",
                    TokenType.SlashEquals => "/",
                    _ => throw new ParserException("Unknown compound operator", Current.Line, Current.Column)
                };
                _pos++;
                ExprNode value = ParseExpression();
                if (expectSemicolon) Expect(TokenType.Semicolon);

                BinaryExprNode desugared = new(
                    new IdentifierNode(identCompound.Name),
                    op,
                    value
                );
                return new AssignNode(identCompound.Name, desugared);
            }

            // Member assignements like instance.foo = 5
            if (expr is MemberAccessNode member && Check(TokenType.Equals))
            {
                _pos++;
                ExprNode value = ParseExpression();
                if (expectSemicolon) Expect(TokenType.Semicolon);
                return new MemberAssignNode(member.Object, member.Member, value);
            }

            // Otherwise it's just an expression like Foo();
            if (expectSemicolon) Expect(TokenType.Semicolon);
            return new ExprStmtNode(expr);
        }

        private bool IsCompoundAssign() => Current.Type is TokenType.PlusEquals
                                                        or TokenType.MinusEquals
                                                        or TokenType.StarEquals
                                                        or TokenType.SlashEquals;

        private ExprNode ParseExpression() => ParseEquality();

        private ExprNode ParseEquality()
        {
            ExprNode left = ParseComparison();
            while (Current.Type is TokenType.DoubleEquals or TokenType.NotEqual)
            {
                string op = _tokens[_pos++].Value;
                ExprNode right = ParseComparison();
                left = new BinaryExprNode(left, op, right);
            }
            return left;
        }

        private ExprNode ParseComparison()
        {
            ExprNode left = ParseAddition();
            while (Current.Type is TokenType.Greater or TokenType.GreaterEquals
                                or TokenType.Lesser or TokenType.LesserEquals)
            {
                string op = _tokens[_pos++].Value;
                ExprNode right = ParseAddition();
                left = new BinaryExprNode(left, op, right);
            }
            return left;
        }

        private ExprNode ParseAddition()
        {
            ExprNode left = ParseMultiplication();
            while (Current.Type is TokenType.Plus or TokenType.Minus)
            {
                string op = _tokens[_pos++].Value;
                ExprNode right = ParseMultiplication();
                left = new BinaryExprNode(left, op, right);
            }
            return left;
        }

        private ExprNode ParseMultiplication()
        {
            ExprNode left = ParseUnary();
            while (Current.Type is TokenType.Star or TokenType.Slash)
            {
                string op = _tokens[_pos++].Value;
                ExprNode right = ParseUnary();
                left = new BinaryExprNode(left, op, right);
            }
            return left;
        }

        private ExprNode ParseUnary()
        {
            if (Current.Type is TokenType.Minus or TokenType.Not)
            {
                string op = _tokens[_pos++].Value;
                ExprNode operand = ParseUnary();
                return new UnaryExprNode(op, operand);
            }
            return ParsePostfix();
        }

        private ExprNode ParsePostfix()
        {
            ExprNode expr = ParsePrimary();

            // Keep consuming postfix operations
            while (true)
            {
                // arr[i]
                if (Check(TokenType.OpenBracket))
                {
                    _pos++;
                    ExprNode index = ParseExpression();
                    Expect(TokenType.CloseBracket);
                    expr = new ArrayAccessNode(expr, index);
                }
                // Travel through all the members, namespaces, etc...
                else if (Check(TokenType.Dot))
                {
                    _pos++;
                    string member = Expect(TokenType.Identifier).Value;

                    // Function call
                    if (Check(TokenType.OpenParen))
                    {
                        _pos++;
                        List<ExprNode> arguments = ParseArgumentsList();
                        Expect(TokenType.CloseParen);
                        expr = new FunctionCallNode(member, arguments);
                    }
                    // Accessing another member
                    else
                    {
                        expr = new MemberAccessNode(expr, member);
                    }
                }
                else break;
            }

            return expr;
        }

        private ExprNode ParsePrimary()
        {
            // Literals
            if (Check(TokenType.IntLiteral))
                return new IntLiteralNode(int.Parse(_tokens[_pos++].Value));
            if (Check(TokenType.FloatLiteral))
                return new FloatLiteralNode(float.Parse(_tokens[_pos++].Value));
            if (Check(TokenType.BoolLiteral))
                return new BoolLiteralNode(_tokens[_pos++].Value == "true");
            if (Check(TokenType.StringLiteral))
                return new StringLiteralNode(_tokens[_pos++].Value);

            // new operator
            if (Check(TokenType.New))
            {
                _pos++;
                TypeNode type = ParseType();

                if (type.IsArray || Check(TokenType.OpenBracket))
                {
                    Expect(TokenType.OpenBracket);
                    ExprNode size = ParseExpression();
                    Expect(TokenType.CloseBracket);
                    return new ArrayCreationNode(type, size);
                }

                // Object creation doesn't call a constructor so no parentheses and params
                return new NewObjectNode(type.Name);
            }

            // Identifier, can be a variable or a call
            if (Check(TokenType.Identifier))
            {
                string name = _tokens[_pos++].Value;
                // If we find parentheses, that's a function call.
                // Otherwise it's smth else
                if (Check(TokenType.OpenParen))
                {
                    _pos++;
                    List<ExprNode> arguments = ParseArgumentsList();
                    Expect(TokenType.CloseParen);
                    return new FunctionCallNode(name, arguments);
                }
                return new IdentifierNode(name);
            }

            // Grouped expression like (x + y)
            if (Check(TokenType.OpenParen))
            {
                _pos++;
                ExprNode expr = ParseExpression();
                Expect(TokenType.CloseParen);
                return expr;
            }

            throw new ParserException($"Unexpected token '{Current.Value}'", Current.Line, Current.Column);
        }

        private List<ExprNode> ParseArgumentsList()
        {
            List<ExprNode> args = new();
            while (!Check(TokenType.CloseParen) && !Check(TokenType.EOF))
            {
                args.Add(ParseExpression());
                if (!Match(TokenType.Comma)) break;
            }
            return args;
        }
    }
}
