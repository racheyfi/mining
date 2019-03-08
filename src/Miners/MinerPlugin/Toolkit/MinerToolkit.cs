﻿using NiceHashMinerLegacy.Common.Algorithm;
using NiceHashMinerLegacy.Common.Device;
using NiceHashMinerLegacy.Common.Enums;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// alias
using TimersTimer = System.Timers.Timer;

namespace MinerPlugin.Toolkit
{
    public static class MinerToolkit
    {
        /// <summary>
        /// Use DemoUser if the miner requires a network benchmark the plugin will blacklist users
        /// </summary>
        public static string DemoUser { get { return "33hGFJZQAfbdzyHGqhJPvZwncDjUBdZqjW"; } }

        public static (AlgorithmType algorithmType, bool hasOnlyOneAlgorithmType) GetAlgorithmSingleType(this IEnumerable<(BaseDevice device, Algorithm algorithm)> mps)
        {
            var algorithmTypes = mps.Select(pair => {
                var (_, algo) = pair;
                return algo.IDs.First();
            });
            var mustIncludeSingle = new HashSet<AlgorithmType>(algorithmTypes);
            if (mustIncludeSingle.Count == 1)
            {
                return (mustIncludeSingle.First(), true);
            }
            return (AlgorithmType.NONE, false);
        }


        public static IEnumerable<string> GetDevicesIDsInOrder(this IEnumerable<(BaseDevice device, Algorithm algorithm)> mps)
        {
            var deviceIds = mps.Select(pair => pair.device.ID).OrderBy(id => id).Select(id => id.ToString());
            return deviceIds;
        }

        //shamelessly copied from StringsExt in extensions library/package
        public static string GetStringAfter(this string s, string after)
        {
            var index = s.IndexOf(after, StringComparison.Ordinal);
            return s.Substring(index + after.Length);
        }

        public static (double, bool) TryGetHashrateAfter(this string s, string after)
        {
            var ret = (hashrate: default(double), found: false);
            if (!s.Contains(after))
            {
                return ret;
            }

            var afterString = s.GetStringAfter(after).ToLower();
            var numString = new string(afterString
                .ToCharArray()
                .SkipWhile(c => !char.IsDigit(c))
                .TakeWhile(c => char.IsDigit(c) || c == '.')
                .ToArray());

            if (!double.TryParse(numString, NumberStyles.Float, CultureInfo.InvariantCulture, out var hash))
            {
                return ret;
            }

            ret.found = true;
            if (afterString.Contains("kh"))
                ret.hashrate = hash * 1000;
            else if (afterString.Contains("mh"))
                ret.hashrate = hash * 1000000;
            else if (afterString.Contains("gh"))
                ret.hashrate = hash * 1000000000;
            else
                ret.hashrate = hash;

            return ret;
        }

        // TODO make one with Start NiceHashProcess
        public static Process CreateMiningProcess(string binPath, string workingDir, string commandLine, Dictionary<string, string> environmentVariables = null)
        {
            // TODO no handling of WINDOW hiding or minimizing
            //////if (IsNeverHideMiningWindow)
            //////{
            //////    P.StartInfo.CreateNoWindow = false;
            //////    if (ConfigManager.GeneralConfig.HideMiningWindows || ConfigManager.GeneralConfig.MinimizeMiningWindows)
            //////    {
            //////        P.StartInfo.WindowStyle = ProcessWindowStyle.Minimized;
            //////        P.StartInfo.UseShellExecute = true;
            //////    }
            //////}
            //////else
            //////{
            //////    P.StartInfo.CreateNoWindow = ConfigManager.GeneralConfig.HideMiningWindows;
            //////}
            var miningHandle = new Process
            {
                StartInfo =
                {
                    // set params
                    FileName = binPath,
                    WorkingDirectory = workingDir,
                    Arguments = commandLine,
                    // common settings
                    UseShellExecute = true,
                },
                EnableRaisingEvents = true, // TODO check out this one
            };
            // add environment if any
            if (environmentVariables != null)
            {
                foreach (var kvp in environmentVariables)
                {
                    var envName = kvp.Key;
                    var envValue = kvp.Value;
                    miningHandle.StartInfo.EnvironmentVariables[envName] = envValue;
                }
            }
            return miningHandle;
        }

        public static Process CreateBenchmarkProcess(string binPath, string workingDir, string commandLine, Dictionary<string, string> environmentVariables = null)
        {
            var benchmarkHandle = new Process
            {
                StartInfo =
                {
                    // set params
                    FileName = binPath,
                    WorkingDirectory = workingDir,
                    Arguments = commandLine,
                    // common settings
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                },
                EnableRaisingEvents = true, // TODO check out this one
            };
            // add environment if any
            if (environmentVariables != null)
            {
                foreach (var kvp in environmentVariables)
                {
                    var envName = kvp.Key;
                    var envValue = kvp.Value;
                    benchmarkHandle.StartInfo.EnvironmentVariables[envName] = envValue;
                }
            }
            return benchmarkHandle;
        }

        public static async Task<(double, bool, string)> WaitBenchmarkResult(BenchmarkProcess benchmarkProcess, TimeSpan timeoutTime, TimeSpan delayTime, CancellationToken stop)
        {
            var retTuple = (speed: 0.0, success: false, msg: ""); 
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            var timeoutTimerTime = timeoutTime + delayTime;

            using (var timeoutSource = new CancellationTokenSource(timeoutTimerTime))
            {
                var timeout = timeoutSource.Token;
                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeout, stop))
                {
                    try
                    {
                        await Task.Delay(delayTime, linkedCts.Token);
                        (retTuple.speed, retTuple.success) = await benchmarkProcess.Execute(linkedCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        // TODO this block is redundant
                        //add finally ??
                        if (timeout.IsCancellationRequested)
                        {
                            Console.WriteLine("Operation timed out.");
                            return (0, false, "Operation timed out.");
                        }
                        else if (stop.IsCancellationRequested)
                        {
                            Console.WriteLine("Cancelling per user request.");
                            stop.ThrowIfCancellationRequested();
                            return (0, false, "Cancelling per user request.");
                        }
                    }

                }

                if (timeout.IsCancellationRequested)
                {
                    Console.WriteLine("Operation timed out.");
                    return (0, false, "Operation timed out.");
                }
                else if (stop.IsCancellationRequested)
                {
                    Console.WriteLine("Cancelling per user request.");
                    return (0, false, "Cancelling per user request.");
                }
            }

            return retTuple;
        }
    }
}
