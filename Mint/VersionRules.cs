using Mint.AstNodes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace Mint
{
    public class VersionRules
    {
        public string Version { get; }
        public HashSet<TokenType> IllegalTokens { get; init; } = new();
        public HashSet<string> Dereferenceable { get; init; } = new();
        public Dictionary<string, string[]> AllowedCasts { get; init; } = new();

        public VersionRules(string version) => Version = version;

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
            "0.2" => new VersionRules(version)
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
                    TokenType.Register,

                    TokenType.Class,
                    TokenType.Enum,
                    TokenType.Interface,
                    TokenType.Pod,
                    TokenType.Rawptr,
                    TokenType.Struct,
                    TokenType.Unknown7,
                    TokenType.Utility
                },
                Dereferenceable = new()
                {
                    "int",
                    "float"
                },
                AllowedCasts = new()
                {
                    ["int"] = ["float"],
                    ["float"] = ["int"]
                }
            },

            /*
            "1.0.5" => new VersionRules(version)
            {
                IllegalTokens = new()
                {
                    TokenType.Object
                }
            },
            */

            _ => throw new NotImplementedException($"Cannot get rules for version {version}")
        };
    }
}
