using Mint;
using Mint.AstNodes;

namespace Mint_Tests
{
    [TestClass]
    public sealed class UnitTests
    {
        [TestMethod]
        public void CompilerTest()
        {
            List<Token> tokens = new Lexer(File.ReadAllText("0.2.mint")).Tokenize();
            foreach (Token token in tokens)
                Console.WriteLine(token.ToString());
            ModuleNode module = new Parser(tokens).Parse();
            return;
        }
    }
}
