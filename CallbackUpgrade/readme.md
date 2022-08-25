 Notes
-------

* Replacements are strictly ordered.

So for example `Upgrading hooks to decorators` comes before `Adding 'CLICK_SOURCE' tag to 'OnPlayerClickPlayer'`, and the latter only looks for `public|forward|@hook` not `public|forward|hook|HOOK__|@hook`, because it will know that the other two can't exist any more.

* Why does this use PCRE.NET instead of the inbuilt regex grammar?

Simple - while the inbuilt grammar does have *expression balancing* which can be used to match `()`s in expressions it doesn't have *subroutines*, so while the complex expressions **can** be built up, they can't be abstracted.  Using PCRE allows things like the regex for a function parameter to be placed in a separate `DEFINE` and reused in all the scanners by name.  The .NET version would require those expressions to be copied and pasted in to every scanner; which would entirely obfuscate their basic use and require massive updates for every single bug fix.
