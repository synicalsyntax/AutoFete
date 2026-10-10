# Fête code layout

The top level contains shared definitions and the code that coordinates the Fête cycle: the registry, controller, dispatcher, target matching, and game interaction helpers.

- `Stages/` contains the five individual Fête implementations. Each owns its metadata, targets, actions, join policy, and run state.
- `Diagnostics/` contains capture recording.

To add or reorder a Fête, create a stage class with a `FeteDefinition`, then register its definition and handler factory in `FeteCatalog.Registrations`. Registration order is cycle order.
