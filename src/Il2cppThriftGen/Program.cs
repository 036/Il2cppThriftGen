using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Gee.External.Capstone.Arm;
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

        public static int cagCounter = 0;

        public static string MetadataFile = @"global-metadata.dat";
        public static string BinaryFile = @"libil2cpp.so";
        private static StringBuilder thrift = new StringBuilder();
        private static Dictionary<ulong, int> vaFieldMapping;
        private static CapstoneArmDisassembler asm = Gee.External.Capstone.CapstoneDisassembler.CreateArmDisassembler(Gee.External.Capstone.Arm.ArmDisassembleMode.Arm);
        private static String outputPath = @"C:\ThriftGen\output.txt";

        static void Main(string[] args)
        {
            Console.WriteLine("Using Il2cppInspector to read il2cpp.so");
            var package = Il2CppInspector.Il2CppInspector.LoadFromFile(
                BinaryFile, MetadataFile, silent: true)[0];

            Console.WriteLine("Creating type model from Il2cppInspector output");
            var model = new TypeModel(package);

            // Find all classes that inherit from TAbstractBase
            var messages = model.TypesByDefinitionIndex
             .Where(t => t.ImplementedInterfaces.Any(i => i.FullName == "Thrift.Protocol.TBase"));

            messages = messages.Where(t => !t.Namespace.Contains("Analytics") && !t.Namespace.Contains("Debug"));

            Console.WriteLine($"Found {messages.Count()} classes that implement the TAbstractBase Interface");

            // All thrift ifa have this property attribute
            var thriftDataMember = model.GetType("System.Runtime.Serialization.DataMemberAttribute");

            // Get the address of the DataMemberAttribute constructor
            var dataMemberCtor = (long)thriftDataMember.DeclaredConstructors[0].VirtualAddress.Value.Start;


            // Get all of the custom attributes generators for DataMember so we can determine field numbers
            var atts = model.CustomAttributeGenerators.Where(x => x.Key.FullName == "DataMemberAttribute").SelectMany(x => x.Value);
            Console.WriteLine($"Found {atts.Count()} DataMemberAttributes being used");


            // Create a mapping of CAG virtual addresses to field numbers
            Console.WriteLine("Mapping custom attributes");
            vaFieldMapping = atts.Select(a => new
            {
                VirtualAddress = a.VirtualAddress.Start,
                FieldNumber = getDataMemberArgument(a, dataMemberCtor)
            })
            .ToDictionary(kv => kv.VirtualAddress, kv => kv.FieldNumber);

            var dataMember = "DataMemberAttribute";

            // Keep a list of all the enums we need to output (HashSet ensures unique values - we only want each enum once!)
            var enums = new List<TypeInfo>();


            foreach (var message in messages)
            {
                var name = message.CSharpName;
                var fields = message.DeclaredFields.Where(f => f.CustomAttributes.Any(a => a.AttributeType.FullName == dataMember));
                var props = message.DeclaredProperties.Where(p => p.CustomAttributes.Any(a => a.AttributeType.FullName == dataMember));
                var friendlyName = message.FullName.Replace(".", "");
                thrift.Append($"struct {friendlyName} {{\n");

                // Output C# fields
                foreach (var field in fields)
                {
                    var pmAtt = field.CustomAttributes.First(a => a.AttributeType.FullName == dataMember);
                    outputField(field.Name, field.FieldType, pmAtt, false);

                    if (field.FieldType.IsEnum)
                    {
                        enums.Add(field.FieldType);
                    }
                }

                // Output C# properties
                foreach (var prop in props)
                {
                    PropertyInfo last = props.Last();
                    var addComma = !prop.Equals(last);
                    var pmAtt = prop.CustomAttributes.First(a => a.AttributeType.FullName == dataMember);
                    outputField(prop.Name, prop.PropertyType, pmAtt, addComma);

                    if (prop.PropertyType.IsEnum)
                    {
                        enums.Add(prop.PropertyType);
                    }

                }

                thrift.Append("}\n");
            }


            // Output enums
            var enumText = new StringBuilder();

            var writtenEnums = new List<string>();

            foreach (var e in enums)
            {
                var match = writtenEnums.FirstOrDefault(writtenEnum => writtenEnum.Contains(e.FullName.Replace(".", "")));

                if(match == null)
                {
                    enumText.Append("enum " + e.FullName.Replace(".", "") + " {\n");
                    var namesAndValues = e.GetEnumNames().Zip(e.GetEnumValues().Cast<int>(), (n, v) => n + " = " + v);
                    foreach (var nv in namesAndValues)
                        enumText.Append("  " + nv + ";\n");
                    enumText.Append("}\n\n");

                    writtenEnums.Add(e.FullName.Replace(".", ""));
                }

            }

            File.WriteAllText(outputPath, enumText.ToString() + thrift.ToString());
        }

        private static void outputField(string name, TypeInfo type, CustomAttributeData pmAtt, bool addComma)
        {
            // Handle arrays
            var isRepeated = type.IsArray;
            var isOptional = false;

            var typeFullName = isRepeated ? type.ElementType.FullName : type.FullName ?? string.Empty;
            var typeFriendlyName = isRepeated ? type.ElementType.FullName : type.FullName;

            // Handle one-dimensional collections like lists
            // We could also use type.Namespace == "System.Collections.Generic" && type.UnmangledBaseName == "List"
            // or typeBaseName == "System.Collections.Generic.List`1" but these are less flexible
            if (type.ImplementedInterfaces.Any(i => i.FullName == "System.Collections.Generic.IList`1"))
            {
                // Get the type of the IList by looking at its first generic argument
                // Note this is a naive implementation which doesn't handle nesting of lists or arrays in lists etc.

                typeFullName = type.GenericTypeArguments[0].FullName;
                typeFriendlyName = type.GenericTypeArguments[0].FullName;

                if(typeFullName.Contains("List`1"))
                {
                    typeFriendlyName = $"list<{type.GenericTypeArguments[0].GenericTypeArguments[0].FullName}>";
                }

                isRepeated = true;
            }

            //Handle THashset
            if (type.FullName == "Thrift.Collections.THashSet`1")
            {
                typeFullName = type.GenericTypeArguments[0].FullName;
                typeFriendlyName = $"set<{type.GenericTypeArguments[0].FullName.Replace(".", "")}>";
            }

            // Handle maps (IDictionary)
                if (type.ImplementedInterfaces.Any(i => i.FullName == "System.Collections.Generic.IDictionary`2"))
            {

                // This time we have two generic arguments to deal with - the key and the value
                var keyFullName = type.GenericTypeArguments[0].FullName;
                var valueFullName = type.GenericTypeArguments[1].FullName;

                // We're going to have to deal with building this thrift type name separately from the value types below
                // We don't set isRepeated because it's implied by using a map type
                thriftTypes.TryGetValue(keyFullName, out var keyFriendlyName);
                thriftTypes.TryGetValue(valueFullName, out var valueFriendlyName);

                if(type.GenericTypeArguments[1].Name.Contains("List`1"))
                {
                    valueFriendlyName = $"list<{type.GenericTypeArguments[1].GenericTypeArguments[0].FullName.Replace(".", "")}>";
                }

                if(type.GenericTypeArguments[1].Name.Contains("THashSet`1"))
                {
                    valueFriendlyName = $"set<{type.GenericTypeArguments[1].GenericTypeArguments[0].FullName.Replace(".", "")}>";
                }

                typeFriendlyName = $"map<{keyFriendlyName ?? type.GenericTypeArguments[0].FullName.Replace(".", "")}, {valueFriendlyName ?? type.GenericTypeArguments[1].FullName.Replace(".", "")}>";
            }


            // Handle nullable types
            if (type.FullName == "System.Nullable`1")
            {
                // Once again we look at the first generic argument to get the real type

                typeFullName = type.GenericTypeArguments[0].FullName;
                typeFriendlyName = type.GenericTypeArguments[0].FullName.Replace(".", "");
                isOptional = true;
            }

            // Handle primitive value types
            if (thriftTypes.TryGetValue(typeFullName, out var thriftTypeName))
                typeFriendlyName = thriftTypeName;

            // Handle repeated fields
            var annotatedName = typeFriendlyName;
            if (isRepeated)
                annotatedName = "list<" + annotatedName + ">";

            // Handle nullable (optional) fields
            if (isOptional)
                annotatedName = "optional " + annotatedName;

            var comma = addComma ? "," : string.Empty;
            thrift.Append($"  {vaFieldMapping[pmAtt.VirtualAddress.Start]}: {annotatedName.Replace(".", "")} {name}{comma}\n");
        }

        // Scan the dissassemly code to find the first argument to DataMember for a given CAG
        private static int getDataMemberArgument(CustomAttributeData att, long dataMemberCtor)
        {
            cagCounter++;

            // Disassemble the CAG
            var code = asm.Disassemble(att.GetMethodBody(), (long)att.VirtualAddress.Start);
            var insIndex = -1;
            var disp = 0;

            // Step forwards through each instruction
            while (++insIndex < code.Length && disp == 0)
            {
                var ins = code[insIndex];

                // Look for B instructions to the DataMember constructor
                if ((ins.Id == ArmInstructionId.ARM_INS_B))
                {

                    // Now step backwards looking for the most recent MOV R1 instruction
                    while (--insIndex >= 0 && disp == 0)
                    {
                        ins = code[insIndex];

                        //Check if the instruction is calling MOV R1 #{valuehere}
                        if (ins.Id == ArmInstructionId.ARM_INS_MOV || ins.Id == ArmInstructionId.ARM_INS_MOVW &&
                            ins.Operand.Contains("r1"))
                        {
                            var output = ins.Operand.Split(',');
                            var instruction = output[1];
                            instruction = instruction.Replace("#", "");
                            instruction = instruction.Replace(" ", "");
                            disp = Convert.ToInt32(instruction, 16);

                        }
                    }
                }
            }
            return disp;
        }
    }
}
