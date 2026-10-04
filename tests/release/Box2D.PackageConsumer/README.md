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
poses/events in two fresh worlds. SDK10.0.401 and runtime10.0.12 scope this result; output
reports the actual OS/architecture/runtime. Candidate smoke cannot claim public installed
support; neither smoke selects a product/template or establishes cross-platform determinism.
