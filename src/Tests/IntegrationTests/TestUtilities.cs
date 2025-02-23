﻿// Copyright 2015-2018 The NATS Authors
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Threading;
using System.Diagnostics;
using System.IO;
using NATS.Client;
#if NET452
using System.Reflection;
#endif

namespace IntegrationTests
{
    class NATSServer : IDisposable
    {
#if NET452
        static readonly string SERVEREXE = "nats-server.exe";
#else
        static readonly string SERVEREXE = "nats-server";
#endif
        // Enable this for additional server debugging info.
        static bool debug = false;
        static bool hideWindow = true;
        static bool leaveRunning = false;
        Process p;
        ProcessStartInfo psInfo;

        // enables debug/trace on the server
        internal static bool Debug { set => debug = value; get => debug; }

        // hides the NATS server window.  Default is true;
        internal static bool HideWindow { set => hideWindow = value; get => hideWindow; }

        // leaves the server running after dispose for debugging.
        internal static bool LeaveRunning { set => leaveRunning = value; get => leaveRunning; }

        public NATSServer() : this(true) { }

        public NATSServer(bool verify)
        {
            createProcessStartInfo();
            p = Process.Start(psInfo);
            if (verify)
            {
                for (int i = 0; i < 10; i++)
                {
                    try
                    {
                        var c = new ConnectionFactory().CreateConnection();
                        c.Close();
                        break;
                    }
                    catch
                    {
                        Thread.Sleep(i * 250);
                    }
                }
            }
        }

        private void addArgument(string arg)
        {
            if (psInfo.Arguments == null)
            {
                psInfo.Arguments = arg;
            }
            else
            {
                string args = psInfo.Arguments;
                args += arg;
                psInfo.Arguments = args;
            }
        }

        public NATSServer(int port)
        {
            createProcessStartInfo();
            addArgument("-p " + port);

            p = Process.Start(psInfo);
            Thread.Sleep(500);
        }

        public NATSServer(string args)
        {
            createProcessStartInfo();
            addArgument(args);
            p = Process.Start(psInfo);
            Thread.Sleep(500);
        }

        private void createProcessStartInfo()
        {
            psInfo = new ProcessStartInfo(SERVEREXE);

            if (debug)
            {
                psInfo.Arguments = " -DV ";
            }
            else
            {
                if (hideWindow)
                {
#if NET452
                    psInfo.WindowStyle = ProcessWindowStyle.Hidden;
#else
                    psInfo.CreateNoWindow = true;
#endif
                }
            }

            psInfo.WorkingDirectory = UnitTestUtilities.GetConfigDir();
        }

        public void Bounce(int millisDown)
        {
            Shutdown();
            Thread.Sleep(millisDown);
            p = Process.Start(psInfo);
            Thread.Sleep(500);
        }

        public void Shutdown()
        {
            if (p == null)
                return;

            try
            {
                p.Kill();
            }
            catch (Exception) { }

            p = null;
        }

        void IDisposable.Dispose()
        {
            if (leaveRunning)
                return;

            Shutdown();
        }
    }

    class UnitTestUtilities
    {
        static UnitTestUtilities()
        {
            CleanupExistingServers();
        }

        internal static string GetConfigDir()
        {
            var configDirPath = Path.Combine(Environment.CurrentDirectory, "config");
            if(!Directory.Exists(configDirPath))
                throw new DirectoryNotFoundException($"The Config dir was not found at: '{configDirPath}'.");

            return configDirPath;
        }

        public Options DefaultTestOptions
        {
            get
            {
                var opts = ConnectionFactory.GetDefaultOptions();
                opts.Timeout = 10000;
                return opts;
            }
        }

        public IConnection DefaultTestConnection
        {
            get
            {
                return new ConnectionFactory().CreateConnection(DefaultTestOptions);
            }
        }
       
        internal NATSServer CreateServerOnPort(int p)
        {
            return new NATSServer(p);
        }

        internal NATSServer CreateServerWithConfig(string configFile)
        {
            return new NATSServer(" -config " + configFile);
        }

        internal NATSServer CreateServerWithArgs(string args)
        {
            return new NATSServer(" " + args);
        }

        internal static string GetFullCertificatePath(string certificateName)
            => Path.Combine(GetConfigDir(), "certs", certificateName);

        internal static void CleanupExistingServers()
        {
            Process[] procs = Process.GetProcessesByName("nats-server");
            if (procs == null)
                return;

            foreach (Process proc in procs)
            {
                try
                {
                    proc.Kill();
                }
                catch (Exception) { } // ignore
            }

            // Let the OS cleanup.
            for (int i = 0; i < 10; i++)
            {
                procs = Process.GetProcessesByName("nats-server");
                if (procs == null || procs.Length == 0)
                    break;

                Thread.Sleep(i * 250);
            }

            Thread.Sleep(250);
        }
    }
}
