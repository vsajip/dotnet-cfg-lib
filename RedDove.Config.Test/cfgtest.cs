using System.Collections.Generic;
using System.Linq;
using System.IO;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections;
using System;
using System.Text.RegularExpressions;
using System.Numerics;
using System.Diagnostics;

namespace RedDove.Config.Test
{
    using System.Reflection;
    using System.Threading;
    using IMapping = IDictionary<string, object>;
    using Mapping = Dictionary<string, object>;
    using ISequence = IList<object>;
    using Sequence = List<object>;

    [TestClass]
    public class ConfigTest
    {
        private static readonly Regex SeparatorPattern = new Regex(@"^-- ([A-Z]\d+) -+");

        private IReadOnlyDictionary<string, string> LoadData(string filename)
        {
            var result = new Dictionary<string, string>();
            var lines = File.ReadAllLines(filename);
            List<string> current = new List<string>();
            string key = null;

            foreach (var line in lines)
            {
                var m = SeparatorPattern.Match(line);

                if (!m.Success)
                {
                    current.Add(line);
                }
                else
                {
                    if ((key != null) && (current.Count > 0))
                    {
                        result[key] = String.Join("\n", current);
                    }
                    key = m.Groups[1].Value;
                    current.Clear();
                }
            }
            return result;
        }

        private static Tokenizer MakeTokenizer(string s)
        {
            StringReader reader = new StringReader(s);

            return new Tokenizer(reader);
        }

        private static bool IsDict(object o)
        {
            bool result = false;

            if (o != null)
            {
                result = o.GetType() == typeof(Mapping);
            }
            return result;
        }

        private static Mapping ToMap(List<Tuple<Token, ASTNode>> pairs)
        {
            var result = new Mapping();

            foreach (var kv in pairs)
            {
                string key = (string) kv.Item1.Value;

                result[key] = kv.Item2;
            }
            return result;
        }

        private static object[] ToList(ListItems li)
        {
            var result = new object[li.Items.Count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = li.Items[i];
            }
            return result;
        }

        private static void CompareObjects(object e, object a, object context = null)
        {
            if (ReferenceEquals(e, a))
            {
                return;
            }

            bool equal = ((e != null) && (a != null));
            Type t = null;
            Type at = a.GetType();

            // special case - check for a list of key/value tuples and convert it to a map
            // before comparing
            if (at == typeof(List<Tuple<Token, ASTNode>>))
            {
                a = ToMap((List<Tuple<Token, ASTNode>>) a);
            }
            else if (at == typeof(MappingItems))
            {
                a = ToMap(((MappingItems) a).Items);
            }
            else if (at == typeof(ListItems))
            {
                a = ToList((ListItems) a);
            }
            if (equal) // both must be non-null
            {
                t = e.GetType();
                equal = (t == a.GetType());
            }
            if (equal) // both must be of the same type
            {
                if (t.IsArray)
                {
                    CompareArrays((object[]) e, (object[]) a);
                }
                else if (IsDict(e) && IsDict(a))
                {
                    CompareMappings((Mapping) e, (Mapping) a);
                }
                else if ((e is Sequence) && (a is Sequence))
                {
                    CompareSequences((Sequence) e, (Sequence) a);
                }
                else if (t == typeof(ListItems))
                {
                    var ea = ((ListItems) e).Items.ToArray();
                    var aa = ((ListItems) a).Items.ToArray();

                    CompareArrays(ea, aa);
                }
                else if (t == typeof(UnaryNode))
                {
                    var en = (UnaryNode) e;
                    var an = (UnaryNode) a;

                    CompareObjects(en.Kind, an.Kind);
                    CompareObjects(en.Operand, an.Operand);
                }
                else if (t == typeof(BinaryNode))
                {
                    BinaryNode en = (BinaryNode) e;
                    BinaryNode an = (BinaryNode) a;

                    CompareObjects(en.Kind, an.Kind);
                    CompareObjects(en.Left, an.Left);
                    CompareObjects(en.Right, an.Right);
                }
                else if (t == typeof(SliceNode))
                {
                    SliceNode en = (SliceNode) e;
                    SliceNode an = (SliceNode) a;

                    CompareObjects(en.StartIndex, an.StartIndex);
                    CompareObjects(en.StopIndex, an.StopIndex);
                    CompareObjects(en.Step, an.Step);
                }
                else if (t == typeof(Token))
                {
                    Token etk = (Token) e;
                    Token atk = (Token) a;

                    Assert.AreEqual(etk.Kind, atk.Kind);
                    //Assert.AreEqual(etk.Text, atk.Text);
                    Assert.AreEqual(etk.Value, atk.Value);
                }
                else if (t == typeof(Tuple<TokenKind, object>))
                {
                    var etu = (Tuple<TokenKind, object>) e;
                    var atu = (Tuple<TokenKind, object>) a;

                    Assert.AreEqual(etu.Item1, atu.Item1);
                    CompareObjects(etu.Item2, atu.Item2);
                }
                else
                {
                    equal = e.Equals(a);
                }
            }
            if (!equal)
            {
                if (context != null)
                {
                    context = $" ({context})";
                }
                string msg = $"Failed{context}: expected {e}, actual {a}";

                Assert.Fail(msg);
            }
        }

        private static void CompareArrays(object[] expected, object[] actual)
        {
            Assert.AreEqual(expected.Length, actual.Length);
            for (var i = 0; i < actual.Length; i++)
            {
                object e = expected[i];
                object a = actual[i];

                CompareObjects(e, a, i);
            }
        }

        private static void CompareMappings(IMapping expected, IMapping actual)
        {
            Assert.AreEqual(expected.Count, actual.Count);
            foreach (var kv in actual)
            {
                string k = kv.Key;

                Assert.IsTrue(expected.ContainsKey(k), $"key not found: {k}");
                object v1 = kv.Value;
                object v2 = expected[k];

                CompareObjects(v2, v1, k);
            }
        }

        private static void CompareSequences(Sequence expected, Sequence actual)
        {
            Assert.AreEqual(expected.Count, actual.Count);
            for (var i = 0; i < actual.Count; i++)
            {
                CompareObjects(expected[i], actual[i], i);
            }
        }

        [TestMethod]
        public void Utilities()
        {
            Assert.IsTrue(Utils.IsHexDigit('0'));
            Assert.IsTrue(Utils.IsHexDigit('9'));
            Assert.IsTrue(Utils.IsHexDigit('a'));
            Assert.IsTrue(Utils.IsHexDigit('f'));
            Assert.IsTrue(Utils.IsHexDigit('A'));
            Assert.IsTrue(Utils.IsHexDigit('F'));
            Assert.IsFalse(Utils.IsHexDigit('G'));
            Assert.IsFalse(Utils.IsHexDigit('.'));
        }

        [TestMethod]
        public void Tokens()
        {
            var tokenizer = MakeTokenizer("");

            Assert.AreEqual(TokenKind.EOF, tokenizer.GetToken().Kind);
            Assert.AreEqual(TokenKind.EOF, tokenizer.GetToken().Kind);

            tokenizer = MakeTokenizer("# a comment\n");
            var token = tokenizer.GetToken();
            Assert.AreEqual(TokenKind.Newline, token.Kind);
            Assert.AreEqual(token.Text, "# a comment");
            Assert.AreEqual(TokenKind.EOF, tokenizer.GetToken().Kind);

            Tuple<string, TokenKind, object>[] cases = new Tuple<string, TokenKind, object>[]
            {
                new Tuple<string, TokenKind, object>("foo", TokenKind.Word, "foo"),
                new Tuple<string, TokenKind, object>("2.71828", TokenKind.Float, 2.71828),
                new Tuple<string, TokenKind, object>(".5", TokenKind.Float, 0.5),
                new Tuple<string, TokenKind, object>("-.5", TokenKind.Float, -0.5),
                new Tuple<string, TokenKind, object>("0x123aBc", TokenKind.Integer, 0x123ABCL),
                new Tuple<string, TokenKind, object>("0o123", TokenKind.Integer, 83L),
                new Tuple<string, TokenKind, object>("0123", TokenKind.Integer, 83L),
                new Tuple<string, TokenKind, object>("0b000101100111", TokenKind.Integer, 0x167L),
                new Tuple<string, TokenKind, object>("1e8", TokenKind.Float, 1e8),
                new Tuple<string, TokenKind, object>("1e-8", TokenKind.Float, 1e-8),
                new Tuple<string, TokenKind, object>("-1e-8", TokenKind.Float, -1e-8),
                new Tuple<string, TokenKind, object>("-4", TokenKind.Integer, -4L),
                new Tuple<string, TokenKind, object>("1234_5678", TokenKind.Integer, 12345678L),
                new Tuple<string, TokenKind, object>("123_456_789", TokenKind.Integer, 123456789L),
            };

            foreach (var c in cases)
            {
                tokenizer = MakeTokenizer(c.Item1);
                token = tokenizer.GetToken();
                Assert.AreEqual(c.Item2, token.Kind);
                Assert.AreEqual(c.Item1, token.Text);
                Assert.AreEqual(c.Item3, token.Value);
                Assert.AreEqual(TokenKind.EOF, tokenizer.GetToken().Kind);
            }

            Tuple<string, int, int>[] empties = new Tuple<string, int, int>[]
            {
                new Tuple<string, int, int>("\"\"", 1, 2),
                new Tuple<string, int, int>("''", 1, 2),
                new Tuple<string, int, int>("''''''", 1, 6),
                new Tuple<string, int, int>("\"\"\"\"\"\"", 1, 6),
            };

            foreach (var empty in empties)
            {
                tokenizer = MakeTokenizer(empty.Item1);
                token = tokenizer.GetToken();
                Assert.AreEqual(TokenKind.String, token.Kind);
                Assert.AreEqual(empty.Item1, token.Text);
                Assert.AreEqual("", token.Value);
                Assert.AreEqual(empty.Item2, token.End.Line);
                Assert.AreEqual(empty.Item3, token.End.Column);
                Assert.AreEqual(TokenKind.EOF, tokenizer.GetToken().Kind);
            }
            tokenizer = MakeTokenizer("\"\"\"abc\ndef\n\"\"\"");
            token = tokenizer.GetToken();
            Assert.AreEqual(TokenKind.String, token.Kind);
            Assert.AreEqual("abc\ndef\n", token.Value);
            Assert.AreEqual(TokenKind.EOF, tokenizer.GetToken().Kind);

            tokenizer = MakeTokenizer("9+4j+a*b");
            List<Token> tokens = new List<Token>(tokenizer);
            var kinds = tokens.Select((t) => (object) t.Kind).ToArray();
            object[] expectedKinds = {
                TokenKind.Integer, TokenKind.Plus, TokenKind.Complex,
                TokenKind.Plus, TokenKind.Word, TokenKind.Star,
                TokenKind.Word, TokenKind.EOF };
            CompareArrays(expectedKinds, kinds);
            var texts = tokens.Select((t) => (object) t.Text).ToArray();
            object[] expectedTexts = { "9", "+", "4j", "+", "a", "*", "b", "" };
            CompareArrays(expectedTexts, texts);

            string punct = "< > { } [ ] ( ) + - * / ** // % . <= <> << >= >> == != , : @ ~ & | ^ $";

            tokenizer = MakeTokenizer(punct);
            tokens = new List<Token>(tokenizer);
            kinds = tokens.Select((t) => (object) t.Kind).ToArray();
            texts = tokens.Select((t) => t.Text).ToArray();
            expectedKinds = new object[] {
                TokenKind.LessThan, TokenKind.GreaterThan, TokenKind.LeftCurly, TokenKind.RightCurly,
                TokenKind.LeftBracket, TokenKind.RightBracket, TokenKind.LeftParenthesis, TokenKind.RightParenthesis,
                TokenKind.Plus, TokenKind.Minus, TokenKind.Star, TokenKind.Slash, TokenKind.Power, TokenKind.SlashSlash,
                TokenKind.Modulo, TokenKind.Dot, TokenKind.LessThanOrEqual, TokenKind.AltUnequal, TokenKind.LeftShift,
                TokenKind.GreaterThanOrEqual, TokenKind.RightShift, TokenKind.Equal, TokenKind.Unequal, TokenKind.Comma,
                TokenKind.Colon, TokenKind.At, TokenKind.BitwiseComplement, TokenKind.BitwiseAnd, TokenKind.BitwiseOr,
                TokenKind.BitwiseXor, TokenKind.Dollar, TokenKind.EOF };
            CompareArrays(expectedKinds, kinds);
            expectedTexts = punct.Split(' ');
            Sequence temp = new Sequence(expectedTexts) { "" };
            expectedTexts = temp.ToArray();
            CompareArrays(expectedTexts, texts);

            //keywords
            string keywords = "true false null is in not and or";
            tokenizer = MakeTokenizer(keywords);
            tokens = new List<Token>(tokenizer);
            kinds = tokens.Select((t) => (object) t.Kind).ToArray();
            texts = tokens.Select((t) => t.Text).ToArray();
            expectedKinds = new object[] {
                TokenKind.True, TokenKind.False, TokenKind.None, TokenKind.Is,
                TokenKind.In, TokenKind.Not, TokenKind.And, TokenKind.Or,
                TokenKind.EOF
            };
            CompareArrays(expectedKinds, kinds);
            expectedTexts = keywords.Split(' ');
            temp = new Sequence(expectedTexts) { "" };
            expectedTexts = temp.ToArray();
            CompareArrays(expectedTexts, texts);

            // various newlines
            tokenizer = MakeTokenizer("\n \r \r\n");
            tokens = new List<Token>(tokenizer);
            kinds = tokens.Select((t) => (object) t.Kind).ToArray();
            expectedKinds = new object[] { TokenKind.Newline, TokenKind.Newline,
                                              TokenKind.Newline, TokenKind.EOF };
            CompareArrays(expectedKinds, kinds);

            // test the non-generic enumerator
            IEnumerator enumerator = ((IEnumerable) tokenizer).GetEnumerator();
            Assert.IsTrue(enumerator.MoveNext());
            token = (Token) enumerator.Current;
            Assert.IsNotNull(token);
            Assert.AreEqual(TokenKind.EOF, token.Kind);
            Assert.AreEqual("Token(EOF::)", token.ToString());

            // values
            Assert.AreEqual(false, MakeTokenizer("false").GetToken().Value);
            Assert.AreEqual(true, MakeTokenizer("true").GetToken().Value);
            Assert.AreEqual(Tokenizer.NullValue, MakeTokenizer("null").GetToken().Value);
        }

        [TestMethod]
        public void TokenValues()
        {
            string source = "a + 4";
            Parser p = Parser.MakeParser(source);
            BinaryNode node = (BinaryNode) p.Expr();

            Assert.AreEqual(TokenKind.Plus, node.Kind);
            Token t = node.Left as Token;
            Assert.IsNotNull(t);
            Assert.AreEqual(TokenKind.Word, t.Kind);
            Assert.AreEqual("a", t.Value);
            t = node.Right as Token;
            Assert.IsNotNull(t);
            Assert.AreEqual(TokenKind.Integer, t.Kind);
            Assert.AreEqual(4L, t.Value);

            // check that strings are concatenated appropriately
            p = Parser.MakeParser(" 'foo' \"bar\" 'baz'");
            t = p.Value();
            Assert.AreEqual(TokenKind.String, t.Kind);
            Assert.AreEqual("'foo'\"bar\"'baz'", t.Text);
            Assert.AreEqual("foobarbaz", t.Value);
            CheckLocation(t.Start, 1, 2);
            CheckLocation(t.End, 1, 18);
        }

        private void CheckLocation(Location loc, int line, int col)
        {
            Assert.AreEqual(line, loc.Line);
            Assert.AreEqual(col, loc.Column);
        }

        protected string DataFilePath(params string[] pathComponents)
        {
            int n = pathComponents.Length;
            string[] parts = new string[n + 1];
            parts[0] = "resources";

            Array.Copy(pathComponents, 0, parts, 1, n);
            return Path.Combine(parts);
        }

        [TestMethod]
        public void Locations()
        {
            Tokenizer tokenizer = MakeTokenizer("# a comment\n   foo");

            Func<Token, int, int, int, int, bool> checker = (t, sl, sc, el, ec) =>
            {
                Location s = t.Start;
                Location e = t.End;

                return (s.Line == sl) && (s.Column == sc) && (e.Line == el) && (e.Column == ec);
            };

            Token token = tokenizer.GetToken();
            Assert.IsTrue(checker(token, 1, 1, 2, 1));
            token = tokenizer.GetToken();
            Assert.IsTrue(checker(token, 2, 4, 2, 6));
            token = tokenizer.GetToken();
            Assert.IsTrue(checker(token, 2, 7, 2, 7));

            var expected = new List<int[]>();

            using (var reader = new NamedReader(DataFilePath("pos.forms.cfg.txt")))
            {
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    Assert.IsNotNull(line);
                    var nums = line.Split(' ').Select(int.Parse).ToArray();
                    expected.Add(nums);
                }
            }

            using (var reader = new NamedReader(DataFilePath("forms.cfg")))
            {
                tokenizer = new Tokenizer(reader);

                var tokens = new List<Token>(tokenizer);

                Assert.AreEqual(expected.Count, tokens.Count);

                for (int i = 0; i < tokens.Count; i++)
                {
                    Token t = tokens.ElementAt(i);
                    int[] pos = expected[i];

                    Assert.IsTrue(checker(t, pos[0], pos[1], pos[2], pos[3]));
                }

                // reset stream
                reader.BaseStream.Position = 0;
                reader.DiscardBufferedData();
                Parser p = new Parser(reader);
                Assert.IsNotNull(ToMap(p.MappingBody().Items));
                Assert.IsTrue(p.AtEnd);
            }

        }

        [TestMethod]
        public void BadTokens()
        {
            Tokenizer tokenizer;

            var badNumbers = new[]
            {
                new Tuple<string, int, int, string>("9a", 1, 2, null),
                new Tuple<string, int, int, string>("079", 1, 1, null),
                new Tuple<string, int, int, string>("0xaBcz", 1, 6, null),
                new Tuple<string, int, int, string>("0o79", 1, 4, null),
                new Tuple<string, int, int, string>(".5z", 1, 3, null),
                new Tuple<string, int, int, string>("0.5.7", 1, 4, null),
                new Tuple<string, int, int, string>(" 0.4e-z", 1, 7, null),
                new Tuple<string, int, int, string>(" 0.4e-8.3", 1, 8, null),
                new Tuple<string, int, int, string>(" 089z", 1, 5, null),
                new Tuple<string, int, int, string>("0o89z", 1, 3, null),
                new Tuple<string, int, int, string>("0X89g", 1, 5, null),
                new Tuple<string, int, int, string>("10z", 1, 3, null),
                new Tuple<string, int, int, string>("0.4e-8Z", 1, 7, null),
                new Tuple<string, int, int, string>("`abc", 1, 1, "unterminated `-string: `abc"),
                new Tuple<string, int, int, string>("`abc\n", 1, 5, "invalid char in `-string: `abc"),
                new Tuple<string, int, int, string>("123_", 1, 4, "invalid '_' at end of number: 123_"),
                new Tuple<string, int, int, string>("1__23", 1, 3, "invalid '_' in number: 1__"),
                new Tuple<string, int, int, string>("1_2__3", 1, 5, "invalid '_' in number: 1_2__"),
                new Tuple<string, int, int, string>("0.4e-8_", 1, 7, "invalid '_' at end of number: 0.4e-8_"),
                new Tuple<string, int, int, string>("0.4_e-8", 1, 4, "invalid '_' at end of number: 0.4_"),
                new Tuple<string, int, int, string>("0._4e-8", 1, 3, "invalid '_' in number: 0."),
                new Tuple<string, int, int, string>("\\ ", 1, 2, "unexpected \\"),
            };

            foreach (var bn in badNumbers)
            {
                tokenizer = MakeTokenizer(bn.Item1);
                try
                {
                    Assert.IsNotNull(tokenizer.GetToken());
                    Assert.Fail("Expected exception wasn't thrown");
                }
                catch (RecognizerException e)
                {
                    string msg = bn.Item4;
                    string s = e.ToString();

                    if (msg == null)
                    {
                        msg = "invalid character in number:";
                    }
                    Assert.IsTrue(s.Contains(msg));
                    CheckLocation(e.Location, bn.Item2, bn.Item3);
                }
            }

            var badStrings = new[]
            {
                new Tuple<string, int, int, string>("\"", 1, 1, "unterminated string:"),
                new Tuple<string, int, int, string>("\'", 1, 1, "unterminated string:"),
                new Tuple<string, int, int, string>("\'\'\'", 1, 1, "unterminated string:"),
                new Tuple<string, int, int, string>("  ;", 1, 3, "unexpected character:"),
            };
            foreach (var bs in badStrings)
            {
                tokenizer = MakeTokenizer(bs.Item1);
                try
                {
                    Assert.IsNotNull(tokenizer.GetToken());
                    Assert.Fail("Expected exception wasn't thrown");
                }
                catch (RecognizerException e)
                {
                    Assert.IsTrue(e.ToString().Contains(bs.Item4));
                    CheckLocation(e.Location, bs.Item2, bs.Item3);
                }
            }

            try
            {
                Parser.Parse("{foo", "Mapping");
            }
            catch (RecognizerException e)
            {
                Assert.IsTrue(e.ToString().Contains("key-value separator expected, but found: EOF"));
                CheckLocation(e.Location, 1, 5);
            }
        }

        [TestMethod]
        public void Escapes()
        {
            List<Tuple<string, string>> goodCases = new List<Tuple<string, string>>()
            {
                new Tuple<string, string>(@"'\a'", "\a"),
                new Tuple<string, string>(@"'\b'", "\b"),
                new Tuple<string, string>(@"'\f'", "\f"),
                new Tuple<string, string>(@"'\n'", "\n"),
                new Tuple<string, string>(@"'\r'", "\r"),
                new Tuple<string, string>(@"'\t'", "\t"),
                new Tuple<string, string>(@"'\v'", "\v"),
                new Tuple<string, string>("'\\\"'", "\""),
                new Tuple<string, string>("'\\''", "'"),
                new Tuple<string, string>(@"'\xAB'", "\xAB"),
                new Tuple<string, string>(@"'\u2803'", "\u2803"),
                new Tuple<string, string>(@"'\u28A0abc\u28A0'", "\u28A0abc\u28a0"),
                new Tuple<string, string>(@"'\u28A0abc'", "\u28A0abc"),
                new Tuple<string, string>(@"'\uE000'", "\ue000"),
                new Tuple<string, string>(@"'\U0010FFFF'", "\udbff\udfff"),
            };

            foreach (var c in goodCases)
            {
                var tokenizer = MakeTokenizer(c.Item1);
                var t = tokenizer.GetToken();

                Assert.AreEqual(TokenKind.String, t.Kind);
                Assert.AreEqual(c.Item1, t.Text);
                Assert.AreEqual(c.Item2, t.Value);
            }

            List<string> badCases = new List<string>()
            {
                "'\\z'",
                "'\\x'",
                "'\\xa'",
                "'\\xaz'",
                "'\\u'",
                "'\\u0'",
                "'\\u01'",
                "'\\u012'",
                "'\\u012z'",
                "'\\u012zA'",
                "'\\ud800'",
                "'\\udfff'",
                "'\\U00110000'",
            };

            foreach (var s in badCases)
            {
                var tokenizer = MakeTokenizer(s);

                try
                {
                    tokenizer.GetToken();
                    Assert.Fail("Expected exception wasn't thrown");
                }
                catch (RecognizerException e)
                {
                    Assert.IsTrue(e.ToString().Contains("invalid escape sequence"));
                }
            }
        }

        [TestMethod]
        public void BadParsing()
        {
            try
            {
                Parser.Parse("", "NoSuchRule");
                Assert.Fail("Expected exception wasn't thrown");
            }
            catch (ArgumentException e)
            {
                Assert.IsTrue(e.ToString().Contains("No such thing"));
            }
            try
            {
                Parser.Parse("   :", "Atom");
                Assert.Fail("Expected exception wasn't thrown");
            }
            catch (RecognizerException e)
            {
                Assert.IsTrue(e.ToString().Contains("unexpected: Colon"));
                CheckLocation(e.Location, 1, 4);
            }
            try
            {
                Parser.Parse("   :", "Value");
                Assert.Fail("Expected exception wasn't thrown");
            }
            catch (RecognizerException e)
            {
                Assert.IsTrue(e.ToString().Contains("unexpected when looking for value: Colon"));
                CheckLocation(e.Location, 1, 4);
            }
        }

        [TestMethod]
        public void Fragments()
        {
            object o = Parser.Parse("foo", "Expr");
            Assert.IsInstanceOfType(o, typeof(Token));
            Assert.AreEqual("foo", ((Token) o).Value);
            o = Parser.Parse(".5", "Expr");
            Assert.IsInstanceOfType(o, typeof(Token));
            Assert.AreEqual(0.5, ((Token) o).Value);
            o = Parser.Parse("'foo'\"bar\"", "Expr");
            Assert.IsInstanceOfType(o, typeof(Token));
            Assert.AreEqual("foobar", ((Token) o).Value);
            o = Parser.Parse("a.b", "Expr");
            var expected = BN(".", W("a"), W("b"));
            CompareObjects(expected, o);
            o = Parser.Parse("a.b.c.d", "Expr");
            var ab = BN(".", W("a"), W("b"));
            var abc = BN(".", ab, W("c"));
            var abcd = BN(".", abc, W("d"));
            CompareObjects(abcd, o);
        }

        [TestMethod]
        public void Unaries()
        {
            string[] operators = new string[] { "+", "-", "~", "!", " not " };

            foreach (var op in operators)
            {
                string s = $"{op}foo";
                Token token = MakeTokenizer(op).GetToken();
                UnaryNode o = (UnaryNode) Parser.Parse(s, "Expr");
                Assert.AreEqual(token.Kind, o.Kind);
                //TODO Assert.AreEqual("foo", o.Operand);
            }
        }

        private void Expressions(string[] operators, string rule, bool multiple = true)
        {
            string s;
            BinaryNode o;

            foreach (var op in operators)
            {
                s = $"foo{op}bar";
                TokenKind kind = MakeTokenizer(op).GetToken().Kind;
                o = (BinaryNode) Parser.Parse(s, rule);
                Assert.AreEqual(kind, o.Kind);
                //TODO Assert.AreEqual("foo", o.Left);
                //TODO Assert.AreEqual("bar", o.Right);
            }
            if (multiple)
            {
                Random r = new Random();
                for (int i = 0; i < 10000; i++)
                {
                    var n = r.Next(operators.Length);
                    var op1 = operators[n];
                    n = r.Next(operators.Length);
                    var op2 = operators[n];
                    TokenKind k1 = MakeTokenizer(op1).GetToken().Kind;
                    TokenKind k2 = MakeTokenizer(op2).GetToken().Kind;

                    s = $"foo{op1}bar{op2}baz";
                    o = (BinaryNode) Parser.Parse(s, rule);
                    Assert.AreEqual(k2, o.Kind);
                    //TODO Assert.AreEqual("baz", o.Right);
                    o = (BinaryNode) o.Left;
                    Assert.AreEqual(k1, o.Kind);
                    //TODO Assert.AreEqual("foo", o.Left);
                    //TODO Assert.AreEqual("bar", o.Right);
                }
            }
        }

        [TestMethod]
        public void Binaries()
        {
            string[] operators = { "*", "/", "//", "%" };

            Expressions(operators, "MulExpr");
            operators = new[] { "+", "-" };
            Expressions(operators, "AddExpr");
            operators = new[] { "<<", ">>" };
            Expressions(operators, "ShiftExpr");
            operators = new[] { "&" };
            Expressions(operators, "BitAndExpr");
            operators = new[] { "^" };
            Expressions(operators, "BitXorExpr");
            operators = new[] { "|" };
            Expressions(operators, "BitOrExpr");

            operators = new[] { "**" };
            Expressions(operators, "Power", false);
            var o = (BinaryNode) Parser.Parse("foo**bar**baz", "Power");
            Assert.AreEqual(TokenKind.Power, o.Kind);
            //TODO Assert.AreEqual("foo", o.Left);
            o = (BinaryNode) o.Right;
            Assert.AreEqual(TokenKind.Power, o.Kind);
            //TODO Assert.AreEqual("bar", o.Left);
            //TODO Assert.AreEqual("baz", o.Right);

            o = (BinaryNode) Parser.Parse("foo is not bar", "Comparison");
            Assert.AreEqual(TokenKind.IsNot, o.Kind);
            //TODO Assert.AreEqual("foo", o.Left);
            //TODO Assert.AreEqual("bar", o.Right);

            o = (BinaryNode) Parser.Parse("foo not in bar", "Comparison");
            Assert.AreEqual(TokenKind.NotIn, o.Kind);
            //TODO Assert.AreEqual("foo", o.Left);
            //TODO Assert.AreEqual("bar", o.Right);

            operators = new[]
            {
                "<=", "<>", "<", ">=", ">", "==", "!=", " in ", " is "
            };
            Expressions(operators, "Comparison", false);
            operators = new[] { " and ", "&&" };
            Expressions(operators, "AndExpr");
            operators = new[] { " or ", "||" };
            Expressions(operators, "Expr");
        }

        [TestMethod]
        public void Slices()
        {
            var node = (BinaryNode) Parser.Parse("foo[start:stop:step]", "Expr");
            var expected = BN(":", W("foo"), SN(W("start"), W("stop"), W("step")));
            CompareObjects(expected, node);

            node = (BinaryNode) Parser.Parse("foo[start:stop]", "Expr");
            expected = BN(":", W("foo"), SN(W("start"), W("stop"), null));
            CompareObjects(expected, node);

            node = (BinaryNode) Parser.Parse("foo[start:stop:]", "Expr");
            CompareObjects(expected, node);

            node = (BinaryNode) Parser.Parse("foo[start:]", "Expr");
            expected = BN(":", W("foo"), SN(W("start"), null, null));
            CompareObjects(expected, node);

            node = (BinaryNode) Parser.Parse("foo[start::]", "Expr");
            CompareObjects(expected, node);

            node = (BinaryNode) Parser.Parse("foo[:stop]", "Expr");
            expected = BN(":", W("foo"), SN(null, W("stop"), null));
            CompareObjects(expected, node);

            node = (BinaryNode) Parser.Parse("foo[:stop:]", "Expr");
            CompareObjects(expected, node);

            node = (BinaryNode) Parser.Parse("foo[::step]", "Expr");
            expected = BN(":", W("foo"), SN(null, null, W("step")));
            CompareObjects(expected, node);

            // non-slice case

            node = (BinaryNode) Parser.Parse("foo[start]", "Expr");
            expected = BN("[", W("foo"), W("start"));
            CompareObjects(expected, node);

            // failure cases

            Tuple<string, string>[] cases =
            {
                new Tuple<string, string>("foo[start::step:]", "expected RightBracket but got Colon"),
                new Tuple<string, string>("foo[a, b:c:d]", "invalid index: expected one value, 2 found"),
                new Tuple<string, string>("foo[a:b,c:d]", "invalid index: expected one value, 2 found"),
                new Tuple<string, string>("foo[a:b:c,d, e]", "invalid index: expected one value, 3 found"),
            };

            foreach (var t in cases)
            {
                try
                {
                    Assert.IsNotNull((BinaryNode) Parser.Parse(t.Item1, "Expr"));
                    Assert.Fail("Exception not thrown");
                }
                catch (ParserException e)
                {
                    Assert.AreEqual(e.Message, t.Item2);
                }
            }
        }

        [TestMethod]
        public void Atoms()
        {
            // version with and without a trailing comma
            string[] sources = new string[] { "[1, 2, 3]", "[1, 2, 3,]" };

            foreach (var s in sources)
            {
                ListItems li = Parser.Parse(s, "Atom") as ListItems;
                Assert.IsNotNull(li);
                Assert.AreEqual(3, li.Items.Count);
                Assert.AreEqual(1L, ((Token) li.Items[0]).Value);
                Assert.AreEqual(2L, ((Token) li.Items[1]).Value);
                Assert.AreEqual(3L, ((Token) li.Items[2]).Value);
            }
        }

        private static Mapping MakeDict(params object[] values)
        {
            var result = new Mapping();

            if ((values != null) && (values.Length > 0))
            {
                Debug.Assert((values.Length % 2) == 0);
                for (int i = 0; i < values.Length; i++)
                {
                    string key = (string) values[i++];

                    result[key] = values[i];
                }
            }
            return result;
        }

        /*
        private static MappingItems MI(params object[] values)
        {
            var result = new MappingItems();

            if ((values != null) && (values.Length > 0))
            {
                Debug.Assert((values.Length % 2) == 0);
                for (int i = 0; i < values.Length; i++)
                {
                    string key = (string) values[i++];
                    TokenKind k = key.IndexOf(' ') >= 0 ? TokenKind.String : TokenKind.Word;
                    Token t = new Token(k, null, key);
                    ASTNode value = (ASTNode) values[i];
                    result.Items.Add(new Tuple<Token, ASTNode>(t, value));
                }
            }
            return result;
        }
        */

        private static UnaryNode UN(string op, ASTNode operand)
        {
            TokenKind kind = MakeTokenizer(op).GetToken().Kind;

            return new UnaryNode(kind, operand);
        }

        private static BinaryNode BN(string op, ASTNode left, ASTNode right)
        {
            TokenKind kind = MakeTokenizer(op).GetToken().Kind;

            return new BinaryNode(kind, left, right);
        }

        private static SliceNode SN(ASTNode start, ASTNode stop, ASTNode step)
        {
            return new SliceNode(start, stop, step);
        }

        private static object[] OA(params object[] args)
        {
            return args;
        }

        //private static ListItems LI(params ASTNode[] args)
        //{
        //    var result = new ListItems();

        //    foreach (var arg in args) {
        //        result.Items.Add(arg);
        //    }
        //    return result;
        //}

        /*
        private static int[] IA(params int[] args)
        {
            return args;
        }
        */

        private static Token C(double imaginary, double real = 0)
        {
            Complex value = new Complex(real, imaginary);

            return new Token(TokenKind.Complex, null, value);
        }

        private static Token BT(string s)
        {
            string t = $"`{s}`";
            return new Token(TokenKind.BackTick, t, s);
        }

        private static Token T(string s)
        {
            TokenKind kind;
            object value;

            if (s == null)
            {
                kind = TokenKind.None;
                value = Tokenizer.NullValue;
            }
            else
            {
                kind = TokenKind.String;
                value = s;
            }
            return new Token(kind, null, value);
        }

        private static Token W(string s)
        {
            return new Token(TokenKind.Word, s, s);
        }

        private static Token T(long n)
        {
            return new Token(TokenKind.Integer, null, n);
        }

        private static Token T(double d)
        {
            return new Token(TokenKind.Float, null, d);
        }

        private static Token T(bool b)
        {
            return new Token(b ? TokenKind.True : TokenKind.False, null, b);
        }

        private static readonly Dictionary<string, string> ExpectedMessages = new Dictionary<string, string>()
        {
            {"D01", "unexpected type for key: Integer"},
            {"D02", "unexpected type for key: LeftBracket"},
            {"D03", "unexpected type for key: LeftCurly"},
        };

        private static readonly Mapping ExpectedValues = new Mapping()
        {
            {"C01", MakeDict("message", T("Hello, world!"))},
            {"C02", MakeDict("message", T("Hello, world!"), "ident", T(42L)) },
            {"C03", MakeDict("message", T("Hello, world!"), "ident", T(43L)) },
            {"C04", MakeDict("numbers", OA(T(0L), T(0x12L), T(11L), T(1014L))) },
            {"C05", MakeDict("complex", OA(C(0), C(1), C(0.4), C(0.7))) },
            {"C06", MakeDict("nested", MakeDict("a", W("b"), "c", W("d"), "e f", T("g"))) },
            {"C07", MakeDict("foo", OA(T(1L), T(2L), T(3L)),
                             "bar", OA(BN("+", T(4L), W("x")), T(5L), T(6L)),
                             "baz", MakeDict("foo", OA(T(1L), T(2L), T(3L)),
                                             "bar", BN("+", T("baz"), T(3L)))) },
            {"C08", MakeDict("total_period", T(100L),
                             "header_time", BN("*", T(0.3), W("total_period")),
                             "steady_time", BN("*", T(0.5), W("total_period")),
                             "trailer_time", BN("*", T(0.2), W("total_period")),
                             "base_prefix", T("/my/app/"),
                             "log_file", BN("+", W("base_prefix"), T("test.log"))) },
            {"C09", MakeDict("message", T("Hello, world!"), "stream", BT("sys.stderr")) },
            {"C10", MakeDict("messages", OA(
                MakeDict("message", T("Welcome"), "stream", BT("sys.stderr")),
                MakeDict("message", T("Welkom"), "stream", BT("sys.stdout")),
                MakeDict("message", T("Bienvenue"), "stream", BT("sys.stderr"))
            )) },
            {"C11", MakeDict("messages", OA(
                MakeDict("message", W("Welcome"), "name", T("Harry"), "stream", BT("sys.stderr")),
                MakeDict("message", W("Welkom"), "name", T("Ruud"), "stream", BT("sys.stdout")),
                MakeDict("message", W("Bienvenue"), "name", W("Yves"), "stream", BT("sys.stderr"))
            )) },
            {"C12", MakeDict("messages", OA(
                MakeDict("message", T("Welcome"), "name", T("Harry"), "stream", BT("sys.stderr")),
                MakeDict("message", T("Welkom"), "name", T("Ruud"), "stream", BT("sys.stdout")),
                MakeDict("message", T("Bienvenue"), "name", W("Yves"), "stream",
                    UN("$", BN(".", BN("[", W("messages"), T(0L)), W("stream"))))
            )) },
            {"C13", MakeDict("logging", UN("@", T("logging.cfg")),
                             "test", UN("$", BN(".", BN(".", BN(".", W("logging"), W("handler")), W("email")),
                                                                  W("from")))
             ) },
            // C14 still to do - it's a big one!
            {"C15", MakeDict("a", BN(".", UN("$", BN(".", W("foo"), W("bar"))), W("baz")),
                             "b", BN(".", BT("bish.bash"), W("bosh"))) },
            {"C16", MakeDict("test", T(false), "another_test", T(true)) },
            {"C17", MakeDict("test", T(null)) },
            {"C18", MakeDict("root", T(1L), "stream", T(1.7),
                             "neg", T(-1L),
                             "negfloat", T(-2.0),
                             "posexponent", T(2.0999999e-08),
                             "negexponent", T(-2.0999999e-08),
                             "exponent", T(2.0999999e08)) },
            {"C19", MakeDict("mixed", OA(T("VALIGN"), OA(T(0L), T(0L)), OA(T(-1L), T(-1L)), T("TOP")),
                             "simple", OA(T(1L), T(2L)),
                             "nested", OA(T(1L), OA(T(2L), T(3L)), OA(T(4L), OA(T(5L), T(6L))))) },
            // C20 TODO
            {"C21", MakeDict("stderr", BT("sys.stderr"),
                             "stdout", BT("sys.stdout"),
                             "stdin", BT("sys.stdin"),
                             "debug", BT("debug"),
                             "DEBUG", BT("DEBUG"),
                             "derived", BN("*", UN("$", W("DEBUG")), T(10L))) },
            // C22 TODO
            {"C23", MakeDict("foo", OA(W("bar"), W("baz"), W("bozz"))) },
            {"C24", MakeDict("foo", OA(T("bar"), BN("+", BN("-", BN("+", W("a"), W("b")), W("c")), W("d")))) },
            {"C25", MakeDict("unicode", T("Gr\u00fc\u00df Gott"), "more_unicode", T("\u00d8resund")) },
            {"C26", MakeDict("foo", OA(T("bar"), BN("+", BN("-", BN("+", W("a"), W("b")), W("c")), W("d")))) },
            {"C27", MakeDict("foo", BN("^", BN("&", W("a"), W("b")), BN("|", W("c"), W("d")))) },
        };

        [TestMethod]
        public void FromData()
        {
            var cases = LoadData(DataFilePath("testdata.txt"));

            foreach (var kv in cases)
            {
                string k = kv.Key;
                string v = kv.Value.Trim();

                if (String.Compare(k, "D01", StringComparison.Ordinal) < 0)
                {
                    Parser p = Parser.MakeParser(v);

                    var o = p.MappingBody();
                    Assert.IsTrue(p.AtEnd);
                    if (ExpectedValues.ContainsKey(k))
                    {
                        CompareMappings((Mapping) ExpectedValues[k], ToMap(o.Items));
                    }
                }
                else
                {
                    try
                    {
                        Parser.Parse(v);
                        Assert.Fail("Expected exception wasn't thrown");
                    }
                    catch (RecognizerException e)
                    {
                        var s = e.ToString();

                        if (ExpectedMessages.ContainsKey(k))
                        {
                            Assert.IsTrue(s.Contains(ExpectedMessages[k]));
                        }
                    }
                }
            }
        }

        [TestMethod]
        public void Json()
        {
            using (var reader = new NamedReader(DataFilePath("forms.conf")))
            {
                Parser p = new Parser(reader);
                var d = ToMap(p.Mapping().Items);

                Assert.IsTrue(d.ContainsKey("refs"));
                Assert.IsTrue(d.ContainsKey("fieldsets"));
                Assert.IsTrue(d.ContainsKey("forms"));
                Assert.IsTrue(d.ContainsKey("modals"));
                Assert.IsTrue(d.ContainsKey("pages"));
            }
        }


        // Config API tests

        [TestMethod]
        public void Duplicates()
        {
            var config = new Config();
            var p = DataFilePath("derived", "dupes.cfg");
            try
            {
                config.LoadFile(p);
            }
            catch (ConfigException e)
            {
                Assert.AreEqual("Duplicate key foo seen at (4, 1) (previously at (1, 1))", e.Message);
            }
            config.NoDuplicates = false;
            config.LoadFile(p);
            CheckSamePaths(p, config.Path);
            Assert.AreEqual("not again!", config["foo"]);
        }

        [TestMethod]
        public void BadPaths()
        {
            Tuple<string, string>[] cases =
            {
                new Tuple<string, string>("foo[1, 2]", "invalid index: expected one value, 2 found"),
                new Tuple<string, string>("foo[1] bar", null),
                new Tuple<string, string>("handlers.file/filename", null),
                // Have an error in the very first token
                new Tuple<string, string>("\"handlers.file/filename", null)
            };

            foreach (var t in cases)
            {
                var msg = $"Invalid path: {t.Item1}";
                try
                {
                    Config.ParsePath(t.Item1);
                    Assert.Fail("Path {0} not correctly identified as bad", t.Item1);
                }
                catch (InvalidPathException e)
                {
                    Assert.AreEqual(msg, e.Message);
                    if (e.InnerException != null)
                    {
                        Assert.AreEqual(t.Item2, e.InnerException.Message);
                    }
                    else
                    {
                        Assert.IsNull(t.Item2);
                    }
                }
            }
        }

        [TestMethod]
        public void Identifiers()
        {
            var cases = new[]
            {
                new Tuple<string, bool>("foo", true),
                new Tuple<string, bool>("\u0935\u092e\u0938", true),
                new Tuple<string, bool>("\u73b0\u4ee3\u6c49\u8bed\u5e38\u7528\u5b57\u8868", true),
                new Tuple<string, bool>("foo ", false),
                new Tuple<string, bool>("foo[", false),
                new Tuple<string, bool>("foo [", false),
                new Tuple<string, bool>("foo.", false),
                new Tuple<string, bool>("foo .", false),
                new Tuple<string, bool>("\u0935\u092e\u0938.", false),
                new Tuple<string, bool>("\u73b0\u4ee3\u6c49\u8bed\u5e38\u7528\u5b57\u8868.", false),
                new Tuple<string, bool>("9", false),
                new Tuple<string, bool>("9foo", false),
                new Tuple<string, bool>("hyphenated-key", false)
            };

            foreach (var c in cases)
            {
                var result = Utils.IsIdentifier(c.Item1);

                Assert.AreEqual(c.Item2, result);
            }
        }

        [TestMethod]
        public void PathIteration()
        {
            var p = Config.ParsePath("foo[bar].baz.bozz[3].fizz");
            Sequence actual = new Sequence(Config.PathIterator(p));
            object[] expected = new object[]
            {
                new Token(TokenKind.Word, "foo", "foo"),
                new Tuple<TokenKind, object>(TokenKind.LeftBracket, "bar"),
                new Tuple<TokenKind, object>(TokenKind.Dot, "baz"),
                new Tuple<TokenKind, object>(TokenKind.Dot, "bozz"),
                new Tuple<TokenKind, object>(TokenKind.LeftBracket, 3L),
                new Tuple<TokenKind, object>(TokenKind.Dot, "fizz")
            };
            CompareArrays(expected, actual.ToArray<object>());
            p = Config.ParsePath("foo[1:2]");
            actual = new Sequence(Config.PathIterator(p));
            var sn = new SliceNode(new Token(TokenKind.Integer, "1", 1L),
                                   new Token(TokenKind.Integer, "2", 2L),
                                   null);
            expected = new object[]
            {
                new Token(TokenKind.Word, "foo", "foo"),
                new Tuple<TokenKind, object>(TokenKind.Colon, sn)
            };
            CompareArrays(expected, actual.ToArray<object>());
        }

        private object[] ToObjectArray(object o)
        {
            return ((ISequence) o).ToArray();
        }

        [TestMethod]
        public void MainConfig()
        {
            var config = new Config();
            var p = DataFilePath("derived", "main.cfg");
            config.IncludePath.Add(DataFilePath("base"));

            config.LoadFile(p);

            var logConf = config["logging"] as Config;
            Assert.IsNotNull(logConf);

            var keys = new List<string>(logConf.AsDict().Keys);
            keys.Sort();
            var exp1 = new[] { "formatters", "handlers", "loggers", "root" };
            CompareArrays(exp1, keys.ToArray<string>());
            try
            {
                logConf.Get("handlers.file/filename");
                Assert.Fail("Failed to raise expected exception");
            }
            catch (InvalidPathException ipe)
            {
                Assert.AreEqual("Invalid path: handlers.file/filename", ipe.Message);
            }
            Assert.AreEqual("bar", logConf.Get("foo", "bar"));
            Assert.AreEqual("baz", logConf.Get("foo.bar", "baz"));
            Assert.AreEqual("bozz", logConf.Get("handlers.debug.levl", "bozz"));
            Assert.AreEqual("run/server.log", logConf["handlers.file.filename"]);
            Assert.AreEqual("run/server-debug.log", logConf["handlers.debug.filename"]);
            object[] expected = new object[] { "file", "error", "debug" };
            CompareArrays(expected, ToObjectArray(logConf["root.handlers"]));
            expected = new object[] { "file", "error" };
            CompareArrays(expected, ToObjectArray(logConf["root.handlers[:2]"]));
            expected = new object[] { "file", "debug" };
            CompareArrays(expected, ToObjectArray(logConf["root.handlers[::2]"]));

            var test = (Config) config["test"];
            Assert.AreEqual(1.0e-7, test["float"]);
            Assert.AreEqual(0.3, test["float2"]);
            Assert.AreEqual(3.0, test["float3"]);
            Assert.AreEqual(2L, test["list[1]"]);
            Assert.AreEqual("b", test["dict.a"]);
            Assert.AreEqual(new DateTime(2019, 3, 28), test["date"]);
            var ldt = new DateTime(2019, 3, 28, 23, 27, 4, 314);
            var offset = new TimeSpan(0, 5, 30, 0);
            Assert.AreEqual(new DateTimeOffset(ldt, offset), test["date_time"]);
            offset = new TimeSpan(0, -5, -30, 0);
            Assert.AreEqual(new DateTimeOffset(ldt, offset), test["neg_offset_time"]);
            Assert.AreEqual(new DateTime(2019, 3, 28, 23, 27, 4, 272), test["alt_date_time"]);
            ldt = new DateTime(2019, 3, 28, 23, 27, 4);
            Assert.AreEqual(ldt, test["no_ms_time"]);
            Assert.AreEqual(3.3, test["computed"]);
            Assert.AreEqual(2.7, test["computed2"]);
            Assert.AreEqual(0.9, (double) test["computed3"], 1.0e-7);
            Assert.AreEqual(10.0, test["computed4"]);
            var dummy = (Config) config["base"];
            var exp2 = new object[]
            {
                "derived_foo", "derived_bar", "derived_baz",
                "test_foo", "test_bar", "test_baz",
                "base_foo", "base_bar", "base_baz"
            };
            CompareArrays(exp2, ToObjectArray(config["combined_list"]));
            var emap = new Mapping()
            {
                { "foo_key", "base_foo" },
                { "bar_key", "base_bar" },
                { "baz_key", "base_baz" },
                { "base_foo_key", "base_foo" },
                { "base_bar_key", "base_bar" },
                { "base_baz_key", "base_baz" },
                { "derived_foo_key", "derived_foo" },
                { "derived_bar_key", "derived_bar" },
                { "derived_baz_key", "derived_baz" },
                { "test_foo_key", "test_foo" },
                { "test_bar_key", "test_bar" },
                { "test_baz_key", "test_baz" }
            };
            CompareObjects(emap, config["combined_map_1"]);
            emap = new Mapping()
            {
                { "derived_foo_key", "derived_foo" },
                { "derived_bar_key", "derived_bar" },
                { "derived_baz_key", "derived_baz" }
            };
            CompareObjects(emap, config["combined_map_2"]);
            long l1 = (long) config["number_1"];
            long l2 = (long) config["number_2"];
            Assert.AreEqual(l1 & l2, config["number_3"]);
            Assert.AreEqual(l1 ^ l2, config["number_4"]);
            Tuple<string, string>[] cases = new Tuple<string, string>[]
            {
                new Tuple<string, string>("logging[4]", "string required, but found 4"),
                new Tuple<string, string>("logging[:4]", "slices can only operate on lists")
            };
            foreach (var c in cases)
            {
                try
                {
                    Assert.IsNotNull(config[c.Item1]);
                    Assert.Fail("Expected exception wasn't thrown");
                }
                catch (ConfigException ce)
                {
                    Assert.IsTrue(ce.ToString().Contains(c.Item2));
                }
            }
        }

        [TestMethod]
        public void ExampleConfig()
        {
            var config = new Config();
            var p = DataFilePath("derived", "example.cfg");

            config.IncludePath.Add(DataFilePath("base"));
            config.LoadFile(p);

            Assert.AreEqual(config["snowman_escaped"], config["snowman_unescaped"]);
            Assert.AreEqual("\u2603", config["snowman_escaped"]);
            Assert.AreEqual("\ud83d\ude02", config["face_with_tears_of_joy"]);
            Assert.AreEqual("\ud83d\ude02", config["unescaped_face_with_tears_of_joy"]);
            var strings = (ISequence) config["strings"];
            Assert.AreEqual("Oscar Fingal O'Flahertie Wills Wilde", strings[0]);
            Assert.AreEqual("size: 5\"", strings[1]);
#if Windows
            Assert.AreEqual("Triple quoted form\r\ncan span\r\n'multiple' lines", strings[2]);
            Assert.AreEqual("with \"either\"\r\nkind of 'quote' embedded within", strings[3]);
#else
            Assert.AreEqual("Triple quoted form\ncan span\n'multiple' lines", strings[2]);
            Assert.AreEqual("with \"either\"\nkind of 'quote' embedded within", strings[3]);
#endif
            // special strings
            Assert.AreEqual(FileAccess.ReadWrite, config["special_value_1"]);
            Assert.AreEqual(Environment.GetEnvironmentVariable("HOME"), config["special_value_2"]);
            var dt = (DateTimeOffset) config["special_value_3"];
            Assert.AreEqual(new DateTime(2019, 3, 28, 23, 27, 4, 314), dt.DateTime);
            Assert.AreEqual(new TimeSpan(5, 30, 0), dt.Offset);
            Assert.AreEqual("bar", config["special_value_4"]);
            Assert.AreEqual(DateTime.Today, config["special_value_5"]);
            Assert.AreEqual(Assembly.Load("RedDove.Config"), config["special_value_6"]);

            // integers
            Assert.AreEqual(123L, config["decimal_integer"]);
            Assert.AreEqual(0x123L, config["hexadecimal_integer"]);
            Assert.AreEqual(83L, config["octal_integer"]);
            Assert.AreEqual(291L, config["binary_integer"]);

            // floats
            Assert.AreEqual(123.456, config["common_or_garden"]);
            Assert.AreEqual(0.123, config["leading_zero_not_needed"]);
            Assert.AreEqual(123.0, config["trailing_zero_not_needed"]);
            Assert.AreEqual(1.0e6, config["scientific_large"]);
            Assert.AreEqual(1.0e-7, config["scientific_small"]);
            Assert.AreEqual(3.14159, config["expression_1"]);
            Assert.AreEqual(new Complex(3.0, 2.0), config["expression_2"]);

            // complex
            Assert.AreEqual(new Complex(1.0, 3.0), config["list_value[4]"]);

            // boolean
            Assert.IsTrue((bool) config["boolean_value"]);
            Assert.IsFalse((bool) config["opposite_boolean_value"]);
            Assert.IsFalse((bool) config["computed_boolean_2"]);
            Assert.IsTrue((bool) config["computed_boolean_1"]);

            // list
            object[] exp1 = new object[] { "a", "b", "c" };
            CompareArrays(exp1, ToObjectArray(config["incl_list"]));

            // mapping
            var exp2 = new Mapping()
            {
                { "bar", "baz" },
                { "foo", "bar" }
            };
            CompareObjects(exp2, ((Config) config["incl_mapping"]).AsDict());
            exp2 = new Mapping()
            {
                { "baz", "bozz" },
                { "fizz", "buzz" }
            };
            CompareObjects(exp2, ((Config) config["incl_mapping_body"]).AsDict());
        }

        [TestMethod]
        public void Context()
        {
            var config = new Config();
            var p = DataFilePath("derived", "context.cfg");
            config.Context = new Mapping()
            {
                { "bozz", "bozz-bozz" }
            };
            config.LoadFile(p);
            Assert.AreEqual("bozz-bozz", config["baz"]);
            try
            {
                var dummy = config["bad"];
                Assert.Fail("Excepted exception wasn't thrown");
            }
            catch (ConfigException e)
            {
                Assert.AreEqual("Unknown variable 'not_there'", e.Message);
            }
        }

        private void CheckSamePaths(string expected, string actual)
        {
            var p1 = Path.GetFullPath(expected);
            var p2 = Path.GetFullPath(actual);

            Assert.AreEqual(p1, p2);
        }

        private Config FromPath(string path)
        {
            var result = new Config(path);

            CheckSamePaths(path, result.Path);
            var d = Path.GetDirectoryName(Path.GetFullPath(path));
            CheckSamePaths(d, result.RootDir);
            return result;
        }

        [TestMethod]
        public void Expressions()
        {
            var p = DataFilePath("derived", "test.cfg");
            var config = FromPath(p);

            Mapping exp1 = new Mapping()
            {
                {"a", "b" },
                {"c", "d" }
            };
            CompareObjects(exp1, config["dicts_added"]);
            exp1 = new Mapping()
            {
                {"a", new Mapping() { { "b", "c" }, { "w", "x" } } },
                {"d", new Mapping() { { "e", "f" }, { "y", "z" } } }
            };
            CompareObjects(exp1, config["nested_dicts_added"]);
            var exp2 = new object[] { "a", 1L, "b", 2L };
            CompareArrays(exp2, ToObjectArray(config["lists_added"]));
            exp2 = new object[] { 1L, 2L };
            CompareArrays(exp2, ToObjectArray(config["list[:2]"]));
            exp1 = new Mapping() { { "a", "b" } };
            CompareObjects(exp1, config["dicts_subtracted"]);
            CompareObjects(new Mapping(), config["nested_dicts_subtracted"]);
            exp1 = new Mapping()
            {
                { "a_list", new Sequence() { 1L, 2L, new Mapping() { {  "a", 3L } } } },
                { "a_map", new Mapping() { { "k1", new Sequence() { "b", "c", new Mapping() { { "d", "e" } } } } } }
            };
            CompareObjects(exp1, config["dict_with_nested_stuff"]);
            CompareObjects(new Sequence() { 1L, 2L }, config["dict_with_nested_stuff.a_list[:2]"]);
            Assert.AreEqual(-4L, config["unary"]);
            Assert.AreEqual("mno", config["abcdefghijkl"]);
            Assert.AreEqual(8L, config["power"]);
            Assert.AreEqual(2.5, config["computed5"]);
            Assert.AreEqual(2L, config["computed6"]);
            Assert.AreEqual(new Complex(3, 1), config["c3"]);
            Assert.AreEqual(new Complex(5, 5), config["c4"]);
            Assert.AreEqual(2L, config["computed8"]);
            Assert.AreEqual(160L, config["computed9"]);
            Assert.AreEqual(62L, config["computed10"]);
            Assert.AreEqual("A-4 a test_foo True 10 1E-07 1 b [a, c, e, g]Z", config["interp"]);
            Assert.AreEqual("{a: b}", config["interp2"]);

            // failure cases

            var cases = new[]
            {
                new Tuple<string, string>("computed7", "Not found in configuration: float4"),
                new Tuple<string, string>("bad_include", "@ operand must be a string"),
                new Tuple<string, string>("bad_interp", "Unable to convert string "),
            };

            foreach (var c in cases)
            {
                try
                {
                    Assert.IsNotNull(config[c.Item1]);
                    Assert.Fail("expected exception not thrown");
                }
                catch (ConfigException ce)
                {
                    Assert.IsTrue(ce.ToString().Contains(c.Item2));
                }
            }
        }

        [TestMethod]
        public void Forms()
        {
            var config = new Config();
            var p = DataFilePath("derived", "forms.cfg");

            config.IncludePath.Add(DataFilePath("base"));
            config.LoadFile(p);

            var d = (Mapping) config["modals.deletion.contents[0]"];
            Assert.AreEqual("frm-deletion", d["id"]);
            var cases = new List<Tuple<string, Mapping>>()
            {
                new Tuple<string, Mapping>("refs.delivery_address_field", new Mapping() {
                    { "kind", "field" },
                    { "type", "textarea" },
                    { "name", "postal_address" },
                    { "label", "Postal address" },
                    { "label_i18n", "postal-address" },
                    { "short_name", "address" },
                    { "placeholder", "We need this for delivering to you" },
                    { "ph_i18n", "your-postal-address" },
                    { "message", " " },
                    { "required", true },
                    { "attrs", new Mapping() { { "minlength", 10L } } },
                    { "grpclass", "col-md-6" },
                }),
                new Tuple<string, Mapping>("refs.delivery_instructions_field", new Mapping() {
                    { "kind", "field" },
                    { "type", "textarea" },
                    { "name", "delivery_instructions" },
                    { "label", "Delivery Instructions" },
                    { "short_name", "notes" },
                    { "placeholder", "Any special delivery instructions?" },
                    { "message", " " },
                    { "label_i18n", "delivery-instructions" },
                    { "ph_i18n", "any-special-delivery-instructions" },
                    { "grpclass", "col-md-6" }
                }),
                new Tuple<string, Mapping>("refs.verify_field", new Mapping() {
                    { "kind",  "field" },
                    { "type",  "input" },
                    { "name",  "verification_code" },
                    { "label",  "Verification code" },
                    { "label_i18n",  "verification-code" },
                    { "short_name",  "verification code" },
                    { "placeholder",  "Your verification code (NOT a backup code)" },
                    { "ph_i18n",  "verification-not-backup-code" },
                    { "attrs", new Mapping() {
                        { "minlength",  6L },
                        { "maxlength",  6L },
                        { "autofocus",  true } } },
                    { "append",  new Mapping() {
                        { "label",  "Verify" },
                        { "type",  "submit" },
                        { "classes",  "btn-primary" } } },
                    { "message",  " " },
                    { "required",  true }
                }),
                new Tuple<string, Mapping>("refs.signup_password_field", new Mapping() {
                    { "kind", "field" },
                    { "type", "password" },
                    { "label", "Password" },
                    { "label_i18n", "password" },
                    { "message", " " },
                    { "name", "password" },
                    { "ph_i18n", "password-wanted-on-site" },
                    { "placeholder", "The password you want to use on this site" },
                    { "required", true },
                    { "toggle", true }
                }),
                new Tuple<string, Mapping>("refs.signup_password_conf_field", new Mapping() {
                    { "kind", "field" },
                    { "type", "password" },
                    { "name", "password_conf" },
                    { "label", "Password confirmation" },
                    { "label_i18n", "password-confirmation" },
                    { "placeholder", "The same password, again, to guard against mistyping" },
                    { "ph_i18n", "same-password-again" },
                    { "message", " " },
                    { "toggle", true },
                    { "required", true }
                }),
                new Tuple<string, Mapping>("fieldsets.signup_ident[0].contents[0]", new Mapping() {
                    { "kind", "field" },
                    { "type", "input" },
                    { "name", "display_name" },
                    { "label", "Your name" },
                    { "label_i18n", "your-name" },
                    { "placeholder", "Your full name" },
                    { "ph_i18n", "your-full-name" },
                    { "message", " " },
                    { "data_source", "user.display_name" },
                    { "required", true },
                    { "attrs",  new Mapping() { { "autofocus", true } } },
                    { "grpclass",  "col-md-6" }
                }),
                new Tuple<string, Mapping>("fieldsets.signup_ident[0].contents[1]", new Mapping() {
                    { "kind", "field" },
                    { "type", "input" },
                    { "name", "familiar_name" },
                    { "label", "Familiar name" },
                    { "label_i18n", "familiar-name" },
                    { "placeholder", "If not just the first word in your full name" },
                    { "ph_i18n", "if-not-first-word" },
                    { "data_source", "user.familiar_name" },
                    { "message", " " },
                    { "grpclass", "col-md-6" }
                }),
                new Tuple<string, Mapping>("fieldsets.signup_ident[1].contents[0]", new Mapping() {
                    { "kind", "field" },
                    { "type", "email" },
                    { "name", "email" },
                    { "label", "Email address (used to sign in)" },
                    { "label_i18n", "email-address" },
                    { "short_name", "email address" },
                    { "placeholder", "Your email address" },
                    { "ph_i18n", "your-email-address" },
                    { "message", " " },
                    { "required", true },
                    { "data_source", "user.email" },
                    { "grpclass", "col-md-6" }
                }),
                new Tuple<string, Mapping>("fieldsets.signup_ident[1].contents[1]", new Mapping() {
                    { "kind", "field" },
                    { "type", "input" },
                    { "name", "mobile_phone" },
                    { "label", "Phone number" },
                    { "label_i18n", "phone-number" },
                    { "short_name", "phone number" },
                    { "placeholder", "Your phone number" },
                    { "ph_i18n", "your-phone-number" },
                    { "classes", "numeric" },
                    { "message", " " },
                    { "prepend", new Mapping() { { "icon", "phone" } } },
                    { "attrs", new Mapping() { { "maxlength", 10L } } },
                    { "required", true },
                    { "data_source", "customer.mobile_phone" },
                    { "grpclass", "col-md-6" }
                }),
            };

            foreach (var t in cases)
            {
                var actual = config[t.Item1] as Mapping;

                CompareMappings(t.Item2, actual);
            }
        }

        [TestMethod]
        public void PathAcrossIncludes()
        {
            var p = DataFilePath("base", "main.cfg");
            var config = FromPath(p);

            var e1 = typeof(log4net.Appender.FileAppender);
            Assert.AreEqual(e1, config["logging.appenders.file.class"]);
            Assert.AreEqual("run/server.log", config["logging.appenders.file.filename"]);
            Assert.IsTrue((bool) config["logging.appenders.file.append"]);
            Assert.AreEqual(e1, config["logging.appenders.error.class"]);
            Assert.AreEqual("run/server-errors.log", config["logging.appenders.error.filename"]);
            Assert.IsFalse((bool) config["logging.appenders.error.append"]);
            Assert.AreEqual("https://freeotp.github.io/", config["redirects.freeotp.url"]);
            Assert.IsFalse((bool) config["redirects.freeotp.permanent"]);
        }

        [TestMethod]
        public void Sources()
        {
            var sources = new[]
            {
                "foo[::2]",
                "foo[:]",
                "foo[:2]",
                "foo[2:]",
                "foo[::1]",
                "foo[::-1]"
            };

            foreach (var s in sources)
            {
                var node = Config.ParsePath(s);
                var ts = Config.ToSource(node);

                Assert.AreEqual(s, ts);
            }
        }

        [TestMethod]
        public void BadConversions()
        {
            var sources = new[]
            {
                "foo"
            };
            var config = new Config();

            foreach (var c in sources)
            {
                config.StrictConversions = true;
                try
                {
                    config.ConvertString(c);
                    Assert.Fail("Expected exception wasn't thrown");
                }
                catch (ConfigException ce)
                {
                    var m = $"Unable to convert string {c}";
                    Assert.IsTrue(ce.ToString().Contains(m));
                }
                config.StrictConversions = false;
                var s = config.ConvertString(c);
                Assert.AreEqual(c, s);
            }
        }

        [TestMethod]
        public void CircularReferences()
        {
            var p = DataFilePath("derived", "test.cfg");
            var config = FromPath(p);

            Tuple<string, string>[] cases = new Tuple<string, string>[]
            {
                new Tuple<string, string>("circ_list[1]", "Circular reference: circ_list[1] (42, 7)"),
                new Tuple<string, string>("circ_map.a", "Circular reference: circ_map.a (49, 10), circ_map.b (47, 10), circ_map.c (48, 10)"),
            };

            foreach (var c in cases)
            {
                try
                {
                    var dummy = config[c.Item1];
                    Assert.Fail("expected exception wasn't thrown");
                }
                catch (CircularReferenceException cre)
                {
                    Assert.AreEqual(c.Item2, cre.Message);
                }
            }
        }

        [TestMethod]
        public void Caching()
        {
            var p = DataFilePath("derived", "test.cfg");
            var config = FromPath(p);
            Assert.IsFalse(config.Cached);
            config.Cached = true;
            var v1 = config["time_now"];
            Thread.Sleep(50);
            var v2 = config["time_now"];
            Assert.AreEqual(v1, v2);
            config.Cached = false;
            var v3 = config["time_now"];
            Thread.Sleep(50);
            var v4 = config["time_now"];
            Assert.AreNotEqual(v3, v4);
            Assert.AreNotEqual(v3, v2);
        }

        [TestMethod]
        public void SlicesAndIndices()
        {
            var p = DataFilePath("derived", "test.cfg");
            var config = FromPath(p);
            var theList = new Sequence()
            {
                "a", "b", "c", "d", "e", "f", "g"
            };
            int n = theList.Count;
            var revList = new Sequence(theList);
            revList.Reverse();

            // slices

            Tuple<string, Sequence>[] cases = new Tuple<string, Sequence>[]
            {
                new Tuple<string, Sequence>("test_list[:]", theList),
                new Tuple<string, Sequence>("test_list[::]", theList),
                new Tuple<string, Sequence>("test_list[:20]", theList),
                new Tuple<string, Sequence>("test_list[-20:4]", theList.GetRange(0, 4)),
                new Tuple<string, Sequence>("test_list[-20:20]", theList),
                new Tuple<string, Sequence>("test_list[2:]", theList.GetRange(2, n - 2)),
                new Tuple<string, Sequence>("test_list[-3:]", theList.GetRange(n - 3, 3)),
                new Tuple<string, Sequence>("test_list[-2:2:-1]", new Sequence() {"f", "e", "d" }),
                new Tuple<string, Sequence>("test_list[::-1]", revList),
                new Tuple<string, Sequence>("test_list[2:-2:2]", new Sequence() {"c", "e" }),
                new Tuple<string, Sequence>("test_list[::2]", new Sequence() {"a", "c", "e", "g" }),
                new Tuple<string, Sequence>("test_list[::3]", new Sequence() {"a", "d", "g" }),
                new Tuple<string, Sequence>("test_list[::2][::3]", new Sequence() {"a", "g" }),
            };

            foreach (var c in cases)
            {
                CompareObjects(c.Item2, config[c.Item1]);
            }

            // indices

            for (int i = 0; i < n; i++)
            {
                var key = $"test_list[{i}]";
                Assert.AreEqual(theList[i], config[key]);
            }

            // negative indices

            for (int i = n; i > 0; i--)
            {
                var key = $"test_list[-{i}]";
                Assert.AreEqual(theList[n - i], config[key]);
            }

            // bad indices

            int[] badIndices = new int[] { n, n + 1, -(n + 1), -(n + 2) };

            foreach (var i in badIndices)
            {
                var key = $"test_list[{i}]";

                try
                {
                    Assert.IsNotNull(config[key]);
                    Assert.Fail("Expected exception wasn't thrown");
                }
                catch (BadIndexException bie)
                {
                    Assert.IsTrue(bie.ToString().Contains("index out of range: "));
                }
            }
        }

        [TestMethod]
        public void IncludePaths()
        {
            var p1 = DataFilePath("derived", "test.cfg");
            var p2 = Path.GetFullPath(p1);
            var plist = new [] {p1, p2};

            foreach (var p in plist)
            {
                var source = $"test: @ '{p.Replace('\\', '/')}'";
                var reader = new StringReader(source);
                var cfg = new Config(reader);

                Assert.AreEqual(cfg["test.computed6"], 2L);
            }
        }

        [TestMethod]
        public void NestedIncludePath()
        {
            var p = DataFilePath("base", "top.cfg");
            var cfg = FromPath(p);

            cfg.IncludePath.Add(DataFilePath("derived"));
            cfg.IncludePath.Add(DataFilePath("another"));
            Assert.AreEqual(42L, cfg["level1.level2.final"]);
        }

        [TestMethod]
        public void TestRecursiveConfiguration()
        {
            var p = DataFilePath("derived", "recurse.cfg");
            var cfg = FromPath(p);

            try
            {
                var v = cfg["recurse"];
                Assert.Fail("Expected exception wasn't thrown");
            }
            catch (ConfigException e)
            {
                Assert.AreEqual(e.Message, "Configuration cannot include itself: recurse.cfg");
            }
        }
    }
}
