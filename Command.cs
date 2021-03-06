﻿using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;
using CDBurnerXP;
using Ketarin.Forms;

namespace Ketarin
{
    /// <summary>
    /// Represents a user command, which can be any kind of scripting language.
    /// </summary>
    internal class Command
    {
        /// <summary>
        /// Gets or sets the scripting language.
        /// </summary>
        public ScriptType Type { get; set; }

        /// <summary>
        /// Gets or sets the command text.
        /// </summary>
        public string Text { get; set; }

        public Command(string text, ScriptType type)
        {
            this.Text = text;
            this.Type = type;
        }

        public Command(string text, string type)
        {
            this.Text = text;
            this.Type = ConvertToScriptType(type);
        }

        /// <summary>
        /// Executes the command.
        /// </summary>
        /// <param name="targetFileName">Content for variable "{url:...}"</param>
        public virtual int Execute(ApplicationJob application, string targetFileName = null, ApplicationJobError errorInfo = null)
        {
            switch (Type)
            {
                case ScriptType.CS:
                    UserCSScript script = new UserCSScript(this.Text);
                    script.Execute(application);
                    break;

                case ScriptType.PowerShell:
                    PowerShellScript psScript = new PowerShellScript(this.Text);
                    psScript.Execute(application, errorInfo);
                    return Conversion.ToInt(psScript.LastOutput);

                default:
                    return ExecuteBatchCommand(application, this.Text, targetFileName);
            }

            return 0;
        }

        /// <summary>
        /// Executes a given command for the given application (also resolves variables).
        /// </summary>
        /// <returns>Exit code of the command, if not run in background</returns>
        private static int ExecuteBatchCommand(ApplicationJob job, string commandText, string targetFileName)
        {
            // Ignore empty commands
            if (string.IsNullOrEmpty(commandText)) return 0;

            commandText = commandText.Replace("\r\n", "\n");

            // Job specific data
            commandText = job != null ? job.Variables.ReplaceAllInString(commandText, DateTime.MinValue, targetFileName, false) : UrlVariable.GlobalVariables.ReplaceAllInString(commandText);

            // Replace variable: root
            try
            {
                commandText = UrlVariable.Replace(commandText, "root", Path.GetPathRoot(Application.StartupPath), job);
            }
            catch (ArgumentException) { }

            // Feed cmd.exe with our commands
            ProcessStartInfo cmdExe = new ProcessStartInfo("cmd.exe")
            {
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            bool executeBackground = commandText.EndsWith("&");
            commandText = commandText.TrimEnd('&');

            using (Process proc = Process.Start(cmdExe))
            {
                StringBuilder commandResult = new StringBuilder();

                // Set the event handler to asynchronously read the command output.
                proc.OutputDataReceived += delegate(object sendingProcess, DataReceivedEventArgs outLine)
                {
                    if (!string.IsNullOrEmpty(outLine.Data)) commandResult.AppendLine(outLine.Data);
                };
                proc.ErrorDataReceived += delegate(object sendingProcess, DataReceivedEventArgs outLine)
                {
                    if (!string.IsNullOrEmpty(outLine.Data)) commandResult.AppendLine(outLine.Data);
                };

                // Start the asynchronous read of the command output stream.
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                // Input commands
                using (proc.StandardInput)
                {
                    string[] commands = commandText.Split('\n');
                    foreach (string command in commands)
                    {
                        if (!string.IsNullOrEmpty(command))
                        {
                            LogDialog.Log(job, "Executing command: " + command);
                        }
                        proc.StandardInput.WriteLine(command);
                    }
                }

                // Read output
                if (!executeBackground)
                {
                    proc.WaitForExit();
                    string commandResultString = commandResult.ToString();
                    if (!string.IsNullOrEmpty(commandResultString))
                    {
                        LogDialog.Log(job, "Command result: " + commandResultString);
                    }
                    return proc.ExitCode;
                }
            }

            return 0;
        }

        /// <summary>
        /// Converts a given string to a valid script type.
        /// Batch is the default.
        /// </summary>
        public static ScriptType ConvertToScriptType(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text))
                {
                    return ScriptType.Batch;
                }

                return (ScriptType)Enum.Parse(typeof(ScriptType), text);
            }
            catch (Exception)
            {
                return ScriptType.Batch;
            }
        }
    }
}
