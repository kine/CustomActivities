//-----------------------------------------------------------------------
// <copyright file="WorkflowPsHostUiExt.cs">(c) http://TfsBuildExtensions.codeplex.com/. This source is subject to the Microsoft Permissive License. See http://www.microsoft.com/resources/sharedsource/licensingbasics/sharedsourcelicenses.mspx. All other rights reserved.</copyright>
//-----------------------------------------------------------------------
namespace TfsBuildExtensions.Activities.Scripting
{
    using System;
    using System.Activities;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Globalization;
    using System.Management.Automation;
    using System.Management.Automation.Host;
    using Microsoft.TeamFoundation.Build.Client;
    using Microsoft.TeamFoundation.Build.Workflow.Activities;

    internal class WorkflowPsHostUiExt : PSHostUserInterface
    {
        private readonly CodeActivityContext activityContext;
        private readonly WorkflowRawPsHostUi rawUI;
        private readonly BuildMessageImportance messageImportance;
        private readonly BuildMessageImportance warningImportance;
        private readonly InvokePowerShellCommandExt activity;

        public WorkflowPsHostUiExt(CodeActivityContext activityContext, BuildMessageImportance messageImportance, BuildMessageImportance warningImportance, InvokePowerShellCommandExt activity)
        {
            this.activityContext = activityContext;
            this.messageImportance = messageImportance;
            this.warningImportance= warningImportance;
            this.activity = activity;
            this.rawUI = new WorkflowRawPsHostUi();
        }

        public override PSHostRawUserInterface RawUI
        {
            get { return this.rawUI; }
        }

        public override Dictionary<string, PSObject> Prompt(string caption, string message, Collection<FieldDescription> descriptions)
        {
            return new Dictionary<string, PSObject>();
        }

        public override int PromptForChoice(string caption, string message, Collection<ChoiceDescription> choices, int defaultChoice)
        {
            return 0;
        }

        public override PSCredential PromptForCredential(string caption, string message, string userName, string targetName, PSCredentialTypes allowedCredentialTypes, PSCredentialUIOptions options)
        {
            return null;
        }

        public override PSCredential PromptForCredential(string caption, string message, string userName, string targetName)
        {
            return null;
        }

        public override string ReadLine()
        {
            return string.Empty;
        }

        public override System.Security.SecureString ReadLineAsSecureString()
        {
            return default(System.Security.SecureString);
        }

        public override void Write(ConsoleColor foregroundColor, ConsoleColor backgroundColor, string value)
        {
            this.activity.LogBuildMessage(value, this.messageImportance);
        }

        public override void Write(string value)
        {
            this.activity.LogBuildMessage(value, this.messageImportance);
        }

        public override void WriteDebugLine(string message)
        {
            this.activityContext.TrackBuildMessage(message, BuildMessageImportance.Low);
        }

        public override void WriteErrorLine(string value)
        {
            this.activity.LogBuildError(value);
        }

        /// <summary>
        /// Writes a newline character (carriage return) 
        /// to the output display of the host. 
        /// </summary>
        public override void WriteLine()
        {
            this.activity.LogBuildMessage("\n", this.messageImportance);
        }


        public override void WriteLine(string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                this.activity.LogBuildMessage(value, this.messageImportance);
            }
        }

        /// <summary>
        /// Writes a line of characters to the output display of the host 
        /// with foreground and background colors and appends a newline (carriage return). 
        /// </summary>
        /// <param name="foregroundColor">The forground color of the display. </param>
        /// <param name="backgroundColor">The background color of the display. </param>
        /// <param name="value">The line to be written.</param>
        public override void WriteLine(ConsoleColor foregroundColor, ConsoleColor backgroundColor, string value)
        {
            WriteLine(value);
        }



        public override void WriteProgress(long sourceId, ProgressRecord record)
        {
            if (record == null)
            {
                throw new ArgumentNullException("record");
            }

            this.activityContext.TrackBuildMessage(string.Format(CultureInfo.CurrentCulture, "{0} Progress {1}% Complete", record.CurrentOperation, record.PercentComplete));
        }

        public override void WriteVerboseLine(string message)
        {
            this.activityContext.TrackBuildMessage(message, BuildMessageImportance.Low);
        }

        public override void WriteWarningLine(string message)
        {
            this.activity.LogBuildWarning(message);
        }
    }
}
