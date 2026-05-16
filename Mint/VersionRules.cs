using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace Mint
{
    public class VersionRules
    {
        public ReadOnlyCollection<byte> Version { get; }
        public HashSet<TokenType> IllegalTokens { get; init; } = new();

        public VersionRules(IList<byte> version)
        {
            Version = new ReadOnlyCollection<byte>(version);
        }

        private bool IsTokenLegal(Token token)
        {
            return !IllegalTokens.Contains(token.Type);
        }

        public void ValidateTokens(IList<Token> tokens)
        {
            foreach (Token token in tokens)
                if (!IsTokenLegal(token))
                    throw new LexerException(
                        $"'{token.Value}' is not supported in version {Version.ToString()}",
                        token.Line,
                        token.Column
                    );
        }
    }
}
