using System;
using System.IO;
using System.Reflection;

namespace TiaSharp
{
    /// <summary>
    /// Native .NET assembly resolver for the Openness DLLs.
    /// Direct C# port of the compiled-C# resolver from the V1.0 PowerShell scripts.
    /// A PowerShell-scriptblock AssemblyResolve handler re-entered PowerShell during
    /// Attach() and overflowed the stack; keeping resolution entirely in compiled .NET
    /// avoids that. In C# this is simply our normal AssemblyResolve handler.
    ///
    /// IMPORTANT: Register(apiDir) must run BEFORE any Siemens.* type is touched, i.e.
    /// before the first call into a method that uses Openness types (see Program.Main).
    /// </summary>
    public static class TiaResolver
    {
        private static string _libDir;

        public static void Register(string apiDir)
        {
            _libDir = apiDir;
            AppDomain.CurrentDomain.AssemblyResolve += OnResolve;
        }

        private static Assembly OnResolve(object sender, ResolveEventArgs args)
        {
            // Extract the simple assembly name (drop version/culture/token).
            int idx = args.Name.IndexOf(',');
            string name = idx < 0 ? args.Name : args.Name.Substring(0, idx);

            // Only resolve Siemens.* assemblies from the Openness API folder.
            if (!name.StartsWith("Siemens.", StringComparison.Ordinal))
                return null;

            string path = Path.Combine(_libDir, name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        }
    }
}
