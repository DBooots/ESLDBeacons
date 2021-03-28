using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;

namespace ESLDCore
{
    public static class SerializationHelper
    {
        public static string SerializeObject<T>(T toSerialize)
        {
            XmlSerializer xmlSerializer = new XmlSerializer(toSerialize.GetType());
            using (StringWriter textWriter = new StringWriter())
            {
                xmlSerializer.Serialize(textWriter, toSerialize);
                return textWriter.ToString();
            }
        }
        public static T DeserializeObject<T>(string serializedObject)
        {
            XmlSerializer xmlSerializer = new XmlSerializer(typeof(T));
            {
                using (StringReader textReader = new StringReader(serializedObject))
                {
                    return (T)xmlSerializer.Deserialize(textReader);
                }
            }
        }

        public static bool LoadObjectFromConfig<T>(T obj, ConfigNode node)
        {
            bool success = true;
            /*if (obj is IConfigNode interfaceObj)
            {
                interfaceObj.Load(node);
                return true;
            }*/
            IEnumerable<FieldInfo> fields = typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(fi => fi.GetCustomAttribute<KSPField>()?.isPersistant == true);
            foreach (FieldInfo field in fields)
            {
                string name = field.Name;
                Type type = field.FieldType;
                if (type.IsSubclassOf(typeof(IConfigNode)))
                    if (node.HasNode(name))
                    {
                        object fObj = field.GetValue(obj);
                        if (fObj == null)
                        {
                            fObj = type.GetConstructor(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic, null, new Type[0], null)?.Invoke(new object[0]);
                            if (fObj == null)
                                continue;
                        }
                        ((IConfigNode)fObj).Load(node.GetNode(name));
                    }
                    else
                        success = false;
                /*else if (type.IsArray)
                {

                }
                else if (type.IsGenericType && type.Name.StartsWith("List"))
                {

                }*/
                else if (node.HasValue(field.Name))
                {
                    if (type == typeof(string))
                        field.SetValue(obj, node.GetValue(name));
                    else if (type == typeof(bool))
                        field.SetValue(obj, bool.Parse(node.GetValue(name)));
                    else if (type == typeof(int))
                        field.SetValue(obj, int.Parse(node.GetValue(name)));
                    else if (type == typeof(long))
                        field.SetValue(obj, long.Parse(node.GetValue(name)));
                    else if (type == typeof(float))
                        field.SetValue(obj, float.Parse(node.GetValue(name)));
                    else if (type == typeof(uint))
                        field.SetValue(obj, uint.Parse(node.GetValue(name)));
                    else if (type == typeof(ulong))
                        field.SetValue(obj, ulong.Parse(node.GetValue(name)));
                    else if (type == typeof(double))
                        field.SetValue(obj, double.Parse(node.GetValue(name)));
                    else if (type == typeof(Guid))
                        field.SetValue(obj, Guid.Parse(node.GetValue(name)));
                    else
                        success = false;
                }
            }
            return success;
        }
    }
}
