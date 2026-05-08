using System.Text;

namespace MintAssembler
{
    public class Lexer
    {
        private readonly string _source;
        private int _pos;
        private int _line;
        private int _column;

        private static readonly Dictionary<string, TokenType> Keywords = new()
        {
            ["class"]       = TokenType.Class,
            ["new"]         = TokenType.New,
            ["return"]      = TokenType.Return,
            ["if"]          = TokenType.If,
            ["else"]        = TokenType.Else,
            ["while"]       = TokenType.While,
            ["for"]         = TokenType.For,
            ["int"]         = TokenType.Int,
            ["float"]       = TokenType.Float,
            ["bool"]        = TokenType.Bool,
            ["string"]      = TokenType.String
        };

        public Lexer(string source)
        {
            _source = source;
            _pos = 0;
            _line = 1;
            _column = 1;
        }

        private char Current => _pos < _source.Length ? _source[_pos]: '\0';

        private char Peek => _pos + 1 < _source.Length ? _source[_pos + 1] : '\0';

        private char Advance()
        {
            char ch = _source[_pos++];
            if (ch == '\n')
            {
                _line++;
                _column = 1;
            }
            else _column++;
            return ch;
        }

        private void SkipWhitespaceAndComments()
        {
            while (_pos < _source.Length)
            {
                // Skip whitespaces
                if (char.IsWhiteSpace(Current))
                {
                    Advance();
                }
                // Skip single line comments : "//"
                else if (Current == '/' && Peek == '/')
                    while (_pos < _source.Length && Current != '\n')
                        Advance();
                // Skip multi line comments : "/* */"
                else if (Current == '/' && Peek == '*')
                {
                    Advance(); Advance(); // skip "/*"
                    while (_pos < _source.Length && !(Current == '*' && Peek == '/'))
                        Advance();
                    Advance(); Advance(); // skip "*/"
                }
                else break;
            }
        }

        private Token ReadNextToken()
        {
            // Numbers
            if (char.IsDigit(Current))
                return ReadNumber(_line, _column);

            // Strings
            if (Current == '"')
                return ReadString(_line, _column);

            // Identifiers and keywords
            if (char.IsLetterOrDigit(Current) || Current == '_')
                return ReadIdentifierOrKeyword(_line, _column);

            // Operators and punctuation
            return ReadSymbol(_line, _column);
        }

        private Token ReadNumber(int line, int col)
        {
            StringBuilder sb = new();

            if (Current == '0' && Peek == 'x')
            {
                // Hex value, always assume it's an int
                sb.Append(Advance());
                sb.Append(Advance());
                while (char.IsAsciiHexDigit(Current))
                    sb.Append(Advance());
                return new Token(TokenType.IntLiteral, sb.ToString(), line, col);
            }

            while (char.IsDigit(Current))
                sb.Append(Advance());

            // Check for float, I'm not doing that 'f' char shit because I think just having a dot is enough
            if (Current == '.')
            {
                sb.Append(Advance());
                while (char.IsDigit(Current))
                    sb.Append(Advance());
                return new Token(TokenType.FloatLiteral, sb.ToString(), line, col);
            }

            return new Token(TokenType.IntLiteral, sb.ToString(), line, col);
        }

        private Token ReadString(int line, int col)
        {
            Advance();
            StringBuilder sb = new();
            bool hasUnquote = false;
            while (_pos < _source.Length)
            {
                // Handle end of string
                if (Current == '"')
                {
                    hasUnquote = true;
                    Advance();
                    break;
                }

                // Handle escape sequences
                if (Current == '\\')
                {
                    Advance();
                    if (_pos >= _source.Length) break;
                    sb.Append(Current switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        '"' => '"',
                        '\\' => '\\',
                        _ => throw new LexerException($@"Unknown escape \{Current}", line, col)
                    });
                    Advance();
                }
                else
                {
                    sb.Append(Advance());
                }
            }

            if (!hasUnquote)
                throw new LexerException(
                    $"Reached the end of the file and found no unquote to close the string literal.",
                    line,
                    col
                );

            return new Token(TokenType.StringLiteral, sb.ToString(), line, col);
        }

        private Token ReadIdentifierOrKeyword(int line, int col)
        {
            StringBuilder sb = new();
            while (char.IsLetterOrDigit(Current) || Current == '_')
                sb.Append(Advance());

            string text = sb.ToString();

            // Check if it's a keyword
            if (Keywords.TryGetValue(text, out TokenType type))
                return new Token(type, text, line, col);
            return new Token(TokenType.Identifier, text, line, col);
        }

        private Token ReadSymbol(int line, int col)
        {
            char ch = Advance();
            return ch switch
            {
                '+' => new Token(TokenType.Plus, "+", line, col),
                '-' => new Token(TokenType.Minus, "-", line, col),
                '*' => new Token(TokenType.Star, "*", line, col),
                '/' => new Token(TokenType.Slash, "/", line, col),
                ';' => new Token(TokenType.Semicolon, ";", line, col),
                ',' => new Token(TokenType.Comma, ",", line, col),
                '.' => new Token(TokenType.Dot, ".", line, col),
                ':' => new Token(TokenType.Colon, ":", line, col),
                '(' => new Token(TokenType.OpenParen, "(", line, col),
                ')' => new Token(TokenType.CloseParen, ")", line, col),
                '{' => new Token(TokenType.OpenBrace, "{", line, col),
                '}' => new Token(TokenType.CloseBrace, "}", line, col),
                '[' => new Token(TokenType.OpenBracket, "[", line, col),
                ']' => new Token(TokenType.CloseBracket, "]", line, col),
                '=' => Peek == '=' ? (Advance(), new Token(TokenType.DoubleEquals, "==", line, col)).Item2
                                   : new Token(TokenType.Equals, "=", line, col),
                '!' => Peek == '=' ? (Advance(), new Token(TokenType.NotEqual, "!=", line, col)).Item2
                                   : new Token(TokenType.Not, "!", line, col),
                '>' => Peek == '=' ? (Advance(), new Token(TokenType.GreaterEquals, ">=", line, col)).Item2
                                   : new Token(TokenType.Greater, ">", line, col),
                '<' => Peek == '=' ? (Advance(), new Token(TokenType.LesserEquals, "<=", line, col)).Item2
                                   : new Token(TokenType.Lesser, "<", line, col),
                _ => throw new LexerException($"Unexpected character '{ch}'", line, col)
            };
        }

        public List<Token> Tokenize()
        {
            List<Token> tokens = new();

            while (_pos < _source.Length)
            {
                SkipWhitespaceAndComments();
                if (_pos >= _source.Length) break;

                Token token = ReadNextToken();
                if (token != null)
                    tokens.Add(token);
            }

            tokens.Add(new Token(TokenType.EOF, "", _line, _column));
            return tokens;
        }
    }
}
