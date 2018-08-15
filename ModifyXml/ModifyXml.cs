using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Vanara.PInvoke;
using static Vanara.PInvoke.ShlwApi;

namespace MSBuild.Roslyn.Tasks {
    /// <summary>
    /// Updates a XML document using a XPath.
    /// </summary>
    /// <example>Modify a XML element.
    /// <code><![CDATA[
    ///  <ItemGroup>
    ///   <_OriginalVSTemplate Include="@(VSTemplate)" />
    ///  </ItemGroup>
    ///  <ModifyXml XmlFiles="@(VSTemplate)"
    ///             IntermediatePath="$(IntermediateOutputPath)\ModifiedTemplates"
    ///             Prefix="n"
    ///             Namespace="http://schemas.microsoft.com/developer/vstemplate/2005"
    ///             XPath="/n:VSTemplate/n:WizardExtension/n:Assembly"
    ///             Value="Roslyn.SDK.Template.Wizard, Version=$(AssemblyVersion), Culture=neutral, PublicKeyToken=31bf3856ad364e35">
    ///   <Output TaskParameter="NewXmlFiles" ItemName="VSTemplate" />
    /// </ModifyXml>
    ///<ItemGroup>
    ///  <VSTemplate Remove="@(_OriginalVSTemplate)" />
    ///</ItemGroup>
    /// ]]></code>
    /// </example>
    /// <remarks>
    /// The XML node being updated must exist before using the ModifyXml task.
    /// </remarks>
    public class ModifyXml : Task {

        /// <summary>
        /// Initializes a new instance of the <see cref="T:ModifyXml"/> class.
        /// </summary>
        public ModifyXml() {
        }

        /// <summary>
        /// Gets or sets the set of XML files to modify.
        /// </summary>
        /// <value>The set of XML files to modify.</value>
        [Required]
        public ITaskItem[] XmlFiles { get; set; }

        /// <summary>
        /// Gets or sets the XPath.
        /// </summary>
        /// <value>The XPath.</value>
        [Required]
        public string XPath { get; set; }

        /// <summary>
        /// Gets or sets the intermediate path to write out the new xml file to.
        /// </summary>
        /// <value>The intermediate output path.</value>
        [Required]
        public string IntermediatePath { get; set; }

        /// <summary>
        /// Gets or sets the value to write.
        /// </summary>
        /// <value>The value.</value>
        public string Value { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the matched node is deleted.
        /// </summary>
        /// <value><c>true</c> to delete matched node; otherwise, <c>false</c>.</value>
        public bool Delete { get; set; }

        /// <summary>
        /// Gets or sets the default namespace.
        /// </summary>
        /// <value>The namespace.</value>
        public string Namespace { get; set; }

        /// <summary>
        /// Gets or sets the prefix to associate with the namespace being added.
        /// </summary>
        /// <value>The namespace prefix.</value>
        public string Prefix { get; set; }

        [Output]
        public ITaskItem[] NewXmlFiles { get; private set; }

        /// <summary>
        /// When overridden in a derived class, executes the task.
        /// </summary>
        /// <returns>
        /// true if the task successfully executed; otherwise, false.
        /// </returns>
        public override bool Execute() {
            try {
                if (Directory.Exists(IntermediatePath)) {
                    foreach (var file in new DirectoryInfo(IntermediatePath).GetFiles("*.*", SearchOption.AllDirectories)) {
                        file.IsReadOnly = false;
                    }

                    Directory.Delete(IntermediatePath, recursive: true);
                }

                var list = new List<ITaskItem>();
                var files = ImmutableHashSet.Create<string>(XmlFiles.Select(x => Path.GetFileName(x.ItemSpec)).ToArray());
                foreach (var xmlFile in XmlFiles) {
                    var xmlFileInfo = new FileInfo(xmlFile.ItemSpec);
                    var item = ModifyAndGetNewItem(xmlFileInfo.FullName, xmlFile.GetMetadata("OutputSubPath"), out var newDirectory);
                    list.Add(item);

                    
                    foreach (var extraFile in xmlFileInfo.Directory.GetFiles("*.*", SearchOption.AllDirectories)) {
                        if (extraFile.Extension == xmlFileInfo.Extension && files.Contains(extraFile.Name)) {
                            ModifyExtraXmlFile(newDirectory, extraFile.FullName);
                        }
                        else {
                            CopyExtraFile(newDirectory, xmlFileInfo.Directory.FullName, extraFile.FullName);
                        }
                    }
                }

                NewXmlFiles = list.ToArray();
            }
            catch (Exception ex) {
                Log.LogErrorFromException(ex);
                return false;
            }

            Log.LogMessage($"XmlUpdate Wrote: '{Value}'");
            return true;
        }

        private ITaskItem ModifyAndGetNewItem(string xmlPath, string metadata, out string newDirectory) {
            var xdoc = ModifyAndGetNewXDocument(xmlPath);
            return SaveXmlAndGetNewItem(xdoc, metadata, xmlPath, out newDirectory);
        }

        private void ModifyExtraXmlFile(string newDirectory, string extraFilePath) {
            var referencedXml = ModifyAndGetNewXDocument(extraFilePath);
            var relativePath = GetRelativePath(Path.GetFullPath(newDirectory), extraFilePath).Replace(@"..\", String.Empty);
            var fullPath = Path.GetFullPath(Path.Combine(newDirectory, relativePath));
            referencedXml.Save(fullPath);
        }

        private string GetRelativePath(string fromPath, string toPath) {
            var fromAttr = GetPathAttribute(fromPath);
            var toAttr = GetPathAttribute(toPath);
            var path = new StringBuilder(260);

            if (!(PathRelativePathTo(path, fromPath, fromAttr, toPath, toAttr))) {
                throw new ArgumentException($"Paths {fromPath} and {toPath} do not have a common prefix");
            }

            return path.ToString();
        }

        private static FileFlagsAndAttributes GetPathAttribute(string path) {
            var directoryInfo = new DirectoryInfo(path);
            if (directoryInfo.Exists) {
                return FileFlagsAndAttributes.FILE_ATTRIBUTE_DIRECTORY;
            }

            var fileInfo = new FileInfo(path);
            if (fileInfo.Exists) {
                return FileFlagsAndAttributes.FILE_ATTRIBUTE_NORMAL;
            }

            // If the path doesn't exist assume it to be a folder.
            return FileFlagsAndAttributes.FILE_ATTRIBUTE_DIRECTORY;
        }

        private ITaskItem SaveXmlAndGetNewItem(XDocument xml, string metaDataValue, string oldXmlFilePath, out string newDirectory) {
            newDirectory = Path.Combine(IntermediatePath, Path.GetDirectoryName(oldXmlFilePath));
            if (!Directory.Exists(newDirectory)) {
                Directory.CreateDirectory(newDirectory);
            }

            var newXmlFilePath = Path.Combine(newDirectory, Path.GetFileName(oldXmlFilePath));

            var newXmlItem = new TaskItem(newXmlFilePath);
            newXmlItem.SetMetadata("OutputSubPath", metaDataValue);

            xml.Save(newXmlFilePath);
            return newXmlItem;
        }

        private XDocument ModifyAndGetNewXDocument(string xmlPath) {
            Log.LogMessage($"Updating Xml Document {xmlPath}");

            var xdoc = XDocument.Load(xmlPath);
            var manager = new XmlNamespaceManager(new NameTable());

            if (!string.IsNullOrEmpty(Namespace)) {
                //by default, if _prefix is not specified, set it to "", this way,
                //manager.AddNamespace will add the _namespace as the default namespace
                if (Prefix == null) {
                    Prefix = string.Empty;
                }

                manager.AddNamespace(Prefix, Namespace);
            }

            var items = xdoc.XPathEvaluate(XPath, manager) as IEnumerable<object>;

            Log.LogMessage($"{items.Count()} node(s) selected for update.");

            foreach (var item in items.ToArray()) {
                if (item is XAttribute attr) {
                    if (Delete) {
                        attr.Remove();
                    }
                    else {
                        attr.SetValue(Value);
                    }
                }

                if (item is XElement ele) {
                    if (Delete) {
                        ele.Remove();
                    }
                    else {
                        ele.SetValue(Value);
                    }
                }
            }

            return xdoc;
        }

        private static void CopyExtraFile(string newDirectory, string originalDirectory, string extraFile) {
            var relativePath = extraFile.Substring(originalDirectory.Length);
            var fullPath = Path.GetFullPath(newDirectory + relativePath);

            try {
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                File.Copy(extraFile, fullPath);

                // Ensure the copy isn't read only
                var copyFileInfo = new FileInfo(fullPath) {
                    IsReadOnly = false
                };
            }
            catch (Exception) {
                // continue execution
            }
        }
    }
}
