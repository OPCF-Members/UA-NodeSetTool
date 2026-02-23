using System.Xml;
using System.Xml.Serialization;

namespace Opc.Ua.Export
{
    public partial class UANodeSet
    {
        public static UANodeSet? Read(Stream stream)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit };
            var serializer = new XmlSerializer(typeof(UANodeSet));
            using var reader = XmlReader.Create(stream, settings);
            return (UANodeSet?)serializer.Deserialize(reader);
        }

        public void Write(Stream stream)
        {
            var settings = new XmlWriterSettings
            {
                IndentChars = "  ",
                Indent = true,
                Encoding = System.Text.Encoding.UTF8,
                CloseOutput = false
            };

            var serializer = new XmlSerializer(typeof(UANodeSet));
            using var writer = XmlWriter.Create(stream, settings);
            serializer.Serialize(writer, this);
        }
	}
}