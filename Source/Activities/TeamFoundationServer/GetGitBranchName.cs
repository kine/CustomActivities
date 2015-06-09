//-----------------------------------------------------------------------
// <copyright file="GetGitBranchName.cs">(c) http://TfsBuildExtensions.codeplex.com/. This source is subject to the Microsoft Permissive License. See http://www.microsoft.com/resources/sharedsource/licensingbasics/sharedsourcelicenses.mspx. All other rights reserved.</copyright>
//-----------------------------------------------------------------------
namespace TfsBuildExtensions.Activities.TeamFoundationServer
{
    using System;
    using System.Activities;
    using Microsoft.TeamFoundation.Build.Client;
    using Microsoft.TeamFoundation.Client;
    using Microsoft.TeamFoundation.Build.Common;

    /// <summary>
    /// Retrieves GIT Branch on which the build is working. 
    /// Based on solution provided here: http://stackoverflow.com/questions/21290629/getting-git-branch-name-inside-tfs-build
    /// </summary>
    [BuildActivity(HostEnvironmentOption.All)]
    public sealed class GetGitBranchName : BaseCodeActivity<String>
    {
        /// <summary>
        /// BuildDetail class with needed info
        /// </summary>
        public InArgument<IBuildDetail> BuildDetail { get; set; }

        /// <summary>
        /// Executes the logic for this workflow activity
        /// </summary>
        /// <returns>String</returns>
        protected override String InternalExecute()
        {
            string branch = null;
            string commit;
    
            var environmentVariable = BuildDetail.Get(this.ActivityContext);
            if (environmentVariable != null && !BuildSourceVersion.TryParseGit(environmentVariable.SourceGetVersion, out branch, out commit))
            {
                var defaultSourceProvider = environmentVariable.BuildDefinition.GetDefaultSourceProvider();
                if (BuildSourceProviders.IsGit(defaultSourceProvider.Name))
                    branch = BuildSourceProviders.GetProperty(defaultSourceProvider.Fields, BuildSourceProviders.GitProperties.DefaultBranch);
            }
    
            if (string.IsNullOrEmpty(branch))
                throw new Exception("Could not find Branch");
    
            return(branch.Substring(branch.LastIndexOf("/", StringComparison.InvariantCultureIgnoreCase) + 1));
        }

    }
}
