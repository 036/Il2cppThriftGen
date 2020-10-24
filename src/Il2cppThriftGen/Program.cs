using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Il2CppInspector;
using Il2CppInspector.Reflection;

namespace Il2cppThriftGen
{
    class Program
    {
        // Define type map from .NET types to thrift types
        private static Dictionary<string, string> thriftTypes = new Dictionary<string, string>
        {
            ["System.Int32"] = "i32",
            ["System.UInt32"] = "i32",
            ["System.Byte"] = "i32",
            ["System.SByte"] = "i32",
            ["System.UInt16"] = "i16",
            ["System.Int16"] = "i16",
            ["System.Int64"] = "i64",
            ["System.UInt64"] = "i64",
            ["System.Single"] = "byte",
            ["System.Double"] = "double",
            ["System.Decimal"] = "double",
            ["System.Boolean"] = "bool",
            ["System.String"] = "string",
            ["System.Byte[]"] = "list<byte>",
            ["System.Char"] = "i32",
            ["System.DateTime"] = "Timestamp"
        };
        public static string MetadataFile = @"global-metadata.dat";
        public static string BinaryFile = @"libil2cpp.so";
        static void Main(string[] args)
        {
            Console.WriteLine("Using Il2cppInspector to read il2cpp.so");
            var package = Il2CppInspector.Il2CppInspector.LoadFromFile(
                BinaryFile, MetadataFile, silent: true)[0];

            Console.WriteLine("Creating type model from Il2cppInspector output");
            var model = new TypeModel(package);

            var messages = model.TypesByDefinitionIndex
             .Where(t => t.ImplementedInterfaces.Any(i => i.FullName == "Thrift.Protocol.TAbstractBase"));

            Console.WriteLine($"Found {messages.Count()} classes that implement the TAbstractBase Interface");

            var dataMember = "DataMemberAttribute";
            foreach (var message in messages)
            {
                var name = message.CSharpName;
                var fields = message.DeclaredFields.Where(f => f.CustomAttributes.Any(a => a.AttributeType.FullName == dataMember));
                var props = message.DeclaredProperties.Where(p => p.CustomAttributes.Any(a => a.AttributeType.FullName == dataMember));
                var thrift = new StringBuilder();
                thrift.Append($"struct {name} {{\n");

                // Output C# fields
                foreach (var field in fields)
                {
                    var pmAtt = field.CustomAttributes.First(a => a.AttributeType.FullName == dataMember);
                    thriftTypes.TryGetValue(field.FieldType.FullName ?? string.Empty, out var protoTypeName);
                    thrift.Append($"  {protoTypeName ?? field.FieldType.Name} {field.Name} ;\n");
                }

                // Output C# properties
                foreach (var prop in props)
                {
                    var pmAtt = prop.CustomAttributes.First(a => a.AttributeType.FullName == dataMember);
                    thrift.Append($"  {prop.PropertyType.Name} {prop.Name};\n");
                }

                thrift.Append("}\n");

                Console.WriteLine(thrift);
            }
        }
    }

}
