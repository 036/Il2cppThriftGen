using System;
using System.Collections.Generic;
using System.Linq;
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
        private static StringBuilder thrift = new StringBuilder();
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
                thrift.Append($"struct {name} {{\n");

                // Output C# fields
                foreach (var field in fields)
                {
                    var pmAtt = field.CustomAttributes.First(a => a.AttributeType.FullName == dataMember);
                    outputField(field.Name, field.FieldType, pmAtt);
                }

                // Output C# properties
                foreach (var prop in props)
                {
                    var pmAtt = prop.CustomAttributes.First(a => a.AttributeType.FullName == dataMember);
                    outputField(prop.Name, prop.PropertyType, pmAtt);
                }

                thrift.Append("}\n");

                Console.WriteLine(thrift);
            }
        }

        private static void outputField(string name, TypeInfo type, CustomAttributeData pmAtt)
        {
            var isRepeated = type.IsArray;
            var typeFullName = isRepeated ? type.ElementType.FullName : type.FullName ?? string.Empty;
            var typeFriendlyName = isRepeated ? type.ElementType.Name : type.Name;

            // Handle maps (IDictionary)
            if (type.ImplementedInterfaces
                .Any(i => i.FullName == "System.Collections.Generic.IDictionary`2"))
            {

                // This time we have two generic arguments to deal with - the key and the value
                var keyFullName = type.GenericTypeArguments[0].FullName;
                var valueFullName = type.GenericTypeArguments[1].FullName;

                // We're going to have to deal with building this proto type name
                // separately from the value types below
                // We don't set isRepeated because it's implied by using a map type
                thriftTypes.TryGetValue(keyFullName, out var keyFriendlyName);
                thriftTypes.TryGetValue(valueFullName, out var valueFriendlyName);
                typeFriendlyName = $"map<{keyFriendlyName ?? type.GenericTypeArguments[0].Name}" +
                    $", {valueFriendlyName ?? type.GenericTypeArguments[1].Name}>";
            }

            if (thriftTypes.TryGetValue(typeFullName, out var protoTypeName))
                typeFriendlyName = protoTypeName;

            var annotatedName = typeFriendlyName;
            if (isRepeated)
                annotatedName = "repeated " + annotatedName;

            //Handle lists
            if (type.ImplementedInterfaces.Any(i => i.FullName == "System.Collections.Generic.IList`1"))
            {
                typeFullName = type.GenericTypeArguments[0].FullName;
                typeFriendlyName = type.GenericTypeArguments[0].Name;
                annotatedName = $"list<{typeFriendlyName}>";
            }

            thrift.Append($"  {annotatedName} {name}\n");
        }
    }

}
