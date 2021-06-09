using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace RedDove.Config
{
    using IMapping = IDictionary<string, object>;
    using Mapping = Dictionary<string, object>;
    using ISequence = IList<object>;
    using Sequence = List<object>;

    public enum TokenKind
    {
        EOF,
        Word,
        Integer,
        Float,
        String,
        Newline,
        LeftCurly,
        RightCurly,
        LeftBracket,
        RightBracket,
        LeftParenthesis,
        RightParenthesis,
        LessThan,
        GreaterThan,
        LessThanOrEqual,
        GreaterThanOrEqual,
        Assign,
        Equal,
        Unequal,
        AltUnequal,
        LeftShift,
        RightShift,
        Dot,
        Comma,
        Colon,
        At,
        Plus,
        Minus,
        Star,
        Power,
        Slash,
        SlashSlash,
        Modulo,
        BackTick,
        Dollar,
        True,
        False,
        None,
        Is,
        In,
        Not,
        And,
        Or,
        BitwiseAnd,
        BitwiseOr,
        BitwiseXor,
        BitwiseComplement,
        Complex,
        IsNot,
        NotIn,
    }

    public class Location
    {
        public int Line { get; internal set; }

        public int Column { get; internal set; }

        public Location(int line, int column)
        {
            Line = line;
            Column = column;
        }

        public Location(Location source)
        {
            Update(source);
        }

        internal void Update(Location source)
        {
            Line = source.Line;
            Column = source.Column;
        }

        internal void NextLine()
        {
            Line++;
            Column = 1;
        }
        public override string ToString()
        {
            StringBuilder result = new StringBuilder();

            result.Append('(').Append(Line).Append(", ").Append(Column).Append(')');

            return result.ToString();
        }
    }

    public abstract class ASTNode
    {
        public TokenKind Kind { get; }
        public Location Start { get; internal set; }

        public Location End { get; internal set; }

        protected ASTNode(TokenKind kind)
        {
            Kind = kind;
        }
    }
    public class Token : ASTNode
    {
        private readonly string text;

        public object Value { get; internal set; }

        public string Text => text;

        public Token(TokenKind kind, string text, object value = null) : base(kind)
        {
            this.text = text;
            Value = value;
        }

        public override string ToString()
        {
            return $"Token({Kind}:{text}:{Value})";
        }
    }

    public class MappingItems : ASTNode
    {
        internal List<Tuple<Token, ASTNode>> Items = new List<Tuple<Token, ASTNode>>();

        internal MappingItems() : base(TokenKind.LeftCurly)
        {
        }
    }

    public class ListItems : ASTNode
    {
        internal List<ASTNode> Items = new List<ASTNode>();

        internal ListItems() : base(TokenKind.LeftBracket)
        {
        }
    }

    public abstract class RecognizerException : Exception
    {
        public Location Location { get; internal set; }

        protected RecognizerException(string message) : base(message) { }

        protected RecognizerException(string message, Exception inner) : base(message, inner) { }

        public override string ToString()
        {
            StringBuilder result = new StringBuilder();

            if (Location != null)
            {
                result.Append(Location).Append(": ");
            }
            result.Append(base.ToString());
            return result.ToString();
        }
    }

    public class TokenizerException : RecognizerException
    {
        public TokenizerException(string message) : base(message) { }

        public TokenizerException(string message, Exception inner) : base(message, inner) { }
    }

    public class ParserException : RecognizerException
    {
        public ParserException(string message) : base(message) { }

        public ParserException(string message, Exception inner) : base(message, inner) { }
    }

    public class ConfigException : RecognizerException
    {
        public ConfigException(string message) : base(message) { }

        public ConfigException(string message, Exception inner) : base(message, inner) { }
    }

    public class InvalidPathException : ConfigException
    {
        public InvalidPathException(string message) : base(message) { }

        public InvalidPathException(string message, Exception inner) : base(message, inner) { }

    }

    public class BadIndexException : ConfigException
    {
        public BadIndexException(string message) : base(message) { }

        public BadIndexException(string message, Exception inner) : base(message, inner) { }

    }

    public class CircularReferenceException : ConfigException
    {
        public CircularReferenceException(string message) : base(message) { }

        public CircularReferenceException(string message, Exception inner) : base(message, inner) { }

    }

    public class Tokenizer : IEnumerable<Token>
    {
        private readonly TextReader reader;
        private readonly Location location;
        private readonly Location charLocation;

        private readonly Stack<Tuple<char, Location>> pushedBackChars;

        internal static IDictionary<char, TokenKind> Punctuation = new Dictionary<char, TokenKind>()
        {
            {':', TokenKind.Colon},
            {'-', TokenKind.Minus},
            {'+', TokenKind.Plus},
            {'*', TokenKind.Star },
            {'/', TokenKind.Slash},
            {'%', TokenKind.Modulo},
            {',', TokenKind.Comma},
            {'{', TokenKind.LeftCurly},
            {'}', TokenKind.RightCurly},
            {'[', TokenKind.LeftBracket},
            {']', TokenKind.RightBracket},
            {'(', TokenKind.LeftParenthesis},
            {')', TokenKind.RightParenthesis},
            {'@', TokenKind.At},
            {'$', TokenKind.Dollar},
            {'<', TokenKind.LessThan},
            {'>', TokenKind.GreaterThan},
            {'!', TokenKind.Not},
            {'~', TokenKind.BitwiseComplement},
            {'&', TokenKind.BitwiseAnd},
            {'|', TokenKind.BitwiseOr},
            {'^', TokenKind.BitwiseXor},
            {'.', TokenKind.Dot}
        };

        protected static Dictionary<string, TokenKind> Keywords = new Dictionary<string, TokenKind>()
        {
            { "true", TokenKind.True},
            { "false", TokenKind.False},
            { "null", TokenKind.None},
            { "is", TokenKind.Is},
            { "in", TokenKind.In},
            { "not", TokenKind.Not},
            { "and", TokenKind.And},
            { "or", TokenKind.Or},
        };

        public static object NullValue = new object();

        protected static Dictionary<TokenKind, object> KeywordValues = new Dictionary<TokenKind, object>()
        {
            { TokenKind.True, true },
            { TokenKind.False, false },
            { TokenKind.None, NullValue },
        };

        internal static Dictionary<char, char> Escapes = new Dictionary<char, char>()
        {
            { 'a', '\a' },
            { 'b', '\b' },
            { 'f', '\f' },
            { 'n', '\n' },
            { 'r', '\r' },
            { 't', '\t' },
            { 'v', '\v' },
            { '\\', '\\' },
            { '\"', '\"' },
            { '\'', '\'' },
        };

        internal Tokenizer(TextReader reader)
        {
            this.reader = reader;
            pushedBackChars = new Stack<Tuple<char, Location>>();
            location = new Location(1, 1);
            charLocation = new Location(location);
        }

        private void PushBack(char c)
        {
            if (c != '\0')
            {
                pushedBackChars.Push(new Tuple<char, Location>(c, new Location(charLocation)));
            }
        }

        protected char GetChar()
        {
            char result;

            if (pushedBackChars.Count > 0)
            {
                var t = pushedBackChars.Pop();
                result = t.Item1;
                Location loc = t.Item2;
                charLocation.Update(loc);
                location.Update(loc);  // will be bumped later
            }
            else
            {
                charLocation.Update(location);
                int c = reader.Read();

                if (c < 0)
                {
                    result = '\0';
                }
                else
                {
                    result = (char) c;
                }
            }
            if (result != '\0')
            {
                if (result == '\n')
                {
                    location.NextLine();
                }
                else
                {
                    location.Column++;
                }
            }
            return result;
        }

        internal TokenKind GetNumber(StringBuilder text, out object value, Location startLocation, Location endLocation)
        {
            TokenKind result = TokenKind.Integer;
            bool inExponent = false;
            int radix = 0;
            bool dotSeen = text.IndexOf('.') >= 0;
            bool lastWasDigit = char.IsDigit(text[text.Length - 1]);
            char c;

            Action<char> appendChar = (ch) =>
            {
                text.Append(ch);
                endLocation.Update(charLocation);
            };

            while (true)
            {
                c = GetChar();

                if (c == '\0')
                {
                    break;
                }
                if (c == '.')
                {
                    dotSeen = true;
                }
                else if (c == '_')
                {
                    if (lastWasDigit)
                    {
                        appendChar(c);
                        lastWasDigit = false;
                        continue;
                    }

                    throw new TokenizerException($"invalid '_' in number: {text}{c}") { Location = charLocation };
                }
                lastWasDigit = false;  // unless set in one of the clauses below
                if ((radix == 0) && (c >= '0') && (c <= '9') ||
                    (radix == 8) && (c >= '0') && (c <= '7') ||
                    (radix == 2) && (c >= '0') && (c <= '1') ||
                    (radix == 16) && Utils.IsHexDigit(c))
                {
                    appendChar(c);
                    lastWasDigit = true;
                }
                else if (((c == 'o') || (c == 'O') ||
                          (c == 'x') || (c == 'X') ||
                          (c == 'b') || (c == 'B')) && (text.Length == 1) && (text[0] == '0'))
                {
                    radix = ((c == 'o') || (c == 'O')) ? 8 : (((c == 'x') || (c == 'X')) ? 16 : 2);
                    appendChar(c);
                }
                else if ((radix == 0) && (c == '.') && !inExponent && (text.IndexOf(c) < 0))
                {
                    appendChar(c);
                }
                else if ((radix == 0) && (c == '-') && (text.IndexOf('-', 1) < 0) && inExponent)
                {
                    appendChar(c);
                }
                else if ((radix == 0) && ((c == 'e') || (c == 'E')) &&
                         (text.IndexOf('e') < 0) && (text.IndexOf('E') < 0) &&
                         (text[text.Length - 1] != '_'))
                {
                    appendChar(c);
                    inExponent = true;
                }
                else
                {
                    break;
                }
            }

            // reached the end of any actual number part. Before checking
            // for complex, ensure that the last char wasn't an underscore.
            if (text[text.Length - 1] == '_')
            {
                var e = new TokenizerException($"invalid '_' at end of number: {text}") { Location = charLocation };

                e.Location.Column -= 1;
                throw e;
            }
            if (c != '\0')
            {
                if ((radix == 0) && ((c == 'j') || (c == 'J')))
                {
                    result = TokenKind.Complex;
                    appendChar(c);
                }
                else
                {
                    // not allowed to have a letter or digit which wasn't
                    // accepted, nor a . as that could be intended to be part
                    // of the number
                    if ((c != '.') && !char.IsLetterOrDigit(c))
                    {
                        PushBack(c);
                    }
                    else
                    {
                        throw new TokenizerException($"invalid character in number: {text}{c}")
                        {
                            Location = charLocation
                        };
                    }
                }
            }
            string s = text.ToString().Replace("_", "");
            if (radix != 0)
            {
                value = Convert.ToInt64(s.Substring(2), radix);
            }
            else if (result == TokenKind.Complex)
            {
                double imaginary = Convert.ToDouble(s.Substring(0, s.Length - 1));
                value = new Complex(0, imaginary);
            }
            else if (inExponent || dotSeen)
            {
                result = TokenKind.Float;
                value = Convert.ToDouble(s);
            }
            else
            {
                radix = (s[0] == '0') ? 8 : 10;
                try
                {
                    value = Convert.ToInt64(s, radix);
                }
                catch (FormatException fe)
                {
                    // info on precise location not readily available
                    throw new TokenizerException($"invalid character in number: {s}", fe) { Location = startLocation };
                }
            }
            return result;
        }

        internal string ParseEscapes(string s)
        {
            string result;
            int i = s.IndexOf('\\');

            if (i < 0)
            {
                result = s;
            }
            else
            {
                StringBuilder sb = new StringBuilder();
                bool failed = false;

                while (i >= 0)
                {
                    int n = s.Length;

                    if (i > 0)
                    {
                        sb.Append(s.Substring(0, i));
                    }
                    char c = s[i + 1];

                    if (Escapes.ContainsKey(c))
                    {
                        sb.Append(Escapes[c]);
                        i += 2;
                    }
                    else if ((c == 'x') || (c == 'X') || (c == 'u') || (c == 'U'))
                    {
                        int slen = ((c == 'x') || (c == 'X')) ? 4 : ((c == 'u') ? 6 : 10);

                        if ((i + slen) > n)
                        {
                            failed = true;
                            break;
                        }
                        string p = s.Substring(i + 2, slen - 2);
                        try
                        {
                            UInt32 j = Convert.ToUInt32(p, 16);

                            if (((j >= 0xd800) && (j < 0xe000)) || j >= 0x110000)
                            {
                                failed = true;
                                break;
                            }
                            if (j < 0x10000)
                            {
                                sb.Append((char) j);
                            }
                            else
                            {
                                // surrogate pair dance
                                uint high = ((j - 0x10000) >> 10) | 0xd800;
                                uint low = (j & 0x3FF) | 0xdc00;

                                sb.Append((char) high);
                                sb.Append((char) low);
                            }
                            i += slen;
                        }
                        catch (Exception)
                        {
                            failed = true;
                            break;
                        }
                    }
                    else
                    {
                        failed = true;
                        break;
                    }
                    s = s.Substring(i);
                    i = s.IndexOf('\\');
                }
                if (failed)
                {
                    throw new TokenizerException($"invalid escape sequence at index {i}");
                }
                sb.Append(s);
                result = sb.ToString();
            }
            return result;
        }

        internal Token GetToken()
        {
            TokenKind kind = TokenKind.EOF;
            StringBuilder text = new StringBuilder();
            object value = null;
            string s = null;
            Location startLocation = new Location(location);
            Location endLocation = new Location(location);

            Action<char> appendChar = (c) =>
            {
                text.Append(c);
                endLocation.Update(charLocation);
            };

            while (true)
            {
                char c = GetChar();

                startLocation.Update(charLocation);
                endLocation.Update(charLocation);

                if (c == '\0')
                {
                    break;
                }
                if (c == '#')
                {
                    text.Append(c).Append(reader.ReadLine());
                    kind = TokenKind.Newline;
                    location.NextLine();
                    endLocation.Update(location);
                    break;
                }
                if (c == '\n')
                {
                    text.Append(c);
                    endLocation.Update(location);
                    endLocation.Column--;
                    kind = TokenKind.Newline;
                    break;
                }
                if (c == '\r')
                {
                    c = GetChar();
                    if (c != '\n')
                    {
                        PushBack(c);
                    }
                    kind = TokenKind.Newline;
                    location.NextLine();
                    break;
                }
                if (c == '\\')
                {
                    c = GetChar();
                    if (c != '\n')
                    {
                        throw new TokenizerException("unexpected \\") { Location = charLocation };
                    }
                    endLocation.Update(location);
                }
                else if (char.IsWhiteSpace(c))
                {
                    continue;
                }
                else if (c == '`')
                {
                    kind = TokenKind.BackTick;
                    appendChar(c);
                    while (true)
                    {
                        c = GetChar();
                        if (c == '\0')
                        {
                            break;
                        }
                        if (char.IsControl(c))
                        {
                            // non-printables not allowed here.
                            throw new TokenizerException($"invalid char in `-string: {text}")
                            {
                                Location = charLocation
                            };
                        }
                        appendChar(c);
                        if (c == '`')
                        {
                            break;
                        }
                    }
                    if (c == '\0')
                    {
                        throw new TokenizerException($"unterminated `-string: {text}") { Location = startLocation };
                    }
                    value = ParseEscapes(text.ToString().Substring(1, text.Length - 2));
                    s = text.ToString();
                    break;
                }
                else if ((c == '\'') || (c == '\"'))
                {
                    char quote = c;
                    bool multiLine = false;
                    bool escaped = false;
                    int n;

                    kind = TokenKind.String;
                    appendChar(c);
                    char c1 = GetChar();
                    Location c1Loc = new Location(charLocation);

                    if (c1 != quote)
                    {
                        PushBack(c1);
                    }
                    else
                    {
                        char c2 = GetChar();

                        if (c2 != quote)
                        {
                            PushBack(c2);
                            if (c2 == '\0')
                            {
                                charLocation.Update(c1Loc);
                            }
                            PushBack(c1);
                        }
                        else
                        {
                            multiLine = true;
                            text.Append(quote).Append(quote);
                        }
                    }
                    var quoter = text.ToString();
                    while (true)
                    {
                        c = GetChar();
                        if (c == '\0')
                        {
                            break;
                        }
                        appendChar(c);
                        if ((c == quote) && !escaped)
                        {
                            n = text.Length;

                            if (!multiLine || (n >= 6) && text.SubString(n - 3, 3).Equals(quoter) && text[n - 4] != '\\')
                            {
                                break;
                            }
                        }
                        if (c == '\\')
                        {
                            escaped = !escaped;
                        }
                        else
                        {
                            escaped = false;
                        }
                    }
                    if (c == '\0')
                    {
                        throw new TokenizerException($"unterminated string: {text}") { Location = startLocation };
                    }
                    n = quoter.Length;
                    value = ParseEscapes(text.ToString().Substring(n, text.Length - 2 * n));
                    s = text.ToString();
                    break;
                }
                else if (char.IsLetter(c) || (c == '_'))
                {
                    kind = TokenKind.Word;
                    appendChar(c);
                    c = GetChar();
                    while ((c != '\0') && (char.IsLetterOrDigit(c) || (c == '_')))
                    {
                        appendChar(c);
                        c = GetChar();
                    }
                    PushBack(c);
                    s = text.ToString();
                    value = s; // default
                    if (Keywords.ContainsKey(s))
                    {
                        kind = Keywords[s];
                        if (KeywordValues.ContainsKey(kind))
                        {
                            value = KeywordValues[kind];
                        }
                    }
                    break;
                }
                else if (char.IsDigit(c))
                {
                    appendChar(c);
                    kind = GetNumber(text, out value, startLocation, endLocation);
                    break;
                }
                else if (c == '=')
                {
                    char nc = GetChar();

                    if (nc != '=')
                    {
                        kind = TokenKind.Assign;
                        appendChar(c);
                        PushBack(nc);
                    }
                    else
                    {
                        kind = TokenKind.Equal;
                        text.Append(c);
                        appendChar(c);
                    }
                    break;
                }
                else if (Punctuation.ContainsKey(c))
                {
                    kind = Punctuation[c];

                    appendChar(c);
                    if (c == '.')
                    {
                        c = GetChar();
                        if (!char.IsDigit(c))
                        {
                            PushBack(c);
                        }
                        else
                        {
                            appendChar(c);
                            kind = GetNumber(text, out value, startLocation, endLocation);
                        }
                    }
                    else if (c == '-')
                    {
                        c = GetChar();
                        if (!char.IsDigit(c) && (c != '.'))
                        {
                            PushBack(c);
                        }
                        else
                        {
                            appendChar(c);
                            kind = GetNumber(text, out value, startLocation, endLocation);
                        }
                    }
                    else if (c == '<')
                    {
                        c = GetChar();
                        if (c == '=')
                        {
                            kind = TokenKind.LessThanOrEqual;
                            appendChar(c);
                        }
                        else if (c == '>')
                        {
                            kind = TokenKind.AltUnequal;
                            appendChar(c);
                        }
                        else if (c == '<')
                        {
                            kind = TokenKind.LeftShift;
                            appendChar(c);
                        }
                        else
                        {
                            PushBack(c);
                        }
                    }
                    else if (c == '>')
                    {
                        c = GetChar();
                        if (c == '=')
                        {
                            kind = TokenKind.GreaterThanOrEqual;
                            appendChar(c);
                        }
                        else if (c == '>')
                        {
                            kind = TokenKind.RightShift;
                            appendChar(c);
                        }
                        else
                        {
                            PushBack(c);
                        }
                    }
                    else if (c == '!')
                    {
                        c = GetChar();
                        if (c == '=')
                        {
                            kind = TokenKind.Unequal;
                            appendChar(c);
                        }
                        else
                        {
                            PushBack(c);
                        }
                    }
                    else if (c == '/')
                    {
                        c = GetChar();
                        if (c != '/')
                        {
                            PushBack(c);
                        }
                        else
                        {
                            kind = TokenKind.SlashSlash;
                            appendChar(c);
                        }
                    }
                    else if (c == '*')
                    {
                        c = GetChar();
                        if (c != '*')
                        {
                            PushBack(c);
                        }
                        else
                        {
                            kind = TokenKind.Power;
                            appendChar(c);
                        }
                    }
                    else if ((c == '&') || (c == '|'))
                    {
                        char c2 = GetChar();

                        if (c2 != c)
                        {
                            PushBack(c2);
                        }
                        else
                        {
                            appendChar(c2);
                            kind = (c2 == '&') ? TokenKind.And : TokenKind.Or;
                        }
                    }
                    break;
                }
                else
                {
                    throw new TokenizerException($"unexpected character: {c}") { Location = charLocation };
                }
            }
            if (s == null)
            {
                s = text.ToString();
            }

            Token result = new Token(kind, s, value)
            {
                Start = new Location(startLocation),
                End = new Location(endLocation)
            };

            return result;
        }

        public IEnumerator<Token> GetEnumerator()
        {
            while (true)
            {
                Token t = GetToken();

                yield return t;
                if (t.Kind == TokenKind.EOF)
                    break;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    internal class UnaryNode : ASTNode
    {
        public ASTNode Operand { get; }

        internal UnaryNode(TokenKind kind, ASTNode operand) : base(kind)
        {
            Operand = operand;
        }

        public override string ToString()
        {
            return $"UnaryNode({Kind}, {Operand})";
        }
    }

    internal class BinaryNode : ASTNode
    {
        public ASTNode Left { get; }

        public ASTNode Right { get; }

        internal BinaryNode(TokenKind kind, ASTNode left, ASTNode right) : base(kind)
        {
            Left = left;
            Right = right;
        }

        public override string ToString()
        {
            return $"BinaryNode({Kind}, {Left}, {Right})";
        }
    }

    internal class SliceNode : ASTNode
    {
        public ASTNode StartIndex { get; internal set; }
        public ASTNode StopIndex { get; internal set; }
        public ASTNode Step { get; internal set; }

        internal SliceNode(ASTNode start, ASTNode stop, ASTNode step) : base(TokenKind.Colon)
        {
            StartIndex = start;
            StopIndex = stop;
            Step = step;
        }

        public override string ToString()
        {
            return $"SliceNode({StartIndex},{StopIndex},{Step})";
        }
    }

    public class Parser
    {
        private readonly Tokenizer tokenizer;

        internal Token next;  // internal because used by ParsePath for sanity checking

        public Parser(TextReader reader)
        {
            if (reader != null)
            {
                tokenizer = new Tokenizer(reader);
            }
            Advance();
        }

        public bool AtEnd => next.Kind == TokenKind.EOF;

        internal TokenKind Advance()
        {
            next = tokenizer.GetToken();

            return next.Kind;
        }

        internal Token Expect(TokenKind kind)
        {
            if (next.Kind != kind)
            {
                throw new ParserException($"expected {kind} but got {next.Kind}") { Location = next.Start };
            }
            var result = next;
            Advance();
            return result;
        }

        public Location Position => next.Start;

        protected TokenKind ConsumeNewlines()
        {
            TokenKind kind = next.Kind;

            while (kind == TokenKind.Newline)
            {
                kind = Advance();
            }
            return kind;
        }

        public static Parser MakeParser(string text)
        {
            StringReader reader = new StringReader(text);
            return new Parser(reader);
        }

        public static object Parse(string text, string rule = "MappingBody")
        {
            Parser p = MakeParser(text);
            MethodInfo info = typeof(Parser).GetMethod(rule);

            if (info == null)
            {
                throw new ArgumentException($"No such thing: {rule}");
            }
            try
            {
                return info.Invoke(p, Utils.EMPTY_OBJECT_LIST);
            }
            catch (TargetInvocationException e)
            {
                if (e.InnerException != null)
                {
                    throw e.InnerException;
                }
                throw;
            }
        }

        private static readonly HashSet<TokenKind> ExpressionStarters = new HashSet<TokenKind>()
        {
            TokenKind.LeftCurly, TokenKind.LeftBracket, TokenKind.LeftParenthesis,
            TokenKind.At, TokenKind.Dollar, TokenKind.BackTick, TokenKind.Plus,
            TokenKind.Minus, TokenKind.BitwiseComplement, TokenKind.Integer,
            TokenKind.Integer, TokenKind.Float, TokenKind.Complex,
            TokenKind.True, TokenKind.False, TokenKind.None,
            TokenKind.Not, TokenKind.String, TokenKind.Word
        };

        internal ListItems ListBody()
        {
            var result = new ListItems();
            TokenKind kind = ConsumeNewlines();

            while (ExpressionStarters.Contains(kind))
            {
                result.Items.Add(Expr());
                kind = next.Kind;
                if ((kind != TokenKind.Newline) && (kind != TokenKind.Comma))
                {
                    break;
                }
                Advance();
                kind = ConsumeNewlines();
            }
            return result;
        }

        internal ListItems List()
        {
            Expect(TokenKind.LeftBracket);
            ListItems result = ListBody();
            Expect(TokenKind.RightBracket);
            return result;
        }

        private Token ObjectKey()
        {
            Token result;

            if (next.Kind == TokenKind.String)
            {
                result = Strings();
            }
            else
            {
                result = next;
                Advance();
            }
            return result;
        }

        public MappingItems MappingBody()
        {
            var result = new MappingItems();
            TokenKind kind = ConsumeNewlines();

            if ((kind != TokenKind.RightCurly) && (kind != TokenKind.EOF))
            {
                if ((kind != TokenKind.Word) && (kind != TokenKind.String))
                {
                    throw new ParserException($"unexpected type for key: {kind}") { Location = next.Start };
                }
                while ((kind == TokenKind.Word) || (kind == TokenKind.String))
                {
                    Token key = ObjectKey();

                    kind = next.Kind;
                    if ((kind == TokenKind.Colon) || (kind == TokenKind.Assign))
                    {
                        Advance();
                    }
                    else
                    {
                        throw new ParserException($"key-value separator expected, but found: {kind}")
                        {
                            Location = next.Start
                        };
                    }
                    ConsumeNewlines();
                    ASTNode value = Expr();
                    result.Items.Add(new Tuple<Token, ASTNode>(key, value));
                    kind = next.Kind;
                    if ((kind == TokenKind.Newline) || (kind == TokenKind.Comma))
                    {
                        Advance();
                        kind = ConsumeNewlines();
                    }
                    else if ((kind != TokenKind.RightCurly) && (kind != TokenKind.EOF))
                    {
                        throw new ParserException($"unexpected after key-value: {kind}")
                        {
                            Location = next.Start
                        };
                    }
                }
            }
            return result;
        }

        public MappingItems Mapping()
        {
            Expect(TokenKind.LeftCurly);
            var result = MappingBody();
            Expect(TokenKind.RightCurly);
            return result;
        }

        internal ASTNode Container()
        {
            TokenKind kind = ConsumeNewlines();
            ASTNode result;

            if (kind == TokenKind.LeftCurly)
            {
                result = Mapping();
            }
            else if (kind == TokenKind.LeftBracket)
            {
                result = List();
            }
            else if ((kind == TokenKind.Word) || (kind == TokenKind.String) ||
                     (kind == TokenKind.EOF))
            {
                result = MappingBody();
            }
            else
            {
                throw new ParserException($"unexpected type for container: {kind}") { Location = next.Start };
            }
            ConsumeNewlines();
            return result;
        }

        internal Token Strings()
        {
            Token result = next;

            if (Advance() == TokenKind.String)
            {
                // get all the strings together and make a new
                // token which spans them all
                string s = result.Text;
                string v = (string) result.Value;
                StringBuilder allText = new StringBuilder();
                StringBuilder allValue = new StringBuilder();
                Location end;
                TokenKind kind;
                Location start = result.Start;

                do
                {
                    allText.Append(s);
                    allValue.Append(v);
                    s = next.Text;
                    v = (string) next.Value;
                    end = next.End;
                    kind = Advance();
                } while (kind == TokenKind.String);
                allText.Append(s);  // the last one
                allValue.Append(v);
                result = new Token(TokenKind.String, allText.ToString(), allValue.ToString())
                {
                    Start = start,
                    End = end
                };
            }
            return result;
        }

        internal static HashSet<TokenKind> ValueTokens = new HashSet<TokenKind>()
        {
            TokenKind.Word,
            TokenKind.Integer,
            TokenKind.Float,
            TokenKind.Complex,
            TokenKind.String,
            TokenKind.BackTick,
            TokenKind.None,
            TokenKind.True,
            TokenKind.False
        };

        public Token Value()
        {
            TokenKind kind = next.Kind;
            Token t;

            if (!ValueTokens.Contains(kind))
            {
                throw new ParserException($"unexpected when looking for value: {kind}") { Location = next.Start };
            }
            if (kind == TokenKind.String)
            {
                t = Strings();
            }
            else
            {
                t = next;
                Advance();
            }
            return t;
        }

        public ASTNode Atom()
        {
            TokenKind kind = next.Kind;
            ASTNode result;

            switch (kind)
            {
                case TokenKind.LeftCurly:
                    result = Mapping();
                    break;
                case TokenKind.LeftBracket:
                    result = List();
                    break;
                case TokenKind.Word:
                case TokenKind.Integer:
                case TokenKind.Float:
                case TokenKind.Complex:
                case TokenKind.String:
                case TokenKind.BackTick:
                case TokenKind.True:
                case TokenKind.False:
                case TokenKind.None:
                    result = Value();
                    break;
                case TokenKind.Dollar:
                    Advance();
                    Expect(TokenKind.LeftCurly);
                    var spos = next.Start;
                    result = new UnaryNode(kind, Primary()) { Start = spos };
                    Expect(TokenKind.RightCurly);
                    break;
                case TokenKind.LeftParenthesis:
                    Expect(TokenKind.LeftParenthesis);
                    result = Expr();
                    Expect(TokenKind.RightParenthesis);
                    break;
                default:
                    throw new ParserException($"unexpected: {kind}") { Location = next.Start };
            }
            return result;
        }

        private void InvalidIndex(int n, Location pos)
        {
            throw new ParserException($"invalid index: expected one value, {n} found") { Location = pos };
        }

        private ASTNode GetSliceElement()
        {
            var lb = ListBody();
            var n = lb.Items.Count;

            if (n != 1)
            {
                InvalidIndex(n, lb.Start);
            }
            return lb.Items[0];
        }

        private ASTNode Trailer(out TokenKind op)
        {
            ASTNode result = null;
            op = next.Kind;

            if (op != TokenKind.LeftBracket)
            {
                Expect(TokenKind.Dot);
                result = Expect(TokenKind.Word);
            }
            else
            {
                var spos = next.Start;
                TokenKind kind = Advance();
                bool isSlice = false;
                ASTNode stopIndex, step;

                var startIndex = stopIndex = step = null;
                if (kind == TokenKind.Colon)
                {
                    // it's a slice like [:xyz:abc]
                    isSlice = true;
                }
                else
                {
                    var elem = GetSliceElement();

                    kind = next.Kind;
                    if (kind != TokenKind.Colon)
                    {
                        result = elem;
                    }
                    else
                    {
                        startIndex = elem;
                        isSlice = true;
                    }
                }
                if (isSlice)
                {
                    op = TokenKind.Colon;
                    // at this point startIndex is either null (if foo[:xyz]) or a
                    // value representing the start. We are pointing at the COLON
                    // after the start value
                    kind = Advance();
                    if (kind == TokenKind.Colon)  // no stop, but there might be a step
                    {
                        kind = Advance();
                        if (kind != TokenKind.RightBracket)
                        {
                            step = GetSliceElement();
                        }
                    }
                    else if (kind != TokenKind.RightBracket)
                    {
                        stopIndex = GetSliceElement();
                        kind = next.Kind;
                        if (kind == TokenKind.Colon)
                        {
                            kind = Advance();
                            if (kind != TokenKind.RightBracket)
                            {
                                step = GetSliceElement();
                            }
                        }
                    }
                    result = new SliceNode(startIndex, stopIndex, step) { Start = spos };
                }
                Expect(TokenKind.RightBracket);
            }
            return result;
        }

        public ASTNode Primary()
        {
            ASTNode result = Atom();

            while ((next.Kind == TokenKind.Dot) || (next.Kind == TokenKind.LeftBracket))
            {
                var spos = next.Start;
                TokenKind op;
                ASTNode rhs = Trailer(out op);

                result = new BinaryNode(op, result, rhs) { Start = spos };
            }
            return result;
        }

        public ASTNode Power()
        {
            var spos = next.Start;
            ASTNode result = Primary();

            while (next.Kind == TokenKind.Power)
            {
                Advance();
                result = new BinaryNode(TokenKind.Power, result, UnaryExpr()) { Start = spos };
                spos = next.Start;
            }
            return result;
        }

        public ASTNode UnaryExpr()
        {
            TokenKind kind = next.Kind;
            ASTNode result;

            if ((kind != TokenKind.Plus) && (kind != TokenKind.Minus) &&
                (kind != TokenKind.BitwiseComplement) && (kind != TokenKind.At))
            {
                result = Power();
            }
            else
            {
                var spos = next.Start;
                Advance();
                result = new UnaryNode(kind, UnaryExpr()) { Start = spos };
            }
            return result;
        }

        public ASTNode MulExpr()
        {
            var spos = next.Start;
            ASTNode result = UnaryExpr();
            TokenKind kind = next.Kind;

            while ((kind == TokenKind.Star) ||
                   (kind == TokenKind.Slash) ||
                   (kind == TokenKind.SlashSlash) ||
                   (kind == TokenKind.Modulo))
            {
                Advance();
                result = new BinaryNode(kind, result, UnaryExpr()) { Start = spos };
                kind = next.Kind;
                spos = next.Start;
            }
            return result;
        }

        public ASTNode AddExpr()
        {
            var spos = next.Start;
            ASTNode result = MulExpr();

            while ((next.Kind == TokenKind.Plus) ||
                   (next.Kind == TokenKind.Minus))
            {
                TokenKind op = next.Kind;
                Advance();
                result = new BinaryNode(op, result, MulExpr()) { Start = spos };
                spos = next.Start;
            }
            return result;
        }

        public ASTNode ShiftExpr()
        {
            var spos = next.Start;
            ASTNode result = AddExpr();

            while ((next.Kind == TokenKind.LeftShift) ||
                   (next.Kind == TokenKind.RightShift))
            {
                TokenKind op = next.Kind;
                Advance();
                result = new BinaryNode(op, result, AddExpr()) { Start = spos };
                spos = next.Start;
            }
            return result;
        }

        public ASTNode BitAndExpr()
        {
            var spos = next.Start;
            ASTNode result = ShiftExpr();

            while (next.Kind == TokenKind.BitwiseAnd)
            {
                Advance();
                result = new BinaryNode(TokenKind.BitwiseAnd, result, ShiftExpr()) { Start = spos };
                spos = next.Start;
            }
            return result;
        }

        public ASTNode BitXorExpr()
        {
            var spos = next.Start;
            ASTNode result = BitAndExpr();

            while (next.Kind == TokenKind.BitwiseXor)
            {
                Advance();
                result = new BinaryNode(TokenKind.BitwiseXor, result, BitAndExpr()) { Start = spos };
                spos = next.Start;
            }
            return result;
        }

        public ASTNode BitOrExpr()
        {
            var spos = next.Start;
            ASTNode result = BitXorExpr();

            while (next.Kind == TokenKind.BitwiseOr)
            {
                Advance();
                result = new BinaryNode(TokenKind.BitwiseOr, result, BitXorExpr()) { Start = spos };
                spos = next.Start;
            }
            return result;
        }

        private static readonly HashSet<TokenKind> ComparisonOperators = new HashSet<TokenKind>()
        {
            TokenKind.LessThan,
            TokenKind.LessThanOrEqual,
            TokenKind.GreaterThan,
            TokenKind.GreaterThanOrEqual,
            TokenKind.Equal,
            TokenKind.Unequal,
            TokenKind.AltUnequal,
            TokenKind.Is,
            TokenKind.In,
            TokenKind.Not
        };

        internal TokenKind CompOp()
        {
            TokenKind result = next.Kind;
            bool advance = false;

            Advance();
            if ((result == TokenKind.Is) && (next.Kind == TokenKind.Not))
            {
                result = TokenKind.IsNot;
                advance = true;
            }
            else if ((result == TokenKind.Not) && (next.Kind == TokenKind.In))
            {
                result = TokenKind.NotIn;
                advance = true;
            }
            if (advance)
            {
                Advance();
            }
            return result;
        }

        public ASTNode Comparison()
        {
            ASTNode result = BitOrExpr();

            while (ComparisonOperators.Contains(next.Kind))
            {
                TokenKind op = CompOp();
                result = new BinaryNode(op, result, BitOrExpr());
            }
            return result;
        }

        public ASTNode NotExpr()
        {
            ASTNode result;

            if (next.Kind != TokenKind.Not)
            {
                result = Comparison();
            }
            else
            {
                var spos = next.Start;
                Advance();
                result = new UnaryNode(TokenKind.Not, NotExpr()) { Start = spos };
            }
            return result;
        }


        public ASTNode AndExpr()
        {
            ASTNode result = NotExpr();

            while (next.Kind == TokenKind.And)
            {
                Advance();
                result = new BinaryNode(TokenKind.And, result, NotExpr());
            }
            return result;
        }

        public ASTNode Expr()
        {
            var spos = next.Start;
            ASTNode result = AndExpr();

            while (next.Kind == TokenKind.Or)
            {
                Advance();
                result = new BinaryNode(TokenKind.Or, result, AndExpr()) { Start = spos };
                spos = next.Start;
            }
            return result;
        }
    }

    internal class NamedReader : StreamReader
    {
        public static readonly Encoding Utf8Encoding = new UTF8Encoding(false);
        public string Name { get; internal set; }

        public NamedReader(string path, Encoding encoding) : base(path, encoding)
        {
            Name = path;
        }

        public NamedReader(string path) : this(path, Utf8Encoding) { }
    }

    internal class ListWrapper : Sequence
    {
        internal Config config;

        public ListWrapper(Config config)
        {
            this.config = config;
        }

        public new object this[int index]
        {
            get
            {
                var result = config.Evaluated(base[index]);
                this[index] = result;
                return result;
            }
            internal set { base[index] = value; }
        }

        internal object BaseGet(int index)
        {
            return base[index];
        }

        public ISequence AsList()
        {
            var result = new Sequence(Count);

            foreach (var item in this)
            {
                var rv = config.Evaluated(item);

                if (rv is ListWrapper)
                {
                    rv = ((ListWrapper) rv).AsList();
                }
                else if (rv is DictWrapper)
                {
                    rv = ((DictWrapper) rv).AsDict();
                }
                else if (rv is Config)
                {
                    rv = ((Config) rv).AsDict();
                }
                result.Add(rv);
            }
            return result;
        }

    }

    internal class DictWrapper : Mapping
    {
        internal Config config;

        public DictWrapper(Config config)
        {
            this.config = config;
        }

        public new object this[string key]
        {
            get
            {
                object result;

                if (ContainsKey(key) || Utils.IsIdentifier(key))
                {
                    result = config.Evaluated(base[key]);
                }
                else
                {
                    result = config.GetFromPath(key);
                }
                // There is already caching available at the config level
                // so not sure if this is needed (was to avoid recomputations)
                // this[key] = result;
                return result;
            }
            internal set { base[key] = value; }
        }

        internal object BaseGet(string key)
        {
            return base[key];
        }

        public IMapping AsDict()
        {
            var result = new Mapping();

            foreach (var entry in this)
            {
                var rv = config.Evaluated(entry.Value);

                if (rv is DictWrapper)
                {
                    rv = ((DictWrapper) rv).AsDict();
                }
                else if (rv is ListWrapper)
                {
                    rv = ((ListWrapper) rv).AsList();
                }
                else if (rv is Config)
                {
                    rv = ((Config) rv).AsDict();
                }
                result[entry.Key] = rv;
            }
            return result;
        }
    }

    public delegate object StringConverter(string s, Config cfg);

    internal class Evaluator
    {
        private readonly Config config;
        internal HashSet<ASTNode> refsSeen = new HashSet<ASTNode>();

        internal Evaluator(Config config)
        {
            this.config = config;
        }

        protected static readonly HashSet<TokenKind> SCALAR_TOKENS = new HashSet<TokenKind>()
        {
            TokenKind.String,
            TokenKind.Integer,
            TokenKind.Float,
            TokenKind.Complex,
            TokenKind.False,
            TokenKind.True,
            TokenKind.None
        };

        protected object EvalAt(UnaryNode node)
        {
            var o = Evaluate(node.Operand);
            if (!(o is string))
            {
                throw new ConfigException("@ operand must be a string");
            }
            var fn = (string) o;
            string p = fn;
            var found = false;
            object result;

            if (Path.IsPathRooted(fn))
            {
                if (File.Exists(fn))
                {
                    found = true;
                }
            }
            else
            {
                p = Path.Combine(config.RootDir, fn);

                if (File.Exists(p))
                {
                    found = true;
                }
                else
                {
                    foreach (var dn in config.IncludePath)
                    {
                        p = Path.Combine(dn, fn);
                        if (File.Exists(p))
                        {
                            found = true;
                            break;
                        }
                    }
                }
            }
            if (!found)
            {
                throw new ConfigException($"Unable to locate {fn}") { Location = node.Start };
            }
            var reader = new NamedReader(p);
            var parser = new Parser(reader);
            var cnode = parser.Container();

            if (!(cnode is MappingItems))
            {
                result = cnode;
            }
            else
            {
                var cfg = new Config { Context = config.Context, Cached = config.Cached, Parent = config, IncludePath = config.IncludePath, Path = p, RootDir = System.IO.Path.GetDirectoryName(p) };

                cfg.data = cfg.WrapMapping((MappingItems) cnode);
                result = cfg;
            }
            return result;
        }

        protected object EvalReference(UnaryNode node)
        {
            return GetFromPath(node.Operand);
        }

        protected void MergeDicts(IMapping target, IMapping source)
        {
            foreach (var entry in source)
            {
                var k = entry.Key;
                var v = entry.Value;

                if (target.ContainsKey(k) && (target[k] is IMapping) && (v is IMapping))
                {
                    MergeDicts((IMapping) target[k], (IMapping) v);
                }
                else
                {
                    target[k] = v;
                }
            }
        }

        internal DictWrapper GetMerged(DictWrapper lhs, DictWrapper rhs)
        {
            var result = new DictWrapper(lhs.config);

            MergeDicts(result, lhs.AsDict());
            MergeDicts(result, rhs.AsDict());
            return result;
        }

        protected Complex ToComplex(object o)
        {
            Complex result;

            if (o is Complex)
            {
                result = (Complex) o;
            }
            else if (o is double)
            {
                result = new Complex((double) o, 0.0);
            }
            else if (o is long)
            {
                result = new Complex(Convert.ToDouble(o), 0.0);
            }
            else
            {
                throw new ApplicationException($"cannot convert {o} to a complex number");
            }
            return result;
        }

        protected object EvalAdd(BinaryNode node)
        {
            var lhs = Evaluate(node.Left);
            var rhs = Evaluate(node.Right);
            object result;

            if ((lhs is DictWrapper) && (rhs is DictWrapper))
            {
                result = GetMerged((DictWrapper) lhs, (DictWrapper) rhs);
            }
            else if ((lhs is ListWrapper) && (rhs is ListWrapper))
            {
                var l1 = (ListWrapper) lhs;
                var l2 = (ListWrapper) rhs;
                var combined = new ListWrapper(l1.config);

                combined.AddRange(l1.AsList());
                combined.AddRange(l2.AsList());
                result = combined;
            }
            else if ((lhs is string) && (rhs is string))
            {
                result = string.Concat((string) lhs, (string) rhs);
            }
            else if ((lhs is Complex) || (rhs is Complex))
            {
                result = ToComplex(lhs) + ToComplex(rhs);
            }
            else if ((lhs is long) && (rhs is long))
            {
                result = (long) lhs + (long) rhs;
            }
            else if ((lhs is double) || (rhs is double))
            {
                result = Convert.ToDouble(lhs) + Convert.ToDouble(rhs);
            }
            else
            {
                throw new ApplicationException($"unable to add {rhs} to {lhs}");
            }
            return result;
        }

        protected object EvalSubtract(BinaryNode node)
        {
            var lhs = Evaluate(node.Left);
            var rhs = Evaluate(node.Right);
            object result;

            if ((lhs is DictWrapper) && (rhs is DictWrapper))
            {
                var ldw = (DictWrapper) lhs;
                var rdw = (DictWrapper) rhs;
                DictWrapper dw = new DictWrapper(ldw.config);

                foreach (var entry in ldw)
                {
                    var k = entry.Key;
                    if (!rdw.ContainsKey(k))
                    {
                        dw[k] = entry.Value;
                    }
                }
                result = dw;
            }
            else if ((lhs is Complex) || (rhs is Complex))
            {
                result = ToComplex(lhs) - ToComplex(rhs);
            }
            else if ((lhs is long) && (rhs is long))
            {
                result = (long) lhs - (long) rhs;
            }
            else if ((lhs is double) || (rhs is double))
            {
                result = Convert.ToDouble(lhs) - Convert.ToDouble(rhs);
            }
            else
            {
                throw new ApplicationException($"unable to subtract {rhs} from {lhs}");
            }
            return result;
        }

        protected object EvalMultiply(BinaryNode node)
        {
            var lhs = Evaluate(node.Left);
            var rhs = Evaluate(node.Right);
            object result;

            if ((lhs is Complex) || (rhs is Complex))
            {
                result = ToComplex(lhs) * ToComplex(rhs);
            }
            else if ((lhs is long) && (rhs is long))
            {
                result = (long) lhs * (long) rhs;
            }
            else if ((lhs is double) || (rhs is double))
            {
                result = Convert.ToDouble(lhs) * Convert.ToDouble(rhs);
            }
            else
            {
                throw new ApplicationException($"unable to multiply {lhs} by {rhs}");
            }
            return result;
        }

        protected object EvalDivide(BinaryNode node)
        {
            var lhs = Evaluate(node.Left);
            var rhs = Evaluate(node.Right);
            object result;

            if ((lhs is Complex) || (rhs is Complex))
            {
                result = ToComplex(lhs) / ToComplex(rhs);
            }
            else if ((lhs is double) || (rhs is double))
            {
                result = Convert.ToDouble(lhs) / Convert.ToDouble(rhs);
            }
            else if ((lhs is long) && (rhs is long))
            {
                result = Convert.ToDouble((long) lhs) / (long) rhs;
            }
            else
            {
                throw new ApplicationException($"unable to divide {lhs} by {rhs}");
            }
            return result;
        }

        protected object EvalIntegerDivide(BinaryNode node)
        {
            var lhs = Evaluate(node.Left);
            var rhs = Evaluate(node.Right);
            object result;

            if ((lhs is long) && (rhs is long))
            {
                result = (long) lhs / (long) rhs;
            }
            else
            {
                throw new ApplicationException($"unable to integer divide {lhs} by {rhs}");
            }
            return result;
        }
        protected object EvalModulo(BinaryNode node)
        {
            var lhs = Evaluate(node.Left);
            var rhs = Evaluate(node.Right);
            object result;

            if ((lhs is long) && (rhs is long))
            {
                result = (long) lhs % (long) rhs;
            }
            else if ((lhs is double) || (rhs is double))
            {
                result = Convert.ToDouble(lhs) % Convert.ToDouble(rhs);
            }
            else
            {
                throw new ApplicationException($"unable to determine {lhs} modulo {rhs}");
            }
            return result;
        }

        protected object EvalLeftShift(BinaryNode node)
        {
            var lhs = Evaluate(node.Left);
            var rhs = Evaluate(node.Right);
            object result;

            if ((lhs is long) && (rhs is long))
            {
                result = (long) lhs << checked((int) (long) rhs);
            }
            else
            {
                throw new ApplicationException($"unable to left-shift {lhs} by {rhs}");
            }
            return result;
        }

        protected object EvalRightShift(BinaryNode node)
        {
            var lhs = Evaluate(node.Left);
            var rhs = Evaluate(node.Right);
            object result;

            if ((lhs is long) && (rhs is long))
            {
                result = (long) lhs >> checked((int) (long) rhs);
            }
            else
            {
                throw new ApplicationException($"unable to right-shift {lhs} by {rhs}");
            }
            return result;
        }

        protected object EvalPower(BinaryNode node)
        {
            var lhs = Evaluate(node.Left);
            var rhs = Evaluate(node.Right);
            object result;

            if ((lhs is Complex) || (rhs is Complex))
            {
                result = Complex.Pow(ToComplex(lhs), ToComplex(rhs));
            }
            else if ((lhs is long) && (rhs is long))
            {
                result = Convert.ToInt64(Math.Pow((long) lhs, (long) rhs));
            }
            else if ((lhs is double) || (rhs is double))
            {
                result = Math.Pow(Convert.ToDouble(lhs), Convert.ToDouble(rhs));
            }
            else
            {
                throw new ApplicationException($"unable to raise {lhs} to the power of {rhs}");
            }
            return result;
        }

        protected object EvalBitwiseOr(BinaryNode node)
        {
            var lhs = Evaluate(node.Left);
            var rhs = Evaluate(node.Right);
            object result;

            if ((lhs is DictWrapper) && (rhs is DictWrapper))
            {
                result = GetMerged((DictWrapper) lhs, (DictWrapper) rhs);
            }
            else if ((lhs is long) && (rhs is long))
            {
                result = (long) lhs | (long) rhs;
            }
            else
            {
                throw new ApplicationException($"unable to bitwise-or {rhs} to {lhs}");
            }
            return result;
        }

        protected object EvalBitwiseAnd(BinaryNode node)
        {
            var lhs = Evaluate(node.Left);
            var rhs = Evaluate(node.Right);
            object result;

            if ((lhs is long) && (rhs is long))
            {
                result = (long) lhs & (long) rhs;
            }
            else
            {
                throw new ApplicationException($"unable to bitwise-and {rhs} to {lhs}");
            }
            return result;
        }

        protected object EvalBitwiseXor(BinaryNode node)
        {
            var lhs = Evaluate(node.Left);
            var rhs = Evaluate(node.Right);
            object result;

            if ((lhs is long) && (rhs is long))
            {
                result = (long) lhs ^ (long) rhs;
            }
            else
            {
                throw new ApplicationException($"unable to bitwise-xor {rhs} to {lhs}");
            }
            return result;
        }

        protected bool EvalAnd(BinaryNode node)
        {
            var lhs = (bool) Evaluate(node.Left);

            if (!lhs)
            {
                return false;
            }

            return (bool) Evaluate(node.Right);
        }

        protected bool EvalOr(BinaryNode node)
        {
            var lhs = (bool) Evaluate(node.Left);

            if (lhs)
            {
                return true;
            }

            return (bool) Evaluate(node.Right);
        }

        protected object NegateNode(UnaryNode node)
        {
            var operand = Evaluate(node.Operand);
            object result;

            if (operand is long)
            {
                result = -(long) operand;
            }
            else if (operand is double)
            {
                result = -(double) operand;
            }
            else if (operand is Complex)
            {
                result = -(Complex) operand;
            }
            else
            {
                throw new ApplicationException($"Unable to negate {node}");
            }
            return result;
        }

        internal object Evaluate(ASTNode node)
        {
            object result;

            if (node is Token)
            {
                var t = (Token) node;
                var value = t.Value;

                if (SCALAR_TOKENS.Contains(t.Kind))
                {
                    result = value;
                }
                else if (t.Kind == TokenKind.Word)
                {
                    var key = (String) value;

                    if (config.Context.ContainsKey(key))
                    {
                        result = config.Context[key];
                    }
                    else
                    {
                        throw new ConfigException($"Unknown variable '{key}'") { Location = node.Start };
                    }
                }
                else if (t.Kind == TokenKind.BackTick)
                {
                    result = config.ConvertString((string) t.Value);
                }
                else
                {
                    throw new ConfigException($"Unable to evaluate {node}") { Location = node.Start };
                }
            }
            else if (node is MappingItems)
            {
                result = config.WrapMapping((MappingItems) node);
            }
            else if (node is ListItems)
            {
                result = config.WrapList((ListItems) node);
            }
            else
            {
                switch (node.Kind)
                {
                    case TokenKind.At:
                        result = EvalAt((UnaryNode) node);
                        break;
                    case TokenKind.Dollar:
                        result = EvalReference((UnaryNode) node);
                        break;
                    case TokenKind.Plus:
                        result = EvalAdd((BinaryNode) node);
                        break;
                    case TokenKind.Minus:
                        if (node is BinaryNode)
                        {
                            result = EvalSubtract((BinaryNode) node);
                        }
                        else
                        {
                            result = NegateNode((UnaryNode) node);
                        }
                        break;
                    case TokenKind.Star:
                        result = EvalMultiply((BinaryNode) node);
                        break;
                    case TokenKind.Slash:
                        result = EvalDivide((BinaryNode) node);
                        break;
                    case TokenKind.SlashSlash:
                        result = EvalIntegerDivide((BinaryNode) node);
                        break;
                    case TokenKind.Modulo:
                        result = EvalModulo((BinaryNode) node);
                        break;
                    case TokenKind.LeftShift:
                        result = EvalLeftShift((BinaryNode) node);
                        break;
                    case TokenKind.RightShift:
                        result = EvalRightShift((BinaryNode) node);
                        break;
                    case TokenKind.Power:
                        result = EvalPower((BinaryNode) node);
                        break;
                    case TokenKind.BitwiseOr:
                        result = EvalBitwiseOr((BinaryNode) node);
                        break;
                    case TokenKind.BitwiseAnd:
                        result = EvalBitwiseAnd((BinaryNode) node);
                        break;
                    case TokenKind.BitwiseXor:
                        result = EvalBitwiseXor((BinaryNode) node);
                        break;
                    case TokenKind.Or:
                        result = EvalOr((BinaryNode) node);
                        break;
                    case TokenKind.And:
                        result = EvalAnd((BinaryNode) node);
                        break;
                    default:
                        throw new ApplicationException($"Unable to evaluate {node}");
                }
            }
            return result;
        }

        internal ListWrapper GetSlice(ListWrapper source, SliceNode slice)
        {
            int startIndex, stopIndex;
            int size = source.Count;
            int step = (slice.Step == null) ? 1 : checked((int) (long) config.Evaluated(slice.Step));

            if (step == 0)
            {
                throw new ConfigException("slice step cannot be zero") { Location = slice.Start };
            }
            if (slice.StartIndex == null)
            {
                startIndex = 0;
            }
            else
            {
                startIndex = checked((int) (long) config.Evaluated(slice.StartIndex));
                if (startIndex < 0)
                {
                    if (startIndex >= -size)
                    {
                        startIndex += size;
                    }
                    else
                    {
                        startIndex = 0;
                    }
                }
                else if (startIndex >= size)
                {
                    startIndex = size - 1;
                }
            }

            if (slice.StopIndex == null)
            {
                stopIndex = size - 1;
            }
            else
            {
                stopIndex = checked((int) (long) config.Evaluated(slice.StopIndex));
                if (stopIndex < 0)
                {
                    if (stopIndex >= -size)
                    {
                        stopIndex += size;
                    }
                    else
                    {
                        stopIndex = 0;
                    }
                }
                if (stopIndex > size)
                {
                    stopIndex = size;
                }
                if (step < 0)
                {
                    stopIndex++;
                }
                else
                {
                    stopIndex--;
                }
            }

            if ((step < 0) && (startIndex < stopIndex))
            {
                var tmp = stopIndex;
                stopIndex = startIndex;
                startIndex = tmp;
            }

            var result = new ListWrapper(config);

            var i = startIndex;

            var notDone = (step > 0) ? (i <= stopIndex) : (i >= stopIndex);

            while (notDone)
            {
                result.Add(source[i]);
                i += step;
                notDone = (step > 0) ? (i <= stopIndex) : (i >= stopIndex);
            }

            return result;
        }

        private bool IsRef(object o)
        {
            return (o is ASTNode) ? (((ASTNode) o).Kind == TokenKind.Dollar) : false;
        }

        internal object GetFromPath(ASTNode node)
        {
            IEnumerator<object> pi = Config.PathIterator(node).GetEnumerator();
            pi.MoveNext();
            var first = pi.Current;
            Debug.Assert(first != null, nameof(first) + " != null");
            var result = config.Get((String) ((Token) first).Value);

            // We start the evaluation with the current instance, but a path may
            // cross sub-configuration boundaries, and references must always be
            // evaluated in the context of the immediately enclosing configuration,
            // not the top-level configuration (references are relative to the
            // root of the enclosing configuration - otherwise configurations would
            // not be standalone. So whenever we cross a sub-configuration boundary,
            // the current_evaluator has to be pegged to that sub-configuration.

            var currentEvaluator = (result is Config) ? new Evaluator((Config) result) : this;

            while (pi.MoveNext())
            {
                var item = (Tuple<TokenKind, object>) pi.Current;
                Debug.Assert(item != null, nameof(item) + " != null");
                var kind = item.Item1;
                var operand = item.Item2;
                var sliced = (operand is SliceNode);
                var operpos = (operand is ASTNode) ? ((ASTNode) operand).Start : null;

                if (operand is long)
                {
                    operand = checked((int) (long) operand);
                }
                else if (!sliced && (kind != TokenKind.Dot) && (operand is ASTNode))
                {
                    operand = currentEvaluator.Evaluate((ASTNode) operand);
                }
                if (sliced && !(result is ListWrapper))
                {
                    throw new BadIndexException("slices can only operate on lists") { Location = operpos };
                }
                if (((result is DictWrapper) || (result is Config)) && !(operand is string))
                {
                    throw new BadIndexException($"string required, but found {operand}") { Location = operpos };
                }
                if (result is DictWrapper)
                {
                    var dw = (DictWrapper) result;
                    var key = (string) operand;

                    if (dw.ContainsKey(key))
                    {
                        result = dw.BaseGet(key);
                    }
                    else
                    {
                        throw new ConfigException("Not found in configuration: {key}");
                    }
                }
                else if (result is ListWrapper)
                {
                    ListWrapper lw = (ListWrapper) result;

                    if (operand is int)
                    {
                        var intn = (int) operand;
                        var size = lw.Count;

                        if (intn < 0)
                        {
                            if (intn >= -size)
                            {
                                intn += size;
                            }
                        }
                        if ((intn < 0) || (intn >= size))
                        {
                            throw new BadIndexException($"index out of range: is {intn}, must be between 0 and {size}");
                        }
                        result = lw.BaseGet(intn);
                    }
                    else if (sliced)
                    {
                        result = GetSlice(lw, (SliceNode) operand);
                    }
                    else
                    {
                        throw new BadIndexException($"integer required, but found {operand}");
                    }
                }
                else if (result is Config)
                {
                    var cfg = (Config) result;
                    currentEvaluator = cfg.Evaluator;
                    result = cfg.Get((string) operand);
                }
                else
                {
                    throw new ApplicationException($"unable to process {kind} / {operand}");
                }
                if (IsRef(result))
                {
                    var rn = (ASTNode) result;

                    if (currentEvaluator.refsSeen.Contains(rn))
                    {
                        var parts = new List<string>();

                        foreach (var ritem in currentEvaluator.refsSeen)
                        {
                            var un = ((UnaryNode) ritem);
                            var s = Config.ToSource(un.Operand);
                            var ls = un.Start;

                            parts.Add($"{s} {ls}");
                        }
                        parts.Sort();
                        var ps = string.Join(", ", parts.ToArray());
                        throw new CircularReferenceException($"Circular reference: {ps}");
                    }
                    currentEvaluator.refsSeen.Add(rn);
                }
                if (result is MappingItems)
                {
                    result = config.WrapMapping((MappingItems) result);
                }
                else if (result is ListItems)
                {
                    result = config.WrapList((ListItems) result);
                }
                if (result is ASTNode)
                {
                    result = currentEvaluator.Evaluate((ASTNode) result);
                }
            }
            pi.Dispose();
            return result;
        }
    }

    public class Config
    {
        internal DictWrapper data;
        protected static object MISSING = new object();

        public Config()
        {
            Evaluator = new Evaluator(this);
            NoDuplicates = true;
            StrictConversions = true;
            Context = new Mapping();
            IncludePath = new List<string>();
            StringConverter = DefaultStringConverter;
        }

        public Config(TextReader reader) : this()
        {
            Load(reader);
        }

        public Config(string path) : this()
        {
            LoadFile(path);
        }

        public bool NoDuplicates { get; set; }

        public bool StrictConversions { get; set; }

        public IMapping Context { get; set; }

        protected IMapping cache;

        public bool Cached
        {
            get { return (cache != null); }
            set
            {
                if (value)
                {
                    if (cache == null)
                    {
                        cache = new Mapping();
                    }
                }
                else
                {
                    cache = null;
                }
            }
        }

        public IList<string> IncludePath { get; set; }
        public string Path { get; set; }
        public Config Parent { get; internal set; }
        public string RootDir { get; set; }
        internal StringConverter StringConverter { get; set; }

        protected static Regex ISO_DATETIME_PATTERN = new Regex(@"^(\d{4})-(\d{2})-(\d{2})(([ T])(((\d{2}):(\d{2}):(\d{2}))(\.\d{1,6})?(([+-])(\d{2}):(\d{2})(:(\d{2})(\.\d{1,6})?)?)?))?$");
        protected static Regex ENV_VALUE_PATTERN = new Regex(@"^\$(\w+)(\|(.*))?$");
        protected static Regex COLON_OBJECT_PATTERN = new Regex(@"^(([A-Za-z_]\w*(\.[A-Za-z_]\w*)*),)?([A-Za-z_]\w*(\.[A-Za-z_]\w*)*)(:([A-Za-z_]\w*))?$");
        protected static Regex INTERPOLATION_PATTERN = new Regex(@"\$\{([^}]+)\}");
        protected static object[] NO_PARAMS = new object[] { };

        internal static object DefaultStringConverter(string s, Config cfg)
        {
            object result = s;
            var m = ISO_DATETIME_PATTERN.Match(s);

            if (m.Success)
            {
                int year = int.Parse(m.Groups[1].Value);
                int month = int.Parse(m.Groups[2].Value);
                int day = int.Parse(m.Groups[3].Value);
                bool hasTime = !string.IsNullOrEmpty(m.Groups[5].Value);

                if (!hasTime)
                {
                    result = new DateTime(year, month, day);
                }
                else
                {
                    int hour = int.Parse(m.Groups[8].Value);
                    int minute = int.Parse(m.Groups[9].Value);
                    int second = int.Parse(m.Groups[10].Value);
                    bool hasOffset = !string.IsNullOrEmpty(m.Groups[13].Value);
                    string v = m.Groups[11].Value;
                    int millisecond = string.IsNullOrEmpty(v) ? 0 : Convert.ToInt32(double.Parse(v) * 1000);
                    DateTime dt = new DateTime(year, month, day, hour, minute, second, millisecond);

                    if (!hasOffset)
                    {
                        result = dt;
                    }
                    else
                    {
                        int sign = m.Groups[13].Value.Equals("-") ? -1 : 1;
                        int ohour = int.Parse(m.Groups[14].Value);
                        int ominute = int.Parse(m.Groups[15].Value);
                        int osecond = 0;
                        int omillisecond = 0;
                        /*
                                                v = m.Groups[17].Value;
                                                int osecond, omillisecond;

                                                if (string.IsNullOrEmpty(v))
                                                {
                                                    osecond = 0;
                                                    omillisecond = 0;
                                                }
                                                else
                                                {
                                                    osecond = int.Parse(v);
                                                    v = m.Groups[18].Value;

                                                    omillisecond = string.IsNullOrEmpty(v) ? 0 : Convert.ToInt32(double.Parse(v) * 1000);
                                                }
                         */
                        var ts = new TimeSpan(0, sign * ohour, sign * ominute, sign * osecond, sign * omillisecond);
                        result = new DateTimeOffset(dt, ts);
                    }
                }
            }
            else
            {
                m = ENV_VALUE_PATTERN.Match(s);

                if (m.Success)
                {
                    var name = m.Groups[1].Value;
                    var hasPipe = !string.IsNullOrEmpty(m.Groups[2].Value);
                    var dv = hasPipe ? m.Groups[3].Value : null;

                    result = Environment.GetEnvironmentVariable(name) ?? dv;
                }
                else
                {
                    m = COLON_OBJECT_PATTERN.Match(s);

                    if (m.Success)
                    {
                        var aname = m.Groups[2].Value;
                        var cname = m.Groups[4].Value;
                        var fname = m.Groups[7].Value;
                        Assembly a;

                        if (string.IsNullOrEmpty(aname))
                        {
                            a = Assembly.GetExecutingAssembly();
                        }
                        else
                        {
                            a = Assembly.Load(aname);
                        }
                        var t = a.GetType(cname);

                        if (t != null)
                        {
                            if (string.IsNullOrEmpty(fname))
                            {
                                result = t;
                            }
                            else
                            {
                                var finfo = t.GetField(fname);
                                if ((finfo != null) && finfo.IsStatic && finfo.IsPublic)
                                {
                                    result = finfo.GetValue(null);
                                }
                                else
                                {
                                    MethodInfo minfo;
                                    var pinfo = t.GetProperty(fname);

                                    if (pinfo != null)
                                    {
                                        minfo = pinfo.GetGetMethod();

                                        if (minfo != null)
                                        {
                                            if (!minfo.IsStatic || !minfo.IsPublic)
                                            {
                                                minfo = null;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        minfo = t.GetMethod(fname);
                                        if (minfo != null)
                                        {
                                            if (!minfo.IsStatic || !minfo.IsPublic)
                                            {
                                                minfo = null;
                                            }
                                        }
                                    }
                                    if (minfo != null)
                                    {
                                        var p = minfo.GetParameters();

                                        if (p.Length == 0)
                                        {
                                            result = minfo.Invoke(null, NO_PARAMS);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        var mc = INTERPOLATION_PATTERN.Matches(s);
                        var n = mc.Count;

                        if (mc.Count > 0)
                        {
                            var sb = new StringBuilder();
                            var cp = 0;
                            var failed = false;

                            for (var i = 0; i < n; i++)
                            {
                                var match = mc[i];
                                var sp = match.Index;
                                var ep = sp + match.Length;
                                var path = match.Groups[1].Value;

                                if (cp < sp)
                                {
                                    sb.Append(s.Substring(cp, sp - cp));
                                }

                                try
                                {
                                    var v = cfg[path];

                                    if (v is ISequence)
                                    {
                                        v = Utils.SequenceToString(v as ISequence);
                                    }
                                    else if (v is IMapping)
                                    {
                                        v = Utils.MappingToString(v as IMapping);
                                    }
                                    sb.Append(v);
                                }
                                catch (Exception)
                                {
                                    failed = true;
                                    break;
                                }
                                cp = ep;
                            }

                            if (!failed)
                            {
                                if (cp < s.Length)
                                {
                                    sb.Append(s.Substring(cp));
                                }

                                result = sb.ToString();
                            }
                        }
                    }
                }
            }
            return result;
        }

        internal Evaluator Evaluator { get; set; }

        internal DictWrapper WrapMapping(MappingItems node)
        {
            var result = new DictWrapper(this);
            IDictionary<string, Location> seen = null;

            if (NoDuplicates)
            {
                seen = new Dictionary<string, Location>();
            }

            foreach (var item in node.Items)
            {
                var t = item.Item1;
                var v = item.Item2;
                var k = (String) t.Value;

                if (seen != null)
                {
                    if (seen.ContainsKey(k))
                    {
                        throw new ConfigException($"Duplicate key {k} seen at {t.Start} (previously at {seen[k]})");
                    }
                    seen[k] = t.Start;
                }
                result[k] = v;
            }
            return result;
        }

        internal ListWrapper WrapList(ListItems node)
        {
            var result = new ListWrapper(this);

            result.AddRange(node.Items);
            return result;
        }

        public void Load(TextReader reader)
        {
            var parser = new Parser(reader);
            var node = parser.Container();

            if (!(node is MappingItems))
            {
                throw new ConfigException("Root configuration must be a mapping");
            }
            if (reader is NamedReader)
            {
                var nr = (NamedReader) reader;
                string s = System.IO.Path.GetFullPath(nr.Name);

                Path = s;
                RootDir = System.IO.Path.GetDirectoryName(s);
            }
            data = WrapMapping(node as MappingItems);
        }

        public void LoadFile(string path)
        {
            using (var reader = new NamedReader(path))
            {
                Load(reader);
            }
        }

        internal static ASTNode ParsePath(string source)
        {
            ASTNode result;
            Parser parser;

            // Failure can happen on the very first token ...
            try
            {
                parser = new Parser(new StringReader(source));
            }
            catch (RecognizerException)
            {
                throw new InvalidPathException($"Invalid path: {source}");
            }
            if (parser.next.Kind != TokenKind.Word)
            {
                throw new InvalidPathException($"Invalid path {source}");
            }
            try
            {
                result = parser.Primary();
                if (!parser.AtEnd)
                {
                    throw new InvalidPathException($"Invalid path: {source}");
                }
            }
            catch (ParserException e)
            {
                throw new InvalidPathException($"Invalid path: {source}", e);
            }
            return result;
        }

        internal static string ToSource(object o)
        {
            string result;

            if (o == null)
            {
                result = "null";
            }
            else if (!(o is ASTNode))
            {
                result = o.ToString();
            }
            else if (o is Token)
            {
                result = ((Token) o).Text;
            }
            else
            {
                var node = (ASTNode) o;
                var pi = PathIterator(node).GetEnumerator();
                pi.MoveNext();
                Debug.Assert(pi.Current != null, "pi.Current != null");
                var sb = new StringBuilder((string) ((Token) pi.Current).Value);

                while (pi.MoveNext())
                {
                    var item = (Tuple<TokenKind, object>) pi.Current;
                    Debug.Assert(item != null, nameof(item) + " != null");
                    var kind = item.Item1;
                    var operand = item.Item2;

                    if (kind == TokenKind.Dot)
                    {
                        sb.Append('.').Append((string) operand);
                    }
                    else if (kind == TokenKind.LeftBracket)
                    {
                        sb.Append('[').Append(ToSource(operand)).Append(']');
                    }
                    else if (kind == TokenKind.Colon)
                    {
                        var sn = (SliceNode) operand;

                        sb.Append('[');
                        if (sn.StartIndex != null)
                        {
                            sb.Append(ToSource(sn.StartIndex));
                        }
                        sb.Append(':');
                        if (sn.StopIndex != null)
                        {
                            sb.Append(ToSource(sn.StopIndex));
                        }
                        if (sn.Step != null)
                        {
                            sb.Append(':').Append(ToSource(sn.Step));
                        }
                        sb.Append(']');
                    }
                    else
                    {
                        throw new ApplicationException($"Unable to compute source for {node}");
                    }
                }
                pi.Dispose();
                result = sb.ToString();
            }
            return result;
        }

        private static IEnumerable<object> Visit(ASTNode node)
        {
            if (node is Token)
            {
                yield return node;
            }
            else if (node is UnaryNode)
            {
                foreach (var o in Visit(((UnaryNode) node).Operand))
                {
                    yield return o;
                }
            }
            else if (node is BinaryNode)
            {
                var bn = (BinaryNode) node;
                var kind = bn.Kind;

                foreach (var o in Visit(bn.Left))
                {
                    yield return o;
                }
                switch (kind)
                {
                    case TokenKind.Dot:
                    case TokenKind.LeftBracket:
                        yield return new Tuple<TokenKind, object>(kind, ((Token) bn.Right).Value);
                        break;
                    case TokenKind.Colon:
                        yield return new Tuple<TokenKind, object>(kind, (SliceNode) bn.Right);
                        break;
                    default:
                        var msg = $"Unexpected node kind in path: {node.Kind}";
                        throw new NotSupportedException(msg);
                }
            }
        }

        internal static IEnumerable<object> PathIterator(ASTNode node)
        {
            foreach (var o in Visit(node))
            {
                yield return o;
            }
        }

        public object Get(string key, object defaultValue)
        {
            object result;

            if ((cache != null) && cache.ContainsKey(key))
            {
                result = cache[key];
            }
            else
            {
                bool doEval = true;

                if (data == null)
                {
                    throw new ConfigException("No data in configuration");
                }
                if (data.ContainsKey(key))
                {
                    result = data[key];
                }
                else if (Utils.IsIdentifier(key))
                {
                    if (defaultValue == MISSING)
                    {
                        throw new ConfigException($"Not found in configuration: {key}");
                    }
                    result = defaultValue;
                }
                else
                {
                    // Treat as a path
                    try
                    {
                        result = GetFromPath(key);
                        doEval = false;
                    }
                    catch (InvalidPathException)
                    {
                        throw;
                    }
                    catch (BadIndexException)
                    {
                        throw;
                    }
                    catch (CircularReferenceException)
                    {
                        throw;
                    }
                    catch (ConfigException e)
                    {
                        if (defaultValue == MISSING)
                        {
                            throw new ConfigException($"Not found in configuration: {key}", e);
                        }
                        result = defaultValue;
                    }
                }
                if (doEval)
                {
                    result = Evaluated(result);
                }
                if (cache != null)
                {
                    cache[key] = result;
                }
            }
            return result;
        }

        internal object Get(string key)
        {
            return Get(key, MISSING);
        }

        internal object GetFromPath(string path)
        {
            Evaluator.refsSeen.Clear();
            return Evaluator.GetFromPath(ParsePath(path));
        }

        public object ConvertString(string s)
        {
            var result = StringConverter(s, this);

            // The typecast below is to avoid a spurious Visual C# compiler warning
            if (StrictConversions && (result == (object) s))
            {
                throw new ConfigException($"Unable to convert string {s}");
            }
            return result;
        }

        internal object Evaluated(object o)
        {
            object result = o;

            if (o is ASTNode)
            {
                result = Evaluator.Evaluate((ASTNode) o);
            }
            return result;
        }

        public object this[string key] => Utils.Unwrap(Get(key, MISSING));

        public IMapping AsDict()
        {
            if (data == null)
            {
                throw new ConfigException("No data in configuration");
            }
            return data.AsDict();
        }
    }

    internal static class Utils
    {
        internal static readonly object[] EMPTY_OBJECT_LIST = new object[] { };

#if WANTED
        private static readonly char[] COLON = new char[] { ':' };

        private static Type GetTypeAndAttribute(string spec, out string typename, out string attribute)
        {
            string[] parts = spec.Split(COLON, 2);
            if (parts.Length != 2)
            {
                throw BuildException<ArgumentException>("invalid specification: {0}", spec);
            }
            typename = parts[0];
            attribute = parts[1];
            return Type.GetType(typename);
        }

        private static Type[] NO_ARGS = new Type[] { };
        private static Type[] DICT_ARG = new Type[] { typeof(IReadOnlyDictionary<string, object>) };

        internal static object GetValueFromConfig(object config, IReadOnlyDictionary<string, object> param = null, string defaultFactoryName = "")
        {
            string spec;

            if (config is string)
            {
                spec = (string) config;
            }
            else if (config is IReadOnlyDictionary<string, object>)
            {
                if (param == null)
                {
                    param = (IReadOnlyDictionary<string, object>) config;
                }
                spec = param.GetValueOrDefault("factory", defaultFactoryName);
                if (string.IsNullOrEmpty(spec))
                {
                    throw new ArgumentException("No factory specified: {0}");
                }
            }
            else
            {
                throw BuildException<ArgumentException>("Unable to process config: {0}", config);
            }
            object result = null;
            string tname, attrname;
            Type type = GetTypeAndAttribute(spec, out tname, out attrname);

            if (type == null)
            {
                throw BuildException<ArgumentException>("unknown type: {0}", tname);
            }

            bool found = false;
            FieldInfo finfo = type.GetField(attrname);

            if (finfo != null)
            {
                result = finfo.GetValue(null);
                found = true;
            }
            else
            {
                PropertyInfo pinfo = type.GetProperty(attrname);

                if (pinfo != null)
                {
                    result = pinfo.GetValue(null);
                    found = true;
                }
                else
                {
                    Type[] types;
                    object[] values;

                    if (param == null)
                    {
                        types = NO_ARGS;
                        values = new object[] { };
                    }
                    else
                    {
                        types = DICT_ARG;
                        values = new object[] { param };
                    }

                    MethodInfo minfo = type.GetMethod(attrname, types);

                    if (minfo != null)
                    {
                        result = minfo.Invoke(null, values);
                        found = true;
                    }
                }
            }
            if (!found)
            {
                throw BuildException<ArgumentException>("Type {0} has no field, property or factory method {1}", tname, attrname);
            }
            return result;
        }

        public static V GetValueOrDefault<V>
            (this IReadOnlyDictionary<string, object> dictionary,
             string key, V defaultValue)
        {
            object value = null;

            try
            {
                return dictionary.TryGetValue(key, out value) ? (V) value : defaultValue;
            }
            catch (InvalidCastException)
            {
                throw BuildException<ArgumentException>("expected \"{0}\" to be of type {1}, but it was {2}",
                                                        key, typeof(V), value.GetType().ToString());
            }
        }

        public static string ToDebugString(this IReadOnlyDictionary<string, object> dictionary)
        {
            StringBuilder sb = new StringBuilder('{');

            if (dictionary.Count > 0)
            {
                foreach (var item in dictionary)
                {
                    sb.Append(item.Key);
                    sb.Append(": ");
                    sb.Append(item.Value);
                    sb.Append(", ");
                }
                sb.Length -= 2;
            }
            sb.Append('}');
            return sb.ToString();
        }
#endif
#if WANTED
        private static readonly Type[] ONE_STRING = new Type[] { typeof(string) };
        private static readonly Type[] STRING_AND_EXCEPTION = new Type[] { typeof(string), typeof(Exception) };

        internal static T BuildException<T>(string format, params object[] args)
        {
            string msg = string.Format(format, args);
            var m = typeof(T).GetConstructor(ONE_STRING);
            Debug.Assert(m != null, nameof(m) + " != null");
            return (T) m.Invoke(new object[] { msg });
        }

        internal static T BuildException<T>(Exception inner, string format, params object[] args)
        {
            string msg = string.Format(format, args);
            var m = typeof(T).GetConstructor(STRING_AND_EXCEPTION);
            Debug.Assert(m != null, nameof(m) + " != null");
            return (T) m.Invoke(new object[] { msg, inner });
        }

        public static uint GetCheckedUint(object value)
        {
            if (value is int)
            {
                if (((int) value) < 0)
                {
                    throw Utils.BuildException<ArgumentException>("negative value: {0}", value);
                }
                value = (uint) (int) value;
            }
            else if (value is long)
            {
                if (((long) value) < 0)
                {
                    throw Utils.BuildException<ArgumentException>("negative value: {0}", value);
                }
                value = (uint) (long) value;
            }
            if (!(value is uint))
            {
                throw Utils.BuildException<ArgumentException>("invalid value: {0}", value);
            }
            return (uint) value;
        }
#endif
        internal static bool IsHexDigit(char c)
        {
            bool result = char.IsDigit(c);

            if (!result)
            {
                if ((c >= 'a') && (c <= 'f'))
                {
                    result = true;
                }
                else if ((c >= 'A') && (c <= 'F'))
                {
                    result = true;
                }
            }
            return result;
        }

        private static readonly Regex IDENTIFIER_PATTERN = new Regex(@"^(?!\d)(\w+)$");

        internal static bool IsIdentifier(string s)
        {
            return IDENTIFIER_PATTERN.Match(s.Normalize()).Success;
        }

        internal static int IndexOf(this StringBuilder builder, char c, int pos = 0)
        {
            for (int i = pos; i < builder.Length; i++)
            {
                if (builder[i] == c)
                {
                    return i;
                }
            }
            return -1;
        }

        internal static string SubString(this StringBuilder builder, int pos, int len)
        {
            return builder.ToString().Substring(pos, len);
        }

        internal static object Unwrap(object o)
        {
            var result = o;

            if (o is ListWrapper)
            {
                result = ((ListWrapper) o).AsList();
            }
            else if (o is DictWrapper)
            {
                result = ((DictWrapper) o).AsDict();
            }
            return result;
        }

        internal static string SequenceToString(ISequence list)
        {
            var items = new List<string>();

            foreach (var item in list)
            {
                string s;

                if (item is ISequence)
                {
                    s = SequenceToString(item as ISequence);
                }
                else if (item is IMapping)
                {
                    s = MappingToString(item as IMapping);
                }
                else
                {
                    s = item.ToString();
                }
                items.Add(s);
            }
            var contents = string.Join(", ", items);
            return $"[{contents}]";
        }

        internal static string MappingToString(IMapping mapping)
        {
            var items = new List<string>();

            foreach (var item in mapping)
            {
                string s;
                var v = item.Value;
                if (v is ISequence)
                {
                    s = SequenceToString(v as ISequence);
                }
                else if (v is IMapping)
                {
                    s = MappingToString(v as IMapping);
                }
                else
                {
                    s = v.ToString();
                }
                items.Add($"{item.Key}: {v}");
            }
            var contents = string.Join(", ", items);
            return $"{{{contents}}}";
        }
    }
}
