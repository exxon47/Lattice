using System.Globalization;

namespace OCRuntime.TextIR;

internal enum TextIrTokenType
{
    Keyword,
    Identifier,
    Instruction,
    Number,
    String,

    Arrow,
    Colon,
    Comma,
    Dot,

    LBrace,
    RBrace,
    LBracket,
    RBracket,
    LParen,
    RParen,

    Less,
    Greater,
    LessEqual,
    GreaterEqual,
    EqualEqual,
    BangEqual,

    Newline,
    Eof
}

internal readonly record struct TextIrToken(TextIrTokenType Type, string Value, int Line, int Column);

internal static class TextIrLexer
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "module", "version",
        "class", "interface", "struct", "enum",
        "field", "method", "constructor", "local",
        "public", "private", "protected", "internal",
        "static", "virtual", "override", "abstract", "sealed",
        "implements", "else", "catch", "finally"
    };

    private static readonly HashSet<string> Instructions = new(StringComparer.Ordinal)
    {
        // Core
        "nop", "dup", "pop", "ret",

        // Loads/stores
        "ldarg", "starg", "ldloc", "stloc", "ldfld", "stfld", "ldsfld", "stsfld",
        "ldnull", "ldstr", "ldc", "ldc.i4", "ldc.i8", "ldc.r4", "ldc.r8",

        // Arithmetic / unary
        "add", "sub", "mul", "div", "rem", "neg", "not",

        // Comparisons
        "ceq", "cne", "cgt", "cge", "clt", "cle",

        // Calls / objects
        "call", "callvirt", "newobj", "newarr", "ldelem", "stelem",

        // Control flow (structured)
        "if", "while", "break", "continue", "try",

        // Branch-based (not fully supported in managed runtime yet)
        "br", "brtrue", "brfalse", "beq", "bne", "bgt", "bge", "blt", "ble",
        "br.s", "brtrue.s", "brfalse.s", "beq.s", "bne.s", "bgt.s", "bge.s", "blt.s", "ble.s"
    };

    internal static List<TextIrToken> Lex(string input)
    {
        var tokens = new List<TextIrToken>(capacity: Math.Max(128, input.Length / 4));

        int i = 0;
        int line = 1;
        int col = 1;

        void Add(TextIrTokenType type, string value, int startLine, int startCol)
            => tokens.Add(new TextIrToken(type, value, startLine, startCol));

        bool IsAtEnd() => i >= input.Length;

        char Current() => input[i];
        char Peek(int offset = 1) => (i + offset) < input.Length ? input[i + offset] : '\0';

        void Advance()
        {
            if (IsAtEnd()) return;
            if (input[i] == '\n')
            {
                line++;
                col = 1;
            }
            else
            {
                col++;
            }
            i++;
        }

        void SkipWhitespace()
        {
            while (!IsAtEnd())
            {
                var ch = Current();
                if (ch == ' ' || ch == '\t' || ch == '\r')
                {
                    Advance();
                    continue;
                }

                if (ch == '/' && Peek() == '/')
                {
                    // Comment
                    while (!IsAtEnd() && Current() != '\n')
                        Advance();
                    continue;
                }

                break;
            }
        }

        string ReadWhile(Func<char, bool> predicate)
        {
            int start = i;
            while (!IsAtEnd() && predicate(Current()))
                Advance();
            return input.Substring(start, i - start);
        }

        string ReadString()
        {
            // Assumes Current() == '"'
            Advance();
            var sb = new System.Text.StringBuilder();
            while (!IsAtEnd() && Current() != '"')
            {
                if (Current() == '\\' && Peek() == '"')
                {
                    Advance(); // \
                    Advance(); // "
                    sb.Append('"');
                    continue;
                }

                sb.Append(Current());
                Advance();
            }
            if (!IsAtEnd() && Current() == '"')
                Advance();
            return sb.ToString();
        }

        while (!IsAtEnd())
        {
            SkipWhitespace();
            if (IsAtEnd()) break;

            int startLine = line;
            int startCol = col;
            char ch = Current();

            if (ch == '\n')
            {
                Add(TextIrTokenType.Newline, "\n", startLine, startCol);
                Advance();
                continue;
            }

            // Two-char operators
            if (ch == '-' && Peek() == '>')
            {
                Add(TextIrTokenType.Arrow, "->", startLine, startCol);
                Advance();
                Advance();
                continue;
            }
            if (ch == '=' && Peek() == '=')
            {
                Add(TextIrTokenType.EqualEqual, "==", startLine, startCol);
                Advance();
                Advance();
                continue;
            }
            if (ch == '!' && Peek() == '=')
            {
                Add(TextIrTokenType.BangEqual, "!=", startLine, startCol);
                Advance();
                Advance();
                continue;
            }
            if (ch == '<' && Peek() == '=')
            {
                Add(TextIrTokenType.LessEqual, "<=", startLine, startCol);
                Advance();
                Advance();
                continue;
            }
            if (ch == '>' && Peek() == '=')
            {
                Add(TextIrTokenType.GreaterEqual, ">=", startLine, startCol);
                Advance();
                Advance();
                continue;
            }

            // Single-char
            switch (ch)
            {
                case '{': Add(TextIrTokenType.LBrace, "{", startLine, startCol); Advance(); continue;
                case '}': Add(TextIrTokenType.RBrace, "}", startLine, startCol); Advance(); continue;
                case '[': Add(TextIrTokenType.LBracket, "[", startLine, startCol); Advance(); continue;
                case ']': Add(TextIrTokenType.RBracket, "]", startLine, startCol); Advance(); continue;
                case '(': Add(TextIrTokenType.LParen, "(", startLine, startCol); Advance(); continue;
                case ')': Add(TextIrTokenType.RParen, ")", startLine, startCol); Advance(); continue;
                case ':': Add(TextIrTokenType.Colon, ":", startLine, startCol); Advance(); continue;
                case ',': Add(TextIrTokenType.Comma, ",", startLine, startCol); Advance(); continue;
                case '.': Add(TextIrTokenType.Dot, ".", startLine, startCol); Advance(); continue;
                case '<': Add(TextIrTokenType.Less, "<", startLine, startCol); Advance(); continue;
                case '>': Add(TextIrTokenType.Greater, ">", startLine, startCol); Advance(); continue;
                case '"':
                {
                    var s = ReadString();
                    Add(TextIrTokenType.String, s, startLine, startCol);
                    continue;
                }
            }

            // Number
            if (char.IsDigit(ch))
            {
                var num = ReadWhile(c => char.IsDigit(c) || c == '.');
                Add(TextIrTokenType.Number, num, startLine, startCol);
                continue;
            }

            // Identifier / word
            if (char.IsLetter(ch) || ch == '_')
            {
                var word = ReadWhile(c => char.IsLetterOrDigit(c) || c == '_' || c == '.');

                if (Keywords.Contains(word))
                {
                    Add(TextIrTokenType.Keyword, word, startLine, startCol);
                }
                else if (Instructions.Contains(word))
                {
                    Add(TextIrTokenType.Instruction, word, startLine, startCol);
                }
                else
                {
                    Add(TextIrTokenType.Identifier, word, startLine, startCol);
                }

                continue;
            }

            // Unknown char; skip it.
            Advance();
        }

        tokens.Add(new TextIrToken(TextIrTokenType.Eof, "", line, col));
        return tokens;
    }
}
