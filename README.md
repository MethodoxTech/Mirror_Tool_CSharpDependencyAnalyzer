# C# Dependency Analyzer

Author: Charles Zhang  
Publisher: Methodox Technologies, Inc.

A tool that can quickly help identify dependencies of a source code project. This is useful in identifying unwanted nuget dependencies from any given entry assembly. It is equivalent to manually searching through .csproj to find which projects depend on which and debug NuGet dependencies.

It can do those three things:

1. Print an exhaustive dependency tree with a root node at each source project level down to all dependent projects and most importantly the root NuGets
2. Given a single target project/nuget, prints a filtered tree of all projects that depend on that assembly
3. Given a single target project/nuget and a source assembly, it prints the tree paths that lead from the source assembly to the target - showing clearly the path from the source assembly to the ultimate dependency.

## Implementation

The current implementation:

* Scans a folder for all `.csproj` files and builds a dependency graph of projects and NuGet packages.
* Supports three commands:
  1. **tree**: Prints the full dependency tree for each project.
  2. **depends-on**: Given `--target <name>`, prints only the branches of the tree where that project or NuGet is used.
  3. **entry**: Given `--source <name>`, shows only a single subtree.
  4. **path**: Given `--source <name> --target <name>`, prints every dependency path from the source to the target.

All parsing is done using `System.Xml.Linq` (no external NuGet dependencies), and command‐line arguments are handled manually with error checking.
