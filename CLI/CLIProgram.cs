﻿/*
    Copyright 2010 MCSharp team (Modified for use with MCZall/MCLawl/MCGalaxy)
    
    Dual-licensed under the Educational Community License, Version 2.0 and
    the GNU General Public License, Version 3 (the "Licenses"); you may
    not use this file except in compliance with the Licenses. You may
    obtain a copy of the Licenses at
    
    http://www.opensource.org/licenses/ecl2.php
    http://www.gnu.org/licenses/gpl-3.0.html
    
    Unless required by applicable law or agreed to in writing,
    software distributed under the Licenses are distributed on an "AS IS"
    BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
    or implied. See the Licenses for the specific language governing
    permissions and limitations under the Licenses.
 */
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using MCGalaxy;
using MCGalaxy.UI;

namespace MCGalaxy.Cli {
    public static class CLIProgram {
  
#if CLI        
        [STAThread]
        public static void Main(string[] args) {
            Environment.CurrentDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            RunCLI();
        }
#endif

        public static void RunCLI() {
            if (!File.Exists("MCGalaxy_.dll")) {
                Console.WriteLine("Cannot start server as MCGalaxy_.dll is missing");
                Console.WriteLine("Download it from https://github.com/Hetal728/MCGalaxy/tree/master/Uploads");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey(true);
                return;
            }
            // separate method, in case MCGalaxy_.dll is missing
            StartCLI();
        }
        
        static void StartCLI() {
            FileLogger.Init();
            Server.RestartPath = Path.GetFileName(Assembly.GetEntryAssembly().Location);
            AppDomain.CurrentDomain.UnhandledException += GlobalExHandler;
            
            try {
                Logger.LogHandler += LogMessage;
                Updater.NewerVersionDetected += LogNewerVersionDetected;
                
                Server.Start();
                Console.Title = ServerConfig.Name + " - " + Server.SoftwareNameVersioned;
                ConsoleLoop();
            } catch (Exception e) {
                Logger.LogError(e);
                FileLogger.Flush(null);
            }
        }
        
        public static void LogAndRestart(Exception ex) {
            Logger.LogError(ex);
            FileLogger.Flush(null);
            
            Thread.Sleep(500);
            if (ServerConfig.restartOnError) {
                Thread stopThread = Server.Stop(true, "Server restart - unhandled error");
                stopThread.Join();
            }
        }
        
        static void GlobalExHandler(object sender, UnhandledExceptionEventArgs e) {
            LogAndRestart((Exception)e.ExceptionObject);
        }

        static void LogMessage(LogType type, string message) {
            switch (type) {
                case LogType.Error:
                    WriteToConsole("!!!Error! See " + FileLogger.ErrorLogPath + " for more information.");
                    break;
                case LogType.BackgroundActivity:
                    break;
                default:
                    string now = DateTime.Now.ToString("(HH:mm:ss) ");
                    WriteToConsole(now + message);
                    break;
            }
        }

        static void LogNewerVersionDetected(object sender, EventArgs e) {
            ConsoleColor prevColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Update for MCGalaxy was found!");
            Console.WriteLine("To update, replace with the files from " + Updater.UploadsURL);
            Console.ForegroundColor = prevColor;
        }
        
        static void WriteToConsole(string message) {
            AppendFormatted(message);
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine();
        }

        static void ConsoleLoop() {
            while (true) {
                try {
                    string msg = Console.ReadLine().Trim(); // Make sure we have no whitespace!
                    Thread t = null;
                    
                    if (msg == "/") {
                        t = UIHelpers.RepeatCommand();
                    } else if (msg.Length > 0 && msg[0] == '/') {
                        t = UIHelpers.HandleCommand(msg.Substring(1));
                    } else {
                        UIHelpers.HandleChat(msg);
                    }
                    
                    if (t != null && msg.CaselessEq("/restart")) { t.Join(); break; }
                } catch (Exception ex) {
                    Logger.LogError(ex);
                }
            }
        }
        
        static void AppendFormatted(string message) {
            int index = 0;
            char col = 'S';
            message = UIHelpers.Format(message);
            
            while (index < message.Length) {
                char curCol = col;
                string part = UIHelpers.OutputPart(ref col, ref index, message);
                if (part.Length > 0) {
                    Console.ForegroundColor = GetConsoleCol(curCol);
                    Console.Write(part);
                }
            }
        }

        static ConsoleColor GetConsoleCol(char c) {
            if (c == 'S') return ConsoleColor.White;
            Colors.Map(ref c);
            
            switch (c) {
                    case '0': return ConsoleColor.Black;
                    case '1': return ConsoleColor.DarkBlue;
                    case '2': return ConsoleColor.DarkGreen;
                    case '3': return ConsoleColor.DarkCyan;
                    case '4': return ConsoleColor.DarkRed;
                    case '5': return ConsoleColor.DarkMagenta;
                    case '7': return ConsoleColor.Gray;
                    case '6': return ConsoleColor.DarkYellow;
                    case '8': return ConsoleColor.DarkGray;
                    case '9': return ConsoleColor.Blue;
                    case 'a': return ConsoleColor.Green;
                    case 'b': return ConsoleColor.Cyan;
                    case 'c': return ConsoleColor.Red;
                    case 'd': return ConsoleColor.Magenta;
                    case 'e': return ConsoleColor.Yellow;
                    case 'f': return ConsoleColor.White;
                    
                default:
                    if (!Colors.IsDefined(c)) return ConsoleColor.White;
                    return GetConsoleCol(Colors.Get(c).Fallback);
            }
        }
    }
}
