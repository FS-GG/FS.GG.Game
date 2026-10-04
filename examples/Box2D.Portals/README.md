# Headless rigid portal example

Run the real `Box2D.Portals` entry point with `dotnet run --project examples/Box2D.Portals`.
It uses Box2D.NET 3.1.654 and .NET 10, one circle, two independent worlds and a paired
90-degree portal. Both worlds step once before transfer. Source removal commits the
same game entity to one destination body; transformed velocities are preserved and
subsequent gravity follows the destination. The scene checks position/velocity and orientation modulo one turn within `2e-5`.
Transfer staging uses a direct sine/cosine rotation to avoid accumulating the pinned
engine's approximate rotation-construction error; angle extraction remains approximate.
Five recorded ticks repeat exactly under
this pinned profile. The program refuses to report success when its observations differ.

The transfer slice supports Runtime's centred circle/box descriptors and fixed engine
filter/damping/gravity-scale/sleep defaults. It copies current awake state and refuses
joint-connected bodies. Destination placement conservatively tests enclosing-disc
clearance against non-sensor bodies. Missing areas, capacity, blocked placement and
invalid staging descriptors preserve source ownership; uncertain engine effects stop
the owner. Portal-aware interpolation snaps moved entities; re-arming waits until the
prior endpoint clears the body's radius plus declared hysteresis from the exit segment.
Contacts in the destination start at its next ordinary step.

This source example does not qualify public installed support, product adoption,
browser physics, cross-world contacts/joints, hidden solver rollback or cross-platform
replay. Execution evidence is recorded only after the actual bounded run.
