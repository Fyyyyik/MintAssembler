using System;
using System.Collections.Generic;
using System.Text;

namespace Mint
{
    public enum TokenType
    {
        // Literals
        DecimalLiteral,
        HexLiteral,
        FloatLiteral,
        StringLiteral,
        BoolLiteral,

        // Identifier (class names, variables names...)
        Identifier,

        // Keywords
        New,
        Object,
        Class,
        Enum,
        Interface,
        Pod,
        Rawptr,
        Struct,
        Unknown7,
        Utility,
        Return,
        If,
        Else,
        While,
        For,
        Local,
        Mint,
        Extern,
        This,
        Yield,
        Namespace,
        Const,
        Ref,
        Module,
        Do,
        Include,

        // Types, the basic ones (could also be Identifiers, resolve those later)
        Void,
        Int,
        Float,
        Bool,
        String,
        Byte,
        UShort,
        UInt,
        ULong,
        SByte,
        Short,
        Long,
        Double,
        Char,
        WString,
        Register,

        // Operators
        Plus,                // +
        Minus,               // -
        Star,                // *
        Slash,               // /
        Percent,             // %
        Equals,              // =
        DoubleEquals,        // ==
        NotEqual,            // !=
        Greater,             // >
        Lesser,              // <
        GreaterEquals,       // >=
        LesserEquals,        // <=
        Bang,                 // !
        PlusEquals,          // +=
        MinusEquals,         // -=
        StarEquals,          // *=
        SlashEquals,         // /=
        DoublePlus,          // ++
        DoubleMinus,         // --
        Ampersand,           // &
        Pipe,                // |
        Caret,               // ^
        DoubleLess,          // <<
        DoubleGreater,       // >>
        PercentEquals,       // %=
        AmpersandEquals,     // &=
        PipeEquals,          // |=
        CaretEquals,         // ^=
        DoubleLessEquals,    // <<=
        DoubleGreaterEquals, // >>=
        DoubleAmpersand,     // &&
        DoublePipe,          // ||
        Arrow,               // ->

        // Punctuation
        Semicolon,          // ;
        Comma,              // ,
        Dot,                // .
        Colon,              // :
        OpenParen,          // (
        CloseParen,         // )
        OpenBrace,          // {
        CloseBrace,         // }
        OpenBracket,        // [
        CloseBracket,       // ]

        // Special
        EOF
    }

    /// <summary>
    /// Holds data about a bunch of text from the source file.
    /// </summary>
    /// <param name="Type">What kind of thing is this.</param>
    /// <param name="Value">The actual text read from the source file.</param>
    /// <param name="Line">What line was this token found on.</param>
    /// <param name="Column">What column the token starts.</param>
    public record Token(TokenType Type, string Value, int Line, int Column);
}
