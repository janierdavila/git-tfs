﻿using System.ComponentModel;
using System.IO;
using NDesk.Options;
using Sep.Git.Tfs.Core;
using StructureMap;

namespace Sep.Git.Tfs.Commands
{
    [Pluggable("bootstrap")]
    [RequiresValidGitRepository]
    [Description("bootstrap [parent-commit]\n" +
        " info: if none of your tfs remote exists, always checkout and bootstrap your main remote first.\n")]
    public class Bootstrap : GitTfsCommand
    {
        private readonly RemoteOptions _remoteOptions;
        private readonly Globals _globals;
        private readonly TextWriter _stdout;

        public Bootstrap(RemoteOptions remoteOptions, Globals globals, TextWriter stdout)
        {
            _remoteOptions = remoteOptions;
            _globals = globals;
            _stdout = stdout;
        }

        public OptionSet OptionSet
        {
            get { return _remoteOptions.OptionSet; }
        }

        public int Run()
        {
            return Run("HEAD");
        }

        public int Run(string commitish)
        {
            var tfsParents = _globals.Repository.GetLastParentTfsCommits(commitish);
            foreach (var parent in tfsParents)
            {
                GitCommit commit = _globals.Repository.GetCommit(parent.GitCommit);
                _stdout.WriteLine("commit {0}\nAuthor: {1} <{2}>\nDate:   {3}\n\n    {4}",
                    commit.Sha,
                    commit.AuthorAndEmail.Item1, commit.AuthorAndEmail.Item2,
                    commit.When.ToString("ddd MMM d HH:mm:ss zzz"),
                    commit.Message.Replace("\n","\n    ").TrimEnd(' '));
                if (parent.Remote.IsDerived)
                {
                    var remoteId = GetRemoteId(parent);
                    var remote = _globals.Repository.CreateTfsRemote(new RemoteInfo
                    {
                        Id = remoteId,
                        Url = parent.Remote.TfsUrl,
                        Repository = parent.Remote.TfsRepositoryPath,
                        RemoteOptions = _remoteOptions,
                    }, string.Empty);
                    remote.UpdateTfsHead(parent.GitCommit, parent.ChangesetId);
                    _stdout.WriteLine("-> new remote '" + remote.Id + "'");
                }
                else
                {
                    if (parent.Remote.MaxChangesetId < parent.ChangesetId)
                    {
                        long oldChangeset = parent.Remote.MaxChangesetId;
                        _globals.Repository.MoveTfsRefForwardIfNeeded(parent.Remote);
                        _stdout.WriteLine("-> existing remote {0} (updated from changeset {1})", parent.Remote.Id, oldChangeset);
                    }
                    else
                    {
                        _stdout.WriteLine("-> existing remote {0} (up to date)", parent.Remote.Id);
                    }
                }
                _stdout.WriteLine();
            }
            return GitTfsExitCodes.OK;
        }

        private string GetRemoteId(TfsChangesetInfo parent)
        {
            if (IsAvailable(GitTfsConstants.DefaultRepositoryId))
            {
                _stdout.WriteLine("info: '" + parent.Remote.TfsRepositoryPath + "' will be bootstraped as your main remote...");
                return GitTfsConstants.DefaultRepositoryId;
            }

            //Remove '$/'!
            var expectedRemoteId = parent.Remote.TfsRepositoryPath.Substring(2).Trim('/');
            var indexOfSlash = expectedRemoteId.IndexOf('/');
            if (indexOfSlash != 0)
                expectedRemoteId = expectedRemoteId.Substring(indexOfSlash + 1);
            var remoteId = expectedRemoteId.ToGitRefName();
            var suffix = 0;
            while (!IsAvailable(remoteId))
                remoteId = expectedRemoteId + "-" + (suffix++);
            return remoteId;
        }

        private bool IsAvailable(string remoteName)
        {
            return !_globals.Repository.HasRemote(remoteName);
        }
    }
}
