﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Internal.Performance;
using Microsoft.VisualStudio.ProjectSystem.Logging;
using Microsoft.VisualStudio.ProjectSystem.Properties;
using Microsoft.VisualStudio.ProjectSystem.Utilities;
using NuGet.SolutionRestoreManager;

namespace Microsoft.VisualStudio.ProjectSystem.VS.NuGet
{
    using TIdentityDictionary = IImmutableDictionary<NamedIdentity, IComparable>;

    partial class PackageRestoreInitiator
    {
        private class PackageRestoreInitiatorInstance : AbstractProjectDynamicLoadInstance
        {
            private readonly IUnconfiguredProjectVsServices _projectVsServices;
            private readonly IVsSolutionRestoreService _solutionRestoreService;
            private readonly IActiveConfigurationGroupService _activeConfigurationGroupService;
            private readonly IActiveConfiguredProjectSubscriptionService _activeConfiguredProjectSubscriptionService;
            private readonly IProjectLogger _logger;
            private IDisposable _configurationsSubscription;
            private DisposableBag _designTimeBuildSubscriptionLink;
            private bool _isFirstNomination = true;

            private static ImmutableHashSet<string> s_designTimeBuildWatchedRules = Empty.OrdinalIgnoreCaseStringSet
                .Add(NuGetRestore.SchemaName)
                .Add(ProjectReference.SchemaName)
                .Add(PackageReference.SchemaName)
                .Add(DotNetCliToolReference.SchemaName);

            // Remove the ConfiguredProjectIdentity key because it is unique to each configured project - so it won't match across projects by design.
            // Remove the ConfiguredProjectVersion key because each configuredproject manages it's own version and generally they don't match. 
            private readonly static ImmutableArray<NamedIdentity> s_keysToDrop = ImmutableArray.Create(ProjectDataSources.ConfiguredProjectIdentity, ProjectDataSources.ConfiguredProjectVersion);

            [ImportingConstructor]
            public PackageRestoreInitiatorInstance(
                IUnconfiguredProjectVsServices projectVsServices,
                IVsSolutionRestoreService solutionRestoreService,
                IActiveConfiguredProjectSubscriptionService activeConfiguredProjectSubscriptionService,
                IActiveConfigurationGroupService activeConfigurationGroupService,
                IProjectLogger logger)
                : base(projectVsServices.ThreadingService.JoinableTaskContext)
            {
                _projectVsServices = projectVsServices;
                _solutionRestoreService = solutionRestoreService;
                _activeConfiguredProjectSubscriptionService = activeConfiguredProjectSubscriptionService;
                _activeConfigurationGroupService = activeConfigurationGroupService;
                _logger = logger;
            }

            protected override Task InitializeCoreAsync(CancellationToken cancellationToken)
            {
                Action<IProjectVersionedValue<IConfigurationGroup<ConfiguredProject>>> target = OnActiveConfigurationsChanged;

                _configurationsSubscription = _activeConfigurationGroupService.ActiveConfiguredProjectGroupSource.SourceBlock.LinkTo(
                    target: new ActionBlock<IProjectVersionedValue<IConfigurationGroup<ConfiguredProject>>>(target),
                    linkOptions: new DataflowLinkOptions() { PropagateCompletion = true });

                return Task.CompletedTask;
            }

            protected override Task DisposeCoreAsync(bool initialized)
            {
                _designTimeBuildSubscriptionLink?.Dispose();
                _configurationsSubscription?.Dispose();
                return Task.CompletedTask;
            }

            private void OnActiveConfigurationsChanged(IProjectVersionedValue<IConfigurationGroup<ConfiguredProject>> e)
            {
                if (IsDisposing || IsDisposed)
                    return;

                // Clean up past subscriptions
                _designTimeBuildSubscriptionLink?.Dispose();

                if (e.Value.Count > 0)
                {
                    var sourceLinkOptions = new StandardRuleDataflowLinkOptions
                    {
                        RuleNames = s_designTimeBuildWatchedRules,
                        PropagateCompletion = true
                    };

                    var disposableBag = new DisposableBag(CancellationToken.None);
                    // We are taking source blocks from multiple configured projects and creating a SyncLink to combine the sources.
                    // The SyncLink will only publish data when the versions of the sources match. There is a problem with that.
                    // The sources have some version components that will make this impossible to match across TFMs. We introduce a 
                    // intermediate block here that will remove those version components so that the synclink can actually sync versions. 
                    var sourceBlocks = e.Value.Select(
                        cp =>
                        {
                            var sourceBlock = cp.Services.ProjectSubscription.JointRuleSource.SourceBlock;
                            var versionDropper = CreateVersionDropperBlock();
                            disposableBag.AddDisposable(sourceBlock.LinkTo(versionDropper, sourceLinkOptions));
                            return versionDropper.SyncLinkOptions<IProjectValueVersions>(sourceLinkOptions);
                        });

                    Action<Tuple<ImmutableList<IProjectValueVersions>, TIdentityDictionary>> action = ProjectPropertyChanged;
                    var target = new ActionBlock<Tuple<ImmutableList<IProjectValueVersions>, TIdentityDictionary>>(action);

                    var targetLinkOptions = new DataflowLinkOptions { PropagateCompletion = true };

                    var sourceBlocksAndCapabilitiesOptions = sourceBlocks.ToImmutableList()
                        .Insert(0, _projectVsServices.Project.Capabilities.SourceBlock.SyncLinkOptions<IProjectValueVersions>());

                    disposableBag.AddDisposable(ProjectDataSources.SyncLinkTo(sourceBlocksAndCapabilitiesOptions, target, targetLinkOptions));

                    _designTimeBuildSubscriptionLink = disposableBag;
                }
            }

            private static IPropagatorBlock<IProjectVersionedValue<IProjectSubscriptionUpdate>, IProjectVersionedValue<IProjectSubscriptionUpdate>> CreateVersionDropperBlock()
            {
                var transformBlock = new TransformBlock<IProjectVersionedValue<IProjectSubscriptionUpdate>, IProjectVersionedValue<IProjectSubscriptionUpdate>>(data =>
                {
                    return new ProjectVersionedValue<IProjectSubscriptionUpdate>(data.Value, data.DataSourceVersions.RemoveRange(s_keysToDrop));
                });

                return transformBlock;
            }

            private void ProjectPropertyChanged(Tuple<ImmutableList<IProjectValueVersions>, TIdentityDictionary> sources)
            {
                var capabilitiesSnapshot = sources.Item1[0] as IProjectVersionedValue<IProjectCapabilitiesSnapshot>;
                using (ProjectCapabilitiesContext.CreateIsolatedContext(_projectVsServices.Project, capabilitiesSnapshot.Value))
                {
                    NominateProject(sources.Item1.RemoveAt(0));
                }
            }

            private void NominateProject(ImmutableList<IProjectValueVersions> sources)
            {
                IVsProjectRestoreInfo projectRestoreInfo = ProjectRestoreInfoBuilder.Build(sources, _projectVsServices.Project, _isFirstNomination);
                _isFirstNomination = false;

                if (projectRestoreInfo != null)
                {
                    _projectVsServices.Project.Services.ProjectAsynchronousTasks
                        .RegisterAsyncTask(JoinableFactory.RunAsync(async () =>
                        {
                            LogProjectRestoreInfo(_projectVsServices.Project.FullPath, projectRestoreInfo);

                            await _solutionRestoreService
                                   .NominateProjectAsync(_projectVsServices.Project.FullPath, projectRestoreInfo,
                                        _projectVsServices.Project.Services.ProjectAsynchronousTasks.UnloadCancellationToken)
                                   .ConfigureAwait(false);

                            CodeMarkers.Instance.CodeMarker(CodeMarkerTimerId.PerfPackageRestoreEnd);

                            CompleteLogProjectRestoreInfo(_projectVsServices.Project.FullPath);
                        }),
                        ProjectCriticalOperation.Build | ProjectCriticalOperation.Unload | ProjectCriticalOperation.Rename,
                        registerFaultHandler: true);
                }
            }

            #region ProjectRestoreInfo Logging

            private void LogProjectRestoreInfo(string fullPath, IVsProjectRestoreInfo projectRestoreInfo)
            {
                if (_logger.IsEnabled)
                {
                    using (IProjectLoggerBatch logger = _logger.BeginBatch())
                    {
                        logger.WriteLine();
                        logger.WriteLine("------------------------------------------");
                        logger.WriteLine($"BEGIN Nominate Restore for {fullPath}");
                        logger.IndentLevel++;

                        logger.WriteLine($"BaseIntermediatePath:     {projectRestoreInfo.BaseIntermediatePath}");
                        logger.WriteLine($"OriginalTargetFrameworks: {projectRestoreInfo.OriginalTargetFrameworks}");
                        LogTargetFrameworks(logger, projectRestoreInfo.TargetFrameworks as TargetFrameworks);
                        LogReferenceItems(logger, "Tool References", projectRestoreInfo.ToolReferences as ReferenceItems);

                        logger.IndentLevel--;
                        logger.WriteLine();
                    }
                }
            }

            private void CompleteLogProjectRestoreInfo(string fullPath)
            {
                if (_logger.IsEnabled)
                {
                    using (IProjectLoggerBatch logger = _logger.BeginBatch())
                    {
                        logger.WriteLine();
                        logger.WriteLine("------------------------------------------");
                        logger.WriteLine($"COMPLETED Nominate Restore for {fullPath}");
                        logger.WriteLine();
                    }
                }
            }

            private void LogTargetFrameworks(IProjectLoggerBatch logger, TargetFrameworks targetFrameworks)
            {
                logger.WriteLine($"Target Frameworks ({targetFrameworks.Count})");
                logger.IndentLevel++;

                foreach (var tf in targetFrameworks)
                {
                    LogTargetFramework(logger, tf as TargetFrameworkInfo);
                }
                logger.IndentLevel--;
            }

            private void LogTargetFramework(IProjectLoggerBatch logger, TargetFrameworkInfo targetFrameworkInfo)
            {
                logger.WriteLine(targetFrameworkInfo.TargetFrameworkMoniker);
                logger.IndentLevel++;

                LogReferenceItems(logger, "Project References", targetFrameworkInfo.ProjectReferences as ReferenceItems);
                LogReferenceItems(logger, "Package References", targetFrameworkInfo.PackageReferences as ReferenceItems);
                LogProperties(logger, "Target Framework Properties", targetFrameworkInfo.Properties as ProjectProperties);

                logger.IndentLevel--;
            }

            private void LogProperties(IProjectLoggerBatch logger, string heading, ProjectProperties projectProperties)
            {
                var properties = projectProperties.Cast<ProjectProperty>()
                        .Select(prop => $"{prop.Name}:{prop.Value}");
                logger.WriteLine($"{heading} -- ({string.Join(" | ", properties)})");
            }

            private void LogReferenceItems(IProjectLoggerBatch logger, string heading, ReferenceItems references)
            {
                logger.WriteLine(heading);
                logger.IndentLevel++;

                foreach (var reference in references)
                {
                    var properties = reference.Properties.Cast<ReferenceProperty>()
                        .Select(prop => $"{prop.Name}:{prop.Value}");
                    logger.WriteLine($"{reference.Name} -- ({string.Join(" | ", properties)})");
                }

                logger.IndentLevel--;
            }

            #endregion
        }
    }
}
