using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System;

namespace CSharpSourceCodeDependencyAnalyzer
{
    #region Types
    /// <summary>
    /// Holds command-line options.
    /// </summary>
    internal class Options
    {
        public string Path { get; set; }
        public string Source { get; set; }
        public string Target { get; set; }
    }

    /// <summary>
    /// Represents a node (project or NuGet) in the dependency graph.
    /// </summary>
    internal class Node
    {
        public string Name { get; }
        public bool IsProject { get; }
        public List<Node> Dependencies { get; }

        public Node(string name, bool isProject)
        {
            Name = name;
            IsProject = isProject;
            Dependencies = [];
        }
    }
    #endregion

    /// <summary>
    /// CLI tool to analyze project and NuGet dependencies based on .csproj files.
    /// </summary>
    internal class Program
    {
        private static void Main(string[] args)
        {
            if (args.Length == 0 || args.FirstOrDefault() == "--help")
            {
                ShowUsage();
                return;
            }

            try
            {
                string command = args[0].ToLowerInvariant();
                Options options = ParseOptions([.. args.Skip(1)]);
                DependencyGraph graph = DependencyGraph.Build(options.Path);

                switch (command)
                {
                    case "tree":
                        graph.PrintFullTree();
                        break;

                    case "entry":
                        if (string.IsNullOrEmpty(options.Source))
                        {
                            Console.Error.WriteLine("Error: --source is required for 'entry'.");
                            return;
                        }
                        graph.PrintEntryTree(options.Source);
                        break;

                    case "entry-simple":
                        if (string.IsNullOrEmpty(options.Source))
                        {
                            Console.Error.WriteLine("Error: --source is required for 'entry-simple'.");
                            return;
                        }
                        graph.PrintEntrySimple(options.Source);
                        break;

                    case "depends-on":
                        if (string.IsNullOrEmpty(options.Target))
                        {
                            Console.Error.WriteLine("Error: --target is required for 'depends-on'.");
                            return;
                        }
                        graph.PrintFilteredTree(options.Target);
                        break;

                    case "path":
                        if (string.IsNullOrEmpty(options.Target) || string.IsNullOrEmpty(options.Source))
                        {
                            Console.Error.WriteLine("Error: --source and --target are required for 'path'.");
                            return;
                        }
                        graph.PrintPaths(options.Source, options.Target);
                        break;

                    default:
                        Console.Error.WriteLine($"Unknown command: {command}");
                        ShowUsage();
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unhandled error: {ex.Message}");
            }
        }

        #region Routines
        /// <summary>
        /// Display usage instructions.
        /// </summary>
        private static void ShowUsage()
        {
            Console.WriteLine("Usage: DependencyAnalyzer <command> --path <folder> [--source <name>] [--target <name>]");
            Console.WriteLine("Commands:");
            Console.WriteLine("  tree             Print full dependency tree for each project.");
            Console.WriteLine("  entry            Print dependency tree for a single project.");
            Console.WriteLine("  entry-simple     Print flat list of dependencies (projects then NuGets) for a single project.");
            Console.WriteLine("  depends-on       Print tree of projects depending on a target assembly/NuGet.");
            Console.WriteLine("  path             Print paths from source assembly to target assembly.");
        }
        /// <summary>
        /// Simple option parser for --path, --source, --target.
        /// </summary>
        private static Options ParseOptions(string[] args)
        {
            Options opts = new();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--path":
                        opts.Path = GetArgValue(args, ref i);
                        break;
                    case "--source":
                        opts.Source = GetArgValue(args, ref i);
                        break;
                    case "--target":
                        opts.Target = GetArgValue(args, ref i);
                        break;
                    default:
                        Console.Error.WriteLine($"Unknown option {args[i]}");
                        break;
                }
            }

            if (string.IsNullOrEmpty(opts.Path) || !Directory.Exists(opts.Path))
                throw new ArgumentException("Invalid or missing path. Use --path <folder> to specify the solution directory.");

            return opts;
        }
        #endregion

        #region Helpers
        private static string GetArgValue(string[] args, ref int index)
        {
            if (index + 1 < args.Length)
            {
                index++;
                return args[index];
            }
            throw new ArgumentException($"Missing value for {args[index]}");
        }
        #endregion
    }

    /// <summary>
    /// Builds and interacts with the dependency graph.
    /// </summary>
    internal class DependencyGraph
    {
        #region Construction
        private readonly Dictionary<string, Node> _nodes;
        private DependencyGraph(Dictionary<string, Node> nodes)
            => _nodes = nodes;
        #endregion

        #region Methods
        /// <summary>
        /// Builds the graph by reading all .csproj files in the folder.
        /// </summary>
        public static DependencyGraph Build(string rootFolder)
        {
            string[] csprojFiles = Directory.GetFiles(rootFolder, "*.csproj", SearchOption.AllDirectories);
            Dictionary<string, Node> nodes = new(StringComparer.OrdinalIgnoreCase);

            // First pass: create project nodes
            foreach (string file in csprojFiles)
            {
                XDocument doc = XDocument.Load(file);
                XElement? nameNode = doc.Descendants("AssemblyName").FirstOrDefault();
                string projectName = nameNode?.Value ?? Path.GetFileNameWithoutExtension(file);
                if (!nodes.ContainsKey(projectName))
                    nodes[projectName] = new Node(projectName, true);
            }

            // Second pass: add dependencies
            foreach (string file in csprojFiles)
            {
                XDocument doc = XDocument.Load(file);
                string projectName = doc.Descendants("AssemblyName").FirstOrDefault()?.Value
                                  ?? Path.GetFileNameWithoutExtension(file);
                Node fromNode = nodes[projectName];

                // ProjectReferences
                foreach (XElement pr in doc.Descendants("ProjectReference"))
                {
                    string? include = pr.Attribute("Include")?.Value;
                    if (include == null) continue;
                    string refProjName = Path.GetFileNameWithoutExtension(include);
                    if (nodes.TryGetValue(refProjName, out Node? toNode))
                        fromNode.Dependencies.Add(toNode);
                }

                // PackageReferences
                foreach (XElement pkg in doc.Descendants("PackageReference"))
                {
                    string? id = pkg.Attribute("Include")?.Value;
                    if (string.IsNullOrEmpty(id)) continue;
                    if (!nodes.TryGetValue(id, out Node? pkgNode))
                    {
                        pkgNode = new Node(id, false);
                        nodes[id] = pkgNode;
                    }
                    fromNode.Dependencies.Add(pkgNode);
                }
            }

            return new DependencyGraph(nodes);
        }
        /// <summary>
        /// Print full dependency tree for all projects.
        /// </summary>
        public void PrintFullTree()
        {
            foreach (Node? node in _nodes.Values.Where(n => n.IsProject).OrderBy(n => n.Name))
                PrintTree(node, "", []);
        }
        /// <summary>
        /// Print dependency tree for a single project.
        /// </summary>
        public void PrintEntryTree(string projectName)
        {
            if (!_nodes.TryGetValue(projectName, out Node? root))
            {
                Console.Error.WriteLine($"Project '{projectName}' not found.");
                return;
            }
            PrintTree(root, "", []);
        }
        #endregion

        #region Routines
        private static void PrintTree(Node node, string indent, HashSet<string> visited)
        {
            Console.WriteLine(indent + node.Name);
            if (visited.Contains(node.Name)) 
                return; // prevent cycles
            visited.Add(node.Name);

            foreach (Node? child in node.Dependencies.OrderBy(d => d.Name))
                PrintTree(child, indent + "  ", visited);

            visited.Remove(node.Name);
        }
        /// <summary>
        /// Print trees of projects depending on target.
        /// </summary>
        public void PrintFilteredTree(string target)
        {
            if (!_nodes.TryGetValue(target, out Node? targetNode))
            {
                Console.Error.WriteLine($"Target '{target}' not found.");
                return;
            }

            foreach (Node? node in _nodes.Values.Where(n => n.IsProject).OrderBy(n => n.Name))
                if (HasDependency(node, targetNode, []))
                    PrintFiltered(node, targetNode, "", []);
        }
        #endregion

        #region Entry-Simple
        /// <summary>
        /// Print flat list of all unique dependent projects and NuGet packages for a single project.
        /// </summary>
        public void PrintEntrySimple(string projectName)
        {
            if (!_nodes.TryGetValue(projectName, out Node? root))
            {
                Console.Error.WriteLine($"Project '{projectName}' not found.");
                return;
            }

            // Avoid revisiting the root
            HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase) { root.Name };
            SortedSet<string> projectDeps = new(StringComparer.OrdinalIgnoreCase);
            SortedSet<string> nugetDeps = new(StringComparer.OrdinalIgnoreCase);

            // Collect all transitive deps
            foreach (Node? dep in root.Dependencies.OrderBy(d => d.Name))
                CollectDependencies(dep, visited, projectDeps, nugetDeps);

            // Print them
            Console.WriteLine("Projects:");
            foreach (string p in projectDeps)
                Console.WriteLine("  " + p);

            Console.WriteLine("NuGet Packages:");
            foreach (string n in nugetDeps)
                Console.WriteLine("  " + n);
        }
        private static void CollectDependencies(Node current, HashSet<string> visited, SortedSet<string> projectDeps, SortedSet<string> nugetDeps)
        {
            if (!visited.Add(current.Name))
                return;

            if (current.IsProject) 
                projectDeps.Add(current.Name);
            else 
                nugetDeps.Add(current.Name);

            foreach (Node? child in current.Dependencies.OrderBy(d => d.Name))
                CollectDependencies(child, visited, projectDeps, nugetDeps);
        }
        #endregion

        #region Helpers
        private static bool HasDependency(Node current, Node target, HashSet<string> visited)
        {
            if (current == target) 
                return true;
            if (visited.Contains(current.Name)) 
                return false;

            visited.Add(current.Name);

            foreach (Node dep in current.Dependencies)
            {
                if (HasDependency(dep, target, visited)) 
                    return true;
            }
            return false;
        }
        private static void PrintFiltered(Node current, Node target, string indent, HashSet<string> visited)
        {
            if (visited.Contains(current.Name)) 
                return;
            visited.Add(current.Name);

            Console.WriteLine(indent + current.Name);
            foreach (Node? dep in current.Dependencies.OrderBy(d => d.Name))
            {
                if (HasDependency(dep, target, []))
                    PrintFiltered(dep, target, indent + "  ", visited);
            }

            visited.Remove(current.Name);
        }
        /// <summary>
        /// Print all paths from source to target.
        /// </summary>
        public void PrintPaths(string source, string target)
        {
            if (!_nodes.TryGetValue(source, out Node? srcNode))
            {
                Console.Error.WriteLine($"Source '{source}' not found.");
                return;
            }
            if (!_nodes.TryGetValue(target, out Node? tgtNode))
            {
                Console.Error.WriteLine($"Target '{target}' not found.");
                return;
            }

            List<List<string>> paths = [];
            FindPaths(srcNode, tgtNode, [], [], paths);
            if (paths.Count == 0)
            {
                Console.WriteLine($"No path from '{source}' to '{target}' found.");
                return;
            }

            foreach (List<string> path in paths)
                Console.WriteLine(string.Join(" -> ", path));
        }
        private static void FindPaths(Node current, Node target, HashSet<string> visited, List<string> stack, List<List<string>> results)
        {
            if (visited.Contains(current.Name)) 
                return;
            visited.Add(current.Name);
            stack.Add(current.Name);

            if (current == target)
                results.Add([.. stack]);
            else
            {
                foreach (Node dep in current.Dependencies)
                    FindPaths(dep, target, visited, stack, results);
            }

            stack.RemoveAt(stack.Count - 1);
            visited.Remove(current.Name);
        }
        #endregion
    }
}
