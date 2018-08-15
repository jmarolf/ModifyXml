using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace MSBuild.Roslyn.Tasks {
    /// <summary>
    /// Updates a XML document using a XPath.
    /// </summary>
    /// <example>Modify a XML element.
    /// <code><![CDATA[
    /// <ModifyXml Prefix="n"
    ///     Namespace="http://schemas.microsoft.com/developer/vstemplate/2005" 
    ///     XPath="/n:VSTemplate/n:WizardExtension/n:Assembly"
    ///     XmlFiles="@(VSTemplate)""
    ///     Value="Roslyn.SDK.Template.Wizard, Version=$(AssemblyVersion), Culture=neutral, PublicKeyToken=31bf3856ad364e35">
    ///   <Output TaskParameter="NewXmlFiles" ItemName="VSTemplate" />
    /// </ModifyXml>
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
                foreach (var xmlFile in XmlFiles) {
                    var item = Modify(xmlFile.ItemSpec, xmlFile.GetMetadata("OutputSubPath"));
                    list.Add(item);
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

        private ITaskItem Modify(string xmlPath, string metadata) {
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

            return SaveXml(xdoc, metadata, xmlPath);
        }

        private ITaskItem SaveXml(XDocument xml, string metaDataValue, string oldXmlFilePath) {
            var newDirectory = Path.Combine(IntermediatePath, Path.GetDirectoryName(oldXmlFilePath));
            if (!Directory.Exists(newDirectory)) {
                Directory.CreateDirectory(newDirectory);
            }

            var newXmlFilePath = Path.Combine(newDirectory, Path.GetFileName(oldXmlFilePath));

            var newXmlItem = new TaskItem(newXmlFilePath);
            newXmlItem.SetMetadata("OutputSubPath", metaDataValue);

            xml.Save(newXmlFilePath);
            return newXmlItem;
        }
    }
}
