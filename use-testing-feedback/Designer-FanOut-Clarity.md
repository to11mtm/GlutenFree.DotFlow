# Designer FanOut — Partitioned Legs & Clarity

 - Users testing fan-out are confused about how it works.
 - First: a way to have items fan out **based on their items**. For instance if the input has both
   Foo and Bar and Baz, they want to be able to specify separate outputs for Foo and Bar and Baz,
   or have a way to have Foo on one output and Bar and Baz on a separate leg.
 - Second: they are uncertain how fan-out works in its current state overall. There should be
   better clarity around the existing case, especially if a new module is needed to handle the
   first item.

*Captured 2026-08-04 from user-testing feedback. See
[Designer-FanOut-Clarity-Plan.md](Designer-FanOut-Clarity-Plan.md).*
