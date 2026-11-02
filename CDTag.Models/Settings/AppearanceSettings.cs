using System.Runtime.Serialization;

namespace CDTag.Model.Settings
{
    /// <summary>Persisted appearance options (appearance.json in the local application directory).</summary>
    [DataContract]
    public class AppearanceSettings
    {
        /// <summary>"System", "Light" or "Dark".</summary>
        [DataMember]
        public string Theme { get; set; }
    }
}
