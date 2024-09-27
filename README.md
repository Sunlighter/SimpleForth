<!-- -*- coding: utf-8; fill-column: 118 -*- -->

# SimpleForth
An simple Forth-like interpreter in C#

This was originally written for Visual Studio 2005.

It includes a version of an assembler I once referred to as Forth Assisted Hand Assembly. The 32-bit version works,
and Example.txt contains an example of its use, but the 64-bit version has not been completed.

Recently (in September 2024), I finally fixed a nasty bug that caused the program to terminate if you made a typo at
the interpreter prompt. Now the program wraps the interpreter's line processing in a transaction. If an exception is
thrown while a line you typed is being processed, you will see the exception, but then the whole line will be rolled
back, as if you had never typed it, so you can try again.

The transaction mechanism is not designed for concurrency, which makes it simple, but it supports nesting transactions
to arbitrary depth. Committing a transaction commits it to its parent. The transaction mechanism is probably powerful
enough that it could be used to add transaction words to the Forth itself. (On the other hand, if I did add the
transactions to the Forth itself, and then someone were to write an input line consisting of a &ldquo;commit
transaction&rdquo; command followed by a typo, the interpreter would need to roll back the line by un-committing the
transaction that was committed by the line. This would require &ldquo;meta-transactions.&rdquo;) Another more
difficult possibility is implementing an &ldquo;amb&rdquo; operator.

Blocks of byte memory are *not* protected by transactions yet; I will have to make my transaction implementation
fancier in order to support that. Even if I did make those modifications, they would only protect byte memory from
modifications made by Forth itself. If you generate assembly code and execute it, the whole transaction mechanism is
bypassed, and executing incorrect assembly code can still crash the program.
