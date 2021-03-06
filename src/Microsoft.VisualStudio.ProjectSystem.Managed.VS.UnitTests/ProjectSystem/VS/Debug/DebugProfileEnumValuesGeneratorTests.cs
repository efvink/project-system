﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.ProjectSystem.Properties;
using Moq;
using Xunit;

namespace Microsoft.VisualStudio.ProjectSystem.Debug
{
    [Trait("UnitTest", "ProjectSystem")]
    public class DebugProfileEnumValuesGenerator_Tests
    {
        private List<ILaunchProfile> _profiles = new List<ILaunchProfile>() {
            {new LaunchProfile() {Name="Profile1", LaunchBrowser=true}},
            {new LaunchProfile() { Name = "MyCommand"} },
            {new LaunchProfile() { Name = "Foo"} },
            {new LaunchProfile() {Name="Bar"} }
        };

        [Fact]
        public async Task DebugProfileEnumValuesGenerator_GetListsValuesAsyncTests()
        {
            var testProfiles = new Mock<ILaunchSettings>();
            testProfiles.Setup(m => m.ActiveProfile).Returns(() => {return _profiles[1];});
            testProfiles.Setup(m => m.Profiles).Returns(() =>
            {
                return _profiles.ToImmutableList();
            });

            var moqProfileProvider = new Mock<ILaunchSettingsProvider>();
            moqProfileProvider.Setup(p => p.CurrentSnapshot).Returns(testProfiles.Object);
            var threadingService = new IProjectThreadingServiceMock();

            var generator =  
                new DebugProfileEnumValuesGenerator(moqProfileProvider.Object, threadingService); 
            ICollection<IEnumValue> results = await generator.GetListedValuesAsync();
            Assert.True(results.Count == 4);
            Assert.True(results.ElementAt(0).Name == "Profile1" &&  results.ElementAt(0).DisplayName == "Profile1" );
            Assert.True(results.ElementAt(1).Name == "MyCommand" &&  results.ElementAt(1).DisplayName == "MyCommand" );
            Assert.True(results.ElementAt(2).Name == "Foo" &&  results.ElementAt(2).DisplayName == "Foo" );
            Assert.True(results.ElementAt(3).Name == "Bar" &&  results.ElementAt(3).DisplayName == "Bar" );
        }

        [Fact]
        public async Task DebugProfileEnumValuesGenerator_TryCreateEnumValueAsyncTests()
        {
            var testProfiles = new Mock<ILaunchSettings>();
            testProfiles.Setup(m => m.ActiveProfile).Returns(() => {return _profiles[1];});
            testProfiles.Setup(m => m.Profiles).Returns(() =>
            {
                return _profiles.ToImmutableList();
            });

            var moqProfileProvider = new Mock<ILaunchSettingsProvider>();
            moqProfileProvider.Setup(p => p.CurrentSnapshot).Returns(testProfiles.Object);
            var threadingService = new IProjectThreadingServiceMock();

            var generator = 
                new DebugProfileEnumValuesGenerator(moqProfileProvider.Object, threadingService); 

            Assert.False(generator.AllowCustomValues);
            IEnumValue result = await generator.TryCreateEnumValueAsync("Profile1");
            Assert.True(result.Name == "Profile1" &&  result.DisplayName == "Profile1" );
            result = await generator.TryCreateEnumValueAsync("MyCommand");
            Assert.True(result.Name == "MyCommand" &&  result.DisplayName == "MyCommand" );
            
            // case sensitive check
            result = await generator.TryCreateEnumValueAsync("mycommand");
            Assert.Null(result);

            result = await generator.TryCreateEnumValueAsync("Foo");
            Assert.True(result.Name == "Foo" &&  result.DisplayName == "Foo" );
            result = await generator.TryCreateEnumValueAsync("Bar");
            Assert.True(result.Name == "Bar" &&  result.DisplayName == "Bar" );
        }
    }
}
