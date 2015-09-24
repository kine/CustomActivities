//-----------------------------------------------------------------------
// <copyright file="InvokePowerShellCommandExt.cs">(c) http://TfsBuildExtensions.codeplex.com/. This source is subject to the Microsoft Permissive License. See http://www.microsoft.com/resources/sharedsource/licensingbasics/sharedsourcelicenses.mspx. All other rights reserved.</copyright>
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
    using System.Threading;
    using Microsoft.TeamFoundation.Build.Common;

    /// <summary>
    /// A command to invoke powershell scripts on a build agent with extended properties
    /// </summary>
    [BuildActivity(HostEnvironmentOption.Agent)]
    public sealed class InvokePowerShellCommandExt : BaseCodeActivity<PSObject[]>
    {
        /// <summary>
        /// Interface is used to allow use to mock out calls to the TFS server for testing
        /// </summary>
        private readonly IUtilitiesForPowerShellActivity powershellUtilities;

        /// <summary>
        /// Initializes a new instance of the InvokePowerShellCommand class
        /// </summary>
        public InvokePowerShellCommandExt()
            : this(new UtilitiesForPowerShellActivity())
        {
        }

        /// <summary>
        /// Initializes a new instance of the InvokePowerShellCommandExt class
        /// </summary>
        /// <param name="powershellUtilities">Allows a mock implementation of utilities to be passed in for testing</param>
        internal InvokePowerShellCommandExt(IUtilitiesForPowerShellActivity powershellUtilities)
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
        [Browsable(true)]
        [DefaultValue(BuildMessageImportance.High)]
        [Description("Set importnace for Messages")]
        public InArgument<BuildMessageImportance> MessageImportance { get; set; }

        /// <summary>
        /// Gets or sets the Importance of Warning.
        /// </summary>
        /// <value>The build message importance</value>
        [Browsable(true)]
        [DefaultValue(BuildMessageImportance.High)]
        [Description("Set importance for Warnings")]
        public InArgument<BuildMessageImportance> WarningImportance { get; set; }

        /// <summary>
        /// Gets or sets if the error should be reported as test error.
        /// </summary>
        /// <value>The test</value>
        [Browsable(true)]
        [DefaultValue(false)]
        [Description("Set to true if part of testing")]
        public InArgument<bool> IsTest { get; set; }

        /// <summary>
        /// Fail build on first Error (FailBuildOnError must be true)
        /// </summary>
        /// <value>Set true to Fail build on first error</value>
        [Browsable(true)]
        [DefaultValue(false)]
        [Description("Set to true to fail the build on first error")]
        public InArgument<bool> FailBuildOnFirstError { get; set; }

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
                //buildDetail.Status = BuildStatus.Failed;
                buildDetail.Save();
                if (this.FailBuildOnFirstError.Get(this.ActivityContext))
                {
                    throw new FailingBuildException(errorMessage);
                }
            }
            if (this.IgnoreExceptions.Get(this.ActivityContext))
            {
                this.ActivityContext.TrackBuildWarning(errorMessage);
            }
            else
            {
                this.ActivityContext.TrackBuildError(errorMessage);
            }
        }

        /// <summary>
        /// Logs a message as a build warning
        /// </summary>
        /// <param name="warningMessage">Message to save</param>
        new public void LogBuildWarning(string warningMessage)
        {
            if (this.TreatWarningsAsErrors.Get(this.ActivityContext))
            {
                this.LogBuildError(warningMessage);
            }
            else
            {
                this.ActivityContext.TrackBuildWarning(warningMessage,this.WarningImportance.Get(this.ActivityContext));
            }
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

            context.TrackBuildMessage(string.Format(CultureInfo.CurrentCulture, "Script resolved to {0}", script), BuildMessageImportance.Low);
            this.SetEnvironmentVariables(buildDetail);

            using (var runspace = RunspaceFactory.CreateRunspace(new WorkflowPsHostExt(context, MessageImportance.Get(context), WarningImportance.Get(context), this)))
            {
                runspace.Open();
                using (var ps = PowerShell.Create())
                {
                    ps.Runspace = runspace;
                    ps.AddScript(script);

                    ps.Streams.Error.DataAdded += Error_DataAdded;
                    ps.Streams.Warning.DataAdded += Warning_DataAdded;
                    var psAsyncResult = ps.BeginInvoke();
                    while (!psAsyncResult.IsCompleted)
                    {
                        Thread.Sleep(100);
                    }
                    var psresult = ps.EndInvoke(psAsyncResult);
                    
                    if (this.FailBuildOnError.Get(this.ActivityContext) && this.FailBuildOnFirstError.Get(this.ActivityContext) && (ps.Streams.Error.Count>0))
                    {
                        throw new FailingBuildException("There were errors. Failing the build...");
                    }

                    //var psresult = ps.Invoke();
                    //foreach (var warning in ps.Streams.Warning)
                    //{
                    //    this.LogBuildWarning(warning.Message);
                    //}
                    //foreach (var error in ps.Streams.Error)
                    //{
                    //    this.LogBuildError(error.Exception.Message);
                    //}
                    return psresult.ToArray();

                }
            }
            //using (var runspace = RunspaceFactory.CreateRunspace(new WorkflowPsHostExt(context, MessageImportance.Get(context), WarningImportance.Get(context), this)))
            //{
            //    runspace.Open();
            //    ManualResetEvent wait = new ManualResetEvent(false);

            //    using (var pipeline = runspace.CreatePipeline(script))
            //    {
            //        //pipeline.Commands.Add("out-default");
            //        //pipeline.Commands[0].MergeMyResults(PipelineResultTypes.Error, PipelineResultTypes.Output);
            //        /*
            //        var output = pipeline.Invoke();
            //        if (this.FailBuildOnError.Get(this.ActivityContext) && this.FailBuildOnFirstError.Get(this.ActivityContext) && this.wasError)
            //        {
            //            throw new FailingBuildException("There were errors. Failing the build...");
            //        }
            //        return output.ToArray();
            //         * */
            //        pipeline.Output.DataReady += Output_DataReady;
            //        pipeline.Error.DataReady += Error_DataReady;
            //        pipeline.StateChanged += (s, e) =>
            //            {
            //                var state = e.PipelineStateInfo.State;
            //                if (state == PipelineState.Completed ||
            //                    state == PipelineState.Failed)
            //                {
            //                    wait.Set();
            //                }
            //            };
            //        pipeline.InvokeAsync();

            //        wait.WaitOne();
                    
            //    }
            //}
        }

        void Warning_DataAdded(object sender, DataAddedEventArgs e)
        {
            var warnings = (PSDataCollection<WarningRecord>)sender;
            this.LogBuildWarning(warnings[e.Index].Message);
        }

        void Error_DataAdded(object sender, DataAddedEventArgs e)
        {
            var errors = (PSDataCollection<ErrorRecord>)sender;
            this.LogBuildError(errors[e.Index].Exception.Message);
        }

        string GetGitBranch(IBuildDetail buildDetail)
        {
            string branch = null;
            string commit;

            var environmentVariable = buildDetail;
            if (environmentVariable != null && !BuildSourceVersion.TryParseGit(environmentVariable.SourceGetVersion, out branch, out commit))
            {
                var defaultSourceProvider = environmentVariable.BuildDefinition.GetDefaultSourceProvider();
                if (BuildSourceProviders.IsGit(defaultSourceProvider.Name))
                    branch = BuildSourceProviders.GetProperty(defaultSourceProvider.Fields, BuildSourceProviders.GitProperties.DefaultBranch);
            }

            if (string.IsNullOrEmpty(branch))
                throw new Exception("Could not find Branch");

            return (branch.Substring(branch.LastIndexOf("/", StringComparison.InvariantCultureIgnoreCase) + 1));
        }
        private void SetEnvironmentVariables(IBuildDetail buildDetail)
        {
            Environment.SetEnvironmentVariable("TF_BUILD_BUILDDEFINITIONNAME", buildDetail.BuildDefinition.Name);
            Environment.SetEnvironmentVariable("TF_BUILD", "True");
            Environment.SetEnvironmentVariable("TF_BUILD_BUILDNUMBER", buildDetail.BuildNumber);
            Environment.SetEnvironmentVariable("TF_BUILD_BUILDREASON", buildDetail.Reason.ToString());
            Environment.SetEnvironmentVariable("TF_BUILD_BUILDURI", buildDetail.Uri.ToString());
            Environment.SetEnvironmentVariable("TF_BUILD_DROPLOCATION", buildDetail.DropLocation);
            Environment.SetEnvironmentVariable("TF_BUILD_SOURCEGETVERSION", buildDetail.SourceGetVersion);
            Environment.SetEnvironmentVariable("TF_BUILD_GITBRANCH", GetGitBranch(buildDetail));
            Environment.SetEnvironmentVariable("TF_BUILD_PROJECT", buildDetail.TeamProject);
            //Environment.SetEnvironmentVariable("TF_BUILD_COLLECTIONURI", buildDetail.);
            //Environment.SetEnvironmentVariable("TF_BUILD_BINARIESDIRECTORY", buildDetail.);
            //Environment.SetEnvironmentVariable("TF_BUILD_BUILDDIRECTORY", buildDetail.);
            //Environment.SetEnvironmentVariable("TF_BUILD_SOURCESDIRECTORY ", buildDetail.);
            //Environment.SetEnvironmentVariable("TF_BUILD_TESTRESULTSDIRECTORY", buildDetail.);
        }
    }
}