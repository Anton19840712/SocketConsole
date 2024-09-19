using System.Xml.Serialization;

namespace SocketConsole.Models
{
    [XmlRoot(ElementName = "Envelope", Namespace = "http://schemas.xmlsoap.org/soap/envelope/")]
    public class SoapEnvelope
    {
        [XmlElement(ElementName = "Body")]
        public SoapBody Body { get; set; }
    }
    public class SoapBody
    {
        [XmlElement(ElementName = "card112ChangedRequest", Namespace = "http://www.protei.ru/emergency/integration")]
        public Card112ChangedRequest Card112ChangedRequest { get; set; }
    }

    public class Card112ChangedRequest
    {
        [XmlElement(ElementName = "globalId")]
        public string GlobalId { get; set; }

        [XmlElement(ElementName = "nEmergencyCardId")]
        public int EmergencyCardId { get; set; }

        [XmlElement(ElementName = "dtCreate")]
        public DateTime DtCreate { get; set; }

        [XmlElement(ElementName = "nCallTypeId")]
        public int CallTypeId { get; set; }

        [XmlElement(ElementName = "nCardSyntheticState")]
        public int CardSyntheticState { get; set; }

        [XmlElement(ElementName = "lWithCall")]
        public int WithCall { get; set; }

        [XmlElement(ElementName = "strCreator")]
        public string Creator { get; set; }

        [XmlElement(ElementName = "strAddressLevel1", IsNullable = true)]
        public string AddressLevel1 { get; set; }

        [XmlElement(ElementName = "strIncidentDescription", IsNullable = true)]
        public string IncidentDescription { get; set; }

        // Добавьте остальные поля, соответствующие вашей XML-структуре
    }
}
