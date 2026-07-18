using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace Mint
{
    public class VersionRules
    {
        public HashSet<TokenType> IllegalTokens { get; init; } = new();
        public HashSet<string> Dereferenceable { get; init; } = new();

        private bool IsTokenLegal(Token token)
        {
            return !IllegalTokens.Contains(token.Type);
        }

        public void ValidateTokens(IList<Token> tokens)
        {
            foreach (Token token in tokens)
                if (!IsTokenLegal(token))
                    throw new LexerException(
                        $"'{token.Value}' is not supported.",
                        token.Line,
                        token.Column
                    );
        }

        public static VersionRules GetRules(string version) => version switch
        {
            "0.2" => new VersionRules()
            {
                IllegalTokens = new()
                {
                    TokenType.Byte,
                    TokenType.UShort,
                    TokenType.UInt,
                    TokenType.ULong,
                    TokenType.SByte,
                    TokenType.Short,
                    TokenType.Long,
                    TokenType.Double,
                    TokenType.Char,
                    TokenType.WString,
                    TokenType.Register
                },
                Dereferenceable = new()
                {
                    "int",
                    "float"
                }
            },

            _ => throw new NotImplementedException($"Cannot get rules for version {version}")
        };
    }
}
