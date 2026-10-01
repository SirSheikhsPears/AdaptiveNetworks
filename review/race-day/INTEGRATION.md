# KianCommons dependency integration

AN currently references KianCommons commit
`69a5f8abede9fcb23440b98418fccfaf006877ca` through the submodule
`AdaptiveRoads/KianCommons`.

The test2 build used [zhuchhh/KianCommons](https://github.com/zhuchhh/KianCommons)
at `b922567013664ac6378ad8dabc0082c50b86f666`, plus local changes to
`KianCommons/Util/DynamicFlagsUtil.cs` and `KianCommons/Util/NetUtil.cs`.
They are embedded in the delivered AN assembly, not a separate mod DLL.

## What the patches contain

| Patch | Required KianCommons base | Contents |
| --- | --- | --- |
| [02-KianCommons-from-69a5f8.patch](patches/02-KianCommons-from-69a5f8.patch) | `69a5f8abede9fcb23440b98418fccfaf006877ca` | The three functional C# files needed to match the tested dependency tree: DynamicFlags2, DynamicFlagsUtil and NetUtil. Includes the migration and local corrections; omits upstream fork README edits. |
| [03-KianCommons-local-on-b922567.patch](patches/03-KianCommons-local-on-b922567.patch) | `b922567013664ac6378ad8dabc0082c50b86f666` | Only the local corrections to DynamicFlagsUtil and NetUtil. |

Use exactly one route. Apply the selected patch at the KianCommons repository
root, not the AN root. The five AN changes are already committed in this fork;
`01-AN-core.patch` is supplied only as a portable reference for a clean AN base.

The corrections complete the generic `DynamicFlags<NetInfo>` helpers, restore
`IsAnyFlagSetOrEmpty`, and remove an incorrect negation in the `DynamicFlags2`
`IsAnyFlagSet` helper. NetUtil uses `DynamicFlags<NetInfo>.kTags` and preserves
the existing explicit initializer and missing-registry diagnostic from EF.
The registry relocation and the initialization safeguard were already addressed
by earlier work; they are not newly discovered EF omissions.

## Turn the review patches into a complete source integration

1. Check out the selected KianCommons base in a clean working tree.
2. Run `git apply --check` with the chosen patch, then apply it.
3. Review and commit those changes in a KianCommons repository.
4. Publish that dependency commit so it can be fetched by other reviewers.
5. Record the new dependency commit in AN's submodule gitlink. Update the URL
   in `.gitmodules` if the commit is published in a different repository.
6. Build using the intended post-Race Day references and perform the targeted
   game tests described in [the review guide](../../REVIEW-RACEDAY.md).

The old submodule gitlink remains unchanged in the reviewed AN code commit.
Adding these patch files does not perform steps 1–5. A parent AN commit does
not contain uncommitted changes inside a submodule, and moving the pointer to
`b922567` alone omits the local corrections.

The historical validation report records successful application of both
alternative patches on their respective bases and equality with the functional
test2 dependency sources. This is not a fresh Unity test of this fork.
