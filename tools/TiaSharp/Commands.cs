using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Siemens.Engineering;
using Siemens.Engineering.Cax;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Tags;

namespace TiaSharp
{
    /// <summary>
    /// Command implementations. Each method is a faithful port of one V1.0 PowerShell step.
    /// These methods use Openness types, so they are only ever called AFTER
    /// TiaResolver.Register(...) has run in Program.Main.
    /// </summary>
    internal static class Commands
    {
        /// <summary>
        /// Step 1 port: connect to a running TIA Portal and read the open project.
        /// READ-ONLY. Changes nothing. Writes TiaSharp_connect_log.txt to the working dir.
        /// </summary>
        internal static int Connect(string apiDir)
        {
            var log = new Logger("TiaSharp_connect_log.txt");
            try
            {
                log.Line("C# port of V1.0 Step 1 (connect, READ-ONLY).");
                log.Line("64-bit process  : " + Environment.Is64BitProcess);
                log.Line("Apartment state : " + System.Threading.Thread.CurrentThread.GetApartmentState());
                log.Line("API folder      : " + apiDir);

                string dll = Path.Combine(apiDir, "Siemens.Engineering.dll");
                log.Line("DLL exists      : " + File.Exists(dll));

                // Find running TIA Portal instances.
                var procs = TiaPortal.GetProcesses();
                log.Line("Running TIA instances : " + procs.Count);
                if (procs.Count == 0)
                {
                    log.Line("No running TIA Portal. Open your V17 project first, then run again.");
                    return 1;
                }

                // Attach (approve the access dialog inside TIA).
                log.Line("Attaching to TIA... approve the access dialog inside TIA now.");
                TiaPortal portal = procs[0].Attach();
                log.Line("Attached to TIA Portal OK.");

                // Read the open project.
                log.Line("Open projects : " + portal.Projects.Count);
                if (portal.Projects.Count > 0)
                {
                    Project project = portal.Projects[0];
                    log.Line("Project name        : " + project.Name);
                    log.Line("Project path        : " + project.Path);
                    log.Line("Devices (top-level) : " + project.Devices.Count);
                }
                else
                {
                    log.Line("Connected, but no project is open.");
                }

                log.Line("=== DONE OK ===");
                return 0;
            }
            catch (Exception ex)
            {
                log.Line("ERROR type : " + ex.GetType().FullName);
                log.Line("ERROR msg  : " + ex.Message);

                // Surface loader exceptions (the same detail the PS script logged).
                var rtle = ex as ReflectionTypeLoadException;
                if (rtle != null && rtle.LoaderExceptions != null)
                    foreach (var le in rtle.LoaderExceptions)
                        log.Line("LOADER     : " + le.Message);

                log.Line("STACK      : " + ex.StackTrace);
                return 1;
            }
        }

        /// <summary>
        /// Step 2 port: list the PLC block tree (READ-ONLY). Optional --group &lt;name|A/B&gt; scopes
        /// the walk to one group. Writes TiaSharp_listblocks_log.txt.
        /// </summary>
        internal static int ListBlocks(string apiDir, string[] args)
        {
            var log = new Logger("TiaSharp_listblocks_log.txt");
            try
            {
                TiaPortal portal; Project project;
                if (!TryAttach(log, out portal, out project)) return 1;
                return ListBlocksCore(project, GetFlag(args, "--group"), log);
            }
            catch (Exception ex) { return LogError(log, ex); }
        }

        // Work for ListBlocks, operating on an already-attached project (reused by shell mode).
        private static int ListBlocksCore(Project project, string groupFilter, Logger log)
        {
            log.Line("Searching for PLC software...");
            PlcSoftware plc = null;
            foreach (Device d in project.Devices)
            {
                plc = FindPlc(d.DeviceItems);
                if (plc != null) break;
            }
            if (plc == null) { log.Line("No PLC software found in any device."); return 1; }
            log.Line("PLC software found : " + plc.Name);

            PlcBlockGroup root = plc.BlockGroup;
            if (!string.IsNullOrEmpty(groupFilter))
            {
                root = ResolveGroup(plc, groupFilter, false, log);
                if (root == null) { log.Line("Group not found: " + groupFilter); return 1; }
                log.Line("Scoped to group: " + root.Name);
            }

            log.Line("----- Block tree -----");
            int total = WalkBlocks(root, "", log);
            log.Line(string.Format("----- Total blocks: {0} -----", total));

            log.Line("=== DONE OK ===");
            return 0;
        }

        /// <summary>
        /// Step 7 port: list ALL devices/hardware (READ-ONLY), traversing every container
        /// (project.Devices + UngroupedDevicesGroup + DeviceGroups recursively), de-duped by name.
        /// Writes TiaSharp_listhardware_log.txt; compare the distinct count against TIA_Step7_log.txt.
        /// </summary>
        internal static int ListHardware(string apiDir)
        {
            var log = new Logger("TiaSharp_listhardware_log.txt");
            try
            {
                TiaPortal portal; Project project;
                if (!TryAttach(log, out portal, out project)) return 1;
                return ListHardwareCore(project, log);
            }
            catch (Exception ex) { return LogError(log, ex); }
        }

        // Work for ListHardware, operating on an already-attached project (reused by shell mode).
        private static int ListHardwareCore(Project project, Logger log)
        {
            try
            {
                log.Line("Top-level project.Devices : " + project.Devices.Count);

                var seen = new HashSet<string>();
                var all = new List<Device>();

                foreach (Device d in project.Devices) AddDevice(d, seen, all);
                try { CollectGroup(project.UngroupedDevicesGroup, seen, all); }
                catch { log.Line("  (no UngroupedDevicesGroup)"); }
                try { foreach (DeviceUserGroup g in project.DeviceGroups) CollectGroup(g, seen, all); }
                catch { log.Line("  (no DeviceGroups)"); }

                log.Line("TOTAL distinct devices found : " + all.Count);
                log.Line("===================================================");

                int n = 0;
                foreach (Device device in all)
                {
                    n++;
                    string dtid = TryStr(() => device.TypeIdentifier);
                    string suffix = string.IsNullOrEmpty(dtid) ? "" : "   <" + dtid + ">";
                    log.Line(string.Format("[{0}/{1}] DEVICE: {2}{3}", n, all.Count, device.Name, suffix));
                    WalkHw(device.DeviceItems, "    ", log);
                    log.Line("---------------------------------------------------");
                }

                log.Line(string.Format("----- Total distinct devices: {0} -----", all.Count));
                log.Line("=== DONE OK ===");
                return 0;
            }
            catch (Exception ex) { return LogError(log, ex); }
        }

        /// <summary>
        /// Step 3/4/6 port: export ONE block by name to SimaticML XML (always) and to SCL
        /// source (only for SCL/STL-capable blocks; GRAPH/FBD cannot generate SCL - that limit
        /// is reported, not fatal). Works for standard FBs, safety F-blocks, and GRAPH blocks.
        /// READ-ONLY. Usage: exportblock &lt;blockName&gt; [--out &lt;dir&gt;]
        /// </summary>
        internal static int ExportBlock(string apiDir, string[] args)
        {
            var log = new Logger("TiaSharp_exportblock_log.txt");
            try
            {
                string name = PositionalArg(args);
                if (string.IsNullOrEmpty(name)) { log.Line("Usage: exportblock <blockName> [--out <dir>]"); return 2; }
                string outDir = GetFlag(args, "--out") ?? DefaultExportDir();

                TiaPortal portal; Project project;
                if (!TryAttach(log, out portal, out project)) return 1;
                return ExportBlockCore(project, name, outDir, log);
            }
            catch (Exception ex) { return LogError(log, ex); }
        }

        // Work for ExportBlock, operating on an already-attached project (reused by shell mode).
        private static int ExportBlockCore(Project project, string name, string outDir, Logger log)
        {
            outDir = Path.GetFullPath(outDir); // Openness Export (like Import) refuses relative paths
            PlcSoftware plc = null;
            foreach (Device d in project.Devices) { plc = FindPlc(d.DeviceItems); if (plc != null) break; }
            if (plc == null) { log.Line("No PLC software found."); return 1; }

            PlcBlock block = FindBlock(plc.BlockGroup, name);
            if (block == null) { log.Line("Block not found: " + name); return 1; }
            log.Line(string.Format("Found block : {0}  (type {1}, lang {2})",
                block.Name, block.GetType().Name, block.ProgrammingLanguage));

            Directory.CreateDirectory(outDir);
            string xmlPath = Path.Combine(outDir, name + ".xml");
            string sclPath = Path.Combine(outDir, name + ".scl");
            if (File.Exists(xmlPath)) File.Delete(xmlPath);
            if (File.Exists(sclPath)) File.Delete(sclPath);

            // 1) XML (works for every consistent block: FB, F_FBD, GRAPH, ...).
            log.Line("Exporting XML...");
            block.Export(new FileInfo(xmlPath), ExportOptions.WithDefaults);
            log.Line("XML written : " + xmlPath);

            // 2) SCL source - only valid for SCL/STL blocks; report (not fail) otherwise.
            try
            {
                log.Line("Generating SCL source...");
                var list = new List<PlcBlock> { block };
                plc.ExternalSourceGroup.GenerateSource(list, new FileInfo(sclPath), GenerateOptions.None);
                log.Line("SCL written : " + sclPath);
            }
            catch (Exception sx)
            {
                log.Line("SCL source not generated (expected for non-SCL blocks like GRAPH/FBD): " + sx.Message);
            }

            log.Line("=== DONE OK ===");
            return 0;
        }

        /// <summary>
        /// Step 20 port: export ONE hardware device by name to AutomationML via CaxProvider
        /// (project-level service in V17). The .log file MUST keep the .log extension, and
        /// Export refuses to overwrite, so stale files are deleted first. READ-ONLY.
        /// Usage: exportdevice &lt;deviceName&gt; [--out &lt;dir&gt;]
        /// </summary>
        internal static int ExportDevice(string apiDir, string[] args)
        {
            var log = new Logger("TiaSharp_exportdevice_log.txt");
            try
            {
                string name = PositionalArg(args);
                if (string.IsNullOrEmpty(name)) { log.Line("Usage: exportdevice <deviceName> [--out <dir>]"); return 2; }
                string outDir = GetFlag(args, "--out") ?? DefaultExportDir();

                TiaPortal portal; Project project;
                if (!TryAttach(log, out portal, out project)) return 1;
                return ExportDeviceCore(project, name, outDir, log);
            }
            catch (Exception ex) { return LogError(log, ex); }
        }

        // Work for ExportDevice, operating on an already-attached project (reused by shell mode).
        private static int ExportDeviceCore(Project project, string name, string outDir, Logger log)
        {
            outDir = Path.GetFullPath(outDir); // Openness Export (like Import) refuses relative paths
            // CaxProvider comes from the PROJECT (not portal, not device) in V17. Native GetService<T> in C#.
            var cax = project.GetService<CaxProvider>();
            if (cax == null) { log.Line("CaxProvider not available from project - cannot export."); return 1; }

            // Find the device via full traversal (de-duped), then by name.
            var seen = new HashSet<string>();
            var all = new List<Device>();
            foreach (Device d in project.Devices) AddDevice(d, seen, all);
            try { CollectGroup(project.UngroupedDevicesGroup, seen, all); } catch { }
            try { foreach (DeviceUserGroup g in project.DeviceGroups) CollectGroup(g, seen, all); } catch { }

            Device device = all.Find(d => d.Name == name);
            if (device == null) { log.Line("Device not found: " + name); return 1; }
            log.Line("Found device: " + device.Name);

            Directory.CreateDirectory(outDir);
            string safe = SanitizeFileName(name);
            string amlPath = Path.Combine(outDir, safe + ".aml");
            string caxLog = Path.Combine(outDir, safe + ".caxexport.log"); // MUST end .log
            if (File.Exists(amlPath)) File.Delete(amlPath);
            if (File.Exists(caxLog)) File.Delete(caxLog);

            log.Line("Calling CaxProvider.Export -> " + amlPath);
            bool ok = cax.Export(device, new FileInfo(amlPath), new FileInfo(caxLog));
            log.Line("Export returned: " + ok);

            if (File.Exists(amlPath))
                log.Line(string.Format("AML written: {0}  ({1} bytes)", amlPath, new FileInfo(amlPath).Length));
            else
                log.Line("AML file NOT created - check the cax log below.");

            if (File.Exists(caxLog))
            {
                log.Line("----- CaxProvider export log -----");
                foreach (string l in File.ReadAllLines(caxLog)) log.Line("  " + l);
                log.Line("----- end cax log -----");
            }

            log.Line("=== DONE OK ===");
            return 0;
        }

        /// <summary>
        /// Step 8 port: ensure a program-block group exists (supports a nested path "A/B/C").
        /// WRITE - dry-run unless 'confirm' is given; never saves.
        /// Usage: creategroup &lt;name|A/B/C&gt; [confirm]
        /// </summary>
        internal static int CreateGroup(string apiDir, string[] args)
        {
            var log = new Logger("TiaSharp_creategroup_log.txt");
            try
            {
                string path = PositionalArg(args);
                if (string.IsNullOrEmpty(path)) { log.Line("Usage: creategroup <name|A/B/C> [confirm]"); return 2; }
                TiaPortal portal; Project project;
                if (!TryAttach(log, out portal, out project)) return 1;
                return CreateGroupCore(project, path, HasConfirm(args), log);
            }
            catch (Exception ex) { return LogError(log, ex); }
        }

        private static int CreateGroupCore(Project project, string path, bool confirm, Logger log)
        {
            PlcSoftware plc = FindAnyPlc(project, log);
            if (plc == null) return 1;

            if (!confirm)
            {
                log.Line("DRY-RUN: would ensure group path '" + path + "' exists. Add 'confirm' to apply. Nothing changed.");
                return 0;
            }

            PlcBlockGroup g = ResolveGroup(plc, path, true, log);
            if (g == null) return 1;
            log.Line("Group ready: " + g.Name + "  (not saved - Ctrl+S in TIA to keep, or delete to revert).");
            log.Line("=== DONE OK ===");
            return 0;
        }

        /// <summary>
        /// Step 13 port: import a SimaticML block XML into a target group. The path is resolved
        /// to ABSOLUTE (Openness Import rejects relative paths). Default does NOT overwrite an
        /// existing block (pass --override). Imported blocks are uncompiled - compile in TIA.
        /// WRITE - dry-run unless 'confirm' is given; never saves.
        /// Usage: importblock &lt;xmlPath&gt; --group &lt;name|A/B&gt; [--override] [confirm]
        /// </summary>
        internal static int ImportBlock(string apiDir, string[] args)
        {
            var log = new Logger("TiaSharp_importblock_log.txt");
            try
            {
                string xmlPath = PositionalArg(args);
                if (string.IsNullOrEmpty(xmlPath)) { log.Line("Usage: importblock <xmlPath> --group <name|A/B> [--as <NewName>] [--override] [confirm]"); return 2; }
                string groupPath = GetFlag(args, "--group");
                if (string.IsNullOrEmpty(groupPath)) { log.Line("Missing --group <targetGroup>."); return 2; }
                TiaPortal portal; Project project;
                if (!TryAttach(log, out portal, out project)) return 1;
                return ImportBlockCore(project, xmlPath, groupPath, GetFlag(args, "--as"),
                                       HasFlag(args, "--override"), HasConfirm(args), log);
            }
            catch (Exception ex) { return LogError(log, ex); }
        }

        private static int ImportBlockCore(Project project, string xmlPath, string groupPath, string newName,
                                           bool overrideExisting, bool confirm, Logger log)
        {
            PlcSoftware plc = FindAnyPlc(project, log);
            if (plc == null) return 1;

            string abs = Path.GetFullPath(xmlPath); // Openness Import requires an ABSOLUTE path.
            if (!File.Exists(abs)) { log.Line("File not found: " + abs); return 1; }

            PlcBlockGroup target = ResolveGroup(plc, groupPath, false, log);
            if (target == null) { log.Line("Target group not found: '" + groupPath + "' (create it first with creategroup)."); return 1; }

            string asNote = string.IsNullOrEmpty(newName) ? "" : " as '" + newName + "'";
            if (!confirm)
            {
                log.Line("DRY-RUN: would import '" + abs + "'" + asNote + " into group '" + target.Name +
                         "' (override=" + overrideExisting + "). Add 'confirm' to apply. Nothing changed.");
                return 0;
            }

            // Optional rename: rewrite the block name INSIDE the XML to a temp file (source file untouched).
            string fileToImport = abs;
            string tmp = null;
            if (!string.IsNullOrEmpty(newName))
            {
                tmp = WriteRenamedBlockXml(abs, newName, log);
                if (tmp == null) return 1;
                fileToImport = tmp;
            }

            try
            {
                var opt = overrideExisting ? ImportOptions.Override : ImportOptions.None;
                log.Line("Importing '" + fileToImport + "'" + asNote + " into '" + target.Name + "' (override=" + overrideExisting + ")...");
                var imported = target.Blocks.Import(new FileInfo(fileToImport), opt);
                log.Line("Imported blocks: " + imported.Count);
                foreach (PlcBlock b in imported) log.Line("  - " + b.Name + "  (" + b.GetType().Name + ")");
                log.Line("Imported blocks are uncompiled - COMPILE in TIA to verify. (Not saved automatically.)");
                log.Line("=== DONE OK ===");
                return 0;
            }
            finally
            {
                if (tmp != null && File.Exists(tmp)) File.Delete(tmp);
            }
        }

        /// <summary>
        /// Rewrite the block name inside an exported SimaticML file to a temp file (the source
        /// stays untouched). Changes the block's first &lt;Name&gt;, sets AutoNumber=true and clears
        /// the fixed &lt;Number&gt; so TIA assigns a free number - this is what lets a copied block
        /// import as a NEW, distinct block instead of colliding with the original.
        /// (Minimal text transform; complex self-referencing blocks may need more care.)
        /// </summary>
        private static string WriteRenamedBlockXml(string srcAbs, string newName, Logger log)
        {
            string text = File.ReadAllText(srcAbs);
            int bs = text.IndexOf("<SW.Blocks.", StringComparison.Ordinal);
            if (bs < 0) { log.Line("Could not find a <SW.Blocks.*> element to rename."); return null; }

            string before = text.Substring(0, bs);
            string body = text.Substring(bs);

            var nameRx = new Regex("<Name>([^<]*)</Name>");
            Match m = nameRx.Match(body);
            string oldName = m.Success ? m.Groups[1].Value : "(unknown)";
            body = nameRx.Replace(body, "<Name>" + newName + "</Name>", 1);
            body = new Regex("<AutoNumber>false</AutoNumber>").Replace(body, "<AutoNumber>true</AutoNumber>", 1);
            body = new Regex("<Number>[^<]*</Number>").Replace(body, "", 1);

            string tmp = Path.Combine(Path.GetDirectoryName(srcAbs), newName + ".import.xml");
            File.WriteAllText(tmp, before + body, new System.Text.UTF8Encoding(true)); // keep the BOM the exporter writes
            log.Line("Renamed block '" + oldName + "' -> '" + newName + "' (AutoNumber=true, Number cleared) -> temp file.");
            return tmp;
        }

        /// <summary>
        /// Find device-library AML files by partial name (no TIA needed). Helps build a --from value.
        /// Usage: findaml &lt;partialName&gt;
        /// </summary>
        internal static int FindAml(string[] args)
        {
            var log = new Logger("TiaSharp_findaml_log.txt");
            string needle = PositionalArg(args) ?? "";
            var hits = FindAmlFiles(needle);
            log.Line("AML matches for '" + needle + "': " + hits.Count);
            foreach (string h in hits) log.Line("  " + h);
            return 0;
        }

        /// <summary>
        /// Round B port (the full plc-device-clone recipe): export a template device to AML,
        /// transform it (rename device + group, set IP + PROFINET name, regenerate every GUID),
        /// import it (MoveToParkingLot), assign IoConnectors[0] to the IO system, and set the
        /// module IO start addresses (In/Out share each module base, packed from io_base, two-pass).
        /// WRITE - dry-run unless 'confirm'; never saves. F-params are auto-assigned by TIA;
        /// F-module addressing needs the safety program unlocked (refusals show in address errors).
        /// Usage: clonedevice &lt;template&gt; --as &lt;NewName&gt; --ip &lt;ip&gt; --pn &lt;pnName&gt; --group &lt;targetGroup&gt; --iobase &lt;int&gt; [--subnet X] [--iosystem Y] [--tempband N] [confirm]
        /// </summary>
        internal static int CloneDevice(string apiDir, string[] args)
        {
            var log = new Logger("TiaSharp_clonedevice_log.txt");
            try
            {
                string err;
                CloneOpts o = ParseCloneOpts(args, out err);
                if (o == null) { log.Line(err); return 2; }
                TiaPortal portal; Project project;
                if (!TryAttach(log, out portal, out project)) return 1;
                return CloneDeviceCore(project, o, HasConfirm(args), log);
            }
            catch (Exception ex) { return LogError(log, ex); }
        }

        private static int CloneDeviceCore(Project project, CloneOpts o, bool confirm, Logger log)
        {
            var cax = project.GetService<CaxProvider>();
            if (cax == null) { log.Line("CaxProvider not available from project."); return 1; }

            string cloneDir = Path.Combine(DefaultExportDir(), "clone_work");
            Directory.CreateDirectory(cloneDir);

            // Clean-slate expected: refuse if the clone name already exists.
            if (GatherDevices(project).Exists(d => string.Equals(d.Name, o.NewName, StringComparison.OrdinalIgnoreCase)))
            { log.Line("Device '" + o.NewName + "' already exists. Delete it in TIA first."); return 1; }

            // 1) obtain the source AML + the original device/group names.
            string srcAml, oldName, oldGroup;
            if (!string.IsNullOrEmpty(o.From))
            {
                // Clone FROM an existing AML file (e.g. the device library) - no live export needed.
                // Accepts a full path OR a partial name resolved against device_library / export.
                srcAml = ResolveAmlPath(o.From, log);
                if (srcAml == null) return 1;
                string pnPeek = AmlValue(File.ReadAllText(srcAml), "ProfinetDeviceName");
                if (!ReadAmlDeviceAndGroup(srcAml, pnPeek, out oldName, out oldGroup))
                { log.Line("Could not read the original device/group name from the AML."); return 1; }
                log.Line("Source AML: " + srcAml);
                log.Line("Detected original device '" + oldName + "' in group '" + oldGroup + "'.");
            }
            else
            {
                // Clone from a LIVE device: export it to AML first (read-only).
                Device template = FindDeviceAndGroup(project, o.Template, out oldGroup);
                if (template == null) { log.Line("Template device not found: " + o.Template); return 1; }
                oldName = o.Template;
                srcAml = Path.Combine(cloneDir, SanitizeFileName(o.Template) + ".aml");
                string srcLog = Path.Combine(cloneDir, SanitizeFileName(o.Template) + ".caxexport.log");
                if (File.Exists(srcAml)) File.Delete(srcAml);
                if (File.Exists(srcLog)) File.Delete(srcLog);
                log.Line("Exporting template '" + o.Template + "' to AML...");
                if (!cax.Export(template, new FileInfo(srcAml), new FileInfo(srcLog)))
                { log.Line("Template AML export returned false - see " + srcLog); return 1; }
            }

            // 2) transform the AML into a distinct clone (text edit + GUID regen + validate).
            string dstAml = Path.Combine(cloneDir, SanitizeFileName(o.NewName) + ".aml");
            CloneInfo info = TransformCloneAml(srcAml, dstAml, oldName, o.NewName, oldGroup, o.Group, o.Pn, o.Ip, log);
            log.Line("Transformed: device '" + oldName + "' -> '" + o.NewName + "', group '" + oldGroup + "' -> '" + o.Group + "'.");
            log.Line("PN '" + info.OldPn + "' -> '" + o.Pn + "',  IP '" + info.OldIp + "' -> '" + o.Ip + "'.");
            log.Line("Clone AML: " + dstAml);

            if (!confirm)
            {
                log.Line("DRY-RUN: template exported + transformed only (working files written, project UNCHANGED).");
                log.Line("Add 'confirm' to import + assign + set addresses.");
                return 0;
            }

            // 3) import the clone (MoveToParkingLot; "subnet/group already exists" warnings are harmless).
            string impLog = dstAml.Substring(0, dstAml.Length - 4) + ".import.log";
            if (File.Exists(impLog)) File.Delete(impLog);
            log.Line("Importing clone AML (MoveToParkingLot)...");
            cax.Import(new FileInfo(dstAml), new FileInfo(impLog), CaxImportOptions.MoveToParkingLot);

            Device newDev = GatherDevices(project).Find(d => string.Equals(d.Name, o.NewName, StringComparison.OrdinalIgnoreCase));
            if (newDev == null) { log.Line("Imported device '" + o.NewName + "' not found after import."); return 1; }

            // 4) assign IoConnectors[0] to the IO system (connector [1] = shared-device slot, leave it).
            IoSystem ios = FindIoSystem(project, o.Subnet, o.IoSystem);
            if (ios == null) { log.Line("No IO system found in project to assign to."); return 1; }
            bool assigned = false;
            foreach (DeviceItem it in newDev.DeviceItems)
            {
                var ni = FindNetworkInterface(it);
                if (ni != null) { ni.IoConnectors[0].ConnectToIoSystem(ios); assigned = true; break; }
            }
            if (!assigned) { log.Line("Assign failed: no NetworkInterface/IoConnector on '" + o.NewName + "'."); return 1; }
            log.Line("Assigned to IO system: " + ios.Name);

            // 5) set IO start addresses, TWO passes (park in temp band, then final base).
            var ioItems = new List<DeviceItem>();
            foreach (DeviceItem it in newDev.DeviceItems) WalkIo(it, ioItems);
            var addrErr = new List<string>();
            SetAddressPass(ioItems, o.TempBand, addrErr);
            SetAddressPass(ioItems, o.IoBase, addrErr);

            // read-back the actual addresses (authoritative).
            log.Line("IO modules: " + ioItems.Count + "   (io_base " + o.IoBase + ")");
            foreach (DeviceItem it in ioItems)
                foreach (Address x in it.Addresses)
                {
                    int len = GetIntAttr(x, "Length");
                    if (len <= 0) continue;
                    log.Line(string.Format("  {0}  {1}  start={2}  len_bits={3}",
                        it.Name, GetStrAttr(x, "IoType"), GetIntAttr(x, "StartAddress"), len));
                }
            if (addrErr.Count > 0)
            {
                log.Line("Address errors (e.g. F-module needs safety unlocked):");
                foreach (string e in addrErr) log.Line("  ! " + e);
            }

            log.Line("Clone done: imported + assigned + addressed (project NOT saved). " +
                     "F-params auto-assigned by TIA. Compile hardware + Ctrl+S in TIA to keep.");
            log.Line("=== DONE OK ===");
            return 0;
        }

        /// <summary>
        /// Round C (Step 26/28 port): create/complete a PLC tag table with the given tags.
        /// Idempotent: creates the table only if missing, creates only the tags whose NAME is not
        /// already present (a leftover partial table is completed, not duplicated), then reads back
        /// every tag (name/type/address) to verify. Failsafe-band addresses are refused while the
        /// safety program is locked - each failure is logged individually, the run continues.
        /// WRITE - dry-run unless 'confirm'; never saves.
        /// Usage: createtags &lt;tableName&gt; [--group &lt;name|A/B&gt;] [--tag Name:Type:%Addr]... [--file &lt;csv&gt;] [confirm]
        ///   --group: tag-table USER group path under the tag-table root (default: root itself);
        ///            missing group levels are created on confirm.
        ///   csv lines: name,type,address   (blank lines, #comments and a name,type,address header ignored)
        /// </summary>
        internal static int CreateTags(string apiDir, string[] args)
        {
            var log = new Logger("TiaSharp_createtags_log.txt");
            try
            {
                string err, table; List<TagSpec> tags;
                if (!ParseTagArgs(args, out table, out tags, out err)) { log.Line(err); return 2; }
                TiaPortal portal; Project project;
                if (!TryAttach(log, out portal, out project)) return 1;
                return CreateTagsCore(project, table, GetFlag(args, "--group"), tags, HasConfirm(args), log);
            }
            catch (Exception ex) { return LogError(log, ex); }
        }

        private sealed class TagSpec { public string Name, Type, Addr; }

        private static bool ParseTagArgs(string[] args, out string table, out List<TagSpec> tags, out string err)
        {
            err = null; tags = new List<TagSpec>();
            table = PositionalArg(args);
            if (string.IsNullOrEmpty(table))
            { err = "Usage: createtags <tableName> [--tag Name:Type:%Addr]... [--file <csv>] [confirm]"; return false; }

            foreach (string spec in GetFlagAll(args, "--tag"))
            {
                TagSpec t = ParseTagSpec(spec, ':');
                if (t == null) { err = "Bad --tag '" + spec + "' (expected Name:Type:%Address)."; return false; }
                tags.Add(t);
            }

            string file = GetFlag(args, "--file");
            if (!string.IsNullOrEmpty(file))
            {
                string abs = Path.GetFullPath(file);
                if (!File.Exists(abs)) { err = "Tag file not found: " + abs; return false; }
                foreach (string raw in File.ReadAllLines(abs))
                {
                    string lineTxt = raw.Trim();
                    if (lineTxt.Length == 0 || lineTxt.StartsWith("#")) continue;
                    if (lineTxt.ToLowerInvariant().Replace(" ", "") == "name,type,address") continue; // header
                    TagSpec t = ParseTagSpec(lineTxt, ',');
                    if (t == null) { err = "Bad tag line '" + lineTxt + "' (expected name,type,address)."; return false; }
                    tags.Add(t);
                }
            }

            if (tags.Count == 0) { err = "No tags given - use --tag and/or --file."; return false; }
            return true;
        }

        /// <summary>
        /// Parse one tag spec. The LAST two fields are type and address; everything before them is
        /// the name (device tag names contain '+' and '-' but never the separator character).
        /// </summary>
        private static TagSpec ParseTagSpec(string spec, char sep)
        {
            string[] p = spec.Split(sep);
            if (p.Length < 3) return null;
            string addr = p[p.Length - 1].Trim();
            string type = p[p.Length - 2].Trim();
            string name = string.Join(sep.ToString(), p, 0, p.Length - 2).Trim();
            if (name.Length == 0 || type.Length == 0 || !addr.StartsWith("%")) return null;
            return new TagSpec { Name = name, Type = type, Addr = addr };
        }

        private static int CreateTagsCore(Project project, string tableName, string groupPath, List<TagSpec> tags, bool confirm, Logger log)
        {
            PlcSoftware plc = FindAnyPlc(project, log);
            if (plc == null) return 1;

            // Resolve the tag-table group: root by default, else a (nested) user group under it.
            // On dry-run nothing is created; missing group levels are created on confirm.
            PlcTagTableGroup grp = ResolveTagTableGroup(plc, groupPath, false, null);
            if (grp == null && !confirm)
                log.Line("Tag-table group '" + groupPath + "': MISSING (would create).");
            if (grp == null && confirm)
            {
                grp = ResolveTagTableGroup(plc, groupPath, true, log);
                if (grp == null) return 1;
            }
            if (!string.IsNullOrEmpty(groupPath) && grp != null)
                log.Line("Tag-table group: " + grp.Name);

            // Find the table in the target group (convention: one table per station).
            PlcTagTable table = null;
            if (grp != null)
                foreach (PlcTagTable tt in grp.TagTables)
                    if (tt.Name == tableName) { table = tt; break; }

            var existing = new HashSet<string>();
            if (table != null) foreach (PlcTag t in table.Tags) existing.Add(t.Name);

            var missing = tags.Where(t => !existing.Contains(t.Name)).ToList();
            log.Line("Table '" + tableName + "': " + (table == null ? "MISSING (will create)" : "exists (" + existing.Count + " tag(s))") +
                     "   to create: " + missing.Count + ", already present: " + (tags.Count - missing.Count));

            if (!confirm)
            {
                foreach (TagSpec t in missing) log.Line("  + would create: " + t.Name + "  " + t.Type + "  " + t.Addr);
                foreach (TagSpec t in tags) if (existing.Contains(t.Name)) log.Line("  = exists, would skip: " + t.Name);
                log.Line("DRY-RUN: nothing changed. Add 'confirm' to apply. (Failsafe-band addresses need the safety program unlocked.)");
                return 0;
            }

            if (table == null)
            {
                table = grp.TagTables.Create(tableName);
                log.Line("Created tag table: " + table.Name);
            }

            int created = 0, failed = 0;
            foreach (TagSpec t in missing)
            {
                try
                {
                    PlcTag nt = CreateOneTag(table, t);
                    log.Line("  + created: " + nt.Name + "  " + t.Type + "  " + t.Addr);
                    created++;
                }
                catch (Exception e)
                {
                    log.Line("  ! TAG FAILED: " + t.Name + " @ " + t.Addr + "  -> " + e.Message +
                             (e.InnerException != null ? "  / " + e.InnerException.Message : ""));
                    failed++;
                }
            }
            log.Line("Created " + created + ", skipped " + (tags.Count - missing.Count) + ", failed " + failed +
                     (failed > 0 ? "   (failsafe-band tags are refused while safety is LOCKED)" : ""));

            // VERIFY: read back every tag's name/type/address (authoritative).
            log.Line("Verify (read-back of ALL tags in '" + tableName + "'):");
            foreach (PlcTag t in table.Tags)
                log.Line("    " + t.Name + "  " + GetStrAttr(t, "DataTypeName") + "  " + GetStrAttr(t, "LogicalAddress"));

            log.Line("=== DONE " + (failed == 0 ? "OK" : "WITH FAILURES") + " (NOT saved - Ctrl+S in TIA to keep, or close without saving to revert) ===");
            return failed == 0 ? 0 : 1;
        }

        /// <summary>Create one tag; if the data type is rejected, retry quoted then bare (UDT-name quirk).</summary>
        private static PlcTag CreateOneTag(PlcTagTable table, TagSpec t)
        {
            try { return table.Tags.Create(t.Name, t.Type, t.Addr); }
            catch
            {
                string bare = t.Type.Trim('"');
                try { return table.Tags.Create(t.Name, "\"" + bare + "\"", t.Addr); }
                catch { return table.Tags.Create(t.Name, bare, t.Addr); }
            }
        }

        /// <summary>
        /// Resolve (optionally create) a tag-table group by path ("A/B") under the tag-table root.
        /// Empty/null path = the root system group itself. Tag-table structure: the root
        /// PlcTagTableSystemGroup and nested PlcTagTableUserGroups each carry .TagTables + .Groups.
        /// Pass log=null to resolve silently (dry-run probing).
        /// </summary>
        private static PlcTagTableGroup ResolveTagTableGroup(PlcSoftware plc, string path, bool createMissing, Logger log)
        {
            if (string.IsNullOrEmpty(path)) return plc.TagTableGroup;

            PlcTagTableSystemGroup root = plc.TagTableGroup;
            PlcTagTableUserGroup cur = null;
            foreach (string seg in path.Split('/'))
            {
                string name = seg.Trim();
                if (name.Length == 0) continue;

                PlcTagTableUserGroupComposition groups = (cur == null) ? root.Groups : cur.Groups;
                PlcTagTableUserGroup next = null;
                foreach (PlcTagTableUserGroup g in groups)
                    if (g.Name == name) { next = g; break; }

                if (next == null)
                {
                    if (!createMissing)
                    {
                        if (log != null) log.Line("Tag-table group not found: " + name);
                        return null;
                    }
                    next = groups.Create(name);
                    if (log != null) log.Line("Created tag-table group: " + name);
                }
                cur = next;
            }
            return cur;
        }

        /// <summary>
        /// Round D (Step 29 port + the proven repoint recipe): repoint operands inside an exported
        /// FB XML (working copy; the source file stays untouched), verify by dumping the final
        /// operand list, then Override-import the block into its group. Map forms:
        ///    --map DbName:Member=GlobalTag   (DB-member access -> ONE global-tag component; the DB component is dropped)
        ///    --map OldTag=NewTag             (single-component rename)
        /// Matching is whitespace-tolerant (components are often newline-separated in real exports);
        /// a map that matches NOTHING aborts BEFORE import (the silent-failure gotcha). With no --map
        /// this is a plain Override import of the given XML (literal Step 29).
        /// WRITE - dry-run unless 'confirm' (the dry-run still writes the .wired.xml preview); never saves.
        /// Usage: wirefb &lt;xmlPath&gt; --group &lt;name|A/B&gt; [--map OLD=NEW]... [confirm]
        /// </summary>
        internal static int WireFb(string apiDir, string[] args)
        {
            var log = new Logger("TiaSharp_wirefb_log.txt");
            try
            {
                string xmlPath = PositionalArg(args);
                string groupPath = GetFlag(args, "--group");
                if (string.IsNullOrEmpty(xmlPath) || string.IsNullOrEmpty(groupPath))
                { log.Line("Usage: wirefb <xmlPath> --group <name|A/B> [--map OLD=NEW]... [confirm]"); return 2; }
                TiaPortal portal; Project project;
                if (!TryAttach(log, out portal, out project)) return 1;
                return WireFbCore(project, xmlPath, groupPath, GetFlagAll(args, "--map"), HasConfirm(args), log);
            }
            catch (Exception ex) { return LogError(log, ex); }
        }

        private static int WireFbCore(Project project, string xmlPath, string groupPath, List<string> maps, bool confirm, Logger log)
        {
            PlcSoftware plc = FindAnyPlc(project, log);
            if (plc == null) return 1;

            string abs = Path.GetFullPath(xmlPath);
            if (!File.Exists(abs)) { log.Line("FB XML not found: " + abs); return 1; }

            PlcBlockGroup target = ResolveGroup(plc, groupPath, false, log);
            if (target == null) { log.Line("Target group not found: '" + groupPath + "'."); return 1; }

            string text = File.ReadAllText(abs);
            if (maps.Count == 0) log.Line("No --map given: plain Override import of the XML as-is.");

            foreach (string map in maps)
            {
                int eq = map.IndexOf('=');
                if (eq <= 0 || eq == map.Length - 1) { log.Line("Bad --map '" + map + "' (expected OLD=NEW)."); return 2; }
                string oldOp = map.Substring(0, eq).Trim();
                string newTag = map.Substring(eq + 1).Trim().Trim('"');

                string pattern;
                int colon = oldOp.IndexOf(':');
                if (colon > 0)
                {
                    // TWO components (DB then member) -> ONE global-tag component (drop the DB).
                    string db = Regex.Escape(oldOp.Substring(0, colon).Trim().Trim('"'));
                    string member = Regex.Escape(oldOp.Substring(colon + 1).Trim().Trim('"'));
                    pattern = "<Component\\s+Name=\"" + db + "\"(?:\\s[^>]*)?/>\\s*<Component\\s+Name=\"" + member + "\"(?:\\s[^>]*)?/>";
                }
                else
                {
                    pattern = "<Component\\s+Name=\"" + Regex.Escape(oldOp.Trim('"')) + "\"(?:\\s[^>]*)?/>";
                }

                var rx = new Regex(pattern);
                int n = rx.Matches(text).Count;
                if (n == 0)
                {
                    log.Line("MAP MATCHED NOTHING: '" + map + "' - ABORT before import. Check the DB/member/tag spelling");
                    log.Line("against the exported XML (multi-component operands are newline-separated - already handled).");
                    return 1;
                }
                text = rx.Replace(text, "<Component Name=\"" + newTag + "\" />");
                log.Line("  repointed " + n + " operand(s): " + oldOp + "  ->  " + newTag);
            }

            // Validate well-formedness BEFORE anything can reach TIA (a malformed XML can crash it).
            var doc = new XmlDocument();
            doc.LoadXml(text);

            string wired = Path.Combine(Path.GetDirectoryName(abs), Path.GetFileNameWithoutExtension(abs) + ".wired.xml");
            File.WriteAllText(wired, text, new UTF8Encoding(true)); // keep the exporter-style BOM
            log.Line("Wired XML written (working file; source untouched): " + wired);

            // VERIFY: dump every distinct <Symbol> component chain - the final operand list.
            log.Line("Final operand list (distinct):");
            var chains = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (Match sm in Regex.Matches(text, "<Symbol[^>]*>([\\s\\S]*?)</Symbol>"))
            {
                var names = new List<string>();
                foreach (Match cm in Regex.Matches(sm.Groups[1].Value, "<Component\\s+Name=\"([^\"]*)\""))
                    names.Add(cm.Groups[1].Value);
                string chain = string.Join(".", names);
                chains[chain] = chains.ContainsKey(chain) ? chains[chain] + 1 : 1;
            }
            foreach (var kv in chains) log.Line("    " + kv.Key + "   (x" + kv.Value + ")");

            if (!confirm)
            {
                log.Line("DRY-RUN: transform + verify only, NOTHING imported. Check the operand list above, then add 'confirm'.");
                return 0;
            }

            log.Line("Override-importing wired FB into '" + target.Name + "'...");
            var imported = target.Blocks.Import(new FileInfo(wired), ImportOptions.Override);
            foreach (PlcBlock b in imported) log.Line("  imported: " + b.Name + "  (" + b.GetType().Name + ")");
            log.Line("Imported block is uncompiled - compile the group ('compile --group ...') and expect 0 errors.");
            log.Line("=== DONE OK (NOT saved - Ctrl+S in TIA to keep) ===");
            return 0;
        }

        /// <summary>
        /// Round E (Step 35 port) - the F-WRITE GATE: import an ORDERED list of block XMLs into one
        /// group (Override), each file independently try/caught so a refused F-object is pinpointed
        /// instead of killing the whole run. Proven order: standard DBs -> safety DBs -> FFB -> FB.
        /// The SAFETY PROGRAM MUST BE UNLOCKED in TIA first, or every F-object is rejected - and
        /// safety RE-LOCKS on every TIA restart, so check each session. Re-running after unlock is
        /// safe (imports are Override).
        /// WRITE - dry-run unless 'confirm'; does NOT compile; never saves.
        /// Usage: importchain --group &lt;name|A/B&gt; --files &lt;a.xml,b.xml,...&gt; [--file &lt;x.xml&gt;]... [confirm]
        /// </summary>
        internal static int ImportChain(string apiDir, string[] args)
        {
            var log = new Logger("TiaSharp_importchain_log.txt");
            try
            {
                string groupPath = GetFlag(args, "--group");
                var files = new List<string>();
                string csv = GetFlag(args, "--files");
                if (!string.IsNullOrEmpty(csv))
                    files.AddRange(csv.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0));
                files.AddRange(GetFlagAll(args, "--file"));
                if (string.IsNullOrEmpty(groupPath) || files.Count == 0)
                { log.Line("Usage: importchain --group <name|A/B> --files <a.xml,b.xml,...> [--file <x.xml>]... [confirm]"); return 2; }
                TiaPortal portal; Project project;
                if (!TryAttach(log, out portal, out project)) return 1;
                return ImportChainCore(project, groupPath, files, HasConfirm(args), log);
            }
            catch (Exception ex) { return LogError(log, ex); }
        }

        private static int ImportChainCore(Project project, string groupPath, List<string> files, bool confirm, Logger log)
        {
            PlcSoftware plc = FindAnyPlc(project, log);
            if (plc == null) return 1;

            PlcBlockGroup target = ResolveGroup(plc, groupPath, false, log);
            if (target == null) { log.Line("Target group not found: '" + groupPath + "'."); return 1; }
            log.Line("Target group: " + target.Name + "   files: " + files.Count + "  (imported IN THIS ORDER)");

            var resolved = new List<string>();
            bool anyMissing = false;
            foreach (string f in files)
            {
                string abs = Path.GetFullPath(f);
                bool ok = File.Exists(abs);
                if (!ok) anyMissing = true;
                log.Line("  " + (ok ? "ok       " : "MISSING  ") + abs);
                resolved.Add(abs);
            }

            if (!confirm)
            {
                log.Line("DRY-RUN: nothing imported. REMINDER: F-blocks (FFB/F-DBs) need the safety program");
                log.Line("UNLOCKED in TIA - and safety RE-LOCKS on every TIA restart. Add 'confirm' to import.");
                return anyMissing ? 1 : 0;
            }

            int okCount = 0, failCount = 0;
            foreach (string abs in resolved)
            {
                string name = Path.GetFileName(abs);
                if (!File.Exists(abs)) { log.Line("  SKIPPED (missing): " + name); failCount++; continue; }
                try
                {
                    var imported = target.Blocks.Import(new FileInfo(abs), ImportOptions.Override);
                    log.Line("  IMPORTED ok: " + name + "  (" + imported.Count + " block(s))");
                    okCount++;
                }
                catch (Exception e)
                {
                    log.Line("  IMPORT FAILED: " + name + "  -> " + e.Message);
                    if (e.InnerException != null) log.Line("       INNER: " + e.InnerException.Message);
                    failCount++;
                }
            }

            log.Line("Chain done: " + okCount + " ok, " + failCount + " failed.");
            if (failCount > 0)
                log.Line("An F-object refusal usually means the safety program is LOCKED - unlock in TIA and re-run (Override makes re-runs safe).");
            log.Line("NOT compiled, NOT saved - compile the group ('compile --group ...' or in TIA), check 0 errors, then Ctrl+S.");
            log.Line("=== DONE " + (failCount == 0 ? "OK" : "WITH FAILURES") + " ===");
            return failCount == 0 ? 0 : 1;
        }

        /// <summary>
        /// ONE compile at the highest level (V17 has no group-level compile - proven live:
        /// PlcBlockGroup exposes no ICompilable; per-block loops pay the F-consistency cost
        /// once PER BLOCK, so we don't do that anymore).
        ///   compile                     -> ONE software-level compile of the PLC software
        ///   compile --all / --group <g> -> same single software pass (--group is just noted)
        ///   compile <blockName>         -> that block only
        ///   compile ... --hw [<device>] -> ALSO one hardware compile (named device, else the
        ///                                  PLC station). --hw alone = hardware only. A HW
        ///                                  compile (re)generates the F-I/O QBAD DBs.
        /// Changes compile state in memory only; never saves.
        /// </summary>
        internal static int Compile(string apiDir, string[] args)
        {
            var log = new Logger("TiaSharp_compile_log.txt");
            try
            {
                string group = GetFlag(args, "--group");
                string pos = PositionalArg(args);
                bool all = HasFlag(args, "--all") || string.Equals(pos, "all", StringComparison.OrdinalIgnoreCase);
                string block = all ? null : pos;
                bool hw; string hwDev;
                ParseHwFlag(args, out hw, out hwDev);
                TiaPortal portal; Project project;
                if (!TryAttach(log, out portal, out project)) return 1;
                return CompileCore(project, block, group, all, hw, hwDev, log);
            }
            catch (Exception ex) { return LogError(log, ex); }
        }

        /// <summary>--hw is a bare flag with an OPTIONAL device-name value.</summary>
        private static void ParseHwFlag(string[] args, out bool hw, out string hwDevice)
        {
            hw = false; hwDevice = null;
            for (int i = 0; i < args.Length; i++)
                if (args[i].Equals("--hw", StringComparison.OrdinalIgnoreCase))
                {
                    hw = true;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--") &&
                        !args[i + 1].Equals("confirm", StringComparison.OrdinalIgnoreCase) &&
                        !args[i + 1].Equals("y", StringComparison.OrdinalIgnoreCase))
                        hwDevice = args[i + 1];
                }
        }

        private static int CompileCore(Project project, string blockName, string groupPath, bool all,
                                       bool hw, string hwDevice, Logger log)
        {
            PlcSoftware plc = FindAnyPlc(project, log);
            if (plc == null) return 1;

            int errors = 0, warnings = 0;
            // Software pass is wanted unless the request was hardware-ONLY.
            bool swWanted = !string.IsNullOrEmpty(blockName) || all || !string.IsNullOrEmpty(groupPath) || !hw;

            if (!string.IsNullOrEmpty(blockName))
            {
                PlcBlock b = FindBlock(plc.BlockGroup, blockName);
                if (b == null) { log.Line("Block not found: " + blockName); return 1; }
                var bc = b.GetService<ICompilable>();
                if (bc == null) { log.Line("Block '" + b.Name + "' exposes no ICompilable service."); return 1; }
                log.Line("Compiling block '" + b.Name + "' ...");
                ReportCompile(bc.Compile(), log, ref errors, ref warnings);
            }
            else if (swWanted)
            {
                if (!string.IsNullOrEmpty(groupPath))
                    log.Line("Note: V17 cannot compile a group by itself - doing ONE software-level compile (covers '" + groupPath + "').");
                log.Line("Compiling PLC software (ONE pass): " + plc.Name + " ...  (F-consistency processing can take minutes - not a hang)");
                var comp = plc.GetService<ICompilable>();
                if (comp == null) { log.Line("PlcSoftware exposes no ICompilable service."); return 1; }
                ReportCompile(comp.Compile(), log, ref errors, ref warnings);
            }

            if (hw)
            {
                Device dev = null;
                if (!string.IsNullOrEmpty(hwDevice))
                {
                    dev = GatherDevices(project).Find(d => string.Equals(d.Name, hwDevice, StringComparison.OrdinalIgnoreCase));
                    if (dev == null) { log.Line("HW device not found: " + hwDevice); return 1; }
                }
                else
                {
                    foreach (Device d in GatherDevices(project))
                        if (FindPlc(d.DeviceItems) != null) { dev = d; break; }
                    if (dev == null) { log.Line("PLC station for the hardware compile not found."); return 1; }
                }
                ICompilable hc = dev.GetService<ICompilable>();
                if (hc == null)
                    foreach (DeviceItem it in dev.DeviceItems)
                    { hc = it.GetService<ICompilable>(); if (hc != null) break; }
                if (hc == null) { log.Line("No hardware compile service on '" + dev.Name + "'."); return 1; }
                log.Line("Compiling HARDWARE (ONE pass): " + dev.Name + " ...");
                ReportCompile(hc.Compile(), log, ref errors, ref warnings);
            }

            log.Line("TOTAL: " + errors + " error(s), " + warnings + " warning(s).");
            log.Line("=== DONE " + (errors == 0 ? "OK" : "WITH ERRORS") + " (compile state changed in memory only; NOT saved) ===");
            return errors == 0 ? 0 : 1;
        }

        private static void ReportCompile(CompilerResult r, Logger log, ref int errors, ref int warnings)
        {
            errors += r.ErrorCount; warnings += r.WarningCount;
            log.Line("  result: " + r.State + "   errors=" + r.ErrorCount + "  warnings=" + r.WarningCount);
            foreach (CompilerResultMessage m in r.Messages) ReportCompileMsg(m, "    ", log);
        }

        private static void ReportCompileMsg(CompilerResultMessage m, string indent, Logger log)
        {
            string path = TryStr(() => m.Path);
            log.Line(indent + m.State + "  " + (string.IsNullOrEmpty(path) ? "" : path + ": ") + m.Description);
            foreach (CompilerResultMessage c in m.Messages) ReportCompileMsg(c, indent + "  ", log);
        }

        /// <summary>
        /// Persistent-session mode: attach to TIA ONCE (a single access-dialog approval), then
        /// loop reading commands from stdin and dispatching them against the SAME live project -
        /// no re-attach, no repeated dialog. This is the V1.5 preview of the V2.5 MCP host loop.
        /// (Read-only commands for now; write/clone/tags/safety are added next.)
        /// </summary>
        internal static int Shell(string apiDir)
        {
            var log = new Logger("TiaSharp_shell_log.txt");
            try
            {
                TiaPortal portal; Project project;
                if (!TryAttach(log, out portal, out project)) return 1;

                Console.WriteLine();
                Console.WriteLine("TiaSharp shell - attached once, no re-attach. Type 'help' or 'exit'.");
                PrintShellHelp();

                Action pending = null; // last dry-run write; applied by typing 'confirm' / 'y'

                while (true)
                {
                    Console.Write("tia> ");
                    string line = Console.ReadLine();
                    if (line == null) break;             // EOF (Ctrl+Z, Enter)
                    line = line.Trim();
                    if (line.Length == 0) continue;

                    string[] parts = Tokenize(line);
                    string cmd = parts[0].ToLowerInvariant();
                    if (cmd == "exit" || cmd == "quit") break;

                    // Any new command invalidates a pending preview - you can only confirm the latest.
                    if (cmd != "confirm" && cmd != "y") pending = null;

                    try
                    {
                        DispatchShell(project, cmd, parts, log, ref pending);
                    }
                    catch (Exception cmdEx)
                    {
                        // One bad command must not kill the attached session.
                        LogError(log, cmdEx);
                    }
                }

                log.Line("Shell closed - single attached session, no re-attach occurred.");
                return 0;
            }
            catch (Exception ex) { return LogError(log, ex); }
        }

        /// <summary>
        /// Dispatch ONE shell command (used by the interactive loop AND by 'order' files).
        /// 'pending' carries the last dry-run write preview; 'confirm'/'y' applies it.
        /// </summary>
        private static void DispatchShell(Project project, string cmd, string[] parts, Logger log, ref Action pending)
        {
                        switch (cmd)
                        {
                            case "confirm":
                            case "y":
                                if (pending != null) { var apply = pending; pending = null; apply(); }
                                else Console.WriteLine("Nothing to confirm.");
                                break;
                            case "help":
                                PrintShellHelp();
                                break;
                            case "status":
                                Console.WriteLine("Project: " + project.Name +
                                                  "   top-level devices: " + project.Devices.Count);
                                break;
                            case "listblocks":
                                ListBlocksCore(project, GetFlag(parts, "--group"), log);
                                break;
                            case "listhardware":
                                ListHardwareCore(project, log);
                                break;
                            case "findaml":
                            {
                                string needle = PositionalArg(parts) ?? "";
                                var hits = FindAmlFiles(needle);
                                Console.WriteLine("AML matches for '" + needle + "': " + hits.Count);
                                foreach (string h in hits) Console.WriteLine("  " + h);
                        