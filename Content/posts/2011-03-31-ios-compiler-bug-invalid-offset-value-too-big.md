---
title: >
    iOs Compiler (Bug?): "invalid offset, value too big"
tags:
    - Apple
    - iOS
    - build
    - bugs
    - code
---
This is a bug that's bit me a few times already. Basically, the iOS compiler fails to generate some function somewhere in the code file causing it to bail with the error "invalid offset, value too big". Problem is, it doesn't tell you which function it failed on (the numbers surrounding the error seem largely meaningless).

Fixing it is simple, if tedious. Go through each function definition (including C++ methods and Obj-C message handlers), starting with the largest, and comment out the body. If commenting the body out makes the file compile, you've found your culprit. All that's left is to break the function out into several parts and call them all in sequence from the original function. Note that while this bug usually comes up around complex flow-control (giant switch-case statements, hugely nested ifs, etc), that isn't always the case (though perhaps the compiler is failing on some inlined-in flow-control).

I don't know if this is a bug in the compiler's code generator or not (though that seems to be the case). It may just be a limitation of the target platform, in which case the real bug is how useless the error message is.