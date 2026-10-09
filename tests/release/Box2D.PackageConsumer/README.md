# Independent Box2D package consumer

`../test-box2d-package-consumer.sh --packages /absolute/retained/packages` copies this
source into a new private directory outside the repository and qualifies candidate packages.
`../test-box2d-package-consumer.sh --public` instead cold-restores the exact published0.17.0
package from public nuget.org; it is the installed-consumer route after both-feed publication
and readback. An optional `--output /absolute/new/directory` retains all restored inputs and
lock evidence at a chosen private location. Every attempt uses a new package cache.

The copied project contains only exact PackageReferences, locally copied Compile inputs and
no repository ProjectReference/import. The boundary check verifies the exact package closure,
Core's FSharp.Core-only runtime dependencies and the adapter's lack of rendering dependencies.
The real entry point then drives 240 ticks from recorded commands, observes falling motion,
a distance joint, sensor enter/exit and floor contact, and compares exact per-tick copied
poses/events in two fresh worlds. It also executes the copied real five-tick rotated portal
scene against package APIs, requiring one live traveller after transfer, transformed pose/momentum,
one step per local world per tick, one traversal, no repeated transfer and exact replay. SDK10.0.401 and runtime10.0.12 scope this result; output
reports the actual OS/architecture/runtime. Candidate smoke cannot claim public installed
support; neither smoke selects a product/template or establishes cross-platform determinism.

## Explicit package-only portal presentation

`../test-box2d-package-consumer.sh --public --presentation --output /absolute/new/directory`
selects the presentation consumer. It copies the real portal presentation and command-line
modules from `examples/Box2D.Portals`, together with the existing package-only portal scene,
into a fresh directory outside the checkout. The consumer has exact PackageReferences to
Game Physics.Box2D and Render `0.17.0` and FSharp.Core `10.1.302`; it uses no sibling projects,
repository imports or linked sources. Presentation selection requires the public feed.

Before restore, the route rejects missing or linked inputs and validates the copied project
and exact direct pins. After restore, it requires exactly seven packages: Game Core,
Physics.Box2D and Render `0.17.0`, Box2D.NET `3.1.654`, FSharp.Core `10.1.302`, and
UI Scene and KeyboardInput `0.31.0`. Missing, foreign or project dependencies refuse
before compilation. The original no-presentation route keeps its four-package physics
boundary and falling-body/portal entrypoint.

The presentation route generates a lock through public restore, checks unchanged locked
restore, and builds the copied source. It runs the actual default and `--presentation`
entrypoint modes under runtime `10.0.12`; an invalid argument must print usage and exit 2.
Presentation checks destination-only traversal snaps and ordinary interpolation over
five ticks and twelve frames. Generated locks, package inputs and invalid-argument output
remain in the new private directory. The invoking owner must separately admit acquisition
and execution, bound the process lifetime/resources, and retain failure and cleanup evidence.
Source preparation alone does not establish a successful public consumer, generated-product
adoption, retained upgrade/removal, performance, browser/GPU support or historical cleanup.
