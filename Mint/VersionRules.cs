using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace Mint
{
    internal class VersionRules
    {
        internal ReadOnlyCollection<byte> Version { get; }
        internal HashSet<TokenType> IllegalTokens { get; init; } = new();

        internal VersionRules(IList<byte> version)
        {
            Version = new ReadOnlyCollection<byte>(version);
        }

        private bool IsTokenLegal(Token token)
        {
            return !IllegalTokens.Contains(token.Type);
        }

        internal void ValidateTokens(IList<Token> tokens)
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
