# Arcade-powered source-build

Source-build existing outside the Microsoft build process impedes efforts to
maintain the source-buildability of .NET Core. Integration into the normal build
process is driven by two major goals:

1. Official build. When the .NET Core SDK official build completes, its
   artifacts include validated, ship-ready source-build outputs, in addition to
   the Microsoft build outputs.

   * If we treat the source-build outputs with the same care and criticality as
     the Microsoft build outputs, we eliminate the delay between Microsoft build
     availability and source-build tarball availability.

2. PR validation. Every repo involved in source-build validates
   source-buildability in its PR validation build.

   * This allows developers and release owners to understand the source-build
     impact of changes, reducing the frequency the source-build servicing team
     has to root-cause and patch over problems.

This doc is about where we can start, what incremental progress would look like,
and the vision.

## Starting point: official build

The output of source-build is a set of tarballs that can be used to build the
.NET Core SDK from source. We can add the current behavior of source-build to
the Core-SDK official build. That is, Core-SDK clones all constituent repos,
applies patches, builds each repo using customized build commands, and produces
the source-build tarballs as artifacts.

The gap here is build performance. It is simply too slow (> 2hrs) to build all
constituent repos within one official build. It needs to be fast enough that it
is reasonable for the entire official build to be rejected when the source-build
fails.

> Note: practically, the source-build official build should run in an
> independent build pipeline at first: the long build time would interfere with
> other .NET 5 work if integrated directly.

## Starting point: PR validation

We can start here by adding extra jobs that run the standard source-build
command and arguments. This is a simple step to confirm the build isn't
fundamentally broken.

There are many gaps:
* Prebuilt dependency usage isn't tracked, because all dependencies are
  downloaded as non-source-built prebuilts.
* Source-build behavior may not work with source-built upstreams.
* The artifacts built by the repo may not work downstream.
* Advanced build flows aren't checked, such as source-build bootstrap or using
  an N-1 SDK.

## Incremental progress

### The performance gap
We need to avoid building all constituent repos in the Core-SDK build. To do
this, each repo needs to produce intermediate source-built artifacts during its
official build, to be consumed by downstream repos. On the other end,
source-build needs to support restoring from an intermediate artifact.

To make incremental progress, one of Core-SDK's upstreams should produce
source-built intermediates, and Core-SDK should consume them. We should choose a
leaf in the source-build dependency graph, say, SourceLink. When Core-SDK is
looking at the build graph to determine which repos to build, instead of
building SourceLink, it should restore the SourceLink intermediate artifact.

Once we have this flow working, the functionality should be integrated into
Arcade SDK for easy onboarding. Then, working from the bottom (leaves) upward
(towards Core-SDK), more repos should consume and produce source-built
intermediates in their official builds. When this completes, each repo only
needs to build itself.

> Note: some constituent repos aren't maintained by Microsoft, so it isn't
> feasible to add them to this flow. We could fork them and set up an official
> source-build. If it builds quickly, however, it might be better to simply
> rebuild them whenever the outputs are needed.

### Getting into Arcade
The initial plan to run source-build in Core-SDK doesn't assume any changes to
Arcade: this should be possible due to the extension points that already exist
in the Arcade SDK. Once we have that, it will be clearer what logic is missing,
and how to add it. This allows us to migrate source-build logic incrementally
and in parallel with other work.

### The speculative SDK
It's difficult to validate that a PR won't break downstream repos. This problem
is shared by source-build and the Microsoft build. "Speculative builds" have
been proposed to try to help with this, but would be very difficult to implement
in the Microsoft build. It may be more reasonable in the context of
source-build: all builds happen on a single machine.

This is also necessary in source-build to validate advanced scenarios: by making
a PR, is it still possible to run a bootstrap build of the .NET Core SDK? Can
.NET Core SDK version N be built using SDK N-1?

This can be developed in parallel to other efforts.

## End result



## Q&A

### Q: How do we patch without an orchestration-focused repo?
In the best world, patches are no longer considered as a solution to build or
functionality problems. If source-build doesn't work, we fix it and respin the
official build, because a source-build issue is treated the same as an issue
with the Microsoft-built .NET Core SDK.

However, there are reasons this may not be feasible. In an intermediate state,
the source-build performance may be slow enough that it must be built and fixed
up outside the Microsoft official build. But even if that's fixed, there may be
a case where an issue with source-build is discovered very late, and we can
identify a patch that fixes it but can't afford to respin the Microsoft build.

In these cases, we should create a branch in the affected repo based on the
current commit that includes the patch as a Git commit. This avoids a
specialized "patch file" workflow, and it is business as usual to merge the
hotfix into the branch for the next build.

This implies that Darc/Maestro++/BAR must be able to flow builds from the new
branch through official builds using a "source-build patch" channel.
