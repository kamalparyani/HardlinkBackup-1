﻿using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using HardLinkBackup;
using Renci.SshNet;

namespace Backuper
{
    public static class Program
    {
        private static readonly string[] SshParams = {"-sl:", "-sp:", "-sr:", "-sh:", "-spp:"};

        private static int? _previousCategory;
        private static SshClient _client;

        private static bool TryGetParameter(string[] args, string name, out string value)
        {
            value = null;
            value = args.FirstOrDefault(x => x.StartsWith(name))?.Replace(name, null);
            return !string.IsNullOrEmpty(value);
        }

        private static string GetParameter(string[] args, string name)
        {
            if (!TryGetParameter(args, name, out var value))
                throw new Exception("Failed to get arg " + name);

            return value;
        }

        // -s:<source>
        // -t:<target>
        // -sl:<ssh login>
        // -sp:<ssh password>
        // -sr:<ssh root dir>
        // -sh:<ssh host>
        // -spp:<ssh port>
        public static async Task Main(string[] args)
        {
            System.Diagnostics.Debugger.Launch();

            Console.OutputEncoding = System.Text.Encoding.UTF8;

            if (args?.FirstOrDefault()?.StartsWith("-bdf:") ?? false)
            {
                return;
            }

            if (args == null || args.Length == 0)
            {
                Console.WriteLine("Wrong args");
                return;
            }

            if (!TryGetParameter(args, "-s:", out var source))
            {
                Console.WriteLine("Source folder is not specified");
                return;
            }

            if (!Directory.Exists(source))
            {
                Console.WriteLine("Source folder does not exist");
                return;
            }

            if (!TryGetParameter(args, "-t:", out var target))
            {
                Console.WriteLine("Target folder is not specified");
                return;
            }

            IHardLinkHelper helper;
            var networkConnection = Helpers.GetDummyDisposable();

            var sshParams = args.Where(x => SshParams.Any(x.StartsWith)).ToList();
            if (sshParams.Count == 0)
            {
                helper = new WinHardLinkHelper();
            }
            else
            {
                if (sshParams.Count == SshParams.Length)
                {
                    var sshLogin = GetParameter(args, "-sl:");
                    var sshPwd = GetParameter(args, "-sp:");
                    var sshHost = GetParameter(args, "-sh:");
                    var sshHostRoot = GetParameter(args, "-sr:");
                    var sshPort = int.Parse(GetParameter(args, "-spp:"));

                    Console.WriteLine($"Connecting to {sshLogin}@{sshHost}:{sshPort}...");

                    var ci = new ConnectionInfo(sshHost, sshPort, sshLogin, new PasswordAuthenticationMethod(sshLogin, sshPwd));

                    _client = new SshClient(ci);
                    _client.Connect();

                    helper = new NetShareSshHardLinkHelper(target, sshHostRoot, _client);
                    networkConnection = new NetworkConnection($@"\\{sshHost}", new NetworkCredential(sshLogin, sshPwd));
                }
                else
                {
                    Console.WriteLine("Wrong ssh args");
                    return;
                }
            }

            using (networkConnection)
            using(_client ?? Helpers.GetDummyDisposable())
            {
                if (!Directory.Exists(target))
                {
                    Console.WriteLine("Target folder does not exist");
                    return;
                }

                try
                {
                    var testFile = Path.Combine(target, "write_access_test.txt");
                    if (File.Exists(testFile))
                        File.Delete(testFile);

                    File.WriteAllText(testFile, "Write access test file");
                    File.Delete(testFile);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Failed to write in target directory:\r\n" + e.Message);
                    return;
                }

                await BackupHardLinks(source, target, helper);
            }

            Console.WriteLine("Done. Press return to exit");

            Console.ReadLine();
        }

        private static async Task BackupHardLinks(string source, string target, IHardLinkHelper helper)
        {
            Console.CursorVisible = false;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                using (var vssHelper = new VssHelper(new DirectoryInfo(source).Root.Name))
                {
                    Console.WriteLine("Creating VSS snapshot...");

                    var actualSource = vssHelper.CreateSnapshot()
                        ? vssHelper.GetSnapshotFilePath(source)
                        : source;

                    var engine = new HardLinkBackupEngine(actualSource, target, true, helper);
                    engine.Log += WriteLog;
                    engine.LogExt += WriteLogExt;
                    await engine.DoBackup();
                }

                sw.Stop();

                Console.WriteLine($"Done in {sw.Elapsed:hh\\:mm\\:ss}");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            finally
            {
                sw.Stop();
                Console.CursorVisible = true;
            }
        }

        private static void WriteLogExt(string msg)
        {
            var left = Console.CursorLeft;

            Console.Write("".PadRight(Console.BufferWidth - 1 - left));
            Console.CursorLeft = left;

            Console.Write(msg);

            Console.CursorLeft = left;
        }

        private const string LogFileName = "log.txt";

        private static void WriteLog(string msg, int category)
        {
            Console.CursorLeft = 0;
            Console.Write("".PadRight(Console.BufferWidth - 1));
            Console.CursorLeft = 0;

            if (category == _previousCategory)
                Console.Write(msg);
            else
                Console.WriteLine(msg);

            File.AppendAllText(LogFileName, msg + "\r\n");

            _previousCategory = category;
        }
    }
}