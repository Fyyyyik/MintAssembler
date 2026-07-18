using Mint.AstNodes;
using System;
using System.Collections.Generic;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Xml.Linq;

namespace Mint
{
    public class Parser
    {
        // This error can occur in so many places, that's why I define it here as const.
        private const string VOID_VARIABLE_MSG = "A variable needs to have a type.";

        private readonly List<Token> _tokens;
        private readonly FileInfo? _file;
        private readonly bool _isInclude;
        private int _pos;
        private bool _foundThis;

        private static List<string> _includesVisited = new();

        public Parser(List<Token> tokens, FileInfo? file = null, bool isInclude = false)
        {
            _tokens = tokens;
            _file = file;
            _isInclude = isInclude;
        }

        private (int Line, int Column) CurrentPosition => (Current.Line, Current.Column);

        private bool IsObjectToken => Current.Type is TokenType.Object;

        private Token Current => _tokens[_pos];

        private Token Peek(int offset = 1) => _tokens[Math.Min(_pos + offset, _tokens.Count - 1)];

        private bool Check(TokenType type) => Current.Type == type;

        private Token Expect(TokenType type)
        {
            if (!Check(type))
                throw new ParserException(
                    $"Expected '{type}' but got '{Current.Type}'",
                    Current.Line, Current.Column);
            return _tokens[_pos++];
        }

        // Same as Check but advances by one token if it is the correct type.
        private bool Match(TokenType type)
        {
            if (!Check(type)) return false;
            _pos++;
            return true;
        }

        private string ReadFullName()
        {
            StringBuilder sb = new();
            bool isEnd = false;
            while (!isEnd)
            {
                sb.Append(Expect(TokenType.Identifier).Value);
                if (Match(TokenType.Dot))
                    sb.Append('.');
                else
                    isEnd = true;
            }
            return sb.ToString();
        }

        public ModuleNode Parse()
        {
            string fullName = "";
            if (!_isInclude)
            {
                Expect(TokenType.Module);
                fullName = ReadFullName();
                Expect(TokenType.Semicolon);
            }

            List<ObjectNode> objects = new();
            while (!Check(TokenType.EOF))
            {
                var (line, col) = CurrentPosition;

                if (Check(TokenType.Namespace))
                    objects.AddRange(ParseNamespace());
                else if (Check(TokenType.Include))
                    objects.AddRange(ParseInclude());
                else
                    objects.Add(ParseObject());
            }

            return new ModuleNode(fullName, objects, 0, 0);
        }

        // the compiler doesn't actually care about namespaces, only the objects and their full name
        private List<ObjectNode> ParseNamespace(string currentName = "")
        {
            var (line, col) = CurrentPosition;

            Expect(TokenType.Namespace);
            string fullName;
            if (currentName != "")
                fullName = $"{currentName}.{ReadFullName()}";
            else
                fullName = ReadFullName();
            Expect(TokenType.OpenBrace);

            List<ObjectNode> objects = new();
            while (!Check(TokenType.EOF) && !Check(TokenType.CloseBrace))
            {
                var (nameLine, nameCol) = CurrentPosition;

                if (Check(TokenType.Namespace))
                    objects.AddRange(ParseNamespace(fullName));
                else
                {
                    ObjectNode newObj = ParseObject();
                    objects.Add(newObj with { Name = $"{fullName}.{newObj.Name}" });
                }
            }
            Expect(TokenType.CloseBrace);

            return objects;
        }

        private List<ObjectNode> ParseInclude()
        {
            var (line, col) = CurrentPosition;

            Expect(TokenType.Include);

            if (_file == null)
                throw new ParserException("Cannot parse include. No file was provided.", line, col);

            string headerFileName = Expect(TokenType.StringLiteral).Value.Replace('/', '\\');
            Expect(TokenType.Semicolon);
            string? moduleDir = Path.GetDirectoryName(_file.FullName);
            if (moduleDir == null)
                throw new ParserException("Failed to get the directory containing the module file.", line, col);
            string headerPath = $"{moduleDir}{Path.DirectorySeparatorChar}{headerFileName}";

            if (_includesVisited.Contains(headerPath))
                return new(); // we already did this file, leave now before blowing up the program with infinite recursions
            _includesVisited.Add(headerPath);

            if (!File.Exists(headerPath))
                throw new ParserException($"Cannot parse include. No file was found at the resolved path '{headerPath}'.", line, col);

            string headerContent = File.ReadAllText(headerPath);
            return new Parser(new Lexer(headerContent).Tokenize(), new FileInfo(headerPath), true).Parse().Objects;
        }

        private ObjectNode ParseObject()
        {
            var (line, col) = CurrentPosition;

            // figure out the location
            ObjectLocation loc = ObjectLocation.Local; // default location is Local
            switch (Current.Type)
            {
                case TokenType.Local:
                    loc = ObjectLocation.Local;
                    _pos++;
                    break;
                case TokenType.Mint:
                    loc = ObjectLocation.Mint;
                    _pos++;
                    break;
                case TokenType.Extern:
                    loc = ObjectLocation.Extern;
                    _pos++;
                    break;
                default:
                    if (!IsObjectToken)
                        throw new ParserException($"Unknown object location token : '{Current.Type}'.", line, col);
                    break;
            }
            bool isLoc = loc == ObjectLocation.Local;

            (line, col) = CurrentPosition;
            if (!IsObjectToken)
                throw new ParserException($"Expected an object type but got '{Current.Type}'.", line, col);
            _pos++;

            string name = string.Empty;
            if (isLoc)
            {
                name = Expect(TokenType.Identifier).Value;
                if (_isInclude)
                    throw new ParserException($"Object '{name}' from include file '{_file?.FullName}' cannot be set to 'local'.", line, col);
            }
            else
                name = ReadFullName();
            Expect(TokenType.OpenBrace);

            List<MemberNode> members = new();
            while (!Check(TokenType.CloseBrace) && !Check(TokenType.EOF))
                members.Add(ParseMember(isLoc));

            Expect(TokenType.CloseBrace);

            return new ObjectNode(name, members, loc, line, col);
        }

        private MemberNode ParseMember(bool isLocal)
        {
            var (line, col) = CurrentPosition;

            // TODO for future versions : handle enums

            TypeNode? type = ParseType();
            string name = Expect(TokenType.Identifier).Value;

            // Check whether it's a variable or a function.
            // If we find a '(' then it's a function.
            if (Check(TokenType.OpenParen))
            {
                if (isLocal)
                    return ParseFunction(type, name, line, col);
                else
                    return ParseExternalFunction(type, name, line, col); // don't expect a body
            }
            else if (type == null)
                throw new ParserException(VOID_VARIABLE_MSG, line, col);
            else
                return ParseVariable(type, name, line, col);
        }

        private VariableNode ParseVariable(TypeNode type, string name, int line, int col)
        {
            Expect(TokenType.Semicolon);
            return new VariableNode(type, name, line, col);
        }

        private FunctionNode ParseFunction(TypeNode? returnType, string name, int line, int col)
        {
            List<ParamNode> parameters = ParseParameterList();
            bool isConst = Match(TokenType.Const);
            _foundThis = false;
            BlockNode block = ParseBlock();
            return new FunctionNode(returnType, name, isConst, parameters, _foundThis, block, line, col);
        }

        private ExternalFunctionNode ParseExternalFunction(TypeNode? returnType, string name, int line, int col)
        {
            List<TypeNode> types = ParseTypeList();
            bool isConst = Match(TokenType.Const);
            Expect(TokenType.Semicolon);
            return new ExternalFunctionNode(returnType, name, isConst, types, line, col);
        }

        private List<ParamNode> ParseParameterList()
        {
            Expect(TokenType.OpenParen);
            List<ParamNode> parameters = new();

            while (!Check(TokenType.CloseParen) && !Check(TokenType.EOF))
            {
                var (line, col) = CurrentPosition;

                TypeNode? type = ParseType();
                if (type == null)
                    throw new ParserException(VOID_VARIABLE_MSG, line, col);
                string name = Expect(TokenType.Identifier).Value;
                parameters.Add(new ParamNode(type, name, line, col));

                if (!Match(TokenType.Comma)) break;
            }

            Expect(TokenType.CloseParen);
            return parameters;
        }

        private List<TypeNode> ParseTypeList()
        {
            Expect(TokenType.OpenParen);

            List<TypeNode> types = new();
            while (!Check(TokenType.CloseParen) && !Check(TokenType.EOF))
            {
                var (line, col) = CurrentPosition;

                TypeNode? type = ParseType();
                if (type == null)
                    throw new ParserException(VOID_VARIABLE_MSG, line, col);
                types.Add(type);

                if (!Match(TokenType.Comma)) break;
            }

            Expect(TokenType.CloseParen);
            return types;
        }

        public TypeNode? ParseType()
        {
            var (line, col) = CurrentPosition;

            bool isConst = Match(TokenType.Const);
            bool isRef = Match(TokenType.Ref);

            if (Match(TokenType.Void))
                return null;

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
            while (Match(TokenType.Dot))
                name += "." + Expect(TokenType.Identifier).Value;

            // Check for array suffix []
            bool isArray = false;
            if (Check(TokenType.OpenBracket) && Peek().Type == TokenType.CloseBracket)
            {
                _pos += 2;
                isArray = true;
            }

            return new TypeNode(name, isConst, isRef, line, col, isArray);
        }

        private BlockNode ParseBlock()
        {
            var (line, col) = CurrentPosition;

            List<StmtNode> statements = new();

            if (Match(TokenType.OpenBrace))
            {
                while (!Check(TokenType.CloseBrace) && !Check(TokenType.EOF))
                    statements.Add(ParseStatement());
                Expect(TokenType.CloseBrace);
            }
            else statements.Add(ParseStatement());

            return new BlockNode(statements, line, col);
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

            // do ... while
            if (Check(TokenType.Do))
                return ParseDoWhile();

            // return
            if (Check(TokenType.Return))
                return ParseReturn();

            // yield
            if (Check(TokenType.Yield))
                return ParseYield();

            // Var declaration
            if (IsVarDecl())
                return ParseVarDecl();

            return ParseAssignOrExpr();
        }

        private bool IsVarDecl()
        {
            // If we're starting with a const token then we're defining a const value.
            if (Check(TokenType.Const))
                return true;

            // Same with ref
            if (Check(TokenType.Ref))
                return true;

            // Check for the basic 4
            if (Current.Type is TokenType.Int or TokenType.Float or TokenType.Bool or TokenType.String)
                return true;

            if (Check(TokenType.Identifier))
            {
                int i = 1;
                while (Peek(i).Type is TokenType.Identifier or TokenType.Dot)
                {
                    if ((Peek(i).Type == TokenType.Identifier && i % 2 == 0) ||
                        (Peek(i).Type == TokenType.Dot && i % 2 == 1))
                        i++;
                    else if ((Peek(i).Type == TokenType.Identifier && i % 2 == 1) ||
                             (Peek(i).Type == TokenType.OpenBracket &&
                              Peek(i + 1).Type == TokenType.CloseBracket &&
                              Peek(i + 2).Type == TokenType.Identifier))
                        return true;
                }
            }

            // Also check for arrays like "MyClass[] classes"
            if (Check(TokenType.Identifier) &&
                Peek().Type == TokenType.OpenBracket &&
                Peek(2).Type == TokenType.CloseBracket &&
                Peek(3).Type == TokenType.Identifier
            )
                return true;

            return false;
        }

        private IfNode ParseIf()
        {
            var (line, col) = CurrentPosition;

            Expect(TokenType.If);
            Expect(TokenType.OpenParen);
            ExprNode condition = ParseExpression();
            Expect(TokenType.CloseParen);
            BlockNode then = ParseBlock();

            if (Match(TokenType.Else))
            {
                if (Check(TokenType.If))
                    return new IfNode(condition, then, ParseIf(), null, line, col);

                return new IfNode(condition, then, null, ParseBlock(), line, col);
            }

            return new IfNode(condition, then, null, null, line, col);
        }

        private WhileNode ParseWhile()
        {
            var (line, col) = CurrentPosition;

            Expect(TokenType.While);
            Expect(TokenType.OpenParen);
            ExprNode condition = ParseExpression();
            Expect(TokenType.CloseParen);
            BlockNode body = ParseBlock();
            return new WhileNode(condition, body, false, line, col);
        }

        private ForNode ParseFor()
        {
            var (line, col) = CurrentPosition;

            Expect(TokenType.For);
            Expect(TokenType.OpenParen);
            
            StmtNode initializer = ParseStatement();

            ExprNode condition = ParseExpression();
            Expect(TokenType.Semicolon);

            // Can't use ParseStatement() since that one expects a semicolon
            StmtNode increment = ParseAssignOrExpr(expectSemicolon: false);

            Expect(TokenType.CloseParen);

            BlockNode body = ParseBlock();
            return new ForNode(initializer, condition, increment, body, line, col);
        }

        private WhileNode ParseDoWhile()
        {
            var (line, col) = CurrentPosition;

            Expect(TokenType.Do);
            
            BlockNode body = ParseBlock();

            Expect(TokenType.While);
            Expect(TokenType.OpenParen);

            ExprNode cond = ParseExpression();

            Expect(TokenType.CloseParen);
            Expect(TokenType.Semicolon);

            return new WhileNode(cond, body, true, line, col);
        }

        private ReturnNode ParseReturn()
        {
            var (line, col) = CurrentPosition;

            Expect(TokenType.Return);
            ExprNode? value = null;
            if (!Check(TokenType.Semicolon))
                value = ParseExpression();
            Expect(TokenType.Semicolon);
            return new ReturnNode(value, line, col);
        }

        private YieldNode ParseYield()
        {
            var (line, col) = CurrentPosition;

            Expect(TokenType.Yield);
            ExprNode frameCount = ParseExpression();
            Expect(TokenType.Semicolon);
            return new YieldNode(frameCount, line, col);
        }

        private VarDeclNode ParseVarDecl()
        {
            var (line, col) = CurrentPosition;

            TypeNode? type = ParseType();
            if (type == null)
                throw new ParserException(VOID_VARIABLE_MSG, line, col);
            string name = Expect(TokenType.Identifier).Value;
            Expect(TokenType.Equals);
            ExprNode initializer = ParseExpression();
            Expect(TokenType.Semicolon);
            return new VarDeclNode(type, name, initializer, line, col);
        }

        private StmtNode ParseAssignOrExpr(bool expectSemicolon = true)
        {
            var (line, col) = CurrentPosition;

            ExprNode expr = ParseExpression();
            
            // Equals means assign
            if (Check(TokenType.Equals))
            {
                _pos++; // consume =
                ExprNode value = ParseExpression();
                if (expectSemicolon) Expect(TokenType.Semicolon);
                return new AssignNode(expr, value, line, col);
            }

            // Compound assignements like x += 5
            if (IsCompoundAssign())
            {
                string op = Current.Type switch
                {
                    TokenType.PlusEquals => "+",
                    TokenType.MinusEquals => "-",
                    TokenType.StarEquals => "*",
                    TokenType.SlashEquals => "/",
                    TokenType.PercentEquals => "%",
                    TokenType.AmpersandEquals => "&",
                    TokenType.PipeEquals => "|",
                    TokenType.CaretEquals => "^",
                    TokenType.DoubleGreaterEquals => ">>",
                    TokenType.DoubleLessEquals => "<<",
                    _ => throw new ParserException("Unknown compound operator", Current.Line, Current.Column)
                };
                _pos++;
                ExprNode value = ParseExpression();
                if (expectSemicolon) Expect(TokenType.Semicolon);

                BinaryExprNode desugared = new(
                    expr,
                    op,
                    value,
                    line,
                    col
                );
                return new AssignNode(expr, desugared, line, col);
            }

            // Otherwise it's just a standalone expression
            if (expectSemicolon) Expect(TokenType.Semicolon);
            return new ExprStmtNode(expr, line, col);
        }

        private bool IsCompoundAssign() => Current.Type is TokenType.PlusEquals
                                                        or TokenType.MinusEquals
                                                        or TokenType.StarEquals
                                                        or TokenType.SlashEquals
                                                        or TokenType.PercentEquals
                                                        or TokenType.AmpersandEquals
                                                        or TokenType.PipeEquals
                                                        or TokenType.CaretEquals
                                                        or TokenType.DoubleGreaterEquals
                                                        or TokenType.DoubleLessEquals;

        private ExprNode ParseExpression() => ParseEquality();

        private ExprNode ParseEquality()
        {
            ExprNode left = ParseComparison();
            while (Current.Type is TokenType.DoubleEquals or TokenType.NotEqual)
            {
                var (line, col) = CurrentPosition;
                string op = _tokens[_pos++].Value;
                ExprNode right = ParseComparison();
                left = new BinaryExprNode(left, op, right, line, col);
            }
            return left;
        }

        private ExprNode ParseComparison()
        {
            ExprNode left = ParseLogicalOr();
            while (Current.Type is TokenType.Greater or TokenType.GreaterEquals
                                or TokenType.Lesser or TokenType.LesserEquals)
            {
                var (line, col) = CurrentPosition;
                string op = _tokens[_pos++].Value;
                ExprNode right = ParseLogicalOr();
                left = new BinaryExprNode(left, op, right, line, col);
            }
            return left;
        }

        private ExprNode ParseLogicalOr()
        {
            ExprNode left = ParseLogicalAnd();
            while (Current.Type is TokenType.DoublePipe)
            {
                var (line, col) = CurrentPosition;
                string op = _tokens[_pos++].Value;
                ExprNode right = ParseLogicalAnd();
                left = new BinaryExprNode(left, op, right, line, col);
            }
            return left;
        }

        private ExprNode ParseLogicalAnd()
        {
            ExprNode left = ParseOr();
            while (Current.Type is TokenType.DoubleAmpersand)
            {
                var (line, col) = CurrentPosition;
                string op = _tokens[_pos++].Value;
                ExprNode right = ParseOr();
                left = new BinaryExprNode(left, op, right, line, col);
            }
            return left;
        }

        private ExprNode ParseOr()
        {
            ExprNode left = ParseXor();
            while (Current.Type is TokenType.Pipe)
            {
                var (line, col) = CurrentPosition;
                string op = _tokens[_pos++].Value;
                ExprNode right = ParseXor();
                left = new BinaryExprNode(left, op, right, line, col);
            }
            return left;
        }

        private ExprNode ParseXor()
        {
            ExprNode left = ParseAnd();
            while (Current.Type is TokenType.Caret)
            {
                var (line, col) = CurrentPosition;
                string op = _tokens[_pos++].Value;
                ExprNode right = ParseAnd();
                left = new BinaryExprNode(left, op, right, line, col);
            }
            return left;
        }

        private ExprNode ParseAnd()
        {
            ExprNode left = ParseShift();
            while (Current.Type is TokenType.Ampersand)
            {
                var (line, col) = CurrentPosition;
                string op = _tokens[_pos++].Value;
                ExprNode right = ParseShift();
                left = new BinaryExprNode(left, op, right, line, col);
            }
            return left;
        }

        private ExprNode ParseShift()
        {
            ExprNode left = ParseAddition();
            while (Current.Type is TokenType.DoubleGreater or TokenType.DoubleLess)
            {
                var (line, col) = CurrentPosition;
                string op = _tokens[_pos++].Value;
                ExprNode right = ParseAddition();
                left = new BinaryExprNode(left, op, right, line, col);
            }
            return left;
        }

        private ExprNode ParseAddition()
        {
            ExprNode left = ParseMultiplication();
            while (Current.Type is TokenType.Plus or TokenType.Minus)
            {
                var (line, col) = CurrentPosition;
                string op = _tokens[_pos++].Value;
                ExprNode right = ParseMultiplication();
                left = new BinaryExprNode(left, op, right, line, col);
            }
            return left;
        }

        private ExprNode ParseMultiplication()
        {
            ExprNode left = ParseUnary();
            while (Current.Type is TokenType.Star or TokenType.Slash or TokenType.Percent)
            {
                var (line, col) = CurrentPosition;
                string op = _tokens[_pos++].Value;
                ExprNode right = ParseUnary();
                left = new BinaryExprNode(left, op, right, line, col);
            }
            return left;
        }

        private ExprNode ParseUnary()
        {
            if (Current.Type is TokenType.Minus or TokenType.Bang)
            {
                var (line, col) = CurrentPosition;
                string op = _tokens[_pos++].Value;
                ExprNode operand = ParseUnary();
                return new UnaryExprNode(op, operand, line, col);
            }
            return ParseIncrementDecrement();
        }

        private ExprNode ParseIncrementDecrement()
        {
            if (Current.Type is TokenType.DoublePlus or TokenType.DoubleMinus)
            {
                var (line, col) = CurrentPosition;
                bool increment = Current.Type == TokenType.DoublePlus;
                _pos++;
                ExprNode target = ParsePostfix();
                CheckTargetIncrement(target, line, col);
                return new IncrementNode(target, true, increment, line, col);
            }

            ExprNode expr = ParsePostfix();

            if (Current.Type is TokenType.DoublePlus or TokenType.DoubleMinus)
            {
                var (line, col) = CurrentPosition;
                CheckTargetIncrement(expr, line, col);
                bool increment = Current.Type == TokenType.DoublePlus;
                _pos++;
                expr = new IncrementNode(expr, false, increment, line, col);
            }

            return expr;
        }

        private void CheckTargetIncrement(ExprNode expr, int line, int col)
        {
            if (expr is not IAssignable)
                throw new ParserException("Cannot increment/decrement on this expression.", line, col);
        }

        private ExprNode ParsePostfix()
        {
            ExprNode expr = ParsePrimary();

            // Keep consuming postfix operations
            while (true)
            {
                var (line, col) = CurrentPosition;

                // arr[i]
                if (Match(TokenType.OpenBracket))
                {
                    ExprNode index = ParseExpression();
                    Expect(TokenType.CloseBracket);
                    expr = new ArrayAccessNode(expr, index, line, col);
                }
                else if (Check(TokenType.Dot) && expr is IdentifierNode ident)
                {
                    // We see something like 'Identifier.Identifier.Identifier'
                    // The parser doesn't know if we're accessing a member or if
                    // it's a fully qualified name for a function or variable
                    // That's a semantic concern, so we just assume it's a
                    // qualified name for now.

                    StringBuilder fullName = new(ident.Name);

                    while (Match(TokenType.Dot))
                        fullName.Append("." + Expect(TokenType.Identifier).Value);

                    if (Match(TokenType.OpenParen))
                    {
                        // A fully qualified function call from wherever the fuck
                        // Let the semantic analyser decide if that shit is valid later
                        List<ExprNode> args = ParseArgumentsList();
                        Expect(TokenType.CloseParen);
                        expr = new QualifiedCallNode(fullName.ToString(), args, line, col);
                    }
                    else expr = new QualifiedAccessNode(fullName.ToString(), line, col); // probably an xref var
                }
                else if (Check(TokenType.Dot) && expr is not IdentifierNode)
                {
                    // Don't know from where and how but we're accessing a
                    // member here.
                    // Example : 'Identifier.Identifier.func().member'
                    // The '.member' part here is what we see.

                    _pos++; // consume the dot
                    string member = Expect(TokenType.Identifier).Value;
                    if (Match(TokenType.OpenParen))
                    {
                        List<ExprNode> args = ParseArgumentsList();
                        Expect(TokenType.CloseParen);
                        expr = new MemberCallNode(expr, member, args, line, col);
                    }
                    else expr = new MemberAccessNode(expr, member, line, col);
                }
                else if (Match(TokenType.Arrow))
                {
                    string member = Expect(TokenType.Identifier).Value;
                    expr = new MemberOffsetNode(expr, member, line, col);
                }
                else break;
            }

            return expr;
        }

        private ExprNode ParsePrimary()
        {
            var (line, col) = CurrentPosition;

            // Literals
            if (Check(TokenType.DecimalLiteral))
                return new IntLiteralNode(int.Parse(_tokens[_pos++].Value), line, col);
            if (Check(TokenType.HexLiteral))
                return new IntLiteralNode(Convert.ToInt32(_tokens[_pos++].Value, 16), line, col);
            if (Check(TokenType.FloatLiteral))
                return new FloatLiteralNode(float.Parse(_tokens[_pos++].Value), line, col);
            if (Check(TokenType.BoolLiteral))
                return new BoolLiteralNode(_tokens[_pos++].Value == "true", line, col);
            if (Check(TokenType.StringLiteral))
                return new StringLiteralNode(_tokens[_pos++].Value, line, col);

            // new operator
            if (Match(TokenType.New))
            {
                TypeNode? type = ParseType();
                if (type == null)
                    throw new ParserException("Cannot create new instance of 'void'.", line, col);

                if (type.IsArray)
                    _pos -= 2; // hacky solution

                if (Check(TokenType.OpenBracket))
                {
                    // Array can be 'new int[]', 'new int[3]', 'new int[] { 0, 1, 2 }' or 'new int[3] { 0, 1, 2 }'

                    type = type with { IsArray = false };

                    _pos++;
                    ExprNode? size = null;
                    if (!Match(TokenType.CloseBracket))
                    {
                        size = ParseExpression();
                        Expect(TokenType.CloseBracket);
                    }
                    else if (Match(TokenType.OpenBrace))
                    {
                        List<ExprNode> inits = new List<ExprNode>();
                        while (!Match(TokenType.CloseBrace))
                        {
                            inits.Add(ParseExpression());
                            Match(TokenType.Comma);
                        }
                        return new ArrayCreationNode(type, size, line, col, inits);
                    }

                    // No initializer
                    return new ArrayCreationNode(type, size, line, col);
                }

                // Trying to create new instance of int, bool, float and string doesn't mean shit
                if (type.Name is "int" or "float" or "bool" or "string")
                    throw new ParserException($"Cannot create new instance of '{type.Name}'.", line, col);

                // Object creation doesn't call a constructor so no parentheses and params (for now)
                return new NewObjectNode(type.Name, line, col);
            }

            // Identifier, can be a variable or a call or some struct (example : Vector3)
            if (Check(TokenType.Identifier))
            {
                string name = _tokens[_pos++].Value;

                // If we find parentheses, that's a function call.
                // Otherwise it's smth else
                if (Match(TokenType.OpenParen))
                {
                    List<ExprNode> arguments = ParseArgumentsList();
                    Expect(TokenType.CloseParen);
                    return new QualifiedCallNode(name, arguments, line, col);
                }

                return new IdentifierNode(name, line, col);
            }

            // Grouped expression and casts
            if (Match(TokenType.OpenParen))
            {
                try
                {
                    TypeNode? type = ParseType();
                    if (type != null)
                    {
                        Expect(TokenType.CloseParen);
                        return new TypeCastNode(type, ParseExpression(), line, col);
                    }
                }
                catch { }

                // probably not a cast then
                ExprNode expr = ParseExpression();
                Expect(TokenType.CloseParen);
                return expr;
            }

            if (Match(TokenType.This))
            {
                _foundThis = true;
                if (Match(TokenType.Dot))
                {
                    // we are accessing something from 'this'
                    string member = Expect(TokenType.Identifier).Value;
                    if (Match(TokenType.OpenParen))
                    {
                        // it's a function
                        List<ExprNode> args = ParseArgumentsList();
                        Expect(TokenType.CloseParen);
                        return new MemberCallNode(new ThisNode(line, col), member, args, line, col);
                    }

                    // it's accessing a variable
                    return new MemberAccessNode(new ThisNode(line, col), member, line, col);
                }

                // it's just a lone 'this'
                return new ThisNode(line, col);
            }

            if (Match(TokenType.Star))
                return new DereferenceNode(ParseExpression(), line, col);

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
