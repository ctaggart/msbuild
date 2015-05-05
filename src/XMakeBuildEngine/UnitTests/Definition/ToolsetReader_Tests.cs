﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Collections.Generic;
using System.Configuration;
using Microsoft.Win32;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Build.Collections;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Internal;
using Microsoft.Build.Shared;

using RegistryKeyWrapper = Microsoft.Build.Internal.RegistryKeyWrapper;
using RegistryException = Microsoft.Build.Exceptions.RegistryException;
using InvalidToolsetDefinitionException = Microsoft.Build.Exceptions.InvalidToolsetDefinitionException;
using InternalUtilities = Microsoft.Build.Internal.Utilities;

namespace Microsoft.Build.UnitTests.Definition
{
    /// <summary>
    /// Unit tests for ToolsetReader class and its derived classes
    /// </summary>
    [TestClass]
    public class ToolsetReaderTests
    {
        // The registry key that is passed as the baseKey parameter to the ToolsetRegistryReader class
        private RegistryKey _testRegistryKey = null;
        // Subkey "4.0"
        private RegistryKey _currentVersionRegistryKey = null;
        // Subkey "ToolsVersions"
        private RegistryKey _toolsVersionsRegistryKey = null;

        // Path to the registry key under HKCU
        // Note that this is a test registry key created solely for unit testing.
        private const string testRegistryPath = @"msbuildUnitTests";

        /// <summary>
        /// Store the value of the "VisualStudioVersion" environment variable here so that 
        /// we can unset it for the duration of the test.
        /// </summary>
        private string _oldVisualStudioVersion;

        /// <summary>
        /// Reset the testRegistryKey
        /// </summary>
        [TestInitialize]
        public void Setup()
        {
            TearDown();
            _testRegistryKey = Registry.CurrentUser.CreateSubKey(testRegistryPath);
            _currentVersionRegistryKey = Registry.CurrentUser.CreateSubKey(testRegistryPath + "\\" + Constants.AssemblyVersion);
            _toolsVersionsRegistryKey = Registry.CurrentUser.CreateSubKey(testRegistryPath + "\\ToolsVersions");

            _oldVisualStudioVersion = Environment.GetEnvironmentVariable("VisualStudioVersion");

            Environment.SetEnvironmentVariable("VisualStudioVersion", null);
        }

        [TestCleanup]
        public void TearDown()
        {
            ToolsetConfigurationReaderTestHelper.CleanUp();

            DeleteTestRegistryKey();

            Environment.SetEnvironmentVariable("VisualStudioVersion", _oldVisualStudioVersion);
        }

        /// <summary>
        /// Callback for toolset collection
        /// </summary>
        public void ToolsetAdded(Toolset toolset)
        {
            // Do nothing
        }

        /// <summary>
        /// Helper class to delete the testRegistryKey tree.
        /// </summary>
        private void DeleteTestRegistryKey()
        {
            if (Registry.CurrentUser.OpenSubKey(testRegistryPath) != null)
            {
                Registry.CurrentUser.DeleteSubKeyTree(testRegistryPath);
            }
        }

        /// <summary>
        /// Test to make sure machine.config file has the section registered
        /// and we are picking it up from there.
        /// </summary>
        [TestMethod]
        public void GetToolsetDataFromConfiguration_SectionNotRegisteredInConfigFile()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                   </configSections>
                   <msbuildToolsets>
                     <toolset toolsVersion=""2.0"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v2.0.x86ret\""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            ToolsetReader reader = GetStandardConfigurationReader();

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);
            string msbuildOverrideTasksPath = null;
            string defaultOverrideToolsVersion = null;
            string defaultToolsVersion = reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), true, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);

            Assert.AreEqual(null, msbuildOverrideTasksPath);
            Assert.AreEqual(null, defaultToolsVersion);
            Assert.AreEqual(0, values.Count);
        }

        #region "Reading from application configuration file tests"

        /// <summary>
        /// Tests that the data is correctly populated using function GetToolsetDataFromConfiguration
        /// </summary>
        [TestMethod]
        public void GetToolsetDataFromConfiguration_Basic()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""2.0"" msbuildOverrideTasksPath=""c:\Cat"" DefaultOverrideToolsVersion=""4.0"">
                     <toolset toolsVersion=""2.0"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v2.0.x86ret\""/>
                     </toolset>
                     <toolset toolsVersion=""4.0"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v4.0.x86ret\""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            ToolsetReader reader = GetStandardConfigurationReader();

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);
            string msbuildOverrideTasksPath = null;
            string defaultOverrideToolsVersion = null;
            string defaultToolsVersion = reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), true, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);

            Assert.AreEqual("c:\\Cat", msbuildOverrideTasksPath);
            Assert.AreEqual("4.0", defaultOverrideToolsVersion);
            Assert.AreEqual("2.0", defaultToolsVersion);
            Assert.AreEqual(2, values.Count);
            Assert.AreEqual(0, values["2.0"].Properties.Count);
            Assert.AreEqual(@"D:\windows\Microsoft.NET\Framework\v2.0.x86ret", values["2.0"].ToolsPath);
            Assert.AreEqual(0, values["4.0"].Properties.Count);
            Assert.AreEqual(@"D:\windows\Microsoft.NET\Framework\v4.0.x86ret", values["4.0"].ToolsPath);
        }

        /// <summary>
        /// Relative paths can be used in a config file value
        /// </summary>
        [TestMethod]
        public void RelativePathInValue()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""2.0"" msbuildOverrideTasksPath=""..\Foo"">
                     <toolset toolsVersion=""2.0"">
                       <property name=""MSBuildToolsPath"" value=""..\foo""/>
                       <!-- derelativization occurs before comparing toolspath and binpath -->
                       <property name=""MSBuildBinPath"" value=""..\.\foo""/>
                     </toolset>
                     <toolset toolsVersion=""3.0"">
                       <!-- properties are expanded before derelativization-->
                       <property name=""MSBuildBinPath"" value=""$(DotDotSlash)bar""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            ToolsetReader reader = GetStandardConfigurationReader();

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);
            PropertyDictionary<ProjectPropertyInstance> pg = new PropertyDictionary<ProjectPropertyInstance>();
            pg.Set(ProjectPropertyInstance.Create("DotDotSlash", @"..\"));
            string msbuildOverrideTasksPath = null;
            string defaultOverrideToolsVersion = null;
            reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), pg, true, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);

            string expected1 = Path.GetFullPath(Path.Combine(FileUtilities.CurrentExecutableDirectory, @"..\foo"));
            string expected2 = Path.GetFullPath(Path.Combine(FileUtilities.CurrentExecutableDirectory, @"..\bar"));
            Console.WriteLine(values["2.0"].ToolsPath);
            Assert.AreEqual(expected1, values["2.0"].ToolsPath);
            Assert.AreEqual(expected2, values["3.0"].ToolsPath);
            Assert.AreEqual("..\\Foo", msbuildOverrideTasksPath);
        }

        /// <summary>
        /// Invalid relative path in msbuildbinpath value
        /// </summary>
        [TestMethod]
        public void InvalidRelativePath()
        {
            string invalidRelativePath = @"..\|invalid|";
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""2.0"" msbuildOverrideTasksPath="""">
                     <toolset toolsVersion=""2.0"">
                       <property name=""MSBuildBinPath"" value=""" + invalidRelativePath + @"""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            ToolsetReader reader = GetStandardConfigurationReader();


            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);
            string msbuildOverrideTasksPath = null;
            string defaultOverrideToolsVersion = null;
            reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), true, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);

            // Don't crash (consistent with invalid absolute path)
            Assert.AreEqual(invalidRelativePath, values["2.0"].ToolsPath);
            Assert.AreEqual(null, msbuildOverrideTasksPath);
        }

        /// <summary>
        /// Tests the case where application configuration file is invalid
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(InvalidToolsetDefinitionException))]
        public void GetToolsetDataFromConfiguration_InvalidXmlFile()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"<INVALIDXML>");

            ToolsetReader reader = GetStandardConfigurationReader();

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string msbuildOverrideTasksPath = null;
            string defaultOverrideToolsVersion = null;
            reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), true, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);
        }

        /// <summary>
        /// Tests the case where application configuration file is invalid
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(InvalidToolsetDefinitionException))]
        public void GetToolsetDataFromConfiguration_InvalidConfigFile()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets>
                     <toolset toolsVersion=""2.0"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v2.0.x86ret\""/>
                     </toolsVersion>
                     <SOMEINVALIDTAG/>
                   </msbuildToolsets>
                 </configuration>");

            ToolsetReader reader = GetStandardConfigurationReader();

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);
            string msbuildOverrideTasksPath = null;
            string defaultOverrideToolsVersion = null;
            reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), true, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);
        }

        /// <summary>
        /// Tests the case where application configuration file is empty
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(InvalidToolsetDefinitionException))]
        public void GetToolsetDataFromConfiguration_FileEmpty()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"");

            ToolsetReader reader = new ToolsetConfigurationReader(new ProjectCollection().EnvironmentProperties, new PropertyDictionary<ProjectPropertyInstance>(), new ReadApplicationConfiguration(
                ToolsetConfigurationReaderTestHelper.ReadApplicationConfigurationTest));

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string msbuildOverrideTasksPath = null;
            string defaultOverrideToolsVersion = null;
            reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), true, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);
        }

        /// <summary>
        /// Tests the case when ReadConfiguration throws exception
        /// Make sure that we don't eat it and always throw ConfigurationErrorsException
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(InvalidToolsetDefinitionException))]
        public void GetToolsetDataFromConfiguration_ConfigurationExceptionThrown()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"", new ConfigurationErrorsException());

            ToolsetReader reader = GetStandardConfigurationReader();

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            // this should throw ...
            string msbuildOverrideTasksPath = null;
            string defaultOverrideToolsVersion = null;
            reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), true, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);
        }

        /// <summary>
        /// Tests the case when ReadConfiguration throws exception
        /// Make sure that we don't eat it and always throw ConfigurationErrorsException
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(InvalidToolsetDefinitionException))]
        public void GetToolsetDataFromConfiguration_ConfigurationErrorsExceptionThrown()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"", new ConfigurationErrorsException());

            ToolsetReader reader = GetStandardConfigurationReader();

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            // this should throw ...
            string msbuildOverrideTasksPath = null;
            string defaultOverrideToolsVersion = null;
            reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), true, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);
        }

        /// <summary>
        /// Tests the case where default attribute is not specified in the config file
        /// </summary>
        [TestMethod]
        public void GetToolsetDataFromConfiguration_DefaultAttributeNotSpecified()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets msbuildOverrideTasksPath=""C:\Cat"">
                     <toolset toolsVersion=""2.0"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v2.0.x86ret\""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            ToolsetReader reader = GetStandardConfigurationReader();

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);
            string msbuildOverrideTasksPath = null;
            string defaultOverrideToolsVersion = null;
            string defaultToolsVersion = reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), true, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);

            Assert.AreEqual(null, defaultToolsVersion);
            Assert.AreEqual(1, values.Count);
            Assert.AreEqual(0, values["2.0"].Properties.Count);
            Assert.AreEqual(@"D:\windows\Microsoft.NET\Framework\v2.0.x86ret", values["2.0"].ToolsPath);
            Assert.AreEqual("C:\\Cat", msbuildOverrideTasksPath);
        }

        /// <summary>
        /// Default toolset has no toolsVersion element definition
        /// </summary>
        [TestMethod]
        public void GetToolsetDataFromConfiguration_DefaultToolsetUndefined()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""nonexistent"">
                     <toolset toolsVersion=""2.0"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v2.0.x86ret\""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            ToolsetReader reader = GetStandardConfigurationReader();

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            // Should not throw
            string msbuildOverrideTasksPath = null;
            string defaultOverrideToolsVersion = null;
            reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), true, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);
        }

        /// <summary>
        /// Tests the case where msbuildToolsets is not specified in the config file
        /// Basically in the code we should be checking if config.GetSection("msbuildToolsets") returns a null
        /// </summary>
        [TestMethod]
        public void GetToolsetDataFromConfiguration_MSBuildToolsetsNodeNotPresent()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                 </configuration>");

            ToolsetReader reader = GetStandardConfigurationReader();

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string msbuildOverrideTasksPath = null;
            string defaultOverrideToolsVersion = null;
            string defaultToolsVersion = reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), true, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);

            Assert.AreEqual(null, defaultToolsVersion);
            Assert.AreEqual(0, values.Count);
        }

        /// <summary>
        /// Tests that we handle empty MSBuildToolsets element correctly
        /// </summary>
        [TestMethod]
        public void GetToolsetDataFromConfiguration_EmptyMSBuildToolsetsNode()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets/>
                 </configuration>");

            ToolsetReader reader = GetStandardConfigurationReader();

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string msbuildOverrideTasksPath = null;
            string defaultOverrideToolsVersion = null;
            string defaultToolsVersion = reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), true, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);

            Assert.AreEqual(null, defaultToolsVersion);
            Assert.AreEqual(0, values.Count);
        }

        /// <summary>
        /// Tests the case where only default ToolsVersion is specified in the application configuration file
        /// </summary>
        [TestMethod]
        public void GetToolsetDataFromConfiguration_OnlyDefaultSpecified()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""2.0""/>
                 </configuration>");

            ToolsetReader reader = GetStandardConfigurationReader();

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            // Should not throw
            string msbuildOverrideTasksPath = null;
            string defaultOverrideToolsVersion = null;
            string defaultToolsVersion = reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), true, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);

            Assert.AreEqual(0, values.Count);
        }

        /// <summary>
        /// Tests the case where only one ToolsVersion data is specified in the application configuration file
        /// </summary>
        [TestMethod]
        public void GetToolsetDataFromConfiguration_OneToolsVersionNode()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""2.0"">
                     <toolset toolsVersion=""2.0"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v2.0.x86ret\""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            ToolsetReader reader = GetStandardConfigurationReader();

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string msbuildOverrideTasksPath = null;
            string defaultOverrideToolsVersion = null;
            string defaultToolsVersion = reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), true, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);

            Assert.AreEqual("2.0", defaultToolsVersion);
            Assert.AreEqual(@"D:\windows\Microsoft.NET\Framework\v2.0.x86ret", values["2.0"].ToolsPath);
            Assert.AreEqual(1, values.Count);
        }

        /// <summary>
        /// Tests the case when an invalid value of ToolsVersion is specified
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(InvalidToolsetDefinitionException))]
        public void GetToolsetDataFromConfiguration_ToolsVersionIsEmptyString()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""2.0"">
                     <toolset toolsVersion="""">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v2.0.x86ret\""/>
                     </toolset>
                     <toolset toolsVersion=""4.0"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v4.0.x86ret\""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            ToolsetReader reader = GetStandardConfigurationReader();

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            //this should throw ...
            string msbuildOverrideTasksPath = null;
            string defaultOverrideToolsVersion = null;
            reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), true, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);
        }

        /// <summary>
        /// If both MSBuildToolsPath and MSBuildBinPath are present, they must match
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(InvalidToolsetDefinitionException))]
        public void GetToolsetDataFromConfiguration_ToolsPathAndBinPathDiffer()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""2.0"">
                     <toolset toolsVersion=""2.0"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v2.0.x86ret\""/>
                       <property name=""MSBuildToolsPath"" value=""D:\windows\Microsoft.NET\Framework\v4.0.x86ret\""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            ToolsetReader reader = GetStandardConfigurationReader();

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string msbuildOverrideTasksPath = null;
            string defaultOverrideToolsVersion = null;
            reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), true, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);
        }

        /// <summary>
        /// Tests the case when a blank value of PropertyName is specified in the config file
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(InvalidToolsetDefinitionException))]
        public void BlankPropertyNameInConfigFile()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""2.0"">
                     <toolset toolsVersion=""2.0"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v2.0.x86ret\""/>
                       <property name="""" value=""foo""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            ToolsetReader reader = GetStandardConfigurationReader();

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            //this should throw ...
            string msbuildOverrideTasksPath = null;
            string defaultOverrideToolsVersion = null;
            reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), true, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);
        }

        /// <summary>
        /// Tests the case when a blank property name is specified in the registry
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(InvalidToolsetDefinitionException))]
        public void BlankPropertyNameInRegistry()
        {
            RegistryKey rk = _toolsVersionsRegistryKey.CreateSubKey("2.0");
            rk.SetValue("MSBuildBinPath", "someBinPath");
            rk.SetValue("", "foo");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            // Should throw ...
            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.Registry
                                                      );
        }

        /// <summary>
        /// Tests the case when a blank property name is specified in the registry in a 
        /// sub-toolset.
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(InvalidToolsetDefinitionException))]
        public void BlankPropertyNameInRegistrySubToolset()
        {
            RegistryKey rk = _toolsVersionsRegistryKey.CreateSubKey("2.0");
            rk.SetValue("MSBuildBinPath", "someBinPath");

            RegistryKey subToolsetKey = rk.CreateSubKey("11.0");
            subToolsetKey.SetValue("", "foo");

            PropertyDictionary<ProjectPropertyInstance> globalProperties = new PropertyDictionary<ProjectPropertyInstance>();
            globalProperties.Set(ProjectPropertyInstance.Create("VisualStudioVersion", "11.0"));

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            // Should throw ...
            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           globalProperties,
                                                           ToolsetDefinitionLocations.Registry
                                                      );
        }

        /// <summary>
        /// Tests the case when a blank property value is specified in the config file
        /// </summary>
        [TestMethod]
        public void BlankPropertyValueInConfigFile()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""2.0"">
                     <toolset toolsVersion=""2.0"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v2.0.x86ret\""/>
                       <property name=""foo"" value=""""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            ToolsetReader reader = GetStandardConfigurationReader();

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            //this should not throw ...
            string msbuildOverrideTasksPath = null;
            string defaultOverrideToolsVersion = null;
            reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), true, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);
        }

        /// <summary>
        /// Tests the case when a blank property value is specified in the registry
        /// </summary>
        [TestMethod]
        public void BlankPropertyValueInRegistry()
        {
            RegistryKey rk = _toolsVersionsRegistryKey.CreateSubKey("2.0");
            rk.SetValue("MSBuildBinPath", "someBinPath");
            rk.SetValue("foo", "");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            // Should not throw ...
            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.Registry
                                                       );
        }

        /// <summary>
        /// Tests the case when a blank property value is specified in the registry
        /// </summary>
        [TestMethod]
        public void BlankPropertyValueInRegistrySubToolset()
        {
            RegistryKey rk = _toolsVersionsRegistryKey.CreateSubKey("2.0");
            rk.SetValue("MSBuildBinPath", @"c:\someBinPath");

            RegistryKey subToolsetKey = rk.CreateSubKey("11.0");
            subToolsetKey.SetValue("foo", "");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            // Should not throw ...
            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.Registry
                                                       );

            Assert.AreEqual("2.0", defaultToolsVersion);
            Assert.AreEqual(0, values["2.0"].Properties.Count);
            Assert.AreEqual(@"c:\someBinPath", values["2.0"].ToolsPath);
            Assert.AreEqual(1, values["2.0"].SubToolsets.Count);
            Assert.AreEqual("", values["2.0"].SubToolsets["11.0"].Properties["foo"].EvaluatedValue);
        }

        /// <summary>
        /// Tests the case when an invalid value of PropertyName is specified in the config file
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(InvalidToolsetDefinitionException))]
        public void InvalidPropertyNameInConfigFile()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""2.0"">
                     <toolset toolsVersion=""2.0"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v2.0.x86ret\""/>
                       <property name=""&amp;"" value=""foo""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            ToolsetReader reader = GetStandardConfigurationReader();

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            //this should throw ...
            string msbuildOverrideTasksPath = null;
            string defaultOverrideToolsVersion = null;
            reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), true, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);
        }

        /// <summary>
        /// Tests the case when an invalid value of PropertyName is specified in the registry
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(InvalidToolsetDefinitionException))]
        public void InvalidPropertyNameInRegistry()
        {
            RegistryKey rk = _toolsVersionsRegistryKey.CreateSubKey("2.0");
            rk.SetValue("MSBuildBinPath", "someBinPath");
            rk.SetValue("foo|bar", "x");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            // Should throw ...
            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.Registry
                                                       );
        }

        /// <summary>
        /// Tests the case when an invalid value of PropertyName is specified in the registry
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(InvalidToolsetDefinitionException))]
        public void InvalidPropertyNameInRegistrySubToolset()
        {
            RegistryKey rk = _toolsVersionsRegistryKey.CreateSubKey("2.0");

            RegistryKey subToolsetKey = rk.CreateSubKey("10.0");
            subToolsetKey.SetValue("MSBuildBinPath", "someBinPath");
            subToolsetKey.SetValue("foo|bar", "x");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            // Should throw ...
            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.Registry
                                                       );
        }


        /// <summary>
        /// Tests that empty string is an invalid value for MSBuildBinPath
        /// </summary>
        [TestMethod]
        public void GetToolsetDataFromConfiguration_PropertyValueIsEmptyString1()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""2.0"">
                     <toolset toolsVersion=""2.0"">
                       <property name=""MSBuildBinPath"" value=""""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            ToolsetReader reader = GetStandardConfigurationReader();

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string msbuildOverrideTasksPath;
            string defaultOverrideToolsVersion = null;
            string defaultToolsVersion = reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), true, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);

            Assert.AreEqual(0, values.Count);
        }

        /// <summary>
        /// Tests that empty string is a valid property value for an arbitrary property
        /// </summary>
        [TestMethod]
        public void GetToolsetDataFromConfiguration_PropertyValueIsEmptyString2()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""2.0"">
                     <toolset toolsVersion=""2.0"">
                       <property name=""MSBuildToolsPath"" value=""xxx""/>
                       <property name=""foo"" value=""""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            ToolsetReader reader = GetStandardConfigurationReader();

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string msbuildOverrideTasksPath;
            string defaultOverrideToolsVersion = null;
            string defaultToolsVersion = reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), true, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);

            Assert.AreEqual(1, values.Count);
            Assert.AreEqual(1, values["2.0"].Properties.Count);
            Assert.AreEqual(String.Empty, values["2.0"].Properties["foo"].EvaluatedValue);
        }

        /// <summary>
        /// Tests that any escaped xml in config file, is treated well
        /// Note that this comes for free with the current implementation using the 
        /// framework api to access section in the config file
        /// </summary>
        [TestMethod]
        public void GetToolsetDataFromConfiguration_XmlEscapedCharacters()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""2&gt;.0"">
                     <toolset toolsVersion=""2&gt;.0"">
                       <property name=""MSBuildBinPath"" value=""x""/>
                       <property name=""foo"" value=""some&gt;value""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            ToolsetReader reader = GetStandardConfigurationReader();

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);
            string msbuildOverrideTasksPath = null;
            string defaultOverrideToolsVersion = null;
            string defaultToolsVersion = reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), true, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);

            Assert.AreEqual("2>.0", defaultToolsVersion);
            Assert.AreEqual(1, values.Count);
            Assert.AreEqual(@"some>value", values["2>.0"].Properties["foo"].EvaluatedValue);
        }
        #endregion

        #region "GetToolsetData tests"

        /// <summary>
        /// Tests the case where registry and config file contains different toolsVersion
        /// </summary>
        [TestMethod]
        public void GetToolsetData_NoConflict()
        {
            // Set up registry with two tools versions and one property each
            _currentVersionRegistryKey.SetValue("DefaultToolsVersion", "2.0");
            RegistryKey key1 = _toolsVersionsRegistryKey.CreateSubKey("2.0");
            key1.SetValue("MSBuildBinPath", @"D:\somepath");
            RegistryKey key2 = _toolsVersionsRegistryKey.CreateSubKey("4.0");
            key2.SetValue("MSBuildBinPath", @"D:\somepath2");

            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""4.5"">
                     <toolset toolsVersion=""4.5"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v2.0.x86ret\""/>
                     </toolset>
                     <toolset toolsVersion=""5.0"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v4.0.x86ret\""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.ConfigurationFile | ToolsetDefinitionLocations.Registry
                                                       );

            // Verifications
            Assert.AreEqual(4, values.Count);
            Assert.AreEqual("4.5", defaultToolsVersion);
            Assert.AreEqual(@"D:\somepath", values["2.0"].ToolsPath);
            Assert.AreEqual(@"D:\somepath2", values["4.0"].ToolsPath);
            Assert.AreEqual(@"D:\windows\Microsoft.NET\Framework\v2.0.x86ret", values["4.5"].ToolsPath);
            Assert.AreEqual(@"D:\windows\Microsoft.NET\Framework\v4.0.x86ret", values["5.0"].ToolsPath);
        }

        /// <summary>
        /// Tests that ToolsetInitialization are respected.
        /// </summary>
        [TestMethod]
        public void ToolsetInitializationFlagsSetToNone()
        {
            // Set up registry with two tools versions and one property each
            _currentVersionRegistryKey.SetValue("DefaultToolsVersion", "22.0");
            RegistryKey key1 = _toolsVersionsRegistryKey.CreateSubKey("33.0");
            key1.SetValue("MSBuildBinPath", @"D:\somepath");
            RegistryKey key2 = _testRegistryKey.CreateSubKey("55.0");
            key2.SetValue("MSBuildBinPath", @"D:\somepath2");

            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""4.5"">
                     <toolset toolsVersion=""5.5"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v2.0.x86ret\""/>
                     </toolset>
                     <toolset toolsVersion=""6.5"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v4.0.x86ret\""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.None
                                                       );

            // Verifications
            Assert.AreEqual(1, values.Count);

            string expectedDefault = "2.0";
            if (FrameworkLocationHelper.PathToDotNetFrameworkV20 == null)
            {
                expectedDefault = ObjectModelHelpers.MSBuildDefaultToolsVersion;
            }

            Assert.AreEqual(expectedDefault, defaultToolsVersion);
        }

        /// <summary>
        /// Tests that ToolsetInitialization are respected.
        /// </summary>
        [TestMethod]
        public void ToolsetInitializationFlagsSetToRegistry()
        {
            // Set up registry with two tools versions and one property each
            _currentVersionRegistryKey.SetValue("DefaultToolsVersion", "2.0");
            RegistryKey key1 = _toolsVersionsRegistryKey.CreateSubKey("2.0");
            key1.SetValue("MSBuildBinPath", @"D:\somepath");
            RegistryKey key2 = _toolsVersionsRegistryKey.CreateSubKey("4.0");
            key2.SetValue("MSBuildBinPath", @"D:\somepath2");

            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""4.5"">
                     <toolset toolsVersion=""4.5"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v2.0.x86ret\""/>
                     </toolset>
                     <toolset toolsVersion=""5.0"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v4.0.x86ret\""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);
            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.Registry
                                                       );

            // Verifications
            Assert.AreEqual(2, values.Count);
            Assert.AreEqual("2.0", defaultToolsVersion);
            Assert.AreEqual(@"D:\somepath", values["2.0"].ToolsPath);
            Assert.AreEqual(@"D:\somepath2", values["4.0"].ToolsPath);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidToolsetDefinitionException))]
        public void ThrowOnNonStringRegistryValueTypes()
        {
            RegistryKey rk = _toolsVersionsRegistryKey.CreateSubKey("2.0");
            rk.SetValue("MSBuildBinPath", "someBinPath");

            // Non-string
            rk.SetValue("QuadWordValue", 42, RegistryValueKind.QWord);

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            // Should throw ...
            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.Registry
                                                       );
        }

        [TestMethod]
        public void PropertiesInRegistryCannotReferToOtherPropertiesInRegistry()
        {
            RegistryKey rk = _toolsVersionsRegistryKey.CreateSubKey("2.0");
            rk.SetValue("MSBuildBinPath", "c:\\x$(p1)");
            rk.SetValue("p0", "$(p1)");
            rk.SetValue("p1", "v");
            rk.SetValue("p2", "$(p1)");
            rk.SetValue("MSBuildToolsPath", "c:\\x$(p1)");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.Registry
                                                       );

            Assert.AreEqual("", values["2.0"].Properties["p0"].EvaluatedValue);
            Assert.AreEqual("v", values["2.0"].Properties["p1"].EvaluatedValue);
            Assert.AreEqual("", values["2.0"].Properties["p2"].EvaluatedValue);
            Assert.AreEqual("c:\\x", values["2.0"].ToolsPath);
        }

        [TestMethod]
        public void SubToolsetPropertiesInRegistryCannotReferToOtherPropertiesInRegistry()
        {
            RegistryKey rk = _toolsVersionsRegistryKey.CreateSubKey("2.0");
            rk.SetValue("MSBuildBinPath", "c:\\x$(p1)");
            rk.SetValue("p0", "$(p1)");
            rk.SetValue("p1", "v");

            RegistryKey subToolsetKey = rk.CreateSubKey("dogfood");
            subToolsetKey.SetValue("p2", "$(p1)");
            subToolsetKey.SetValue("p3", "c:\\x$(p1)");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.Registry
                                                       );

            Assert.AreEqual("", values["2.0"].Properties["p0"].EvaluatedValue);
            Assert.AreEqual("v", values["2.0"].Properties["p1"].EvaluatedValue);
            Assert.AreEqual("", values["2.0"].SubToolsets["dogfood"].Properties["p2"].EvaluatedValue);
            Assert.AreEqual("c:\\x", values["2.0"].SubToolsets["dogfood"].Properties["p3"].EvaluatedValue);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidToolsetDefinitionException))]
        public void SubToolsetsCannotDefineMSBuildToolsPath()
        {
            RegistryKey rk = _toolsVersionsRegistryKey.CreateSubKey("2.0");
            rk.SetValue("p0", "$(p1)");
            rk.SetValue("p1", "v");

            RegistryKey subToolsetKey = rk.CreateSubKey("dogfood");
            subToolsetKey.SetValue("p2", "$(p1)");
            subToolsetKey.SetValue("MSBuildToolsPath", "c:\\x$(p1)");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            // throws
            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.Registry
                                                       );
        }

        /// <summary>
        /// Tests that ToolsetInitialization are respected.
        /// </summary>
        [TestMethod]
        public void ToolsetInitializationFlagsSetToConfigurationFile()
        {
            // Set up registry with two tools versions and one property each
            _currentVersionRegistryKey.SetValue("DefaultToolsVersion", "2.0");
            RegistryKey key1 = _toolsVersionsRegistryKey.CreateSubKey("2.0");
            key1.SetValue("MSBuildBinPath", @"D:\somepath");
            RegistryKey key2 = _toolsVersionsRegistryKey.CreateSubKey("4.0");
            key2.SetValue("MSBuildBinPath", @"D:\somepath2");

            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""4.5"">
                     <toolset toolsVersion=""4.5"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v2.0.x86ret\""/>
                     </toolset>
                     <toolset toolsVersion=""5.0"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v4.0.x86ret\""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.ConfigurationFile
                                                       );

            // Verifications
            Assert.AreEqual(2, values.Count);
            Assert.AreEqual("4.5", defaultToolsVersion);
            Assert.AreEqual(@"D:\windows\Microsoft.NET\Framework\v2.0.x86ret", values["4.5"].ToolsPath);
            Assert.AreEqual(@"D:\windows\Microsoft.NET\Framework\v4.0.x86ret", values["5.0"].ToolsPath);
        }

        /// <summary>
        /// Properties in the configuration file may refer to a registry location by using the syntax for example
        /// "$(Registry:HKEY_LOCAL_MACHINE\Software\Vendor\Tools@TaskLocation)", where "HKEY_LOCAL_MACHINE\Software\Vendor\Tools" is the key and
        /// "TaskLocation" is the name of the value.  The name of the value and the preceding "@" may be omitted if
        /// the default value is desired.
        /// </summary>
        [TestMethod]
        public void PropertyInConfigurationFileReferencesRegistryLocation()
        {
            // Registry Read
            RegistryKey key1 = Registry.CurrentUser.CreateSubKey(@"Software\Vendor\Tools");
            key1.SetValue("TaskLocation", @"somePathToTasks");
            key1.SetValue("TargetsLocation", @"D:\somePathToTargets");
            key1.SetValue("SchemaLocation", @"Schemas");
            key1.SetValue(null, @"D:\somePathToDefault");  //this sets the default value for this key

            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""2.0"">
                     <toolset toolsVersion=""2.0"">
                       <property name=""MSBuildBinPath"" value=""D:\$(Registry:HKEY_CURRENT_USER\Software\Vendor\Tools@TaskLocation)""/>
                       <property name=""p1"" value=""$(p2)$(REGISTRY:HKEY_CURRENT_USER\Software\Vendor\Tools)""/>
                       <property name=""p2"" value=""$(p1)\$(Registry:hkey_current_user\Software\Vendor\Tools@TaskLocation)\$(Registry:HKEY_CURRENT_USER\Software\Vendor\Tools@SchemaLocation)\2.0""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.ConfigurationFile | ToolsetDefinitionLocations.Registry
                                                       );

            Assert.AreEqual(1, values.Count);
            Assert.AreEqual(@"D:\somePathToTasks", values["2.0"].ToolsPath);
            Assert.AreEqual(2, values["2.0"].Properties.Count);
            Assert.AreEqual(@"D:\somePathToDefault", values["2.0"].Properties["p1"].EvaluatedValue);
            Assert.AreEqual(@"D:\somePathToDefault\somePathToTasks\Schemas\2.0", values["2.0"].Properties["p2"].EvaluatedValue);

            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Vendor");
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidToolsetDefinitionException))]
        public void ToolsPathInRegistryHasInvalidPathChars()
        {
            _currentVersionRegistryKey.SetValue("DefaultToolsVersion", "2.0");
            RegistryKey key1 = _toolsVersionsRegistryKey.CreateSubKey("2.0");
            key1.SetValue("MSBuildBinPath", @"D:\some\foo|bar\path\");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            // should throw... 
            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           null,
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.Registry
                                                       );
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidToolsetDefinitionException))]
        public void SamePropertyDefinedMultipleTimesForSingleToolsVersionInConfigurationFile()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""2.0"">
                     <toolset toolsVersion=""2.0"">
                       <property name=""MSBuildBinPath"" value=""D:\some\folder\on\disk""/>
                       <property name=""p1"" value=""another""/>
                       <property name=""MSBuildBinPath"" value=""C:\$(p1)\folder""/>
                       <property name=""p1"" value=""newValue""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           null,
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.ConfigurationFile
                                                       );
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidToolsetDefinitionException))]
        public void SamePropertyDifferentCaseDefinedMultipleTimesForSingleToolsVersionInConfigurationFile()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""2.0"">
                     <toolset toolsVersion=""2.0"">
                       <property name=""MSBuildBinPath"" value=""D:\some\folder\on\disk""/>
                       <property name=""p1"" value=""another""/>
                       <property name=""mSbUiLdBiNpAtH"" value=""C:\$(p1)\folder""/>
                       <property name=""P1"" value=""newValue""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           null,
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.ConfigurationFile
                                                       );
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidToolsetDefinitionException))]
        public void SameToolsVersionDefinedMultipleTimesInConfigurationFile()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""2.0"">
                     <toolset toolsVersion=""2.0"">
                       <property name=""MSBuildBinPath"" value=""D:\some\folder\on\disk""/>
                       <property name=""p1"" value=""another""/>
                     </toolset>
                     <toolset toolsVersion=""4.0"">
                       <property name=""MSBuildBinPath"" value=""D:\folder""/>
                       <property name=""p2"" value=""anotherValue""/>
                     </toolset>
                     <toolset toolsVersion=""2.0"">
                       <property name=""MSBuildBinPath"" value=""D:\some\folder\on\disk""/>
                       <property name=""p1"" value=""another""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           null,
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.ConfigurationFile
                                                       );
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidToolsetDefinitionException))]
        public void SameToolsVersionDifferentCaseDefinedMultipleTimesInConfigurationFile()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""2.0"">
                     <toolset toolsVersion=""DevDiv"">
                       <property name=""MSBuildBinPath"" value=""D:\some\folder\on\disk""/>
                       <property name=""p1"" value=""another""/>
                     </toolset>
                     <toolset toolsVersion=""4.0"">
                       <property name=""MSBuildBinPath"" value=""D:\folder""/>
                       <property name=""p2"" value=""anotherValue""/>
                     </toolset>
                     <toolset toolsVersion=""DEVDIV"">
                       <property name=""MSBuildBinPath"" value=""D:\some\folder\on\disk""/>
                       <property name=""p1"" value=""another""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           null,
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.ConfigurationFile
                                                       );
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidToolsetDefinitionException))]
        public void CannotSetReservedPropertyInConfigFile()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""2.0"">
                     <toolset toolsVersion=""2.0"">
                       <property name=""MSBuildBinPath"" value=""D:\some\folder\on\disk""/>
                       <property name=""MSBuildProjectFile"" value=""newValue""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           null,
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.ConfigurationFile
                                                       );
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidToolsetDefinitionException))]
        public void CannotSetReservedPropertyInRegistry()
        {
            // Registry Read
            RegistryKey key1 = _toolsVersionsRegistryKey.CreateSubKey("2.0");
            key1.SetValue("MSBuildBinPath", @"D:\somepath");
            key1.SetValue("MSBuildProjectFile", @"SomeRegistryValue");


            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);
            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           null,
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.Registry
                                                       );
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidToolsetDefinitionException))]
        public void CannotSetReservedPropertyInRegistrySubToolset()
        {
            // Registry Read
            RegistryKey key1 = _toolsVersionsRegistryKey.CreateSubKey("2.0");
            RegistryKey subKey1 = key1.CreateSubKey("Foo");

            subKey1.SetValue("MSBuildBinPath", @"D:\somepath");
            subKey1.SetValue("MSBuildProjectFile", @"SomeRegistryValue");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);
            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           null,
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.Registry
                                                       );
        }

        /// <summary>
        /// Properties defined in previously processed toolset definitions should
        /// not affect the evaluation of subsequent toolset definitions.
        /// </summary>
        [TestMethod]
        public void NoInterferenceBetweenToolsetDefinitions()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""2.0"">
                     <toolset toolsVersion=""2.0"">
                       <property name=""MSBuildBinPath"" value=""D:\20\some\folder\on\disk""/>
                       <property name=""p1"" value=""another""/>
                       <property name=""p4"" value=""fourth$(p3)Value"" />
                     </toolset>
                     <toolset toolsVersion=""4.0"">
                       <property name=""MSBuildBinPath"" value=""D:\35\some\folder\on\disk""/>
                       <property name=""p2"" value=""some$(p1)value""/>
                       <property name=""p3"" value=""propertyValue""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           null,
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),

                                                           ToolsetDefinitionLocations.ConfigurationFile
                                                       );

            Assert.AreEqual(2, values.Count);

            Assert.AreEqual(@"D:\20\some\folder\on\disk", values["2.0"].ToolsPath);
            Assert.AreEqual(2, values["2.0"].Properties.Count);
            Assert.AreEqual(@"another", values["2.0"].Properties["p1"].EvaluatedValue);
            Assert.AreEqual(@"fourthValue", values["2.0"].Properties["p4"].EvaluatedValue);

            Assert.AreEqual(@"D:\35\some\folder\on\disk", values["4.0"].ToolsPath);
            Assert.AreEqual(2, values["4.0"].Properties.Count);
            Assert.AreEqual(@"somevalue", values["4.0"].Properties["p2"].EvaluatedValue);
            Assert.AreEqual(@"propertyValue", values["4.0"].Properties["p3"].EvaluatedValue);
        }

        /// <summary>
        /// Properties in the configuration file may refer to a registry location by using the syntax for example
        /// "$(Registry:HKEY_LOCAL_MACHINE\Software\Vendor\Tools@TaskLocation)", where "HKEY_LOCAL_MACHINE\Software\Vendor\Tools" is the key and
        /// "TaskLocation" is the name of the value.  The name of the value and the preceding "@" may be omitted if
        /// the default value is desired.
        /// </summary>

        [TestMethod]
        public void ConfigFileInvalidRegistryExpression1()
        {
            // No location
            ConfigFileInvalidRegistryExpressionHelper(@"<property name=""p"" value=""$(Registry:)""/>");
        }

        [TestMethod]
        public void ConfigFileInvalidRegistryExpression2()
        {
            // Bogus key expression
            ConfigFileInvalidRegistryExpressionHelper(@"<property name=""p"" value=""$(Registry:__bogus__)""/>");
        }

        [TestMethod]
        public void ConfigFileInvalidRegistryExpression3()
        {
            // No registry location just @
            ConfigFileInvalidRegistryExpressionHelper(@"<property name=""p"" value=""$(Registry:@)""/>");
        }

        [TestMethod]
        public void ConfigFileInvalidRegistryExpression4()
        {
            // Double @
            ConfigFileInvalidRegistryExpressionHelper(@"<property name=""p"" value=""$(Registry:HKEY_CURRENT_USER\Software\Vendor\Tools@@TaskLocation)""/>");
        }

        [TestMethod]
        public void ConfigFileInvalidRegistryExpression5()
        {
            // Trailing @
            ConfigFileInvalidRegistryExpressionHelper(@"<property name=""p"" value=""$(Registry:HKEY_CURRENT_USER\Software\Vendor\Tools@TaskLocation@)""/>");
        }

        [TestMethod]
        public void ConfigFileInvalidRegistryExpression6()
        {
            // Leading @
            ConfigFileInvalidRegistryExpressionHelper(@"<property name=""p"" value=""$(Registry:@HKEY_CURRENT_USER\Software\Vendor\Tools@TaskLocation)""/>");
        }

        [TestMethod]
        public void ConfigFileInvalidRegistryExpression7()
        {
            // Bogus hive
            ConfigFileInvalidRegistryExpressionHelper(@"<property name=""p"" value=""$(Registry:BOGUS_HIVE\Software\Vendor\Tools@TaskLocation)""/>");
        }

        [TestMethod]
        public void ConfigFileStringEmptyRegistryExpression1()
        {
            // Regular undefined property beginning with "Registry"
            ConfigFileValidRegistryExpressionHelper(@"<property name=""p"" value=""$(Registry)""/>",
                                          String.Empty);
        }

        [TestMethod]
        public void ConfigFileStringEmptyRegistryExpression2()
        {
            // Nonexistent key
            ConfigFileValidRegistryExpressionHelper(@"<property name=""p"" value=""$(Registry:HKEY_CURRENT_USER\Nonexistent_Key\Software\Vendor\Tools@TaskLocation)""/>",
                                          String.Empty);
        }

        [TestMethod]
        public void ConfigFileNonPropertyRegistryExpression1()
        {
            // Property not terminated with paren, does not look like property
            ConfigFileValidRegistryExpressionHelper(@"<property name=""p"" value=""$(Registry:HKEY_CURRENT_USER\Software\Vendor\Tools@TaskLocation""/>",
                                          @"$(Registry:HKEY_CURRENT_USER\Software\Vendor\Tools@TaskLocation");
        }

        [TestMethod]
        public void ConfigFileNonPropertyRegistryExpression2()
        {
            // Missing colon, looks like regular property (but with invalid property name chars, we will return blank as a result)
            ConfigFileValidRegistryExpressionHelper(@"<property name=""p"" value=""$(RegistryHKEY_CURRENT_USER\Software\Vendor\Tools@@TaskLocation)""/>",
                                          String.Empty);
        }

        [TestMethod]
        public void ConfigFileItemExpressionsDoNotExpandInConfigurationProperties()
        {
            // Expect that item expressions such as '@(SomeItem)' are not evaluated in any way, e.g., they are treated literally
            ConfigFileValidRegistryExpressionHelper(@"<property name=""p"" value=""@(SomeItem)""/>",
                                          @"@(SomeItem)");
        }

        [TestMethod]
        public void RegistryInvalidRegistryExpression1()
        {
            // Bogus key expression
            RegistryInvalidRegistryExpressionHelper("$(Registry:__bogus__)");
        }

        [TestMethod]
        public void RegistryValidRegistryExpression1()
        {
            // Regular undefined property beginning with "Registry"
            RegistryValidRegistryExpressionHelper("$(Registry)", String.Empty);
        }

        private void RegistryInvalidRegistryExpressionHelper(string propertyExpression)
        {
            bool caught = false;
            try
            {
                // this should throw...
                RegistryValidRegistryExpressionHelper(propertyExpression, String.Empty);
            }
            catch (InvalidToolsetDefinitionException ex)
            {
                Console.WriteLine(ex.Message);
                caught = true;
            }

            Assert.AreEqual(true, caught);
        }

        private void RegistryValidRegistryExpressionHelper(string propertyExpression, string expectedValue)
        {
            // Registry Read
            _currentVersionRegistryKey.SetValue("DefaultToolsVersion", "2.0");
            RegistryKey key1 = _toolsVersionsRegistryKey.CreateSubKey("2.0");
            key1.SetValue("MSBuildBinPath", "xxxx");
            key1.SetValue("p", propertyExpression);

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            ToolsetReader.ReadAllToolsets
                                       (
                                           values,
                                           GetStandardRegistryReader(),
                                           GetStandardConfigurationReader(),
                                           new ProjectCollection().EnvironmentProperties,
                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                           ToolsetDefinitionLocations.Registry
                                       );

            Assert.AreEqual(1, values.Count);
            Assert.AreEqual(expectedValue, values["2.0"].Properties["p"].EvaluatedValue);
        }

        /// <summary>
        /// Tests that an invalid registry property expression causes an exception (resulting in a project load error)
        /// </summary>
        /// <param name="propertyExpression"></param>
        private void ConfigFileInvalidRegistryExpressionHelper(string propertyExpression)
        {
            bool caught = false;
            try
            {
                // this should throw...
                ConfigFileValidRegistryExpressionHelper(propertyExpression, String.Empty);
            }
            catch (InvalidToolsetDefinitionException ex)
            {
                Console.WriteLine(ex.Message);
                caught = true;
            }

            Assert.AreEqual(true, caught);
        }

        /// <summary>
        /// Tests that a specified registry property expression evaluates to specified value
        /// </summary>
        /// <param name="propertyExpression"></param>
        private void ConfigFileValidRegistryExpressionHelper(string propertyExpression, string expectedValue)
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""2.0"">
                     <toolset toolsVersion=""2.0"">
                       <property name=""MSBuildBinPath"" value=""xxxx""/>
                       " + propertyExpression + @"
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            ToolsetReader.ReadAllToolsets
                                       (
                                           values,
                                           GetStandardRegistryReader(),
                                           GetStandardConfigurationReader(),
                                           new ProjectCollection().EnvironmentProperties,
                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                           ToolsetDefinitionLocations.ConfigurationFile
                                       );

            Assert.AreEqual(1, values.Count);
            Assert.AreEqual(expectedValue, values["2.0"].Properties["p"].EvaluatedValue);
        }

        /// <summary>
        /// Tests the case where application configuration file overrides a value already specified in the registry
        /// </summary>
        [TestMethod]
        public void GetToolsetData_ConflictingPropertyValuesSameCase()
        {
            // Registry Read
            RegistryKey key1 = _toolsVersionsRegistryKey.CreateSubKey("2.0");
            key1.SetValue("MSBuildBinPath", @"D:\somepath");

            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""2.0"">
                     <toolset toolsVersion=""2.0"">
                       <property name=""MSBuildBinPath"" value=""D:\somedifferentpath""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.ConfigurationFile | ToolsetDefinitionLocations.Registry
                                                       );

            Assert.AreEqual(1, values.Count);
            Assert.AreEqual(0, values["2.0"].Properties.Count);
            Assert.AreEqual(@"D:\somedifferentpath", values["2.0"].ToolsPath);
        }

        /// <summary>
        /// Tests the case where application configuration file overrides a value already specified in the registry, 
        /// where that registry value is bogus and would otherwise throw.  However, since the config file also 
        /// contains an entry for that toolset, the registry toolset never gets read, and thus never throws.  
        /// </summary>
        [TestMethod]
        public void GetToolsetData_ConflictingPropertyValuesRegistryThrows()
        {
            // Registry Read
            RegistryKey key1 = _toolsVersionsRegistryKey.CreateSubKey("2.0");
            key1.SetValue("MSBuildBinPath", @"$([MSBuild]::SomeNonexistentPropertyFunction())");

            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""2.0"">
                     <toolset toolsVersion=""2.0"">
                       <property name=""MSBuildBinPath"" value=""D:\somepath""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.ConfigurationFile | ToolsetDefinitionLocations.Registry
                                                       );

            Assert.AreEqual(1, values.Count);
            Assert.AreEqual(0, values["2.0"].Properties.Count);
            Assert.AreEqual(@"D:\somepath", values["2.0"].ToolsPath);
        }

        /// <summary>
        /// Tests when properties are defined in the registry as
        /// well as in the config file for the same tools version.
        /// We should not merge them; we should take the config file ones only
        /// </summary>
        [TestMethod]
        public void GetToolsetData_NoMerging()
        {
            // Registry Read
            RegistryKey key1 = _toolsVersionsRegistryKey.CreateSubKey("2.0");
            key1.SetValue("MSBuildBinPath", @"D:\somepath");
            key1.SetValue("SomeRegistryProperty", @"SomeRegistryValue");

            // Set the config file contents as needed
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""2.0"">
                     <toolset toolsVersion=""2.0"">
                       <property name=""MSBuildBinPath"" value=""D:\someotherpath""/>
                       <property name=""SomeConfigProperty"" value=""SomeConfigValue""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.ConfigurationFile | ToolsetDefinitionLocations.Registry
                                                       );

            Assert.AreEqual(1, values.Count);
            Assert.AreEqual(1, values["2.0"].Properties.Count);
            Assert.AreEqual(@"D:\someotherpath", values["2.0"].ToolsPath);
            Assert.AreEqual(null, values["2.0"].Properties["SomeRegistryProperty"]); // Was zapped
            Assert.AreEqual(@"SomeConfigValue", values["2.0"].Properties["SomeConfigProperty"].EvaluatedValue);
        }

        /// <summary>
        /// The absence of the ToolsVersion attribute on the main Project element in a project file means
        /// that the engine's default tools version should be used.
        /// </summary>
        [TestMethod]
        public void ToolsVersionAttributeNotSpecifiedOnProjectElementAndDefaultVersionSpecifiedInRegistry()
        {
            string oldValue = Environment.GetEnvironmentVariable("MSBUILDLEGACYDEFAULTTOOLSVERSION");

            try
            {
                // In the new world of figuring out the ToolsVersion to use, we completely ignore the default
                // ToolsVersion in the ProjectCollection.  However, this test explicitly depends on modifying 
                // that, so we need to turn the new defaulting behavior off in order to verify that this still works.  
                Environment.SetEnvironmentVariable("MSBUILDLEGACYDEFAULTTOOLSVERSION", "1");
                InternalUtilities.RefreshInternalEnvironmentValues();

                ProjectCollection projectCollection = new ProjectCollection();

                string msbuildOverrideTasksPath = null;
                projectCollection.AddToolset(new Toolset("2.0", "20toolsPath", projectCollection, msbuildOverrideTasksPath));
                projectCollection.AddToolset(new Toolset(ObjectModelHelpers.MSBuildDefaultToolsVersion, "120toolsPath", projectCollection, msbuildOverrideTasksPath));

                string projectPath = ObjectModelHelpers.CreateFileInTempProjectDirectory("x.proj", @"<Project xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"" />");

                Project project = projectCollection.LoadProject(projectPath);

                string defaultExpected = "14.1";
                if (FrameworkLocationHelper.PathToDotNetFrameworkV20 == null)
                {
                    defaultExpected = ObjectModelHelpers.MSBuildDefaultToolsVersion;
                }

                Assert.AreEqual(defaultExpected, project.ToolsVersion);
                Assert.AreEqual(defaultExpected, projectCollection.DefaultToolsVersion);
            }
            finally
            {
                Environment.SetEnvironmentVariable("MSBUILDLEGACYDEFAULTTOOLSVERSION", oldValue);
                InternalUtilities.RefreshInternalEnvironmentValues();
            }
        }

        /// <summary>
        /// Tests the case when no values are specified in the registry
        /// </summary>
        [TestMethod]
        public void GetToolsetData_RegistryNotPresent()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""2.0"">
                     <toolset toolsVersion=""2.0"">
                       <property name=""MSBUILDBINPATH"" value=""D:\somedifferentpath""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.ConfigurationFile | ToolsetDefinitionLocations.Registry
                                                       );

            Assert.AreEqual(1, values.Count);
            Assert.AreEqual(0, values["2.0"].Properties.Count);
            Assert.AreEqual(@"D:\somedifferentpath", values["2.0"].ToolsPath);
        }

        /// <summary>
        /// Test the case where nothing is specified in the config file
        /// Note that config file not present is same as config file 
        /// with no MSBuildToolsets Section 
        /// </summary>
        [TestMethod]
        public void GetToolsetData_ConfigFileNotPresent()
        {
            // Registry Read
            RegistryKey key1 = _toolsVersionsRegistryKey.CreateSubKey("2.0");
            key1.SetValue("MSBuildBinPath", @"D:\somepath");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.ConfigurationFile | ToolsetDefinitionLocations.Registry
                                                       );

            Assert.AreEqual(1, values.Count);
            Assert.AreEqual(0, values["2.0"].Properties.Count);
            Assert.AreEqual(@"D:\somepath", values["2.0"].ToolsPath);
        }

        /// <summary>
        /// Tests the case where nothing is specified in registry and config file
        /// </summary>
        [TestMethod]
        public void GetToolsetData_RegistryAndConfigNotPresent()
        {
            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.ConfigurationFile | ToolsetDefinitionLocations.Registry
                                                       );

            // Should either be the last-ditch 2.0 toolset, or if 2.0 is not installed, then the last-last-ditch of 4.0
            Assert.AreEqual(1, values.Count);
            if (FrameworkLocationHelper.PathToDotNetFrameworkV20 != null)
            {
                Assert.AreEqual("2.0", defaultToolsVersion);
            }
            else
            {
                Assert.AreEqual(ObjectModelHelpers.MSBuildDefaultToolsVersion, defaultToolsVersion);
            }
        }

        /// <summary>
        /// Tests the case when reading config file throws an exception
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(InvalidToolsetDefinitionException))]
        public void GetToolsetData_ReadConfigThrowsException()
        {
            // Registry Read
            RegistryKey key1 = _toolsVersionsRegistryKey.CreateSubKey("2.0");
            key1.SetValue("MSBuildBinPath", @"D:\somepath");

            // Set the config helper to throw exception
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"", new ConfigurationErrorsException());

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            ToolsetReader.ReadAllToolsets
                       (
                           values,
                           GetStandardRegistryReader(),
                           GetStandardConfigurationReader(),
                           new ProjectCollection().EnvironmentProperties,
                           new PropertyDictionary<ProjectPropertyInstance>(),
                           ToolsetDefinitionLocations.ConfigurationFile | ToolsetDefinitionLocations.Registry
                       );
        }

        /// <summary>
        /// Tests the case where reading from registry throws exception
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(InvalidToolsetDefinitionException))]
        public void GetToolsetData_ReadRegistryOpenSubKeyThrowsException()
        {
            RegistryKeyWrapper mockRegistryKey =
                new MockRegistryKey(testRegistryPath, MockRegistryKey.WhereToThrow.OpenSubKey);

            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""2.0"">
                     <toolset toolsVersion=""2.0"">
                       <property name=""MSBuildBinPath"" value=""D:\somedifferentpath""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           new ToolsetRegistryReader(new ProjectCollection().EnvironmentProperties, new PropertyDictionary<ProjectPropertyInstance>(), mockRegistryKey),
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.ConfigurationFile | ToolsetDefinitionLocations.Registry
                                                       );
        }

        #endregion

        #region "SetDefaultToolsetVersion tests"

        /// <summary>
        /// Tests that the default ToolsVersion is correctly resolved when specified
        /// in registry and config file
        /// </summary>
        [TestMethod]
        public void SetDefaultToolsetVersion_SpecifiedInRegistryAndConfigFile()
        {
            // Set up registry with two tools versions and one property each
            _currentVersionRegistryKey.SetValue("DefaultToolsVersion", "2.0");
            RegistryKey key1 = _toolsVersionsRegistryKey.CreateSubKey("2.0");
            key1.SetValue("MSBuildBinPath", @"D:\somepath");
            RegistryKey key2 = _toolsVersionsRegistryKey.CreateSubKey("4.0");
            key2.SetValue("MSBuildBinPath", @"D:\somepath2");

            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""5.0"">
                     <toolset toolsVersion=""4.0"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v2.0.x86ret\""/>
                     </toolset>
                     <toolset toolsVersion=""5.0"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v4.0.x86ret\""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.ConfigurationFile | ToolsetDefinitionLocations.Registry
                                                       );

            Assert.AreEqual("5.0", defaultToolsVersion);
        }

        /// <summary>
        /// Tests that the default ToolsVersion is correctly resolved when specified in registry only
        /// </summary>
        [TestMethod]
        public void SetDefaultToolsetVersion_SpecifiedOnlyInRegistry()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets>
                     <toolset toolsVersion=""3.0"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v2.0.x86ret\""/>
                     </toolset>
                     <toolset toolsVersion=""5.0"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v4.0.x86ret\""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            // Set up registry with two tools versions and one property each
            _currentVersionRegistryKey.SetValue("DefaultToolsVersion", "4.0");
            RegistryKey key2 = _toolsVersionsRegistryKey.CreateSubKey("4.0");
            key2.SetValue("MSBuildBinPath", @"D:\somepath2");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.ConfigurationFile | ToolsetDefinitionLocations.Registry
                                                       );

            Assert.AreEqual("4.0", defaultToolsVersion);
        }

        /// <summary>
        /// Tests that the override task path is correctly resolved when specified in registry only
        /// </summary>
        [TestMethod]
        public void SetOverrideTasks_SpecifiedOnlyInRegistry()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets>
                     <toolset toolsVersion=""3.0"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v2.0.x86ret\""/>
                     </toolset>
                     <toolset toolsVersion=""5.0"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v4.0.x86ret\""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            // Set up registry with two tools versions and one property each
            _currentVersionRegistryKey.SetValue("DefaultToolsVersion", "4.0");
            _currentVersionRegistryKey.SetValue("msbuildOverrideTasksPath", "c:\\TaskOverridePath");
            RegistryKey key2 = _toolsVersionsRegistryKey.CreateSubKey("4.0");
            key2.SetValue("MSBuildBinPath", @"D:\somepath2");
            key2.SetValue("MSBuildBinPath", @"D:\OtherTaskOverridePath");
            RegistryKey key3 = _toolsVersionsRegistryKey.CreateSubKey("5.0");
            key3.SetValue("MSBuildBinPath", @"D:\somepath3");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.ConfigurationFile | ToolsetDefinitionLocations.Registry
                                                       );

            Assert.AreEqual("4.0", defaultToolsVersion);
            Assert.AreEqual("c:\\TaskOverridePath", values["4.0"].OverrideTasksPath);
            // Assert.AreEqual("c:\\OtherTaskOverridePath", values["5.0"].OverrideTasksPath); // UNDONE: Per-toolset override paths don't work.
        }

        /// <summary>
        /// Tests that the override default toolsversion is correctly resolved when specified in registry only
        /// </summary>
        [TestMethod]
        public void SetDefaultOverrideToolsVersion_SpecifiedOnlyInRegistry()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets>
                     <toolset toolsVersion=""3.0"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v2.0.x86ret\""/>
                     </toolset>
                     <toolset toolsVersion=""5.0"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v4.0.x86ret\""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            // Set up registry with two tools versions and one property each
            _currentVersionRegistryKey.SetValue("DefaultOverrideToolsVersion", "13.0");
            RegistryKey key = _toolsVersionsRegistryKey.CreateSubKey("13.0");
            key.SetValue("MSBuildBinPath", @"D:\somepath2");
            RegistryKey key2 = _toolsVersionsRegistryKey.CreateSubKey("4.0");
            key2.SetValue("MSBuildBinPath", @"D:\somepath3");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.ConfigurationFile | ToolsetDefinitionLocations.Registry
                                                       );

            Assert.AreEqual("13.0", values["4.0"].DefaultOverrideToolsVersion);
        }

        /// <summary>
        /// Tests that the default ToolsVersion is correctly resolved
        /// when specified in config file only
        /// </summary>
        [TestMethod]
        public void SetDefaultToolsetVersion_SpecifiedOnlyInConfigFile()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""5.0"">
                     <toolset toolsVersion=""4.0"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v2.0.x86ret\""/>
                     </toolset>
                     <toolset toolsVersion=""5.0"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v4.0.x86ret\""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.ConfigurationFile | ToolsetDefinitionLocations.Registry
                                                       );


            Assert.AreEqual("5.0", defaultToolsVersion);
        }

        /// <summary>
        /// Tests that the override tasks path ToolsVersion is correctly resolved
        /// when specified in config file only.
        /// Also, that MSBuildOverrideTasksPath can be overridden.
        /// </summary>
        [TestMethod]
        public void SetOverrideTaskPath_SpecifiedOnlyInConfigFile()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""5.0"" msbuildOverrideTasksPath=""C:\TaskOverride"">
                     <toolset toolsVersion=""4.0"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v2.0.x86ret\""/>
                     </toolset>
                     <toolset toolsVersion=""5.0"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v4.0.x86ret\""/>
                       <property name=""MSBuildOverrideTasksPath"" value=""c:\OtherTaskOverride""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.ConfigurationFile | ToolsetDefinitionLocations.Registry
                                                       );


            Assert.AreEqual("5.0", defaultToolsVersion);
            Assert.AreEqual("C:\\TaskOverride", values["4.0"].OverrideTasksPath);
            // Assert.AreEqual("C:\\OtherTaskOverride", values["5.0"].OverrideTasksPath); // UNDONE: Per-toolset override paths aren't working
        }

        /// <summary>
        /// Tests that the override default ToolsVersion is correctly resolved
        /// when specified in config file only.
        /// </summary>
        [TestMethod]
        public void SetDefaultOverrideToolsVersion_SpecifiedOnlyInConfigFile()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets default=""5.0"" DefaultOverrideToolsVersion=""3.0"">
                     <toolset toolsVersion=""4.0"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v2.0.x86ret\""/>
                     </toolset>
                     <toolset toolsVersion=""5.0"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v4.0.x86ret\""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.ConfigurationFile | ToolsetDefinitionLocations.Registry
                                                       );


            Assert.AreEqual("5.0", defaultToolsVersion);
            Assert.AreEqual("3.0", values["4.0"].DefaultOverrideToolsVersion);
        }

        /// <summary>
        /// Tests that the default ToolsVersion is correctly resolved when specified nowhere
        /// </summary>
        [TestMethod]
        public void SetDefaultToolsetVersion_SpecifiedNowhere()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets>
                     <toolset toolsVersion=""4.0"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v2.0.x86ret\""/>
                     </toolset>
                     <toolset toolsVersion=""5.0"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v4.0.x86ret\""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new PropertyDictionary<ProjectPropertyInstance>(),
                                                           ToolsetDefinitionLocations.ConfigurationFile | ToolsetDefinitionLocations.Registry
                                                       );

            string expectedDefault = "2.0";
            if (FrameworkLocationHelper.PathToDotNetFrameworkV20 == null)
            {
                expectedDefault = ObjectModelHelpers.MSBuildDefaultToolsVersion;
            }

            Assert.AreEqual(expectedDefault, defaultToolsVersion); // built-in default
            Assert.AreEqual(null, values[expectedDefault].OverrideTasksPath);
            Assert.AreEqual(null, values[expectedDefault].DefaultOverrideToolsVersion);
        }

        /// <summary>
        /// Tests that properties are properly expanded when reading them from the config file
        /// </summary>
        [TestMethod]
        public void PropertiesInToolsetsFromConfigFileAreExpanded()
        {
            // $(COMPUTERNAME) is just a convenient env var. $(NUMBER_OF_PROCESSORS) isn't defined on Longhorn
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets>
                     <toolset toolsVersion=""4.0"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v2.0.x86ret\""/>
                       <property name=""p1"" value=""v1$(p2)""/>
                       <property name=""p2"" value=""__$(p1)__""/>
                       <property name=""p3"" value=""$(COMPUTERNAME)""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new ProjectCollection().GlobalPropertiesCollection,
                                                           ToolsetDefinitionLocations.ConfigurationFile | ToolsetDefinitionLocations.Registry
                                                       );

            Assert.AreEqual("v1", values["4.0"].Properties["p1"].EvaluatedValue);
            // Properties can refer to other properties also defined in the config file
            Assert.AreEqual("__v1__", values["4.0"].Properties["p2"].EvaluatedValue);
            Assert.AreEqual(Environment.MachineName, values["4.0"].Properties["p3"].EvaluatedValue);
        }

        /// <summary>
        /// Tests that properties in MSBuildToolsPath are properly expanded when reading them from the config file
        /// </summary>
        [TestMethod]
        public void PropertiesInToolsetsFromConfigFileAreExpandedInToolsPath()
        {
            // $(COMPUTERNAME) is just a convenient env var. $(NUMBER_OF_PROCESSORS) isn't defined on Longhorn
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets>
                     <toolset toolsVersion=""4.0"">
                       <property name=""p1"" value=""Microsoft.NET""/>
                       <property name=""p2"" value=""windows""/>
                       <property name=""MSBuildBinPath"" value=""D:\windows\$(p1)\Framework\v2.0.x86ret\$(COMPUTERNAME)""/>
                       <property name=""MSBuildToolsPath"" value=""D:\$(p2)\$(p1)\Framework\v2.0.x86ret\$(COMPUTERNAME)""/>
                       <property name=""p3"" value=""v3$(MSBuildToolsPath)""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           GetStandardConfigurationReader(),
                                                           new ProjectCollection().EnvironmentProperties,
                                                           new ProjectCollection().GlobalPropertiesCollection,
                                                           ToolsetDefinitionLocations.ConfigurationFile | ToolsetDefinitionLocations.Registry
                                                       );

            Assert.AreEqual("Microsoft.NET", values["4.0"].Properties["p1"].EvaluatedValue);
            Assert.AreEqual("windows", values["4.0"].Properties["p2"].EvaluatedValue);
            string expectedToolsPath = @"D:\windows\Microsoft.NET\Framework\v2.0.x86ret\" + Environment.MachineName;
            Assert.AreEqual(expectedToolsPath, values["4.0"].ToolsPath);
            Assert.AreEqual("v3" + expectedToolsPath, values["4.0"].Properties["p3"].EvaluatedValue);
        }

        /// <summary>
        /// Global properties are available, but they cannot be overwritten by other toolset properties, just as they cannot
        /// be overwritten by project file properties.
        /// </summary>
        [TestMethod]
        public void GlobalPropertiesInToolsetsAreExpandedButAreNotOverwritten()
        {
            ToolsetConfigurationReaderTestHelper.WriteConfigFile(@"
                 <configuration>
                   <configSections>
                     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, SourceLink.Build"" />
                   </configSections>
                   <msbuildToolsets>
                     <toolset toolsVersion=""4.0"">
                       <property name=""MSBuildBinPath"" value=""D:\windows\Microsoft.NET\Framework\v2.0.x86ret\""/>
                       <property name=""p1"" value=""$(gp1)""/>
                       <property name=""gp1"" value=""v2""/>
                       <property name=""p2"" value=""$(gp1)""/>
                     </toolset>
                   </msbuildToolsets>
                 </configuration>");

            Dictionary<string, string> globalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            globalProperties["gp1"] = "gv1";
            ProjectCollection e = new ProjectCollection(globalProperties, null, ToolsetDefinitionLocations.None);
            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string defaultToolsVersion = ToolsetReader.ReadAllToolsets
                                                       (
                                                           values,
                                                           GetStandardRegistryReader(),
                                                           GetStandardConfigurationReader(),
                                                           e.EnvironmentProperties,
                                                           e.GlobalPropertiesCollection,
                                                           ToolsetDefinitionLocations.ConfigurationFile | ToolsetDefinitionLocations.Registry
                                                       );

            Assert.AreEqual("gv1", values["4.0"].Properties["p1"].EvaluatedValue);
            Assert.AreEqual("gv1", values["4.0"].Properties["p2"].EvaluatedValue);
        }

        #endregion

        private ToolsetRegistryReader GetStandardRegistryReader()
        {
            return new ToolsetRegistryReader(new ProjectCollection().EnvironmentProperties, new PropertyDictionary<ProjectPropertyInstance>(), new MockRegistryKey(testRegistryPath));
        }

        private ToolsetConfigurationReader GetStandardConfigurationReader()
        {
            return new ToolsetConfigurationReader(new ProjectCollection().EnvironmentProperties, new PropertyDictionary<ProjectPropertyInstance>(), new ReadApplicationConfiguration(
                ToolsetConfigurationReaderTestHelper.ReadApplicationConfigurationTest));
        }
    }

    internal class MockRegistryKey : RegistryKeyWrapper
    {
        public enum WhereToThrow
        {
            None,
            Name,
            GetValue,
            GetValueNames,
            GetSubKeyNames,
            OpenSubKey
        }

        private WhereToThrow _whereToThrow = WhereToThrow.None;
        private string _subKeyThatDoesNotExist = null;

        /// <summary>
        /// Construct the mock key with a specified key
        /// </summary>
        /// <param name="path"></param>
        protected MockRegistryKey(RegistryKey wrappedKey, RegistryKey registryHive)
            : base(wrappedKey, registryHive)
        { }

        /// <summary>
        /// Construct the mock key with a wrapper
        /// </summary>
        /// <param name="path"></param>
        public MockRegistryKey(string path)
            : base(path, Registry.CurrentUser)
        { }

        /// <summary>
        /// Construct the mock key with a wrapper and a designated method
        /// to throw from
        /// </summary>
        /// <param name="path"></param>
        /// <param name="whereToThrow"></param>
        public MockRegistryKey(string path, WhereToThrow whereToThrow)
            : base(path, Registry.CurrentUser)
        {
            _whereToThrow = whereToThrow;
        }

        /// <summary>
        /// Construct the mock key with a wrapper and a designated subkey
        /// to refuse to open
        /// </summary>
        /// <param name="path"></param>
        /// <param name="whereToThrow"></param>
        public MockRegistryKey(string path, string subKeyThatDoesNotExist)
            : base(path, Registry.CurrentUser)
        {
            _subKeyThatDoesNotExist = subKeyThatDoesNotExist;
        }

        /// <summary>
        /// Name of the registry key
        /// </summary>
        public override string Name
        {
            get
            {
                if (_whereToThrow == WhereToThrow.Name)
                {
                    throw new RegistryException("registryException", "registry");
                }
                return base.Name;
            }
        }

        /// <summary>
        /// Gets the value with name "name" stored under this registry key
        /// </summary>
        public override object GetValue(string name)
        {
            if (_whereToThrow == WhereToThrow.GetValue)
            {
                throw new RegistryException("registryException", "registry");
            }
            return base.GetValue(name);
        }

        /// <summary>
        /// Gets the names of all values underneath this registry key
        /// </summary>
        public override string[] GetValueNames()
        {
            if (_whereToThrow == WhereToThrow.GetValueNames)
            {
                throw new RegistryException("registryException", "registry");
            }
            return base.GetValueNames();
        }

        /// <summary>
        /// Gets the names of all sub keys immediately below this registry key
        /// </summary>
        /// <returns></returns>
        public override string[] GetSubKeyNames()
        {
            if (_whereToThrow == WhereToThrow.GetSubKeyNames)
            {
                throw new RegistryException("registryException", "registry");
            }
            return base.GetSubKeyNames();
        }

        /// <summary>
        /// Returns the sub key with name "name" as a read only key
        /// </summary>
        public override RegistryKeyWrapper OpenSubKey(string name)
        {
            if (_whereToThrow == WhereToThrow.OpenSubKey)
            {
                throw new RegistryException("registryException", "registry");
            }

            if (_subKeyThatDoesNotExist == name)
            {
                // Return wrapper around null key
                return new MockRegistryKey((RegistryKey)null, Registry.LocalMachine);
            }

            return base.OpenSubKey(name);
        }
    }
}

