# Mint Assembler

A fanmade compiler for HAL's in-house bytecode language "Mint", used notably in modern Kirby games.

## Supported versions

These are the versions that the compiler can currently compile to.
Please see **[this spreadsheet](https://docs.google.com/spreadsheets/d/1A_08ytw1oIBhqBzpkxDIU86RwmYAjG4DopogqCQllMo/edit?usp=sharing)** to see what version is used in what game.

- 0.2

This project aims to cover every Mint version we are aware of.

## Usage

`MintAssembler`

	Description:
	  Mint to bytecode CLI tool

	Usage:
	  MintAssembler [command] [options]

	Options:
	  -?, -h, --help  Show help and usage information
	  --version       Show version information

	Commands:
	  compile <file> <version>  Compile a .mint file

---

`MintAssembler compile`

	Description:
	  Compile a .mint file

	Usage:
	  MintAssembler compile <file> <version> [options]

	Arguments:
	  <file>     The file to compile.
	  <version>  The target version of the module.

	Options:
	  -v, --verbose            Show more info.
	  -o, --output <output>    Specify where the compiled binary should be created. If the path points to a Mint archive the module will be added to it.
	  -a, --archive <archive>  The Mint archives to use as context.
	  -?, -h, --help           Show help and usage information

## Examples

To help you get started with the **Mint** language there are some example scripts for each version available **[here](https://github.com/Fyyyyik/MintAssembler/tree/master/MintAssembler/Examples)**.