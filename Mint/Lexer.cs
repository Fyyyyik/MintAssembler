using System.Text;

namespace Mint
{
    public class Lexer
    {
        private readonly string _source;
        private int _pos;
        private int _line;
        private int _column;

        private static readonly Dictionary<string, TokenType> Keywords = new()
        {
            ["object"]    = TokenType.Object,
            ["new"]       = TokenType.New,
            ["return"]    = TokenType.Return,
            ["if"]        = TokenType.If,
            ["else"]      = TokenType.Else,
            ["while"]     = TokenType.While,
            ["for"]       = TokenType.For,
            ["local"]     = TokenType.Local,
            ["mint"]      = TokenType.Mint,
            ["extern"]    = TokenType.Extern,
            ["this"]      = TokenType.This,
            ["void"]      = TokenType.Void,
            ["int"]       = TokenType.Int,
            ["float"]     = TokenType.Float,
            ["bool"]      = TokenType.Bool,
            ["string"]    = TokenType.String,
            ["byte"]      = TokenType.Byte,
            ["ushort"]    = TokenType.UShort,
            ["uint"]      = TokenType.UInt,
            ["ulong"]     = TokenType.ULong,
            ["sbyte"]     = TokenType.SByte,
            ["short"]     = TokenType.Short,
            ["long"]      = TokenType.Long,
            ["double"]    = TokenType.Double,
            ["char"]      = TokenType.Char,
            ["wstring"]   = TokenType.WString,
            ["register"]  = TokenType.Register,
            ["yield"]     = TokenType.Yield,
            ["namespace"] = TokenType.Namespace,
            ["const"]     = TokenType.Const,
            ["ref"]       = TokenType.Ref,
            ["module"]    = TokenType.Module,
            ["do"]        = TokenType.Do,
            ["true"]      = TokenType.BoolLiteral,
            ["false"]     = TokenType.BoolLiteral
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
                // Hex value
                sb.Append(Advance());
                sb.Append(Advance());
                while (char.IsAsciiHexDigit(Current))
                    sb.Append(Advance());
                return new Token(TokenType.HexLiteral, sb.ToString(), line, col);
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

            return new Token(TokenType.DecimalLiteral, sb.ToString(), line, col);
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
            switch (ch)
            {
                case '+':
                    switch (Current)
                    {
                        case '=':
                            Advance();
                            return new Token(TokenType.PlusEquals, "+=", line, col);
                        case '+':
                            Advance();
                            return new Token(TokenType.DoublePlus, "++", line, col);
                    }
                    return new Token(TokenType.Plus, "+", line, col);
                case '-':
                    switch (Current)
                    {
                        case '=':
                            Advance();
                            return new Token(TokenType.MinusEquals, "-=", line, col);
                        case '-':
                            Advance();
                            return new Token(TokenType.DoubleMinus, "--", line, col);
                        case '>':
                            Advance();
                            return new Token(TokenType.Arrow, "->", line, col);
                    }
                    return new Token(TokenType.Minus, "-", line, col);
                case '*':
                    if (Current == '=')
                    {
                        Advance();
                        return new Token(TokenType.StarEquals, "*=", line, col);
                    }
                    return new Token(TokenType.Star, "*", line, col);
                case '/':
                    if (Current == '=')
                    {
                        Advance();
                        return new Token(TokenType.SlashEquals, "/=", line, col);
                    }
                    return new Token(TokenType.Slash, "/", line, col);
                case '%':
                    if (Current == '=')
                    {
                        Advance();
                        return new Token(TokenType.PercentEquals, "%=", line, col);
                    }
                    return new Token(TokenType.Percent, "%", line, col);
                case ';':
                    return new Token(TokenType.Semicolon, ";", line, col);
                case ',':
                    return new Token(TokenType.Comma, ",", line, col);
                case '.':
                    return new Token(TokenType.Dot, ".", line, col);
                case ':':
                    return new Token(TokenType.Colon, ":", line, col);
                case '(':
                    return new Token(TokenType.OpenParen, "(", line, col);
                case ')':
                    return new Token(TokenType.CloseParen, ")", line, col);
                case '{':
                    return new Token(TokenType.OpenBrace, "{", line, col);
                case '}':
                    return new Token(TokenType.CloseBrace, "}", line, col);
                case '[':
                    return new Token(TokenType.OpenBracket, "[", line, col);
                case ']':
                    return new Token(TokenType.CloseBracket, "]", line, col);
                case '=':
                    if (Current == '=')
                    {
                        Advance();
                        return new Token(TokenType.DoubleEquals, "==", line, col);
                    }
                    return new Token(TokenType.Equals, "=", line, col);
                case '!':
                    if (Current == '=')
                    {
                        Advance();
                        return new Token(TokenType.NotEqual, "!=", line, col);
                    }
                    return new Token(TokenType.Bang, "!", line, col);
                case '>':
                    switch (Current)
                    {
                        case '=':
                            Advance();
                            return new Token(TokenType.GreaterEquals, ">=", line, col);
                        case '>':
                            Advance();
                            if (Current == '=')
                            {
                                Advance();
                                return new Token(TokenType.DoubleGreaterEquals, ">>=", line, col);
                            }
                            return new Token(TokenType.DoubleGreater, ">>", line, col);
                    }
                    return new Token(TokenType.Greater, ">", line, col);
                case '<':
                    switch (Current)
                    {
                        case '=':
                            Advance();
                            return new Token(TokenType.LesserEquals, "<=", line, col);
                        case '<':
                            Advance();
                            if (Current == '=')
                            {
                                Advance();
                                return new Token(TokenType.DoubleLessEquals, "<<=", line, col);
                            }
                            return new Token(TokenType.DoubleLess, "<<", line, col);
                    }
                    return new Token(TokenType.Lesser, "<", line, col);
                case '&':
                    switch (Current)
                    {
                        case '=':
                            Advance();
                            return new Token(TokenType.AmpersandEquals, "&=", line, col);
                        case '&':
                            Advance();
                            return new Token(TokenType.DoubleAmpersand, "&&", line, col);
                    }
                    return new Token(TokenType.Ampersand, "&", line, col);
                case '|':
                    switch (Current)
                    {
                        case '=':
                            Advance();
                            return new Token(TokenType.PipeEquals, "|=", line, col);
                        case '|':
                            Advance();
                            return new Token(TokenType.DoublePipe, "||", line, col);
                    }
                    return new Token(TokenType.Pipe, "|", line, col);
                case '^':
                    if (Current == '=')
                    {
                        Advance();
                        return new Token(TokenType.CaretEquals, "^=", line, col);
                    }
                    return new Token(TokenType.Caret, "^", line, col);
            }

            throw new LexerException($"Unexpected character '{ch}'", line, col);
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
