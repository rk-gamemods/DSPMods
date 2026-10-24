# Source-copy and licensing evaluation

Reviewed 2026-10-24 against SphereOpt commit
`a06ca9a7bd53d907bea6160eec333b8b30336574`.

## Decision: clone locally first

A clone creates a local Git checkout with history and an upstream remote. It is
enough for inspection, branches, builds and private experiments. It creates no
hosted repository and offers no off-machine backup by itself.

A GitHub fork creates a hosted repository associated with upstream. It helps
with independent releases, sharing branches and contributing pull requests.
You still clone the fork to work locally. It adds a hosted repository to maintain
and makes publication a more immediate concern.

The current phase uses a full local clone under `external/SphereOpt/`, ignored by
the workspace repository. A fork can be added later without abandoning local
work. No GitHub fork or new remote publication was performed.

## The Unlicense is not an absent license

SphereOpt's `LICENSE.txt` contains the Unlicense public-domain dedication and
permission to copy, modify and distribute the author's covered software.
GitHub identifies it as the Unlicense.

Vendored `thirdparty/UnityMeshSimplifier/Runtime/` files carry MIT notices,
including attribution to Mattias Edlund and earlier mesh-simplification work.
Preserve those notices. The top-level dedication cannot waive another author's
rights.

The mod author does not own DSP itself, but that does not make all original mod
code ownerless. Original mod code and pre-existing game code/assets involve
separate rights. Permission for SphereOpt's covered code does not establish
permission to redistribute game assemblies, extracted assets or game-derived
shader material. This review does not establish the provenance of every shader
line or asset in the bundle.

Public visibility also does not, by itself, give general permission to reuse an
otherwise unlicensed repository's implementation. Studying an approach and
independently implementing ideas differs from copying source or assets.

## Sources

- [SphereOpt license](https://github.com/andyoneal/SphereOpt/blob/a06ca9a7bd53d907bea6160eec333b8b30336574/LICENSE.txt)
- [Vendored MIT notice](https://github.com/andyoneal/SphereOpt/blob/a06ca9a7bd53d907bea6160eec333b8b30336574/thirdparty/UnityMeshSimplifier/Runtime/MeshSimplifier.cs)
- [The Unlicense](https://unlicense.org/)
- [GitHub licensing guidance](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/licensing-a-repository)
- [U.S. Copyright Office: derivative works](https://www.copyright.gov/circs/circ14.pdf)
