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
        }

        static void Compile(FileInfo inputFile)
        {
            if (!inputFile.Exists)
            {
                Console.Error.WriteLine($"{inputFile.FullName} does not exist");
                return;
            }

            List<Token> tokens = new Lexer(File.ReadAllText(inputFile.FullName)).Tokenize();
            ProgramNode program = new Parser(tokens).Parse();

        }
    }
}
