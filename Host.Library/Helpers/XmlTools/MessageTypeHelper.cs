using Newtonsoft.Json;
using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Schema;
using Microsoft.CSharp;
using System.Xml.Linq;
using System.Xml.Serialization;


namespace Host.Library.Helpers.XmlTools
{
    public class MessageTypeHelper
    {
        private static string GetJsonSchema()
        {
            string xml = string.Empty;
            using (var stringWriter = new StringWriter())
            {
                using (var xmlWriter = XmlWriter.Create(stringWriter))
                {
                    XmlQualifiedName qname = new XmlQualifiedName("PurchaseOrder", "http://tempuri.org");
                    XmlSampleGenerator generator = new XmlSampleGenerator("PO.xsd", qname);
                    generator.WriteXml(xmlWriter);

                }
                xml = stringWriter.ToString();
            }

            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(xml);
            var xmlNode = xmlDoc.FirstChild.GetType() == typeof(System.Xml.XmlDeclaration) ? xmlDoc.FirstChild.NextSibling : xmlDoc.FirstChild;
            var jsonDoc = JsonConvert.SerializeXmlNode(xmlNode, Newtonsoft.Json.Formatting.Indented, true);

            return jsonDoc;
        }

        public static XmlSchema JsonToXmlSchema(string json, string rootName)
        {
            var xDoc = JsonConvert.DeserializeXNode(json, rootName);
            //var xDoc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);

            var builder = new StringBuilder();

            var className = xDoc.Root.Name.LocalName;

            builder.AppendFormat("public class ").Append(className).Append(" { ");

            foreach (XNode node in xDoc.Elements().Nodes())
            {
                var n = (node as XElement);

                if (n != null)
                    builder.AppendFormat("string _")
                           .Append(n.Name.LocalName)
                           .Append(" =\"")
                           .Append(n.Value).Append("\";   public string ")
                           .Append(n.Name.LocalName).Append(" { get { return _")
                           .Append(n.Name.LocalName).Append(";} set{ _")
                           .Append(n.Name.LocalName).Append(" = value ;} } ");
            }
            builder.Append(" } ");

            var compilerParameters = new CompilerParameters();

            compilerParameters.GenerateExecutable = false;
            compilerParameters.GenerateInMemory = true;

            var cCompiler = CSharpCodeProvider.CreateProvider("CSharp");
            var compileResult = cCompiler.CompileAssemblyFromSource(compilerParameters, builder.ToString());

            if (compileResult.Errors.HasErrors)
            {
                throw new Exception("There is error while building type");
            }

            var module = compileResult.CompiledAssembly.GetModules().FirstOrDefault();
            if (module == null)
            {
                throw new ApplicationException("Unable to generate class");
            }

            Type type = module.GetType(className);
            var soapReflectionImporter = new SoapReflectionImporter();
            var xmlTypeMapping = soapReflectionImporter.ImportTypeMapping(type);
            var xmlSchemas = new XmlSchemas();
            var xmlSchema = new XmlSchema();
            xmlSchemas.Add(xmlSchema);
            var xmlSchemaExporter = new XmlSchemaExporter(xmlSchemas);
            xmlSchemaExporter.ExportTypeMapping(xmlTypeMapping);

            return xmlSchema;
        }
    }
}
