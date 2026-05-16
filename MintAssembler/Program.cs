using System.CommandLine;
using Mint;
using Mint.AstNodes;

namespace MintAssembler
{
    internal class Program
    {
        static void Main(string[] args)
        {
            RootCommand root = new("Mint to bytecode CLI tool");

            Command compileCom = new("compile", "Compile a .mint file");
            Argument<FileInfo> compileFileArg = new("file")
            {
                Description = "The file to compile"
            };
            compileCom.Arguments.Add(compileFileArg);
            compileCom.SetAction(result =>
            {
                FileInfo? file = result.GetValue(compileFileArg);
                if (file is null)
                {
                    Console.Error.WriteLine("No file to compile provided.");
                    return;
                }
                Compile(file);
            });
            root.Subcommands.Add(compileCom);

            ParseResult result = root.Parse(args);
            result.Invoke();
        }

        static void Compile(FileInfo inputFile)
        {
            if (!inputFile.Exists)
            {
                Console.Error.WriteLine($"{inputFile.FullName} does not exist");
                return;
            }

            List<Token> tokens = new Lexer(File.ReadAllText(inputFile.FullName)).Tokenize();
            Console.WriteLine("Tokens :");
            foreach (Token token in tokens)
                Console.WriteLine(token.ToString());
            Console.WriteLine("\nNodes :");
            ModuleNode program = new Parser(tokens).Parse();
            Console.WriteLine(program.ToString());

            Console.WriteLine("MintAssembler has exited.");
        }
    }
}
