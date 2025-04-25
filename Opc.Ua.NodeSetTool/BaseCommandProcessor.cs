using McMaster.Extensions.CommandLineUtils;
using System.Xml;
using System.Xml.Serialization;

namespace NodeSetTool
{
    public static class BaseCommandProcessor
    {
        public static T Load<T>(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                return Load<T>(stream);
            }
        }

        public static T Load<T>(Stream stream)
        {
            var settings = new XmlReaderSettings()
            {
                DtdProcessing = DtdProcessing.Prohibit
            };

            XmlSerializer serializer = new XmlSerializer(typeof(T));

            using (var reader = XmlReader.Create(stream, settings))
            {
                return (T)serializer.Deserialize(reader);
            }
        }

        public static void Save<T>(string path, T output)
        {
            using (var ostrm = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                Save(ostrm, output);
            }
        }

        public static void Save<T>(Stream stream, T output)
        {
            var settings = new XmlWriterSettings()
            {
                IndentChars = "  ",
                Indent = true,
                Encoding = System.Text.Encoding.UTF8,
                CloseOutput = true
            };

            XmlSerializer serializer = new XmlSerializer(typeof(T));

            using (var writer = XmlWriter.Create(stream, settings))
            {
                serializer.Serialize(writer, output);
            }
        }

        public static string GetOption(this CommandLineApplication application, string name, string defaultValue = "")
        {
            var option = application.Options
                .Where(x => (x.ShortName == name || x.LongName == name) && x.HasValue())
                .FirstOrDefault();

            if (option == null)
            {
                return defaultValue;
            }

            return option.Value();
        }

        public static bool GetOption(this CommandLineApplication application, string name, bool defaultValue = false)
        {
            var option = application.Options
                .Where(x => (x.ShortName == name || x.LongName == name) && x.HasValue())
                .FirstOrDefault();

            if (option == null)
            {
                return defaultValue;
            }

            return true;
        }

        public static int GetOption(this CommandLineApplication application, string name, int defaultValue = 0)
        {
            var option = application.Options
                .Where(x => (x.ShortName == name || x.LongName == name) && x.HasValue())
                .FirstOrDefault();

            if (option == null)
            {
                return defaultValue;
            }

            return Int32.Parse(option.Value());
        }

        public static bool IsOptionSet(this CommandLineApplication application, string name, bool defaultValue = false)
        {
            var option = application.Options
                .Where(x => (x.ShortName == name || x.LongName == name) && x.HasValue())
                .Select(x => x)
                .FirstOrDefault();

            if (option == null)
            {
                return defaultValue;
            }

            return true;
        }
    }
}
