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

        }

        private StmtNode ParseAssignOrExpr(bool expectSemicolon = true)
        {
            ExprNode expr = ParseExpression();
            
            // If the expression is an identifier followed by = it's an assignement
            if (expr is IdentifierNode ident && Check(TokenType.Equals))
            {
                _pos++; // consume =
                ExprNode value = ParseExpression();
                if (expectSemicolon) Expect(TokenType.Semicolon);
                return new AssignNode(ident.Name, value);
            }


        }

        private ExprNode ParseExpression();
    }
}
