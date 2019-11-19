﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Newtonsoft.Json;
using System.IO;
using Fclp;

namespace NewRelic
{
    static class Log
    {
        private static EventLog ELog = new EventLog("Application")
        {
            Source = "nri-perfmon"
        };
        public static bool Verbose;

        public static void WriteLog(string message, Log.LogLevel loglevel)
        {
            if (Log.Verbose)
            {
                Console.Error.WriteLine("Thread-" + (object)Thread.CurrentThread.ManagedThreadId + " : " + message);
            }
            else
            {
                if (loglevel == Log.LogLevel.VERBOSE)
                {
                    return;
                }
                if (loglevel == Log.LogLevel.CONSOLE)
                {
                    Console.Out.WriteLine(message);
                }
                else
                {
                    Log.ELog.WriteEntry(message, (EventLogEntryType)loglevel);
                }
            }
        }

        public static void WriteLog(string message, object toSerialize, Log.LogLevel loglevel)
        {
            if (loglevel == Log.LogLevel.CONSOLE && !Log.Verbose)
            {
                Log.WriteLog(JsonConvert.SerializeObject(toSerialize, Formatting.None), loglevel);
            }
            else
            {
                Log.WriteLog(message + ":\n" + JsonConvert.SerializeObject(toSerialize, Formatting.Indented), loglevel);
            }
        }

        public enum LogLevel
        {
            ERROR = 1,
            WARN = 2,
            INFO = 4,
            VERBOSE = 8,
            CONSOLE = 16
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var pollingIntervalFloor = 10000;
            var defaultCompName = "ThisComputer";
            var defaultConfigFilePath = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
            var defaultConfigFile = defaultConfigFilePath + "\\config.json";

            // create a generic parser for the ApplicationArguments type
            var parser = new FluentCommandLineParser<Options>();

            parser.Setup(arg => arg.ConfigFile)
            .As('c', "configFile")
            .SetDefault(defaultConfigFile)
            .WithDescription("Config file to use");

            parser.Setup(arg => arg.PollingInterval)
            .As('i', "pollInt")
            .SetDefault(pollingIntervalFloor)
            .WithDescription("Frequency of polling (ms)");

            parser.Setup(arg => arg.ComputerName)
            .As('n', "compName")
            .SetDefault(defaultCompName)
            .WithDescription("Name of computer that you want to poll");

            parser.Setup(arg => arg.Verbose)
            .As('v', "verbose")
            .SetDefault(false)
            .WithDescription("Verbose logging & pretty-print (for testing purposes)");

            parser.SetupHelp("?", "help")
             .Callback(text => Console.WriteLine(text));

            var parse = parser.Parse(args);

            if (parse.HasErrors)
            {
                parser.HelpOption.ShowHelp(parser.Options);
                Environment.Exit(1);
            } else if (parse.HelpCalled)
            {
                Environment.Exit(0);
            }

            var options = parser.Object;
            Log.Verbose = options.Verbose;
            options.ComputerName = Environment.GetEnvironmentVariable("COMPUTERNAME") ?? options.ComputerName;
            options.ConfigFile = Environment.GetEnvironmentVariable("CONFIGFILE") ?? options.ConfigFile;
            if (String.IsNullOrEmpty(options.ComputerName) || String.Equals(options.ComputerName, defaultCompName))
            {
                options.ComputerName = Environment.MachineName;
            }

            // All of the possibilities for polling interval figured here...
            string env_PollingInterval = Environment.GetEnvironmentVariable("POLLINGINTERVAL");
            int pollingInterval = pollingIntervalFloor;
            if(String.IsNullOrEmpty(env_PollingInterval) || !int.TryParse(env_PollingInterval, out pollingInterval))
            {
                pollingInterval = options.PollingInterval;
            }
            if (pollingInterval < pollingIntervalFloor)
            {
                pollingInterval = pollingIntervalFloor;
            }
            options.PollingInterval = pollingInterval;

            List<Counterlist> counterlist = null;
            try
            {
                StreamReader configFileReader = new StreamReader(options.ConfigFile);
                Config properties = JsonConvert.DeserializeObject<Config>(configFileReader.ReadToEnd());
                counterlist = properties.counterlist;
            }
            catch (IOException)
            {
                Log.WriteLog(options.ConfigFile + " could not be found or opened.", Log.LogLevel.ERROR);
                Environment.Exit(1);
            }

            if (counterlist == null || counterlist.Count == 0)
            {
                Log.WriteLog("'counterlist' is empty. Please verify " + options.ConfigFile + " is in the expected format (see README).", Log.LogLevel.ERROR);
                Environment.Exit(1);
            }

            List<Counterlist> mainCounters = new List<Counterlist>();
            List<Thread> eventThreads = new List<Thread>();

            foreach (var thisCounter in counterlist)
            {
                if (thisCounter.querytype.Equals(PerfmonPlugin.WMIEvent) || !thisCounter.querynamespace.Equals(PerfmonPlugin.DefaultNamespace))
                {
                    PerfmonPlugin aPlugin = new PerfmonPlugin(options, thisCounter);
                    Thread aThread = new Thread(new ThreadStart(aPlugin.RunThread));
                    eventThreads.Add(aThread);
                    aThread.Start();
                }
                else
                {
                    mainCounters.Add(thisCounter);
                }
            }

            if (mainCounters.Count > 0)
            {
                Log.WriteLog("nri-perfmon starting with options", (object)options, Log.LogLevel.INFO);
                Log.WriteLog("nri-perfmon counters", (object)mainCounters, Log.LogLevel.VERBOSE);
                PerfmonPlugin thisPlugin = new PerfmonPlugin(options, mainCounters);
                thisPlugin.RunThread();
            }

            // If the main function has nothing or exits, wait on other threads (which should stay running)
            foreach (Thread aThread in eventThreads)
            {
                aThread.Join();
            }
        }
    }
}
