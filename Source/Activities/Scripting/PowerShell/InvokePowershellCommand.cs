//-----------------------------------------------------------------------
// <copyright file="InvokePowerShellCommand.cs">(c) http://TfsBuildExtensions.codeplex.com/. This source is subject to the Microsoft Permissive License. See http://www.microsoft.com/resources/sharedsource/licensingbasics/sharedsourcelicenses.mspx. All other rights reserved.</copyright>
//-----------------------------------------------------------------------
namespace TfsBuildExtensions.Activities.Scripting
{
    using System;
    using System.Activities;
    using System.ComponentModel;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Management.Automation;
    using System.Management.Automation.Runspaces;
    using Microsoft.TeamFoundation.Build.Client;
    using Microsoft.TeamFoundation.Build.Workflow.Activities;
    using Microsoft.TeamFoundation.VersionControl.Client;

    /// <summary>
    /// A command to invoke powershell scripts on a build agent
    /// </summary>
    [BuildActivity(HostEnvironmentOption.Agent)]
    public sealed class InvokePowerShellCommand : BaseCodeActivity<PSObject[]>
    {
        /// <summary>
        /// Interface is used to allow use to mock out calls to the TFS server for testing
        /// </summary>
        private readonly IUtilitiesForPowerShellActivity powershellUtilities;

        /// <summary>
        /// Initializes a new instance of the InvokePowerShellCommand class
        /// </summary>
        public InvokePowerShellCommand()
            : this(new UtilitiesForPowerShellActivity())
        {
        }

        /// <summary>
        /// Initializes a new instance of the InvokePowerShellCommand class
        /// </summary>
        /// <param name="powershellUtilities">Allows a mock implementation of utilities to be passed in for testing</param>
        internal InvokePowerShellCommand(IUtilitiesForPowerShellActivity powershellUtilities)
        {
            this.powershellUtilities = powershellUtilities;
        }

        /// <summary>
        /// Gets or sets the powershell command script to execute.
        /// </summary>
        /// <value>The command script in string form</value>
        [RequiredArgument]
        [Browsable(true)]
        public InArgument<string> Script { get; set; }

        /// <summary>
        /// Gets or sets any arguments to be provided to the script
        /// <value>An arguments list for the command as a string</value>
        /// </summary>
        [Browsable(true)]
        public InArgument<string> Arguments { get; set; }

        /// <summary>
        /// Gets or sets the build workspace. This is used to obtain
        /// a powershell script from a source control path
        /// </summary>
        /// <value>The workspace used by the current build</value>
        [Browsable(true)]
        [DefaultValue(null)]
        public InArgument<Workspace> BuildWorkspace { get; set; }

        /// <summary>
        /// Gets or sets the Importance of Message.
        /// </summary>
        /// <value>Build message importance</value>
        [RequiredArgument]
        [Browsable(true)]
        public InArgument<BuildMessageImportance> MessageImportance { get; set; }

        /// <summary>
        /// Gets or sets the Importance of Warning.
        /// </summary>
        /// <value>The build message importance</value>
        [RequiredArgument]
        [Browsable(true)]
        public InArgument<BuildMessageImportance> WarningImportance { get; set; }

        /// <summary>
        /// Gets or sets if the error should be reported as test error.
        /// </summary>
        /// <value>The test</value>
        [RequiredArgument]
        [Browsable(true)]
        public InArgument<bool> IsTest { get; set; }

        /// <summary>
        /// Resolves the provided script parameter to either a server stored 
        /// PS file or an inline script for direct execution.
        /// </summary>
        /// <param name="workspace">The TFS workspace</param>
        /// <param name="script">The powershell script or path</param>
        /// <param name="arguments">The powershell script arguments</param>
        /// <returns>An executable powershell command</returns>
        internal string ResolveScript(Workspace workspace, string script, string arguments)
        {
            if (string.IsNullOrWhiteSpace(script))
            {
                throw new ArgumentNullException("script");
            }
            script=Environment.ExpandEnvironmentVariables(script);
            if (this.powershellUtilities.IsServerItem(script))
            {
                var workspaceFilePath = this.powershellUtilities.GetLocalFilePathFromWorkspace(workspace, script);

                if (!this.powershellUtilities.FileExists(workspaceFilePath))
                {
                    throw new FileNotFoundException("Script", string.Format(CultureInfo.CurrentCulture, "Workspace local path [{0}] for source path [{1}] was not found", script, workspaceFilePath));
                }

                script = string.Format("& '{0}' {1}", workspaceFilePath, arguments);
            }
            else if (this.powershellUtilities.FileExists(script))
            {
                script = string.Format("& '{0}' {1}", script, arguments);
            }

            return script;
        }

        /// <summary>
        /// Logs a message as a build error
        /// Also can fail the build if the FailBuildOnError flag is set
        /// </summary>
        /// <param name="errorMessage">Message to save</param>
        new public void LogBuildError(string errorMessage)
        {
            if (this.FailBuildOnError.Get(this.ActivityContext))
            {
                var buildDetail = this.ActivityContext.GetExtension<IBuildDetail>();
                if (this.IsTest.Get(this.ActivityContext))
                {
                    buildDetail.TestStatus = BuildPhaseStatus.Failed;
                }
                else
                {
                    buildDetail.CompilationStatus = BuildPhaseStatus.Failed;
                }
                buildDetail.Save();
            }
            base.LogBuildError(errorMessage);
        }

        /// <summary>
        /// Logs a message as a build warning
        /// </summary>
        /// <param name="warningMessage">Message to save</param>
        new public void LogBuildWarning(string warningMessage)
        {
            base.LogBuildWarning(warningMessage);
        }

        /// <summary>
        /// Logs a generical build message
        /// </summary>
        /// <param name="message">The message to save</param>
        /// <param name="importance">The verbosity importance of the message</param>
        new public void LogBuildMessage(string message, BuildMessageImportance importance = BuildMessageImportance.Normal)
        {
            base.LogBuildMessage(message, importance);
        }



        /// <summary>
        /// When implemented in a derived class, performs the execution of the activity.
        /// </summary>
        /// <returns>PSObject array</returns>
        protected override PSObject[] InternalExecute()
        {
            CodeActivityContext context = this.ActivityContext;
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            var script = this.ResolveScript(
              this.BuildWorkspace.Get(context),
              this.Script.Get(context),
              this.Arguments.Get(context));

            var buildDetail = context.GetExtension<IBuildDetail>();
            //buildDetail.TestStatus

            context.TrackBuildMessage(string.Format(CultureInfo.CurrentCulture, "Script resolved to {0}", script), BuildMessageImportance.Low);

            using (var runspace = RunspaceFactory.CreateRunspace(new WorkflowPsHost(context, MessageImportance.Get(context), WarningImportance.Get(context), this)))
            {
                runspace.Open();

                using (var pipeline = runspace.CreatePipeline(script))
                {
                    pipeline.Commands.Add("out-default");
                    pipeline.Commands[0].MergeMyResults(PipelineResultTypes.Error, PipelineResultTypes.Output);
                    var output = pipeline.Invoke();
                    return output.ToArray();
                }
            }
        }
    }
}