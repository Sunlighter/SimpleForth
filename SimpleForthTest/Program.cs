using System;
using System.Collections.Generic;
using System.Text;
using SimpleForth;
using System.Runtime.InteropServices;

namespace SimpleForthTest
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
#if false
                using (MemoryBlock mb = new MemoryBlock((UIntPtr)0x20000))
                {
                    Console.WriteLine("Is64Bit = " + MemoryBlock.Is64Bit);
                    Console.WriteLine("mb.Location = " + mb.Location);
                    Console.WriteLine("mb.Size = " + mb.Size);

                    Marshal.WriteByte(mb.Location, (byte)32);
                    byte b = Marshal.ReadByte(mb.Location);
                    System.Diagnostics.Debug.Assert(b == (byte)32);

                    mb.Security = MemoryBlockSecurity.ReadOnly;

                    byte b2 = Marshal.ReadByte(mb.Location);
                    Marshal.WriteByte(mb.Location, (byte)64);
                }
#endif
                
                Console.WriteLine("Sunlit World Forth in C#");
                Console.WriteLine("Copyright (c) 2007 by Edward Kiser");
                Console.WriteLine("All Rights Reserved");
                if (MemoryBlock.Is64Bit) Console.WriteLine("[Running in 64-bit Mode]");
                using (MemoryBlock mb = new MemoryBlock((UIntPtr)0x20000))
                {
                    mb.Security = MemoryBlockSecurity.ExecuteReadWrite;
                    Forth f = new Forth();
                    f.ByteMemory = new MemoryAccessor(mb.Location, mb.Size);
                    f.LoadX86Asm();
                    bool done = false;
                    f.Execute("also forth definitions previous");
                    f.Define("bye", delegate(Forth g) { done = true; });
                    f.Execute("vocabulary user  also user definitions");
                    while (!done)
                    {
                        if (f.IsCompiling) Console.WriteLine("ok (compiling)"); else Console.WriteLine("ok");
                        string? x = Console.ReadLine();
                        if (x is null) break;
                        bool beganTransaction = false;
                        try
                        {
                            f.BeginTransaction();
                            beganTransaction = true;
                            f.Execute(x);
                            f.CommitTransaction();
                        }
                        catch(Exception exc)
                        {
                            if (beganTransaction)
                            {
                                f.RollBackTransaction();
                            }
                            else
                            {
                                Console.WriteLine("Failed to begin transaction!");
                            }
                            Console.WriteLine($"{exc.GetType().FullName}: {exc.Message}");
                        }
                    }
                }
            }
            catch (Exception exc)
            {
                Console.WriteLine();
                Console.WriteLine("***** Exception! *****");
                Console.WriteLine();
                Console.WriteLine(exc);
            }
            Console.WriteLine("Press a key...");
            Console.ReadKey();
        }
    }
}
