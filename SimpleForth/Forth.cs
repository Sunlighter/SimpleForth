using System;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using System.IO;
using System.Runtime.InteropServices;

namespace SimpleForth
{
    public class Forth
    {
        private ForthStack<object?> dStack;
        private ForthStack<object?> aStack;
        private ForthStack<int> rStack;

        private ForthStack<Vocabulary?> searchOrder;
        private Vocabulary definitions;

        private ForthDictionaryEntry? SearchVocabularies(string name)
        {
            int iEnd = searchOrder.Depth;
            for (int i = 0; i < iEnd; ++i)
            {
                Dictionary<string, ForthDictionaryEntry> dict = searchOrder[i].AssertNotNull().Dict;
                if (dict.ContainsKey(name)) return dict[name];
            }
            return null;
        }

        public void Define(string name, ExecutionToken xt)
        {
            Dictionary<string, ForthDictionaryEntry> dict = definitions.Dict;
            if (dict.ContainsKey(name)) dict.Remove(name);
            dict.Add(name, new ForthDictionaryEntry(name, xt, false));
            lastWordCompiled = name;
        }

        private void Define(string name, Vocabulary vocabulary)
        {
            Dictionary<string, ForthDictionaryEntry> dict = definitions.Dict;
            if (dict.ContainsKey(name)) dict.Remove(name);
            dict.Add(name, new ForthDictionaryEntry(vocabulary));
            lastWordCompiled = name;
        }

        private void LoadResourceStream(string name)
        {
            using (Stream s = typeof(Forth).Assembly.GetManifestResourceStream(name).AssertNotNull())
            {
                StreamReader sr = new StreamReader(s);
                int lineNumber = 1;
                while (true)
                {
                    string? line = sr.ReadLine();
                    if (line == null) break;
                    try
                    {
                        Execute(line);
                    }
                    catch (Exception exc)
                    {
                        throw new Exception("Error in line " + lineNumber, exc);
                    }
                    ++lineNumber;
                }
            }
        }

        public void LoadX86Asm()
        {
            LoadResourceStream("SimpleForth.x86asm.txt");
        }

        private int numericBase;

        private readonly object?[] memory;
        private int here;

        private bool isCompiling;

        public bool IsCompiling { get { return isCompiling; } }
        
        private string? lastWordCompiled; // or, if (isCompiling == false), word last compiled or created

        private ForthStack<CompileState?> compileStack;
        private ForthStack<LoopDeDoo?> loopStack;

        private GetWordProc? getWord;

        private string GetWord(char delim) => getWord.AssertNotNull()(delim).AssertNotNull();

        private delegate void SemicolonProc(CompositeWord cw);

        private class CompileState
        {
            private CompositeWord compositeWord;
            private SemicolonProc semicolonProc;

            public CompileState(CompositeWord compositeWord, SemicolonProc semicolonProc)
            {
                this.compositeWord = compositeWord;
                this.semicolonProc = semicolonProc;
            }

            public CompositeWord CompositeWord { get { return compositeWord; } }
            public SemicolonProc SemicolonProc { get { return semicolonProc; } }
        }

        private class ForthDictionaryEntry
        {
            private string name;
            private ExecutionToken proc;
            private bool isImmediate;
            private Vocabulary? vocabulary;

            public ForthDictionaryEntry(string name, ExecutionToken proc, bool isImmediate)
            {
                this.name = name;
                this.proc = proc;
                this.isImmediate = isImmediate;
                this.vocabulary = null;
            }

            public ForthDictionaryEntry(Vocabulary vocabulary)
            {
                this.name = vocabulary.Name;
                this.vocabulary = vocabulary;
                this.proc = delegate(Forth f) { f.searchOrder.Top = this.vocabulary; };
                this.isImmediate = false;
            }

            public string Name { get { return name; } }

            public ExecutionToken Proc { get { return proc; } set { proc = value; } }

            public bool IsImmediate { get { return isImmediate; } set { isImmediate = value; } }

            public Vocabulary? Vocabulary { get { return vocabulary; } }
        }

        private class Vocabulary
        {
            private string name;
            private Dictionary<string, ForthDictionaryEntry> dict;

            public Vocabulary(string name)
            {
                this.name = name;
                this.dict = new Dictionary<string, ForthDictionaryEntry>();
            }

            public string Name { get { return name; } }
            public Dictionary<string, ForthDictionaryEntry> Dict { get { return dict; } }
        }

        private CompositeWord CodeBeingCompiled { get { return compileStack.Top.AssertNotNull().CompositeWord; } }

        private static bool Within(long lo, long medium, long hi)
        {
            if (lo <= medium && medium < hi) return true;
            if (medium < hi && hi < lo) return true;
            if (hi < lo && lo <= medium) return true;
            return false;
        }

        private class LoopDeDoo
        {
            private long loopCounter;
            private long loopCountEnd;
            private int loopCodeBegin;
            private int loopCodeEnd;

            public LoopDeDoo(long loopCounter, long loopCountEnd, int loopCodeBegin, int loopCodeEnd)
            {
                this.loopCounter = loopCounter;
                this.loopCountEnd = loopCountEnd;
                this.loopCodeBegin = loopCodeBegin;
                this.loopCodeEnd = loopCodeEnd;
            }

            public long LoopCounter { get { return loopCounter; } }
            public long LoopCountEnd { get { return loopCountEnd; } }
            public int LoopCodeBegin { get { return loopCodeBegin; } }
            public int LoopCodeEnd { get { return loopCodeEnd; } }

            public bool IncreaseLoopCounter(long amount)
            {
                long newLoopCounter = loopCounter + amount;
                bool result = Within(loopCounter, loopCountEnd - 1, newLoopCounter);
                loopCounter = newLoopCounter;
                return result;
            }

            public bool DecreaseLoopCounter(long amount)
            {
                long newLoopCounter = loopCounter - amount;
                bool result = Within(newLoopCounter, loopCountEnd, LoopCounter);
                loopCounter = newLoopCounter;
                return result;
            }
        }

        public void PushBool(bool b)
        {
            dStack.Push(b ? -1L : 0L);
        }

        public bool PopBool()
        {
            object? obj = dStack.Pop();
            if (obj is long)
            {
                long l = (long)obj;
                return (l != 0);
            }
            else return true;
        }

        public void PushInt64(long l)
        {
            dStack.Push(l);
        }

        public long PopInt64()
        {
            object? obj = dStack.Pop();
            if (obj is long L)
            {
                return L;
            }
            else
            {
                throw new InvalidCastException("Object at top of stack was not a long");
            }
        }

        public void PushUInt64(ulong u)
        {
            dStack.Push(unchecked((long)u));
        }

        public ulong PopUInt64()
        {
            object? obj = dStack.Pop();
            if (obj is long l)
            {
                return unchecked((ulong)l);
            }
            else
            {
                throw new InvalidCastException("Object at top of stack was not a long");
            }
        }

        public void PushExecutionToken(ExecutionToken et)
        {
            dStack.Push(et);
        }

        public ExecutionToken PopExecutionToken()
        {
            object? obj = dStack.Pop();
            if (obj is ExecutionToken xt)
            {
                return xt;
            }
            else
            {
                throw new Exception("PopExecutionToken: item on top of stack was not an ExecutionToken");
            }
        }

        public void PushBytes(byte[] b)
        {
            dStack.Push(b);
        }

        public byte[] PopByteArray()
        {
            object? val = dStack.Pop();
            if (val is byte[] b)
            {
                return b;
            }
            else
            {
                throw new Exception("PopByteArray: item on top of stack was not a byte array");
            }
        }

        public Forth()
        {
            dStack = new ForthStack<object?>(null);
            aStack = new ForthStack<object?>(null);
            rStack = new ForthStack<int>(0);

            searchOrder = new ForthStack<Vocabulary?>(null);
            definitions = new Vocabulary("forth");
            definitions.Dict.Add("forth", new ForthDictionaryEntry(definitions));
            searchOrder.Push(definitions);

            compileStack = new ForthStack<CompileState?>(null);

            loopStack = new ForthStack<LoopDeDoo?>(null);
            numericBase = 10;

            memory = new object[1024];
            here = 0;

            isCompiling = false;

            // populate initial dictionary

            Dictionary<string, ForthDictionaryEntry> dict = definitions.Dict;

            foreach (MethodInfo mi in typeof(Forth).GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static))
            {
                object[] obj = mi.GetCustomAttributes(typeof(ForthWordAttribute), false);
                if (obj == null || obj.Length != 1 || !(obj[0] is ForthWordAttribute)) continue;
                ParameterInfo[] pInfo = mi.GetParameters();
                if (pInfo.Length != 1) continue;
                if (pInfo[0].ParameterType != typeof(Forth)) continue;
                if (mi.ReturnType != typeof(void)) continue;
                ForthWordAttribute fwa = (ForthWordAttribute)obj[0];
                ForthDictionaryEntry fde = new ForthDictionaryEntry
                (
                    fwa.Name,
                    (ExecutionToken)Delegate.CreateDelegate
                    (
                        typeof(ExecutionToken),
                        mi
                    ),
                    fwa.IsImmediate
                );
                dict.Add(fde.Name, fde);
            }
        }

        private static bool NumericValueUnsafe(char ch, out int i)
        {
            if (ch >= '0' && ch <= '9') { i = (int)ch - (int)'0'; return true; }
            else if (ch >= 'A' && ch <= 'Z') { i = (int)ch - (int)'A' + 10; return true; }
            else if (ch >= 'a' && ch <= 'z') { i = (int)ch - (int)'a' + 10; return true; }
            else { i = 0; return false; }
        }

        public static bool NumericValue(char ch, int numericBase, out int i)
        {
            bool b = NumericValueUnsafe(ch, out i);
            if (!b) return false;
            if (i >= numericBase) return false;
            return true;
        }

        public static bool NumericValue(string number, int numericBase, out long l)
        {
            int posEnd = number.Length;
            if (posEnd == 0) { l = 0L; return false; }
            int pos = 0;
            bool isNegative = false;
            long numericValue = 0L;
            while (pos < posEnd)
            {
                if (pos == 0 && number[pos] == '-')
                {
                    isNegative = true;
                }
                else
                {
                    int digit; bool b = NumericValue(number[pos], numericBase, out digit);
                    if (!b) { l = 0L; return false; }
                    numericValue *= (long)numericBase;
                    numericValue += (long)digit;
                }
                ++pos;
            }
            if (isNegative) numericValue = -numericValue;
            l = numericValue;
            return true;
        }

        public static bool MatchesDelimiter(char test, char delimiter)
        {
            if (delimiter == ' ')
            {
                return char.IsWhiteSpace(test);
            }
            else if (delimiter == '\n')
            {
                return (test == '\r') || (test == '\n');
            }
            else return (test == delimiter);
        }

        public static string? ReadWord(TextReader reader, char delimiter)
        {
            StringBuilder theWord = new StringBuilder();
            while (true)
            {
                int peek = reader.Peek();
                if (peek < 0) return null;
                if (!MatchesDelimiter((char)peek, delimiter)) break;
                reader.Read();
            }
            while (true)
            {
                int peek = reader.Peek();
                if (peek < 0) break;
                if (MatchesDelimiter((char)peek, delimiter))
                {
                    reader.Read();
                    break;
                }
                theWord.Append((char)peek);
                reader.Read();
            }
            return theWord.ToString();
        }

        public void Execute(TextReader tr)
        {
            getWord = delegate(char delimiter)
            {
                return ReadWord(tr, delimiter);
            };

            while (true)
            {
                string? word = ReadWord(tr, ' ');
                if (word == null) break;
                ForthDictionaryEntry? de = SearchVocabularies(word);
                if (de != null)
                {
                    if (isCompiling)
                    {
                        if (de.IsImmediate)
                        {
                            de.Proc(this);
                        }
                        else
                        {
                            AppendCode(de.Proc);
                        }
                    }
                    else
                    {
                        de.Proc(this);
                    }
                }
                else
                {
                    long value; bool b = NumericValue(word, numericBase, out value);
                    if (b)
                    {
                        if (isCompiling)
                        {
                            AppendCode(MakeLiteralOp(value));
                        }
                        else
                        {
                            dStack.Push(value);
                        }
                    }
                    else throw new ArgumentException("Undefined word " + word);
                }
            }
        }

        public void Execute(string cmds)
        {
            StringReader sr = new StringReader(cmds);
            Execute(sr);
        }

        [ForthWord("true")]
        public static void True(Forth f)
        {
            f.PushBool(true);
        }

        [ForthWord("false")]
        public static void False(Forth f)
        {
            f.PushBool(false);
        }

        [ForthWord("null")]
        public static void Null(Forth f)
        {
            f.dStack.Push(null);
        }

        [ForthWord("dup")]
        public static void Dup(Forth f)
        {
            f.dStack.Dup();
        }

        [ForthWord("drop")]
        public static void Drop(Forth f)
        {
            f.dStack.Drop();
        }

        [ForthWord("swap")]
        public static void Swap(Forth f)
        {
            f.dStack.Swap();
        }

        [ForthWord("over")]
        public static void Over(Forth f)
        {
            f.dStack.Over();
        }

        [ForthWord("nip")]
        public static void Nip(Forth f)
        {
            f.dStack.Nip();
        }

        [ForthWord("tuck")]
        public static void Tuck(Forth f)
        {
            f.dStack.Tuck();
        }

        [ForthWord("swapstacks")]
        public static void SwapStacks(Forth f)
        {
            ForthStack<object?> temp = f.dStack;
            f.dStack = f.aStack;
            f.aStack = temp;
        }

        [ForthWord(">a")]
        public static void ToAux(Forth f)
        {
            f.aStack.Push(f.dStack.Pop());
        }

        [ForthWord("a>")]
        public static void FromAux(Forth f)
        {
            f.dStack.Push(f.aStack.Pop());
        }

        [ForthWord("a@")]
        public static void AuxAt(Forth f)
        {
            f.dStack.Push(f.aStack.Top);
        }

        [ForthWord("dup>a")]
        public static void DupToAux(Forth f)
        {
            f.aStack.Push(f.dStack.Top);
        }

        [ForthWord("a>drop")]
        public static void AuxToDrop(Forth f)
        {
            f.aStack.Drop();
        }

        [ForthWord("depth")]
        public static void Depth(Forth f)
        {
            f.PushInt64((long)(f.dStack.Depth));
        }

        [ForthWord("alloc")]
        public static void Alloc(Forth f)
        {
            long count = f.PopInt64();
            if (count > 256 || count < 0) throw new ArgumentException("alloc too much");
            f.dStack.Alloc((int)count);
        }

        [ForthWord("free")]
        public static void Free(Forth f)
        {
            long count = f.PopInt64();
            if (count > 256 || count < 0) throw new ArgumentException("free too much");
            f.dStack.Free((int)count);
        }

        [ForthWord("pick")]
        public static void Pick(Forth f)
        {
            long pos = f.PopInt64();
            if (pos >= 256 || pos < 0) throw new ArgumentException("pick too far");
            f.dStack.Push(f.dStack[(int)pos]);
        }

        [ForthWord("pick!")]
        public static void PickBang(Forth f)
        {
            long pos = f.PopInt64();
            if (pos >= 256 || pos < 0) throw new ArgumentException("pick! too far");
            object? v = f.dStack.Pop();
            f.dStack[(int)pos] = v;
        }

        [ForthWord("adepth")]
        public static void AuxDepth(Forth f)
        {
            f.PushInt64((long)(f.aStack.Depth));
        }

        [ForthWord("aalloc")]
        public static void AuxAlloc(Forth f)
        {
            long count = f.PopInt64();
            if (count > 256 || count < 0) throw new ArgumentException("aalloc too much");
            f.aStack.Alloc((int)count);
        }

        [ForthWord("afree")]
        public static void AuxFree(Forth f)
        {
            long count = f.PopInt64();
            if (count > 256 || count < 0) throw new ArgumentException("afree too much");
            f.aStack.Free((int)count);
        }

        [ForthWord("apick")]
        public static void AuxPick(Forth f)
        {
            long pos = f.PopInt64();
            if (pos >= 256 || pos < 0) throw new ArgumentException("apick too far");
            f.dStack.Push(f.aStack[(int)pos]);
        }

        [ForthWord("apick!")]
        public static void AuxPickBang(Forth f)
        {
            long pos = f.PopInt64();
            if (pos >= 256 || pos < 0) throw new ArgumentException("apick! too far");
            object? v = f.dStack.Pop();
            f.aStack[(int)pos] = v;
        }

        [ForthWord("invert")]
        public static void Invert(Forth f)
        {
            ulong u = f.PopUInt64();
            f.PushUInt64(~u);
        }

        [ForthWord("negate")]
        public static void Negate(Forth f)
        {
            long l = f.PopInt64();
            f.PushInt64(unchecked(-l));
        }

        [ForthWord("+")]
        public static void Add(Forth f)
        {
            long b = f.PopInt64();
            long a = f.PopInt64();
            f.PushInt64(unchecked(a + b));
        }

        [ForthWord("-")]
        public static void Subtract(Forth f)
        {
            long b = f.PopInt64();
            long a = f.PopInt64();
            f.PushInt64(unchecked(a - b));
        }

        [ForthWord("*")]
        public static void Multiply(Forth f)
        {
            long b = f.PopInt64();
            long a = f.PopInt64();
            f.PushInt64(unchecked(a * b));
        }

        [ForthWord("/")]
        public static void Divide(Forth f)
        {
            long b = f.PopInt64();
            long a = f.PopInt64();
            f.PushInt64(unchecked(a / b));
        }

        [ForthWord("mod")]
        public static void Modulus(Forth f)
        {
            long b = f.PopInt64();
            long a = f.PopInt64();
            f.PushInt64(unchecked(a % b));
        }

        [ForthWord("u/")]
        public static void UnsignedDivide(Forth f)
        {
            ulong b = f.PopUInt64();
            ulong a = f.PopUInt64();
            f.PushUInt64(unchecked(a / b));
        }

        [ForthWord("umod")]
        public static void UnsignedModulus(Forth f)
        {
            ulong b = f.PopUInt64();
            ulong a = f.PopUInt64();
            f.PushUInt64(unchecked(a % b));
        }

        [ForthWord("and")]
        public static void And(Forth f)
        {
            ulong b = f.PopUInt64();
            ulong a = f.PopUInt64();
            f.PushUInt64(a & b);
        }

        [ForthWord("or")]
        public static void Or(Forth f)
        {
            ulong b = f.PopUInt64();
            ulong a = f.PopUInt64();
            f.PushUInt64(a | b);
        }

        [ForthWord("xor")]
        public static void Xor(Forth f)
        {
            ulong b = f.PopUInt64();
            ulong a = f.PopUInt64();
            f.PushUInt64(a ^ b);
        }

        [ForthWord("within")]
        public static void Within(Forth f)
        {
            long end = f.PopInt64();
            long begin = f.PopInt64();
            long mid = f.PopInt64();
            f.PushBool(Within(begin, mid, end));
        }

        [ForthWord("decimal")]
        public static void Decimal(Forth f)
        {
            f.numericBase = 10;
        }

        [ForthWord("hex")]
        public static void Hex(Forth f)
        {
            f.numericBase = 16;
        }

        [ForthWord("=")]
        public static void Equals(Forth f)
        {
            ulong b = f.PopUInt64();
            ulong a = f.PopUInt64();
            f.PushBool(a == b);
        }

        [ForthWord("<>")]
        public static void DoesNotEqual(Forth f)
        {
            ulong b = f.PopUInt64();
            ulong a = f.PopUInt64();
            f.PushBool(a != b);
        }

        [ForthWord("<")]
        public static void IsLessThan(Forth f)
        {
            long b = f.PopInt64();
            long a = f.PopInt64();
            f.PushBool(a < b);
        }

        [ForthWord(">")]
        public static void IsGreaterThan(Forth f)
        {
            long b = f.PopInt64();
            long a = f.PopInt64();
            f.PushBool(a > b);
        }

        [ForthWord("<=")]
        public static void IsNotGreaterThan(Forth f)
        {
            long b = f.PopInt64();
            long a = f.PopInt64();
            f.PushBool(a <= b);
        }

        [ForthWord(">=")]
        public static void IsNotLessThan(Forth f)
        {
            long b = f.PopInt64();
            long a = f.PopInt64();
            f.PushBool(a >= b);
        }

        [ForthWord("u<")]
        public static void IsLessThanUnsigned(Forth f)
        {
            ulong b = f.PopUInt64();
            ulong a = f.PopUInt64();
            f.PushBool(a < b);
        }

        [ForthWord("u>")]
        public static void IsGreaterThanUnsigned(Forth f)
        {
            ulong b = f.PopUInt64();
            ulong a = f.PopUInt64();
            f.PushBool(a > b);
        }

        [ForthWord("u<=")]
        public static void IsNotGreaterThanUnsigned(Forth f)
        {
            ulong b = f.PopUInt64();
            ulong a = f.PopUInt64();
            f.PushBool(a <= b);
        }

        [ForthWord("u>=")]
        public static void IsNotLessThanUnsigned(Forth f)
        {
            ulong b = f.PopUInt64();
            ulong a = f.PopUInt64();
            f.PushBool(a >= b);
        }

        [ForthWord("not")]
        public static void Not(Forth f)
        {
            bool b = f.PopBool();
            f.PushBool(!b);
        }

        [ForthWord(".")]
        public static void Dot(Forth f)
        {
            object? obj = f.dStack.Pop();
            if (obj is null)
            {
                Console.Write("(null)");
            }
            else
            {
                Console.Write(Convert.ToString(obj));
            }
            Console.Write(" ");
        }

        [ForthWord("u.")]
        public static void UnsignedDot(Forth f)
        {
            object? obj = f.dStack.Pop();
            if (obj is long)
            {
                long l = (long)obj;
                ulong u = unchecked((ulong)l);
                obj = u;
            }
            Console.Write(Convert.ToString(obj));
            Console.Write(" ");
        }

        [ForthWord(".\"", IsImmediate = true)]
        public static void DotQuote(Forth f)
        {
            if (f.isCompiling)
            {
                string str = f.GetWord('"');
                f.AppendCode
                (
                    delegate(Forth g)
                    {
                        Console.Write(str);
                    }
                );
            }
            else
            {
                string str = f.GetWord('"');
                Console.Write(str);
            }
        }

        [ForthWord("\\", IsImmediate = true)]
        public static void Backslash(Forth f)
        {
            f.GetWord('\n');
        }

        [ForthWord("(", IsImmediate = true)]
        public static void LParen(Forth f)
        {
            f.GetWord(')');
        }

        [ForthWord("space")]
        public static void Space(Forth f)
        {
            Console.Write(" ");
        }

        [ForthWord("cr")]
        public static void Cr(Forth f)
        {
            Console.WriteLine();
        }

        [ForthWord("null?")]
        public static void IsNull(Forth f)
        {
            bool b = (f.dStack.Top == null);
            f.dStack.Pop();
            f.dStack.Push(b);
        }

        [ForthWord("long?")]
        public static void IsLong(Forth f)
        {
            bool b = (f.dStack.Top is long);
            f.dStack.Pop();
            f.PushBool(b);
        }

        [ForthWord("xt?")]
        public static void IsExecutionToken(Forth f)
        {
            bool b = (f.dStack.Top is ExecutionToken);
            f.dStack.Pop();
            f.PushBool(b);
        }

        [ForthWord("here")]
        public static void Here(Forth f)
        {
            f.PushInt64((long)(f.here));
        }

        [ForthWord("unused")]
        public static void Unused(Forth f)
        {
            f.PushInt64((long)(f.memory.Length - f.here));
        }

        [ForthWord("@")]
        public static void At(Forth f)
        {
            long index = f.PopInt64();
            f.dStack.Push(f.memory[(int)index]);
        }

        [ForthWord("!")]
        public static void Bang(Forth f)
        {
            long index = f.PopInt64();
            object? v = f.dStack.Pop();
            f.memory[(int)index] = v;
        }

        [ForthWord("on")]
        public static void On(Forth f)
        {
            long index = f.PopInt64();
            f.memory[(int)index] = -1L;
        }

        [ForthWord("off")]
        public static void Off(Forth f)
        {
            long index = f.PopInt64();
            f.memory[(int)index] = 0L;
        }

        [ForthWord(",")]
        public static void Comma(Forth f)
        {
            object? v = f.dStack.Pop();
            f.memory[f.here] = v;
            f.here++;
        }

        [ForthWord("create")]
        public static void Create(Forth f)
        {
            string word = f.GetWord(' ');
            f.Define(word, MakeLiteralOp((long)f.here));
        }

        [ForthWord("allot")]
        public static void Allot(Forth f)
        {
            long amt = f.PopInt64();
            if (amt > (long)(f.memory.Length - f.here)) throw new ArgumentOutOfRangeException("Attempt to allot more memory than is available");
            f.here += (int)amt;
        }

        [ForthWord("variable")]
        public static void Variable(Forth f)
        {
            Create(f);
            f.memory[f.here] = null;
            f.here++;
        }

        private class CompositeWord
        {
            private List<ExecutionToken?> components;

            public CompositeWord()
            {
                components = new List<ExecutionToken?>();
            }

            public void Run(Forth f)
            {
                f.rStack.Push(0);
                while(true)
                {
                    int pos = f.rStack.Pop();
                    if (pos < 0 || pos >= components.Count) break;
                    int posNext = pos + 1;
                    f.rStack.Push(posNext);
                    components[pos].AssertNotNull()(f);
                }
            }

            public void Add(ExecutionToken? xt)
            {
                components.Add(xt);
            }

            public void LabelBack(Forth f)
            {
                f.PushInt64((long)Here);
            }

            public void RefBack(Forth f)
            {
                long target = f.PopInt64();
                Add(MakeLiteralOp(target));
            }

            public void RefForward(Forth f)
            {
                f.PushInt64((long)Here);
                Add(null);
            }

            public void LabelForward(Forth f)
            {
                int fixupPos = (int)f.PopInt64();
                components[fixupPos] = MakeLiteralOp((long)Here);
            }

            public int Here { get { return components.Count; } }
        }

        [ForthWord(":")]
        public static void Define(Forth f)
        {
            if (!f.compileStack.IsEmpty) throw new InvalidOperationException("Nested definitions are not permitted");
            f.isCompiling = true;
            string localWordBeingCompiled = f.GetWord(' ');
            f.compileStack.Push
            (
                new CompileState
                (
                    new CompositeWord(),
                    delegate(CompositeWord finishedWord)
                    {
                        f.Define(localWordBeingCompiled, new ExecutionToken(finishedWord.Run));
                    }
                )
            );
        }

        [ForthWord(":noname", IsImmediate = true)]
        public static void DefineNoname(Forth f)
        {
            if (!f.compileStack.IsEmpty) throw new InvalidOperationException("Nested definitions are not permitted");
            f.isCompiling = true;
            f.compileStack.Push
            (
                new CompileState
                (
                    new CompositeWord(),
                    delegate(CompositeWord finishedWord)
                    {
                        f.dStack.Push(new ExecutionToken(finishedWord.Run));
                    }
                )
            );
        }

        [ForthWord("does>", IsImmediate = true)]
        public static void Does(Forth f)
        {
            if (!f.isCompiling) throw new InvalidOperationException("does> is only valid when compiling");
            f.compileStack.Push
            (
                new CompileState
                (
                    new CompositeWord(),
                    delegate(CompositeWord finishedWord)
                    {
                        //System.Diagnostics.Debug.WriteLine("Finished does>");
                        f.AppendCode
                        (
                            delegate(Forth g)
                            {
                                //System.Diagnostics.Debug.WriteLine("Executing does> (setting up " + g.lastWordCompiled + ")");
                                string localLastWordCompiled = g.lastWordCompiled.AssertNotNull();
                                ForthDictionaryEntry fde = g.definitions.Dict[localLastWordCompiled];
                                ExecutionToken oldBehavior = fde.Proc;
                                fde.Proc = delegate(Forth h)
                                {
                                    //System.Diagnostics.Debug.WriteLine("Executing behavior for " + localLastWordCompiled);
                                    oldBehavior(h);
                                    finishedWord.Run(h);
                                };
                            }
                        );
                    }
                )
            );
        }

        public void AppendCode(ExecutionToken xt)
        {
            CodeBeingCompiled.Add(xt);
        }

        [ForthWord(";", IsImmediate = true)]
        public static void EndDefine(Forth f)
        {
            while (!f.compileStack.IsEmpty)
            {
                CompileState compileState = f.compileStack.Pop().AssertNotNull();
                compileState.SemicolonProc(compileState.CompositeWord);
            }
            f.isCompiling = false;
        }

        [ForthWord("immediate")]
        public static void Immediate(Forth f)
        {
            if (f.lastWordCompiled == null) throw new InvalidOperationException("No word has been compiled");
            if (!f.definitions.Dict.ContainsKey(f.lastWordCompiled)) throw new InvalidOperationException("Compiled word not found");
            f.definitions.Dict[f.lastWordCompiled].IsImmediate = true;
        }

        public static void JumpIfFalse(Forth f)
        {
            int target = (int)f.PopInt64();
            bool flag = f.PopBool();
            if (!flag) f.rStack.Top = target;
        }

        public static void Jump(Forth f)
        {
            int target = (int)f.PopInt64();
            f.rStack.Top = target;
        }

        [ForthWord("begin", IsImmediate = true)]
        public static void Begin(Forth f)
        {
            if (!f.isCompiling) throw new InvalidOperationException("begin is only valid when compiling");
            f.CodeBeingCompiled.LabelBack(f);
        }

        public static void Again(Forth f)
        {
            f.CodeBeingCompiled.RefBack(f);
            f.AppendCode(new ExecutionToken(Jump));
        }

        public static void Ahead(Forth f)
        {
            f.CodeBeingCompiled.RefForward(f);
            f.AppendCode(new ExecutionToken(Jump));
        }

        [ForthWord("then", IsImmediate = true)]
        public static void Then(Forth f)
        {
            if (!f.isCompiling) throw new InvalidOperationException("then is only valid when compiling");
            f.CodeBeingCompiled.LabelForward(f);
        }

        [ForthWord("until", IsImmediate = true)]
        public static void Until(Forth f)
        {
            if (!f.isCompiling) throw new InvalidOperationException("until is only valid when compiling");
            f.CodeBeingCompiled.RefBack(f);
            f.AppendCode(new ExecutionToken(JumpIfFalse));
        }

        [ForthWord("if", IsImmediate = true)]
        public static void If(Forth f)
        {
            if (!f.isCompiling) throw new InvalidOperationException("if / while is only valid when compiling");
            f.CodeBeingCompiled.RefForward(f);
            f.AppendCode(new ExecutionToken(JumpIfFalse));
        }

        [ForthWord("else", IsImmediate = true)]
        public static void Else(Forth f)
        {
            if (!f.isCompiling) throw new InvalidOperationException("else is only valid when compiling");
            Ahead(f);
            f.dStack.Swap();
            Then(f);
        }

        [ForthWord("while", IsImmediate = true)]
        public static void While(Forth f)
        {
            If(f);
        }

        [ForthWord("repeat", IsImmediate = true)]
        public static void Repeat(Forth f)
        {
            if (!f.isCompiling) throw new InvalidOperationException("repeat is only valid when compiling");
            f.dStack.Swap();
            Again(f);
            Then(f);
        }

        public static void RuntimeDo(Forth f)
        {
            int endAddr = (int)f.PopInt64();
            long beginCount = f.PopInt64();
            long endCount = f.PopInt64();
            int beginAddr = f.rStack.Top;
            f.loopStack.Push(new LoopDeDoo(beginCount, endCount, beginAddr, endAddr));
        }

        [ForthWord("do", IsImmediate = true)]
        public static void Do(Forth f)
        {
            if (!f.isCompiling) throw new InvalidOperationException("do is only valid when compiling");
            f.CodeBeingCompiled.RefForward(f);
            f.AppendCode(new ExecutionToken(RuntimeDo));
        }

        public static void RuntimeQuestionDo(Forth f)
        {
            int endAddr = (int)f.PopInt64();
            long beginCount = f.PopInt64();
            long endCount = f.PopInt64();
            int beginAddr = f.rStack.Top;
            if (beginCount == endCount)
            {
                f.rStack.Top = endAddr;
            }
            else
            {
                f.loopStack.Push(new LoopDeDoo(beginCount, endCount, beginAddr, endAddr));
            }
        }

        [ForthWord("?do", IsImmediate = true)]
        public static void QuestionDo(Forth f)
        {
            if (!f.isCompiling) throw new InvalidOperationException("?do is only valid when compiling");
            f.CodeBeingCompiled.RefForward(f);
            f.AppendCode(new ExecutionToken(RuntimeQuestionDo));
        }

        public static void RuntimeLoop(Forth f)
        {
            bool shouldStop = f.loopStack.Top.AssertNotNull().IncreaseLoopCounter(1L);
            if (!shouldStop)
            {
                f.rStack.Top = f.loopStack.Top.AssertNotNull().LoopCodeBegin;
            }
        }

        [ForthWord("loop", IsImmediate = true)]
        public static void Loop(Forth f)
        {
            if (!f.isCompiling) throw new InvalidOperationException("loop is only valid when compiling");
            f.AppendCode(new ExecutionToken(RuntimeLoop));
            f.CodeBeingCompiled.LabelForward(f);
        }

        [ForthWord("i", IsImmediate = true)]
        public static void LoopI(Forth f)
        {
            if (!f.isCompiling) throw new InvalidOperationException("i is only valid when compiling");
            f.AppendCode
            (
                delegate(Forth g)
                {
                    g.PushInt64(g.loopStack.Top.AssertNotNull().LoopCounter);
                }
            );
        }

        [ForthWord("j", IsImmediate = true)]
        public static void LoopJ(Forth f)
        {
            if (!f.isCompiling) throw new InvalidOperationException("j is only valid when compiling");
            f.AppendCode
            (
                delegate(Forth g)
                {
                    g.PushInt64(g.loopStack[1].AssertNotNull().LoopCounter);
                }
            );
        }

        [ForthWord("unloop", IsImmediate = true)]
        public static void Unloop(Forth f)
        {
            if (!f.isCompiling) throw new InvalidOperationException("unloop is only valid when compiling");
            f.AppendCode
            (
                delegate(Forth g)
                {
                    g.loopStack.Drop();
                }
            );
        }

        [ForthWord("leave", IsImmediate = true)]
        public static void Leave(Forth f)
        {
            if (!f.isCompiling) throw new InvalidOperationException("leave is only valid when compiling");
            f.AppendCode
            (
                delegate(Forth g)
                {
                    g.rStack.Top = g.loopStack.Top.AssertNotNull().LoopCodeEnd;
                    g.loopStack.Drop();
                }
            );
        }

        public static void RuntimePlusLoop(Forth f)
        {
            long amt = f.PopInt64();
            bool shouldStop;
            if (amt > 0)
            {
                shouldStop = f.loopStack.Top.AssertNotNull().IncreaseLoopCounter(amt);
            }
            else if (amt < 0)
            {
                shouldStop = f.loopStack.Top.AssertNotNull().DecreaseLoopCounter(-amt);
            }
            else
            {
                shouldStop = false;
            }
            if (!shouldStop)
            {
                f.rStack.Top = f.loopStack.Top.AssertNotNull().LoopCodeBegin;
            }
        }

        [ForthWord("+loop", IsImmediate = true)]
        public static void PlusLoop(Forth f)
        {
            if (!f.isCompiling) throw new InvalidOperationException("+loop is only valid when compiling");
            f.AppendCode(new ExecutionToken(RuntimePlusLoop));
            f.CodeBeingCompiled.LabelForward(f);
        }

        public static void RuntimeUPlusLoop(Forth f)
        {
            long amt = f.PopInt64();
            bool shouldStop = f.loopStack.Top.AssertNotNull().IncreaseLoopCounter(amt);
            if (!shouldStop)
            {
                f.rStack.Top = f.loopStack.Top.AssertNotNull().LoopCodeBegin;
            }
        }

        [ForthWord("u+loop", IsImmediate = true)]
        public static void UnsignedPlusLoop(Forth f)
        {
            if (!f.isCompiling) throw new InvalidOperationException("u+loop is only valid when compiling");
            f.AppendCode(new ExecutionToken(RuntimeUPlusLoop));
            f.CodeBeingCompiled.LabelForward(f);
        }

        public static void RuntimeUMinusLoop(Forth f)
        {
            long amt = f.PopInt64();
            bool shouldStop = f.loopStack.Top.AssertNotNull().DecreaseLoopCounter(amt);
            if (!shouldStop)
            {
                f.rStack.Top = f.loopStack.Top.AssertNotNull().LoopCodeBegin;
            }
        }

        [ForthWord("u-loop", IsImmediate = true)]
        public static void UnsignedMinusLoop(Forth f)
        {
            if (!f.isCompiling) throw new InvalidOperationException("u-loop is only valid when compiling");
            f.AppendCode(new ExecutionToken(RuntimeUMinusLoop));
            f.CodeBeingCompiled.LabelForward(f);
        }

        [ForthWord("[", IsImmediate = true)]
        public static void LeftBracket(Forth f)
        {
            f.isCompiling = false;
        }

        [ForthWord("]")]
        public static void RightBracket(Forth f)
        {
            if (f.compileStack.IsEmpty) throw new InvalidOperationException("] can only be used to continue compiling");
            f.isCompiling = true;
        }

        [ForthWord("literal", IsImmediate = true)]
        public static void Literal(Forth f)
        {
            if (!f.isCompiling) throw new InvalidOperationException("literal is only valid when compiling");
            object? value = f.dStack.Pop();
            f.AppendCode(MakeLiteralOp(value));
        }

        public static void PopAndAppendCode(Forth f)
        {
            ExecutionToken xt = f.PopExecutionToken();
            f.AppendCode(xt);
        }

        [ForthWord("postpone", IsImmediate = true)]
        public static void Postpone(Forth f)
        {
            if (!f.isCompiling) throw new InvalidOperationException("postpone is only valid when compiling");
            string word = f.GetWord(' ');
            ForthDictionaryEntry? fde = f.SearchVocabularies(word);
            if (fde == null) throw new InvalidOperationException("attempt to postpone undefined word " + word);
            if (fde.IsImmediate)
            {
                f.AppendCode(fde.Proc);
            }
            else
            {
                f.AppendCode(MakeLiteralOp(fde.Proc));
                f.AppendCode(new ExecutionToken(PopAndAppendCode));
            }
        }

        [ForthWord("'")]
        public static void Tick(Forth f)
        {
            string word = f.GetWord(' ');
            ForthDictionaryEntry? fde = f.SearchVocabularies(word);
            if (fde == null) throw new InvalidOperationException("attempt to tick undefined word " + word);
            f.dStack.Push(fde.Proc);
        }

        [ForthWord("[']")]
        public static void BracketTick(Forth f)
        {
            if (!f.isCompiling) throw new InvalidOperationException("bracket-tick is only valid when compiling");
            string word = f.GetWord(' ');
            ForthDictionaryEntry? fde = f.SearchVocabularies(word);
            if (fde == null) throw new InvalidOperationException("attempt to bracket-tick undefined word " + word);
            f.AppendCode(MakeLiteralOp(fde.Proc));
        }

        [ForthWord("execute")]
        public static void ExecuteToken(Forth f)
        {
            ExecutionToken xt = f.PopExecutionToken();
            xt(f);
        }

        public static ExecutionToken MakeLiteralOp(object? obj)
        {
            return delegate(Forth f)
            {
                f.dStack.Push(obj);
            };
        }

        [ForthWord("only")]
        public static void Only(Forth f)
        {
            f.searchOrder.Free(f.searchOrder.Depth - 1);
        }

        [ForthWord("also")]
        public static void Also(Forth f)
        {
            f.searchOrder.Dup();
        }

        [ForthWord("previous")]
        public static void Previous(Forth f)
        {
            if (f.searchOrder.Depth > 1) f.searchOrder.Drop();
        }

        [ForthWord("definitions")]
        public static void Definitions(Forth f)
        {
            f.definitions = f.searchOrder.Top.AssertNotNull();
        }

        [ForthWord("vocabulary")]
        public static void CreateVocabulary(Forth f)
        {
            string name = f.GetWord(' ');
            Dictionary<string, ForthDictionaryEntry> dict = f.definitions.Dict;
            if (dict.ContainsKey(name)) dict.Remove(name);
            dict.Add(name, new ForthDictionaryEntry(new Vocabulary(name)));
        }

        [ForthWord("bytearray?")]
        public static void IsBytes(Forth f)
        {
            object? obj = f.dStack.Pop();
            f.PushBool(obj is byte[]);
        }

        private static byte[] HexToBytes(string hex)
        {
            List<byte> bList = new List<byte>();
            int iEnd = hex.Length;
            int i = 0;
            while ((i + 1) < iEnd)
            {
                if (i == ',')
                {
                    ++i;
                }
                else
                {
                    int d1; bool b1 = NumericValue(hex[i], 16, out d1);
                    int d2; bool b2 = NumericValue(hex[i + 1], 16, out d2);
                    bList.Add((byte)((d1 << 4) + d2));
                    i += 2;
                }
            }
            return bList.ToArray();
        }

        [ForthWord("pf-bytes", IsImmediate = true)]
        public static void PfBytes(Forth f)
        {
            if (f.isCompiling)
            {
                string hex = f.GetWord(' ');
                f.AppendCode(MakeLiteralOp(HexToBytes(hex)));
            }
            else
            {
                string hex = f.GetWord(' ');
                f.dStack.Push(HexToBytes(hex));
            }
        }

        [ForthWord("bytearray@")]
        public static void ByteArrayAt(Forth f)
        {
            byte[] bArr = f.PopByteArray();
            long index = f.PopInt64();
            if (index < 0 || index > ((long)(bArr.Length))) throw new IndexOutOfRangeException("Index " + index + " must be 0 to " + bArr.Length);
            f.PushInt64((long)(bArr[(int)index]));
        }

        [ForthWord("bytearraysize")]
        public static void ByteArraySize(Forth f)
        {
            byte[] bArr = f.PopByteArray();
            f.PushInt64((long)(bArr.Length));
        }

        [ForthWord("newbytearray")]
        public static void NewByteArray(Forth f)
        {
            long len = f.PopInt64();
            if (len < 0 || len > 0x40000000L) throw new ArgumentOutOfRangeException("Byte arrays larger than 1 GB are not supported by this implementation");
            byte[] bArr = new byte[(int)len];
            f.dStack.Push(bArr);
        }

        private IByteMemory? byteMemory = null;

        public IByteMemory? ByteMemory { get { return byteMemory; } set { byteMemory = value; } }

        private long byteHere = 0;

        public long ByteHere { get { return byteHere; } set { byteHere = value; } }

        [ForthWord("bytememory@")]
        public static void ByteMemoryAt(Forth f)
        {
            f.dStack.Push(f.byteMemory);
        }

        [ForthWord("bytememory!")]
        public static void ByteMemoryBang(Forth f)
        {
            object? obj = f.dStack.Pop();
            if (obj is byte[] b)
            {
                obj = new ByteArrayWrapper(b);
            }

            if (obj is IByteMemory m)
            {
                f.byteMemory = m;
            }
            else
            {
                throw new Exception("Object on top of stack is not a byte memory");
            }
        }

        [ForthWord("realmemory?")]
        public static void IsRealMemory(Forth f)
        {
            object? obj = f.dStack.Pop();
            f.PushBool(obj is MemoryAccessor);
        }

        [ForthWord("realaddress")]
        public static void RealAddress(Forth f)
        {
            object? obj = f.dStack.Pop();
            if (obj is MemoryAccessor ma)
            {
                f.PushInt64(ma.RealAddress);
            }
            else
            {
                throw new InvalidCastException("Top of stack was not a MemoryAccessor");
            }
        }

        private unsafe delegate void ByteArrayProc(byte* offset, int size);

        [ForthWord("crash32")]
        unsafe public static void Crash32(Forth f)
        {
            if (f.byteMemory is MemoryAccessor ma)
            {
                object? brObj = f.dStack.Pop();
                if (brObj is byte[] br)
                { 
                    ByteArrayProc crash = (ByteArrayProc)Marshal.GetDelegateForFunctionPointer((IntPtr)ma.RealAddress, typeof(ByteArrayProc));
                    Console.WriteLine("crash = " + crash);
                    fixed (byte* bPtr = &br[0])
                    {
                        crash(bPtr, br.Length);
                    }
                }
                else
                {
                    throw new InvalidCastException("Top of stack was not a byte array");
                }
            }
            else throw new Exception("Cannot execute a byte array");
        }

        [ForthWord("crash64")]
        unsafe public static void Crash64(Forth f)
        {
        }

        [ForthWord("c@")]
        public static void ByteAt(Forth f)
        {
            long offset = f.PopInt64();
            byte b = f.byteMemory.AssertNotNull()[checked((int)offset)];
            f.PushInt64((long)b);
        }

        [ForthWord("w@")]
        public static void Int16At(Forth f)
        {
            long offset = f.PopInt64();
            ushort u = unchecked((ushort)f.byteMemory.AssertNotNull().ReadInt16(checked((int)offset)));
            f.PushInt64((long)u);
        }

        [ForthWord("l@")]
        public static void Int32At(Forth f)
        {
            long offset = f.PopInt64();
            uint l = unchecked((uint)f.byteMemory.AssertNotNull().ReadInt32(checked((int)offset)));
            f.PushInt64((long)l);
        }

        [ForthWord("x@")]
        public static void Int64At(Forth f)
        {
            long offset = f.PopInt64();
            long l = f.byteMemory.AssertNotNull().ReadInt64(checked((int)offset));
            f.PushInt64(l);
        }

        [ForthWord("c!")]
        public static void ByteBang(Forth f)
        {
            long offset = f.PopInt64();
            long value = f.PopInt64();
            f.byteMemory.AssertNotNull()[checked((int)offset)] = unchecked((byte)value);
        }

        [ForthWord("w!")]
        public static void Int16Bang(Forth f)
        {
            long offset = f.PopInt64();
            long value = f.PopInt64();
            f.byteMemory.AssertNotNull().WriteInt16(checked((int)offset), unchecked((short)value));
        }

        [ForthWord("l!")]
        public static void Int32Bang(Forth f)
        {
            long offset = f.PopInt64();
            long value = f.PopInt64();
            f.byteMemory.AssertNotNull().WriteInt32(checked((int)offset), unchecked((int)value));
        }

        [ForthWord("x!")]
        public static void Int64Bang(Forth f)
        {
            long offset = f.PopInt64();
            long value = f.PopInt64();
            f.byteMemory.AssertNotNull().WriteInt64(checked((int)offset), value);
        }

        [ForthWord("c,")]
        public static void ByteComma(Forth f)
        {
            long value = f.PopInt64();
            f.byteMemory.AssertNotNull()[f.byteHere] = unchecked((byte)value);
            f.byteHere++;
        }

        [ForthWord("w,")]
        public static void Int16Comma(Forth f)
        {
            long value = f.PopInt64();
            f.byteMemory.AssertNotNull().WriteInt16(f.byteHere, unchecked((short)value));
            f.byteHere+=2;
        }

        [ForthWord("l,")]
        public static void Int32Comma(Forth f)
        {
            long value = f.PopInt64();
            f.byteMemory.AssertNotNull().WriteInt32(f.byteHere, unchecked((int)value));
            f.byteHere += 4;
        }

        [ForthWord("x,")]
        public static void Int64Comma(Forth f)
        {
            long value = f.PopInt64();
            f.byteMemory.AssertNotNull().WriteInt64(f.byteHere, value);
            f.byteHere += 8;
        }

        [ForthWord("bhere")]
        public static void ByteHereOp(Forth f)
        {
            f.PushInt64((long)f.byteHere);
        }

        [ForthWord("bhere!")]
        public static void ByteHereReset(Forth f)
        {
            long l = f.PopInt64();
            f.byteHere = l;
        }

#if true
        private RexStatus rexStatus;

        public RexStatus RexStatus { get { return rexStatus; } set { rexStatus = value; } }

        private long rexLocation;

        public long RexLocation { get { return rexLocation; } set { rexLocation = value; } }

        [ForthWord(",,")]
        public static void DoubleComma(Forth f)
        {
            f.RexStatus = RexStatus.None;
        }

        [ForthWord("[rex?],")]
        public static void RexGoesHere(Forth f)
        {
            if (f.RexStatus == RexStatus.None)
            {
                f.RexStatus = RexStatus.Bookmarked;
                f.RexLocation = f.ByteHere;
            }
            else
            {
                throw new Exception("rex location already specified");
            }
        }

        [ForthWord("rex.0")]
        public static void RexDotZero(Forth f)
        {
            IByteMemory fbm = f.ByteMemory.AssertNotNull();

            if (f.RexStatus == RexStatus.Bookmarked)
            {
                if (f.ByteHere > f.RexLocation)
                {
                    fbm.MemMove(f.RexLocation + 1, f.RexLocation, checked((int)(f.ByteHere - f.RexLocation)));
                }
                f.RexStatus = RexStatus.Inserted;
            }
            
            if (f.RexStatus == RexStatus.Inserted)
            {
                fbm[f.RexLocation] = 0x40;
            }
        }

        private static void RexDotSomething(Forth f, byte bitToSet)
        {
            IByteMemory fbm = f.ByteMemory.AssertNotNull();

            if (f.RexStatus == RexStatus.Bookmarked)
            {
                if (f.ByteHere > f.RexLocation)
                {
                    fbm.MemMove(f.RexLocation + 1, f.RexLocation, checked((int)(f.ByteHere - f.RexLocation)));
                    fbm[f.RexLocation] = 0x40;
                }
                f.RexStatus = RexStatus.Inserted;
            }

            if (f.RexStatus == RexStatus.Inserted)
            {
                fbm[f.RexLocation] |= bitToSet;
            }
            else
            {
                throw new Exception("rex location not specified");
            }
        }

        [ForthWord("rex.w")]
        public static void RexDotW(Forth f)
        {
            RexDotSomething(f, 8);
        }

        [ForthWord("rex.r")]
        public static void RexDotR(Forth f)
        {
            RexDotSomething(f, 4);
        }

        [ForthWord("rex.x")]
        public static void RexDotX(Forth f)
        {
            RexDotSomething(f, 2);
        }

        [ForthWord("rex.b")]
        public static void RexDotB(Forth f)
        {
            RexDotSomething(f, 1);
        }
#endif

        [ForthWord("bsize")]
        public static void ByteMemorySize(Forth f)
        {
            f.PushInt64(f.byteMemory.AssertNotNull().Size);
        }

        [ForthWord("bunused")]
        public static void ByteMemoryUnused(Forth f)
        {
            f.PushInt64(f.byteMemory.AssertNotNull().Size - f.byteHere);
        }

        [ForthWord("sxb")]
        public static void SignExtendByte(Forth f)
        {
            ulong value = f.PopUInt64();
            if ((value & 0x80ul) != 0) value |= 0xFFFFFFFFFFFFFF80ul;
            else value &= 0x7Ful;
            f.PushUInt64(value);
        }

        [ForthWord("sxw")]
        public static void SignExtendInt16(Forth f)
        {
            ulong value = f.PopUInt64();
            if ((value & 0x8000ul) != 0) value |= 0xFFFFFFFFFFFF8000ul;
            else value &= 0x7FFFul;
            f.PushUInt64(value);
        }

        [ForthWord("sxl")]
        public static void SignExtendInt32(Forth f)
        {
            ulong value = f.PopUInt64();
            if ((value & 0x80000000ul) != 0) value |= 0xFFFFFFFF80000000ul;
            else value &= 0x7FFFFFFFul;
            f.PushUInt64(value);
        }

        [ForthWord("zxb")]
        public static void ZeroExtendByte(Forth f)
        {
            ulong value = f.PopUInt64();
            value &= 0xFFul;
            f.PushUInt64(value);
        }

        [ForthWord("zxw")]
        public static void ZeroExtendInt16(Forth f)
        {
            ulong value = f.PopUInt64();
            value &= 0xFFFFul;
            f.PushUInt64(value);
        }

        [ForthWord("zxl")]
        public static void ZeroExtendInt32(Forth f)
        {
            ulong value = f.PopUInt64();
            value &= 0xFFFFFFFFul;
            f.PushUInt64(value);
        }

        [ForthWord("lshift")]
        public static void LShift(Forth f)
        {
            ulong shiftCount = f.PopUInt64();
            ulong value = f.PopUInt64();
            if (shiftCount > 63)
            {
                f.PushUInt64(0ul);
            }
            else
            {
                f.PushUInt64(value << (int)shiftCount);
            }
        }

        [ForthWord("rshift")]
        public static void RShift(Forth f)
        {
            ulong shiftCount = f.PopUInt64();
            ulong value = f.PopUInt64();
            if (shiftCount > 63)
            {
                f.PushUInt64(0ul);
            }
            else
            {
                f.PushUInt64(value >> (int)shiftCount);
            }
        }

        [ForthWord("min")]
        public static void Min(Forth f)
        {
            long b = f.PopInt64();
            long a = f.PopInt64();
            f.PushInt64((a < b) ? a : b);
        }

        [ForthWord("max")]
        public static void Max(Forth f)
        {
            long b = f.PopInt64();
            long a = f.PopInt64();
            f.PushInt64((a > b) ? a : b);
        }

        [ForthWord("umin")]
        public static void UnsignedMin(Forth f)
        {
            ulong b = f.PopUInt64();
            ulong a = f.PopUInt64();
            f.PushUInt64((a < b) ? a : b);
        }

        [ForthWord("umax")]
        public static void UnsignedMax(Forth f)
        {
            ulong b = f.PopUInt64();
            ulong a = f.PopUInt64();
            f.PushUInt64((a > b) ? a : b);
        }

        [ForthWord("order")]
        public static void Order(Forth f)
        {
            Console.WriteLine("Order:");
            int iEnd = f.searchOrder.Depth;
            for(int i = 0; i < iEnd; ++i)
            {
                Console.WriteLine("  " + f.searchOrder[i].AssertNotNull().Name);
            }
            Console.WriteLine("Definitions: " + f.definitions.Name);
        }

        [ForthWord("words")]
        public static void Words(Forth f)
        {
            List<string> words = new List<string>();
            words.AddRange(f.searchOrder[0].AssertNotNull().Dict.Keys);
            words.Sort();
            int width = Console.BufferWidth;
            Console.WriteLine(words.Count + " words in " + f.searchOrder[0].AssertNotNull().Name);
            Console.Write("  ");
            int remain = width - 4;
            foreach (string word in words)
            {
                if (remain < word.Length + 1)
                {
                    Console.WriteLine();
                    Console.Write("  ");
                    remain = width - 4;
                }
                Console.Write(word + " ");
                remain -= (word.Length + 1);
            }
            Console.WriteLine();
        }

        [ForthWord("page")]
        public static void Page(Forth f)
        {
            Console.Clear();
        }

        public static char ToHexChar(int value)
        {
            if (value < 10) return (char)('0' + value);
            else return (char)('A' + value - 10);
        }

        public static string ToHex(int value, int digits)
        {
            char[] d = new char[digits];
            int pos = digits;
            while (pos > 0)
            {
                --pos;
                d[pos] = ToHexChar(value & 0x0F);
                value >>= 4;
            }
            return new string(d);
        }

        public static string DumpLine(IByteMemory bytes, int lBegin, int dBegin, int dEnd)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append(ToHex(lBegin, 6));
            sb.Append(" : ");
            int lEnd = lBegin + 16;
            for (int i = lBegin; i < lEnd; ++i)
            {
                if (i < dBegin || i >= dEnd) sb.Append("   ");
                else
                {
                    sb.Append(ToHex(bytes[i], 2));
                    sb.Append(" ");
                }
            }
            sb.Append(": ");
            for (int i = lBegin; i < lEnd; ++i)
            {
                if (i < dBegin || i >= dEnd) sb.Append(" ");
                else
                {
                    byte b = bytes[i];
                    if (b < 32 || b > 126) sb.Append(".");
                    else sb.Append((char)b);
                }
            }
            return sb.ToString();
        }

        public static void Dump(IByteMemory bytes, int off, int len)
        {
            int lPos = (off & ~0x0F);
            int lEnd = off + len;
            while (lPos < lEnd)
            {
                Console.WriteLine(DumpLine(bytes, lPos, off, lEnd));
                lPos += 16;
            }
        }

        [ForthWord("dump")]
        public static void Dump(Forth f)
        {
            long len = f.PopInt64();
            long offset = f.PopInt64();
            Dump(f.byteMemory.AssertNotNull(), checked((int)offset), checked((int)len));
        }

        [ForthWord("dumparray")]
        public static void DumpArray(Forth f)
        {
            object? objArray = f.dStack.Pop();
            if (objArray is byte[] array)
            {
                long len = f.PopInt64();
                long offset = f.PopInt64();
                Dump(new ByteArrayWrapper(array), checked((int)offset), checked((int)len));
            }
        }

        [ForthWord("1+")]
        public static void OnePlus(Forth f)
        {
            long x = f.PopInt64();
            f.PushInt64(x + 1L);
        }

        [ForthWord("1-")]
        public static void OneMinus(Forth f)
        {
            long x = f.PopInt64();
            f.PushInt64(x - 1L);
        }
    }

    public delegate void ExecutionToken(Forth f);

    public delegate string? GetWordProc(char delimiter);

    [AttributeUsage(AttributeTargets.Method)]
    public class ForthWordAttribute : Attribute
    {
        private string name;
        private bool isImmediate;

        public ForthWordAttribute(string name)
        {
            this.name = name;
            this.isImmediate = false;
        }

        public string Name { get { return name; } }

        public bool IsImmediate { get { return isImmediate; } set { isImmediate = value; } }
    }

    public enum RexStatus
    {
        None,
        Bookmarked,
        Inserted
    }
}
