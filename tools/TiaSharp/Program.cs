using System;

namespace TiaSharp
{
    /// <summary>
    /// Entry point + command dispatcher. Each V1.0 PowerShell step becomes a subcommand
    /// here (mirrors the eventual MCP host model). Step 1 = "connect".
    ///
    /// Usage:  TiaSharp [command] [--api &lt;openness api folder&gt;]
    ///   command : connect   (default)
    ///   --api   : override the Openness API folder (else env TIA_API_DIR, else V17 default)
    /// </summary>
    internal static class Program
    {
        // Default Openness API folder for TIA V17. For V20 later, pass --api or set TIA_API_DIR.
        private const string DefaultApiDir =
            @"C:\Program Files\Siemens\Automation\Portal V17\PublicAPI\V17";

        // Openness requires the calling thread to be STA.
        [STAThread]
        private static int Main(string[] args)
        {
            string command = args.Length > 0 ? args[0].ToLowerInvariant() : "connect";
            string apiDir = GetApiDir(args);

            // Register the resolver BEFORE any Siemens.* type is referenced.
            // Main itself touches no Openness types, so it JITs safely; the command
            // methods (which DO use Openness) are only JIT-compiled when called below,
            // by which point the resolver is live.
            TiaResolver.Register(apiDir);

            switch (command)
            {
                case "connect":
                    return Commands.Connect(apiDir);
                case "listblocks":
                    return Commands.ListBlocks(apiDir, args);
                case "listhardware":
                    return Commands.ListHardware(apiDir);
                case "exportblock":
                    return Commands.ExportBlock(apiDir, args);
                case "exportdevice":
                    return Commands.ExportDevice(apiDir, args);
                case "creategroup":
                    return Commands.CreateGroup(apiDir, args);
                case "importblock":
                    return Commands.ImportBlock(apiDir, args);
                case "clonedevice":
                    return Commands.CloneDevice(apiDir, args);
                case "findaml":
                    return Commands.FindAml(args);
                case "createtags":
                    return Commands.CreateTags(apiDir, args);
                case "wirefb":
                    return Commands.WireFb(apiDir, args);
                case "importchain":
                    return Commands.ImportChain(apiDir, args);
                case "compile":
                    return Commands.Compile(apiDir, args);
                case "shell":
                    return Commands.Shell(apiDir);
                default:
                    Console.Error.WriteLine("Unknown command: " + command);
                    Console.Error.WriteLine("Available commands: connect, listblocks, listhardware, exportblock, exportdevice, creategroup, importblock, clonedevice, createtags, wirefb, importchain, compile, findaml, shell");
                    return 2;
         