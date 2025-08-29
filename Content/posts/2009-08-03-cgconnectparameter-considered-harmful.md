---
title: cgConnectParameter Considered Harmful
tags:
    - code
---
Hooking up a large number of shared effect parameters to a single head parameter via `cgConnectParameter` is, apparently, not the intended use case. Doing so causes any `cgSetParameter` call on the head parameter to become orders of magnitude slower (though the amount is proportional to the number of connected effect parameters). This is also the case when deferred parameter setting is used, which is a fairly surprising result, given that it should mean that parameter values don't force any sort of evaluation until something actually goes to read from them.

Note that much of the wasted time may not actually be the fault of `cgConnectParameter`. I have a strong suspicion that the blame may lie with the implementation of effect-level parameters in general[^1]. Connecting large groups of program parameters may be AOK, though I've yet to test that theory out.

This applies to the April 2009 release of the Cg 2.2 runtime, running on x86 Windows via OpenGL.

[^1]: Perhaps the Cg runtime forces `cgSetParameter*` to loop through connected effect parameters in case one of them might be involved in a state-assignment expression? If this is the case then it strikes me as amazingly silly to not have the effect runtime follow lazy evaluation rules.