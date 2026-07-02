using System;
using System.IO;

namespace TiaSharp
{
    /// <summary>
    /// Tiny logger that mirrors the V1.0 PowerShell "Log" function:
    /// writes "HH:mm:ss  message" to both the console and a log file in the
    /// current working directory, so output can be diffed against the V1 *_log.txt files.
    /// </summary>
    internal sealed class Logger
    {
        private readonly string _path;

        internal Logger(string fileName)
        {
            _path = Path.Combine(Directory.GetCurrentDirectory(), fileName);
            File.WriteAllText(_path, "=== TiaSharp run started " + DateTime.Now + " ===" + Environment.NewLine);
        }

        internal void Line(string msg)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + msg;
            File.AppendAllText(_path, line + Environment.NewLine);
            Console.WriteLine(line);
        }
    }
}
